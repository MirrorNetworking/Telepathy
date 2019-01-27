using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    internal class Client : IDisposable
    {
        const int BuffSize = 1024;

        // The socket used to send/receive messages.
        readonly Socket _clientSocket;

        // Flag for connected socket.
        bool _connected;

        // Listener endpoint.
        readonly IPEndPoint _hostEndPoint;

        // Signals a connection.
        static readonly AutoResetEvent AutoConnectEvent = new AutoResetEvent(false);

        readonly BufferManager _bufferManager;

        readonly List<byte> _buffer;

        readonly List<MySocketEventArgs> _listArgs = new List<MySocketEventArgs>();
        readonly MySocketEventArgs _receiveEventArgs = new MySocketEventArgs();
        int _tagCount;

        public bool Connected => _clientSocket != null && _clientSocket.Connected;

        public EventHandler<EventArgs<byte[]>> ServerDataHandler;

        public EventHandler ServerStopEvent;

        // Create an uninitialized client instance.
        // To start the send/receive processing call the
        // Connect method followed by SendReceive method.
        internal Client(string ip, int port)
        {
            // Instantiates the endpoint and socket.
            _hostEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
            _clientSocket = new Socket(_hostEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _bufferManager = new BufferManager(BuffSize * 2, BuffSize);
            _buffer = new List<byte>();
        }

        internal SocketError Connect()
        {
            var connectArgs = new SocketAsyncEventArgs {UserToken = _clientSocket, RemoteEndPoint = _hostEndPoint};
            connectArgs.Completed += OnConnect;

            _clientSocket.ConnectAsync(connectArgs);
            AutoConnectEvent.WaitOne();
            return connectArgs.SocketError;
        }

        // Disconnect from the host.
        internal void Disconnect()
        {
            _clientSocket.Disconnect(false);
        }

        // Callback for connect operation
        void OnConnect(object sender, SocketAsyncEventArgs e)
        {
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
            var sendArg = new MySocketEventArgs();
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
            var mys = (MySocketEventArgs)e;

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
                var token = (Socket)e.UserToken;
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    byte[] data = new byte[e.BytesTransferred];
                    Array.Copy(e.Buffer, e.Offset, data, 0, e.BytesTransferred);
                    lock (_buffer)
                    {
                        _buffer.AddRange(data);
                        do
                        {
                            byte[] lenBytes = _buffer.GetRange(0, 4).ToArray();
                            int packageLen = BitConverter.ToInt32(lenBytes, 0);
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

        #region read write

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

            //这里一定要记得把事件移走,如果不移走,当断开服务器后再次连接上,会造成多次事件触发.
            foreach (MySocketEventArgs arg in _listArgs)
                arg.Completed -= IO_Completed;

            _receiveEventArgs.Completed -= IO_Completed;
            ServerStopEvent?.Invoke(this, null);
        }

        // Exchange a message with the host.
        internal void Send(byte[] sendBuffer)
        {
            if (_connected)
            {
                //先对数据进行包装,就是把包的大小作为头加入,这必须与服务器端的协议保持一致,否则造成服务器无法处理数据.
                var buff = new byte[sendBuffer.Length + 4];
                Array.Copy(BitConverter.GetBytes(sendBuffer.Length), buff, 4);
                Array.Copy(sendBuffer, 0, buff, 4, sendBuffer.Length);

                //查找有没有空闲的发送MySocketEventArgs,有就直接拿来用,没有就创建新的.So easy!
                MySocketEventArgs sendArgs = _listArgs.Find(a => a.IsUsing == false) ?? InitSendArgs();

                lock (sendArgs) //要锁定,不锁定让别的线程抢走了就不妙了.
                {
                    sendArgs.IsUsing = true;
                    sendArgs.SetBuffer(buff, 0, buff.Length);
                }

                _clientSocket.SendAsync(sendArgs);
            }
            else
            {
                //throw new SocketException((int)SocketError.NotConnected);
            }
        }

        /// <summary>
        /// 使用新进程通知事件回调
        /// </summary>
        /// <param name="buff"></param>
        private void DoReceiveEvent(byte[] buff)
        {
            if (ServerDataHandler == null) return;

            //ServerDataHandler(buff); //可直接调用.
            //但我更喜欢用新的线程,这样不拖延接收新数据.
            var thread = new Thread((obj) => { ServerDataHandler(this, ServerDataHandler.CreateArgs((byte[])obj)); })
            {
                IsBackground = true
            };

            thread.Start(buff);
        }

        #endregion

        #region IDisposable Members

        // Disposes the instance of SocketClient.
        public void Dispose()
        {
            AutoConnectEvent.Close();
            if (_clientSocket.Connected)
            {
                _clientSocket.Close();
            }
        }

        #endregion
    }
}