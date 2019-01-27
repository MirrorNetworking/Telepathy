using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Telepathy.Server
{
    class SocketManager
    {
        private readonly int _maxConnectNum;    //最大连接数
        private readonly int _revBufferSize;    //最大接收字节数
        private readonly BufferManager _bufferManager;
        private const int OpsToAlloc = 2;
        private Socket _listenSocket;            //监听Socket
        private readonly SocketEventPool _pool;
        private int _clientCount;              //连接的客户端数量
        private readonly Semaphore _maxNumberAcceptedClients;

        /// <summary>
        /// 客户端连接数量变化事件
        /// </summary>
        public EventHandler<EventArgs<AsyncUserToken, int>> ClientNumberChange;

        /// <summary>
        /// 接收到客户端的数据事件
        /// </summary>
        public EventHandler<EventArgs<AsyncUserToken, byte[]>> ReceiveClientData;

        /// <summary>
        /// 获取客户端列表
        /// </summary>
        public List<AsyncUserToken> ClientList { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="numConnections">最大连接数</param>
        /// <param name="receiveBufferSize">缓存区大小</param>
        public SocketManager(int numConnections, int receiveBufferSize)
        {
            _clientCount = 0;
            _maxConnectNum = numConnections;
            _revBufferSize = receiveBufferSize;

            // allocate buffers such that the maximum number of sockets can have one outstanding read and
            //write posted to the socket simultaneously
            _bufferManager = new BufferManager(receiveBufferSize * numConnections * OpsToAlloc, receiveBufferSize);

            _pool = new SocketEventPool(numConnections);
            _maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
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

        /// <summary>
        /// 启动服务
        /// </summary>
        /// <param name="localEndPoint"></param>
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

        /// <summary>
        /// 停止服务
        /// </summary>
        public void Stop()
        {
            foreach (AsyncUserToken token in ClientList)
            {
                try
                {
                    token.Socket.Shutdown(SocketShutdown.Both);
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

            ClientNumberChange?.Invoke(-cCount, null);
            OnClientNumChanged(ClientNumberChange.CreateArgs(null, -cCount));
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

        protected virtual void OnClientNumChanged(EventArgs<AsyncUserToken, int> e)
        {
            ClientNumberChange?.Invoke(this, e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
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

                lock (ClientList) { ClientList.Add(userToken); }

                OnClientNumChanged(ClientNumberChange.CreateArgs(userToken, 1));

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
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            try
            {
                // check if the remote host closed the connection
                var token = (AsyncUserToken)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    //读取数据
                    byte[] data = new byte[e.BytesTransferred];
                    Array.Copy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);
                    lock (token.Buffer)
                    {
                        token.Buffer.AddRange(data);
                    }

                    //注意:你一定会问,这里为什么要用do-while循环?
                    //如果当客户发送大数据流的时候,e.BytesTransferred的大小就会比客户端发送过来的要小,
                    //需要分多次接收.所以收到包的时候,先判断包头的大小.够一个完整的包再处理.
                    //如果客户短时间内发送多个小数据包时, 服务器可能会一次性把他们全收了.
                    //这样如果没有一个循环来控制,那么只会处理第一个包,
                    //剩下的包全部留在token.Buffer中了,只有等下一个数据包过来后,才会放出一个来.
                    do
                    {
                        //判断包的长度
                        byte[] lenBytes = token.Buffer.GetRange(0, 4).ToArray();
                        int packageLen = BitConverter.ToInt32(lenBytes, 0);
                        if (packageLen > token.Buffer.Count - 4)
                        {
                            //长度不够时,退出循环,让程序继续接收
                            break;
                        }

                        //包够长时,则提取出来,交给后面的程序去处理
                        byte[] rev = token.Buffer.GetRange(4, packageLen).ToArray();

                        //从数据池中移除这组数据
                        lock (token.Buffer)
                        {
                            token.Buffer.RemoveRange(0, packageLen + 4);
                        }

                        //将数据包交给后台处理,这里你也可以新开个线程来处理.加快速度.
                        var e1 = ReceiveClientData.CreateArgs(token, rev);
                        OnReceiveClientData(e1);

                        //这里API处理完后,并没有返回结果,当然结果是要返回的,却不是在这里, 这里的代码只管接收.
                        //若要返回结果,可在API处理中调用此类对象的SendMessage方法,统一打包发送.不要被微软的示例给迷惑了.
                    } while (token.Buffer.Count > 4);

                    //继续接收. 为什么要这么写,请看Socket.ReceiveAsync方法的说明
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
            ReceiveClientData?.Invoke(this, arg);
        }

        // This method is invoked when an asynchronous send operation completes.
        // The method issues another receive on the socket to read any additional
        // data sent from the client
        //
        // <param name="e"></param>
        private void ProcessSend(SocketAsyncEventArgs e)
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

        //关闭客户端
        private void CloseClientSocket(SocketAsyncEventArgs e)
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

            //如果有事件,则调用事件,发送客户端数量变化通知
            OnClientNumChanged(ClientNumberChange.CreateArgs(token, 1));
        }

        /// <summary>
        /// 对数据进行打包,然后再发送
        /// </summary>
        /// <param name="token"></param>
        /// <param name="message"></param>
        /// <returns></returns>
        public void SendMessage(AsyncUserToken token, byte[] message)
        {
            if (token?.Socket == null || !token.Socket.Connected)
                return;

            try
            {
                //对要发送的消息,制定简单协议,头4字节指定包的大小,方便客户端接收(协议可以自己定)
                var buff = new byte[message.Length + 4];
                byte[] len = BitConverter.GetBytes(message.Length);
                Array.Copy(len, buff, 4);
                Array.Copy(message, 0, buff, 4, message.Length);

                //token.Socket.Send(buff);  //这句也可以发送, 可根据自己的需要来选择

                //新建异步发送对象, 发送消息
                var sendArg = new SocketAsyncEventArgs { UserToken = token };
                sendArg.SetBuffer(buff, 0, buff.Length);  //将数据放置进去.
                token.Socket.SendAsync(sendArg);
            }
            catch (Exception)
            {
                // log
            }
        }
    }
}