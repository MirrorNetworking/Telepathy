using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Server : Common
    {
        readonly int _maxConnectNum;
        readonly BufferManager _bufferManager;
        const int OpsToAlloc = 2;
        Socket _listenSocket;

        // Dict<connId, token>
        ConcurrentDictionary<int, AsyncUserToken> clients = new ConcurrentDictionary<int, AsyncUserToken>();

        // connectionId counter
        // (right now we only use it from one listener thread, but we might have
        //  multiple threads later in case of WebSockets etc.)
        // -> static so that another server instance doesn't start at 0 again.
        static int counter = 0;

        // public next id function in case someone needs to reserve an id
        // (e.g. if hostMode should always have 0 connection and external
        //  connections should start at 1, etc.)
        public static int NextConnectionId()
        {
            int id = Interlocked.Increment(ref counter);

            // it's very unlikely that we reach the uint limit of 2 billion.
            // even with 1 new connection per second, this would take 68 years.
            // -> but if it happens, then we should throw an exception because
            //    the caller probably should stop accepting clients.
            // -> it's hardly worth using 'bool Next(out id)' for that case
            //    because it's just so unlikely.
            if (id == int.MaxValue)
            {
                throw new Exception("connection id limit reached: " + id);
            }

            return id;
        }

        // grab all new messages for all connections
        // -> Connected, Data, Disconnected events are all added here
        // -> no 'is client connected' check because we still want to read the
        //    Disconnected message after a disconnect
        // -> a Queue is passed as parameter so we don't have to allocate a new
        //    one each time. Queue.Dequeue is O(1)
        // -> Queue also guarantees that caller removes first time when using it
        //    unlike List, where they might forget to .Clear it!
        public void GetNextMessages(Queue<Message> messages)
        {
            foreach (KeyValuePair<int, AsyncUserToken> kvp in clients)
            {
                AsyncUserToken token = kvp.Value;

                // copy .Count here so we don't end up in a deadlock if messages
                // are coming in faster than we can dequeue them
                int count = token.incomingQueue.Count;
                for (int i = 0; i < count; ++i)
                {
                    Message message;
                    if (token.incomingQueue.TryDequeue(out message))
                        messages.Enqueue(message);
                }
            }
        }

        public Server(int numConnections, int receiveBufferSize)
        {
            _maxConnectNum = numConnections;

            // allocate buffers such that the maximum number of sockets can have one outstanding read and
            //write posted to the socket simultaneously
            _bufferManager = new BufferManager(receiveBufferSize * numConnections * OpsToAlloc, receiveBufferSize);

            // Allocates one large byte buffer which all I/O operations use a piece of.  This guards
            // against memory fragmentation
            _bufferManager.InitBuffer();
        }

        public bool Start(int port)
        {
            try
            {
                clients.Clear();
                IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);
                _listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listenSocket.Bind(localEndPoint);

                // start the server with a listen backlog of 100 connections
                _listenSocket.Listen(_maxConnectNum);

                // post accepts on the listening socket
                StartAccept(null);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError("Server.Start failed: " + e);
                return false;
            }
        }

        public void Stop()
        {
            foreach (KeyValuePair<int, AsyncUserToken> kvp in clients)
            {
                AsyncUserToken token = kvp.Value;
                try
                {
                    token.Socket.Shutdown(SocketShutdown.Both);
                    OnClientDisconnected(token);
                }
                catch (Exception) { }
            }

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }

            _listenSocket.Close();

            clients.Clear();
        }

        public void Disconnect(int connectionId)
        {
            AsyncUserToken token;
            if (clients.TryGetValue(connectionId, out token))
            {
                try
                {
                    token.Socket.Shutdown(SocketShutdown.Both);
                }
                catch (Exception) { }
            }
        }

        // Begins an operation to accept a connection request from the client
        //
        // <param name="acceptEventArg">The context object to use when issuing
        // the accept operation on the server's listening socket</param>
        void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += AcceptEventArg_Completed;
            }
            else
            {
                // socket must be cleared since the context object is being reused
                acceptEventArg.AcceptSocket = null;
            }

            if (!_listenSocket.AcceptAsync(acceptEventArg))
            {
                ProcessAccept(acceptEventArg);
            }
        }

        // This method is the callback method associated with Socket.AcceptAsync
        // operations and is invoked when an accept operation is complete
        //
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        void OnClientConnected(AsyncUserToken token)
        {
            token.incomingQueue.Enqueue(new Message(token.connectionId, EventType.Connected, null));
        }

        void OnClientDisconnected(AsyncUserToken token)
        {
            token.incomingQueue.Enqueue(new Message(token.connectionId, EventType.Disconnected, null));
        }

        void ProcessAccept(SocketAsyncEventArgs e)
        {
            try
            {
                // create SocketAsyncEventArgs for this client
                SocketAsyncEventArgs readEventArgs = new SocketAsyncEventArgs();
                readEventArgs.Completed += IO_Completed;
                readEventArgs.UserToken = new AsyncUserToken();

                // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
                _bufferManager.SetBuffer(readEventArgs);

                // Get the socket for the accepted client connection and put it into the
                //ReadEventArg object user token
                AsyncUserToken userToken = (AsyncUserToken)readEventArgs.UserToken;
                userToken.Socket = e.AcceptSocket;
                userToken.ConnectTime = DateTime.Now;
                userToken.Remote = e.AcceptSocket.RemoteEndPoint;
                userToken.IpAddress = ((IPEndPoint)(e.AcceptSocket.RemoteEndPoint)).Address;
                userToken.connectionId = NextConnectionId();

                clients[userToken.connectionId] = userToken;

                OnClientConnected(userToken);

                if (!e.AcceptSocket.ReceiveAsync(readEventArgs))
                {
                    ProcessReceive(readEventArgs);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("Server.ProcessAccept failed: " + exception);
            }

            // Accept the next connection request
            if (e.SocketError != SocketError.OperationAborted)
            {
                StartAccept(e);
            }
            else
            {
                Logger.LogError("Server.ProcessAccept: aborted");
            }
        }

        // This method is invoked when an asynchronous receive operation completes.
        // If the remote host closed the connection, then the socket is closed.
        // If data was received then the data is echoed back to the client.
        //
        protected override void ProcessReceive(SocketAsyncEventArgs e)
        {
            try
            {
                // check if the remote host closed the connection
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    // write it all into our memory stream first
                    token.buffer.Write(e.Buffer, e.Offset, e.BytesTransferred);

                    // keep trying headers (we might need to process >1 message)
                    while (token.buffer.Position >= 4)
                    {
                        // we can read a header. so read it.
                        long bufferSize = token.buffer.Position;
                        token.buffer.Position = 0;
                        byte[] header = new byte[4]; // TODO cache
                        token.buffer.Read(header, 0, header.Length);
                        int contentSize = Utils.BytesToIntBigEndian(header);

                        // avoid -1 attacks from hackers
                        if (contentSize > 0)
                        {
                            // enough content to finish the message?
                            if (bufferSize - token.buffer.Position >= contentSize)
                            {
                                // read content
                                byte[] content = new byte[contentSize];
                                token.buffer.Read(content, 0, content.Length);

                                // process message
                                OnReceiveClientData(token, content);

                                // read what's left in the buffer. this is valid
                                // data that we received at some point. can't lose
                                // it.
                                byte[] remainder = new byte[bufferSize - token.buffer.Position];
                                token.buffer.Read(remainder, 0, remainder.Length);

                                // write it to the beginning of the buffer. this
                                // sets position to the new true end automatically.
                                token.buffer.Position = 0;
                                token.buffer.Write(remainder, 0, remainder.Length);
                            }
                            // otherwise we just need to receive more.
                            else break;
                        }
                        else
                        {
                            CloseClientSocket(e);
                            Logger.LogWarning("Server.ProcessReceive: received negative contentSize: " + contentSize + ". Maybe an attacker tries to exploit the server?");
                        }
                    }

                    // continue receiving
                    if (!token.Socket.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    CloseClientSocket(e);
                    Logger.LogWarning("Server.ProcessReceive ended: " + e.BytesTransferred + " transferred. socketerror=" + e.SocketError);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("Server.ProcessReceive failed: " + exception);
            }
        }

        void OnReceiveClientData(AsyncUserToken token, byte[] data)
        {
            token.incomingQueue.Enqueue(new Message(token.connectionId, EventType.Data, data));
        }

        void CloseClientSocket(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = (AsyncUserToken)e.UserToken;

            clients.TryRemove(token.connectionId, out AsyncUserToken temp);

            // close the socket associated with the client
            try
            {
                token?.Socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception) { }
            token?.Socket.Close();

            // Free the SocketAsyncEventArg so they can be reused by another client
            e.UserToken = new AsyncUserToken();

            OnClientDisconnected(token);
        }

        public bool Send(int connectionId, byte[] message)
        {
            // find it
            AsyncUserToken token;
            if (clients.TryGetValue(connectionId, out token))
            {
                if (token?.Socket == null || !token.Socket.Connected)
                    return false;

                try
                {
                    byte[] buff = new byte[message.Length + 4];
                    byte[] header = Utils.IntToBytesBigEndian(message.Length);
                    Array.Copy(header, buff, 4);
                    Array.Copy(message, 0, buff, 4, message.Length);

                    SocketAsyncEventArgs sendArg = new SocketAsyncEventArgs();
                    //sendArg.Completed += IO_Completed; <- no callback = 2x throughput. we don't need to know.
                    sendArg.UserToken = token;
                    sendArg.SetBuffer(buff, 0, buff.Length);
                    token.Socket.SendAsync(sendArg);
                    return true;
                }
                catch (Exception e)
                {
                    // log
                    Logger.LogError("Server.Send failed: " + e);
                }
            }

            return false;
        }
    }
}