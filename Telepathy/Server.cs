using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Telepathy
{
    public class Server
    {
        readonly int _maxConnectNum;
        readonly int _revBufferSize;
        readonly BufferManager _bufferManager;
        const int OpsToAlloc = 2;
        Socket _listenSocket;
        readonly SocketEventPool _pool;
        int _clientCount;
        readonly Semaphore _maxNumberAcceptedClients;

        //public EventHandler<EventArgs<AsyncUserToken, int>> ClientNumberChange;

        public List<AsyncUserToken> ClientList { get; set; }

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

        public Server(int numConnections, int receiveBufferSize)
        {
            _clientCount = 0;
            _maxConnectNum = numConnections;
            _revBufferSize = receiveBufferSize;

            // allocate buffers such that the maximum number of sockets can have one outstanding read and
            //write posted to the socket simultaneously
            _bufferManager = new BufferManager(receiveBufferSize * numConnections * OpsToAlloc, receiveBufferSize);

            _pool = new SocketEventPool(numConnections);
            _maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);

            // Allocates one large byte buffer which all I/O operations use a piece of.  This guards
            // against memory fragmentation
            _bufferManager.InitBuffer();
            ClientList = new List<AsyncUserToken>();

            // preallocate pool of SocketAsyncEventArgs objects
            for (int i = 0; i < _maxConnectNum; i++)
            {
                var readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += IO_Completed;
                readWriteEventArg.UserToken = new AsyncUserToken();

                // assign a byte buffer from the buffer pool to the SocketAsyncEventArg object
                _bufferManager.SetBuffer(readWriteEventArg);

                // add SocketAsyncEventArg to the pool
                _pool.Push(readWriteEventArg);
            }
        }

        public bool Start(IPEndPoint localEndPoint)
        {
            try
            {
                ClientList.Clear();
                _listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listenSocket.Bind(localEndPoint);

                // start the server with a listen backlog of 100 connections
                _listenSocket.Listen(_maxConnectNum);

                // post accepts on the listening socket
                StartAccept(null);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Stop()
        {
            foreach (AsyncUserToken token in ClientList)
            {
                try
                {
                    token.Socket.Shutdown(SocketShutdown.Both);
                    OnClientDisconnected(new EventArgs<AsyncUserToken>(token));
                }
                catch (Exception) { }
            }

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }

            _listenSocket.Close();
            int cCount = ClientList.Count;
            lock (ClientList) { ClientList.Clear(); }
        }

        public void CloseClient(AsyncUserToken token)
        {
            try
            {
                token.Socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception) { }
        }

        // Begins an operation to accept a connection request from the client
        //
        // <param name="acceptEventArg">The context object to use when issuing
        // the accept operation on the server's listening socket</param>
        public void StartAccept(SocketAsyncEventArgs acceptEventArg)
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

            _maxNumberAcceptedClients.WaitOne();
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

        protected virtual void OnClientConnected(EventArgs<AsyncUserToken> e)
        {
            incomingQueue.Enqueue(new Message(e.Value.connectionId, EventType.Connected, null));
        }

        protected virtual void OnClientDisconnected(EventArgs<AsyncUserToken> e)
        {
            incomingQueue.Enqueue(new Message(e.Value.connectionId, EventType.Disconnected, null));
        }

        void ProcessAccept(SocketAsyncEventArgs e)
        {
            try
            {
                Interlocked.Increment(ref _clientCount);

                // Get the socket for the accepted client connection and put it into the
                //ReadEventArg object user token
                SocketAsyncEventArgs readEventArgs = _pool.Pop();
                var userToken = (AsyncUserToken)readEventArgs.UserToken;
                userToken.Socket = e.AcceptSocket;
                userToken.ConnectTime = DateTime.Now;
                userToken.Remote = e.AcceptSocket.RemoteEndPoint;
                userToken.IpAddress = ((IPEndPoint)(e.AcceptSocket.RemoteEndPoint)).Address;
                userToken.connectionId = _clientCount;

                lock (ClientList) { ClientList.Add(userToken); }

                OnClientConnected(new EventArgs<AsyncUserToken>(userToken));

                if (!e.AcceptSocket.ReceiveAsync(readEventArgs))
                {
                    ProcessReceive(readEventArgs);
                }
            }
            catch (Exception)
            {
                // log
            }

            // Accept the next connection request
            if (e.SocketError == SocketError.OperationAborted) return;
            StartAccept(e);
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
                var token = (AsyncUserToken)e.UserToken;
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
                        byte[] lenBytes = token.Buffer.GetRange(0, 4).ToArray();
                        int packageLen = BitConverter.ToInt32(lenBytes, 0);
                        if (packageLen > token.Buffer.Count - 4)
                        {
                            break;
                        }

                        byte[] rev = token.Buffer.GetRange(4, packageLen).ToArray();

                        lock (token.Buffer)
                        {
                            token.Buffer.RemoveRange(0, packageLen + 4);
                        }

                        var e1 = new EventArgs<AsyncUserToken, byte[]>(token, rev);
                        OnReceiveClientData(e1);

                    } while (token.Buffer.Count > 4);

                    if (!token.Socket.ReceiveAsync(e))
                        ProcessReceive(e);
                }
                else
                {
                    CloseClientSocket(e);
                }
            }
            catch (Exception)
            {
                //log;
            }
        }

        protected virtual void OnReceiveClientData(EventArgs<AsyncUserToken, byte[]> arg)
        {
            incomingQueue.Enqueue(new Message(arg.Value.connectionId, EventType.Data, arg.Value2));
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
                var token = (AsyncUserToken)e.UserToken;
                // read the next block of data send from the client
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ProcessReceive(e);
                }
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        void CloseClientSocket(SocketAsyncEventArgs e)
        {
            var token = e.UserToken as AsyncUserToken;

            lock (ClientList) { ClientList.Remove(token); }

            // close the socket associated with the client
            try
            {
                token?.Socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception) { }
            token?.Socket.Close();

            // decrement the counter keeping track of the total number of clients connected to the server
            Interlocked.Decrement(ref _clientCount);
            _maxNumberAcceptedClients.Release();

            // Free the SocketAsyncEventArg so they can be reused by another client
            e.UserToken = new AsyncUserToken();
            _pool.Push(e);

            OnClientDisconnected(new EventArgs<AsyncUserToken>(token));
        }

        public void SendMessage(AsyncUserToken token, byte[] message)
        {
            if (token?.Socket == null || !token.Socket.Connected)
                return;

            try
            {
                var buff = new byte[message.Length + 4];
                byte[] len = BitConverter.GetBytes(message.Length);
                Array.Copy(len, buff, 4);
                Array.Copy(message, 0, buff, 4, message.Length);

                //token.Socket.Send(buff);  //这句也可以发送, 可根据自己的需要来选择

                var sendArg = new SocketAsyncEventArgs { UserToken = token };
                sendArg.SetBuffer(buff, 0, buff.Length);
                token.Socket.SendAsync(sendArg);
            }
            catch (Exception)
            {
                // log
            }
        }
    }
}