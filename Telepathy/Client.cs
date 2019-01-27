using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : IDisposable
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

        List<byte> _buffer;

        readonly List<MySocketEventArgs> _listArgs = new List<MySocketEventArgs>();
        readonly MySocketEventArgs _receiveEventArgs = new MySocketEventArgs();
        int _tagCount;

        public bool Connected => _clientSocket != null && _clientSocket.Connected;

        // incoming message queue
        SafeQueue<Message> incomingQueue = new SafeQueue<Message>();

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
            _buffer = new List<byte>();

            var connectArgs = new SocketAsyncEventArgs {UserToken = _clientSocket, RemoteEndPoint = _hostEndPoint};
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

            InitSendArgs();
            _receiveEventArgs.Completed += IO_Completed;
            _receiveEventArgs.UserToken = e.UserToken;
            _receiveEventArgs.ArgsTag = 0;
            _bufferManager.SetBuffer(_receiveEventArgs);

            if (!e.ConnectSocket.ReceiveAsync(_receiveEventArgs))
                ProcessReceive(_receiveEventArgs);
        }

        MySocketEventArgs InitSendArgs()
        {
            MySocketEventArgs sendArg = new MySocketEventArgs();
            sendArg.Completed += IO_Completed;
            sendArg.UserToken = _clientSocket;
            sendArg.RemoteEndPoint = _hostEndPoint;
            sendArg.IsUsing = false;
            Interlocked.Increment(ref _tagCount);
            sendArg.ArgsTag = _tagCount;
            lock (_listArgs)
            {
                _listArgs.Add(sendArg);
            }

            return sendArg;
        }

        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            MySocketEventArgs mys = (MySocketEventArgs)e;

            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;

                case SocketAsyncOperation.Send:
                    mys.IsUsing = false;
                    ProcessSend(e);
                    break;

                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
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
                    byte[] data = new byte[e.BytesTransferred];
                    Array.Copy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);
                    lock (_buffer)
                    {
                        _buffer.AddRange(data);
                        do
                        {
                            byte[] header = _buffer.GetRange(0, 4).ToArray();
                            int packageLen = Utils.BytesToIntBigEndian(header);
                            if (packageLen <= _buffer.Count - 4)
                            {
                                byte[] rev = _buffer.GetRange(4, packageLen).ToArray();

                                lock (_buffer)
                                {
                                    _buffer.RemoveRange(0, packageLen + 4);
                                }
                                DoReceiveEvent(rev);
                            }
                            else
                            {
                                break;
                            }
                        } while (_buffer.Count > 4);
                    }

                    if (!token.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    ProcessError(e);
                }
            }
            catch (Exception xe)
            {
                Console.WriteLine(xe.Message);
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
        }

        // Close socket in case of failure and throws
        // a SocketException according to the SocketError.
        private void ProcessError(SocketAsyncEventArgs e)
        {
            var s = (Socket)e.UserToken;
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

            foreach (MySocketEventArgs arg in _listArgs)
                arg.Completed -= IO_Completed;

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

                // So easy!
                MySocketEventArgs sendArgs = _listArgs.Find(a => a.IsUsing == false) ?? InitSendArgs();

                lock (sendArgs)
                {
                    sendArgs.IsUsing = true;
                    sendArgs.SetBuffer(buff, 0, buff.Length);
                }

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