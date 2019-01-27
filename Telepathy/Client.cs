using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : Common, IDisposable
    {
        const int BuffSize = 1024;

        // The socket used to send/receive messages.
        Socket _clientSocket;

        // Flag for connected socket.
        bool _connected;

        // Listener endpoint.
        IPEndPoint _hostEndPoint;

        // Signals a connection.
        static readonly AutoResetEvent AutoConnectEvent = new AutoResetEvent(false);

        BufferManager _bufferManager;

        MemoryStream buffer = new MemoryStream();

        readonly SocketAsyncEventArgs _receiveEventArgs = new SocketAsyncEventArgs();

        public bool Connected => _clientSocket != null && _clientSocket.Connected;

        // incoming message queue
        ConcurrentQueue<Message> incomingQueue = new ConcurrentQueue<Message>();

        // removes and returns the oldest message from the message queue.
        // (might want to call this until it doesn't return anything anymore)
        // -> Connected, Data, Disconnected events are all added here
        // -> bool return makes while (GetMessage(out Message)) easier!
        // -> no 'is client connected' check because we still want to read the
        //    Disconnected message after a disconnect
        public bool GetNextMessage(out Message message)
        {
            return incomingQueue.TryDequeue(out message);
        }

        public bool Connect(string ip, int port)
        {
            // Instantiate the endpoint and socket.
            _hostEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
            _clientSocket = new Socket(_hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _bufferManager = new BufferManager(BuffSize * 2, BuffSize);

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs {UserToken = _clientSocket, RemoteEndPoint = _hostEndPoint};
            connectArgs.Completed += OnConnect;

            _clientSocket.ConnectAsync(connectArgs);
            AutoConnectEvent.WaitOne();

            if (connectArgs.SocketError != SocketError.Success)
            {
                Logger.LogWarning("Client.Connect failed: " + connectArgs.SocketError);
                return false;
            }
            return true;
        }

        // Disconnect from the host.
        public void Disconnect()
        {
            _clientSocket.Disconnect(false);
        }

        // Callback for connect operation
        void OnConnect(object sender, SocketAsyncEventArgs e)
        {
            incomingQueue.Enqueue(new Message(0, EventType.Connected, null));

            // Signals the end of connection.
            AutoConnectEvent.Set();

            // Set the flag for socket connected.
            _connected = (e.SocketError == SocketError.Success);
            if (_connected)
                InitArgs(e);
        }

        void InitArgs(SocketAsyncEventArgs e)
        {
            _bufferManager.InitBuffer();

            _receiveEventArgs.Completed += IO_Completed;
            _receiveEventArgs.UserToken = e.UserToken;
            _bufferManager.SetBuffer(_receiveEventArgs);

            if (!e.ConnectSocket.ReceiveAsync(_receiveEventArgs))
                ProcessReceive(_receiveEventArgs);
        }

        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;

                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;

                default:
                    Logger.LogError("The last operation completed on the socket was not a receive or send");
                    break;
            }
        }

        // This method is invoked when an asynchronous receive operation completes.
        // If the remote host closed the connection, then the socket is closed.
        // If data was received then the data is echoed back to the client.
        //
        void ProcessReceive(SocketAsyncEventArgs e)
        {
            try
            {
                // check if the remote host closed the connection
                Socket token = (Socket)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    // write it all into our memory stream first
                    buffer.Write(e.Buffer, e.Offset, e.BytesTransferred);

                    // keep trying headers (we might need to process >1 message)
                    while (buffer.Position >= 4)
                    {
                        // we can read a header. so read it.
                        long bufferSize = buffer.Position;
                        buffer.Position = 0;
                        byte[] header = new byte[4]; // TODO cache
                        buffer.Read(header, 0, header.Length);
                        int contentSize = Utils.BytesToIntBigEndian(header);

                        // avoid -1 attacks from hackers
                        if (contentSize > 0)
                        {
                            // enough content to finish the message?
                            if (bufferSize - buffer.Position >= contentSize)
                            {
                                // read content
                                byte[] content = new byte[contentSize];
                                buffer.Read(content, 0, content.Length);

                                // process message
                                DoReceiveEvent(content);

                                // read what's left in the buffer. this is valid
                                // data that we received at some point. can't lose
                                // it.
                                byte[] remainder = new byte[bufferSize - buffer.Position];
                                buffer.Read(remainder, 0, remainder.Length);

                                // write it to the beginning of the buffer. this
                                // sets position to the new true end automatically.
                                buffer.Position = 0;
                                buffer.Write(remainder, 0, remainder.Length);
                            }
                            // otherwise we just need to receive more.
                            else break;
                        }
                        else
                        {
                            ProcessError(e);
                            Logger.LogWarning("Client.ProcessReceive: received negative contentSize: " + contentSize + ". Maybe an attacker tries to exploit the server?");
                        }
                    }

                    if (!token.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    ProcessError(e);
                    Logger.LogWarning("Client.ProcessReceive ended: " + e.BytesTransferred + " transferred. socketerror=" + e.SocketError);
                }
            }
            catch (Exception exception)
            {
                Logger.Log("Client.ProcessReceive failed: " + exception);
            }
        }

        // This method is invoked when an asynchronous send operation completes.
        // The method issues another receive on the socket to read any additional
        // data sent from the client
        //
        // <param name="e"></param>
        void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                ProcessError(e);
            }

            // retire send args in any case, so we can reuse them without 'new'
            e.Completed -= IO_Completed; // don't want to call it twice next time. TODO is this even necessary or will it only add one function anyway?
            RetireSendArgs(e);
        }

        // Close socket in case of failure and throws
        // a SocketException according to the SocketError.
        void ProcessError(SocketAsyncEventArgs e)
        {
            Socket s = (Socket)e.UserToken;
            if (s.Connected)
            {
                // close the socket associated with the client
                try
                {
                    s.Shutdown(SocketShutdown.Both);
                }
                catch (Exception)
                {
                    // throws if client process has already closed
                }
                finally
                {
                    if (s.Connected)
                    {
                        s.Close();
                    }
                    _connected = false;
                }
            }

            _receiveEventArgs.Completed -= IO_Completed;

            // disconnected event
            incomingQueue.Enqueue(new Message(0, EventType.Disconnected, null));
        }

        // Exchange a message with the host.
        public bool Send(byte[] sendBuffer)
        {
            if (_connected)
            {
                byte[] buff = new byte[sendBuffer.Length + 4];
                byte[] header = Utils.IntToBytesBigEndian(sendBuffer.Length);
                Array.Copy(header, buff, 4);
                Array.Copy(sendBuffer, 0, buff, 4, sendBuffer.Length);

                // create send args
                SocketAsyncEventArgs sendArgs = MakeSendArgs();
                sendArgs.Completed += IO_Completed;
                sendArgs.UserToken = _clientSocket;
                sendArgs.RemoteEndPoint = _hostEndPoint;
                sendArgs.SetBuffer(buff, 0, buff.Length);

                _clientSocket.SendAsync(sendArgs);
                return true;
            }
            else
            {
                //throw new SocketException((int)SocketError.NotConnected);
                return false;
            }
        }

        void DoReceiveEvent(byte[] buff)
        {
            incomingQueue.Enqueue(new Message(0, EventType.Data, buff));
        }

        // Disposes the instance of SocketClient.
        public void Dispose()
        {
            AutoConnectEvent.Close();
            if (_clientSocket.Connected)
            {
                _clientSocket.Close();
            }
        }
    }
}