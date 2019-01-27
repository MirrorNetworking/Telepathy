using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Server
    {
        readonly int _maxConnectNum;
        readonly BufferManager _bufferManager;
        const int OpsToAlloc = 2;
        Socket _listenSocket;

        // Dict<connId, token>
        ConcurrentDictionary<int, AsyncUserToken> clients = new ConcurrentDictionary<int, AsyncUserToken>();

        // incoming message queue
        ConcurrentQueue<Message> incomingQueue = new ConcurrentQueue<Message>();

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
            incomingQueue.Enqueue(new Message(token.connectionId, EventType.Connected, null));
        }

        void OnClientDisconnected(AsyncUserToken token)
        {
            incomingQueue.Enqueue(new Message(token.connectionId, EventType.Disconnected, null));
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
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    byte[] data = new byte[e.BytesTransferred];
                    Array.Copy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);
                    lock (token.Buffer)
                    {
                        token.Buffer.AddRange(data);
                    }

                    do
                    {
                        byte[] header = token.Buffer.GetRange(0, 4).ToArray();
                        int packageLen = Utils.BytesToIntBigEndian(header);
                        if (packageLen > token.Buffer.Count - 4)
                        {
                            break;
                        }

                        byte[] rev = token.Buffer.GetRange(4, packageLen).ToArray();

                        lock (token.Buffer)
                        {
                            token.Buffer.RemoveRange(0, packageLen + 4);
                        }

                        OnReceiveClientData(token, rev);
                    }
                    while (token.Buffer.Count > 4);

                    if (!token.Socket.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    CloseClientSocket(e);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("Server.ProcessReceive failed: " + exception);
            }
        }

        void OnReceiveClientData(AsyncUserToken token, byte[] data)
        {
            incomingQueue.Enqueue(new Message(token.connectionId, EventType.Data, data));
        }

        // This method is invoked when an asynchronous send operation completes.
        // The method issues another receive on the socket to read any additional
        // data sent from the client
        //
        // <param name="e"></param>
        void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                // done echoing data back to the client
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                // read the next block of data send from the client
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ProcessReceive(e);
                }
            }
            else
            {
                Logger.LogError("Server.ProcessSend failed: " + e.SocketError);
                CloseClientSocket(e);
            }
        }

        void CloseClientSocket(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = e.UserToken as AsyncUserToken;

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

                    //token.Socket.Send(buff);  //这句也可以发送, 可根据自己的需要来选择

                    SocketAsyncEventArgs sendArg = new SocketAsyncEventArgs { UserToken = token };
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