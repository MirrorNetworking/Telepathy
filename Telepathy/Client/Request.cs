using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace Telepathy.Client
{
    internal class Request
    {
        //定义事件与委托
        public EventHandler<EventArgs<object>> OnReceiveData;
        public EventHandler OnServerClosed;

        /// <summary>
        /// 心跳定时器
        /// </summary>
        private static Timer _heartTimer;

        /// <summary>
        /// 心跳包
        /// </summary>
        private static ApiResponse _heartRes;

        /// <summary>
        /// 判断是否已连接
        /// </summary>
        public static bool Connected => Manager != null && Manager.Connected;

        /// <summary>
        /// 已登录的用户信息
        /// </summary>
        public UserInfoModel UserInfo;

        internal static SocketManager Manager;

        /// <summary>
        /// 连接到服务器
        /// </summary>
        /// <returns></returns>
        public SocketError Connect()
        {
            if (Connected) return SocketError.Success;

            string ip = "127.0.0.1";
            int port = 13909;
            if (string.IsNullOrWhiteSpace(ip) || port <= 1000) return SocketError.Fault;

            //创建连接对象, 连接到服务器
            Manager = new SocketManager(ip, port);
            SocketError error = Manager.Connect();
            if (error == SocketError.Success)
            {
                //连接成功后,就注册事件. 最好在成功后再注册.
                Manager.ServerDataHandler += OnReceivedServerData;
                Manager.ServerStopEvent += OnServerStopEvent;
                StartHeartbeat();
            }

            return error;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public static void Disconnect()
        {
            try
            {
                Manager.Disconnect();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 发送字节流
        /// </summary>
        /// <param name="buff"></param>
        /// <returns></returns>
        private static bool Send(byte[] buff)
        {
            if (!Connected) return false;

            Manager.Send(buff);
            return true;
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnReceivedServerData(object sender, EventArgs<byte[]> e)
        {
            var str = Encoding.UTF8.GetString(e.Value);
            Console.WriteLine($"receive data {str}");

            //你要处理的代码,可以实现把buff转化成你具体的对象, 再传给前台
            OnReceiveData?.Invoke(null, OnReceiveData.CreateArgs(e.Value));
        }

        /// <summary>
        /// 服务器已断开
        /// </summary>
        private void OnServerStopEvent(object sender, EventArgs e)
        {
            OnServerClosed?.Invoke(sender, e);
        }

        //心跳包也是很重要的,看自己的需要了, 我只定义出来, 你自己找个地方去调用吧
        /// <summary>
        /// 开启心跳
        /// </summary>
        private void StartHeartbeat()
        {
            if (_heartTimer == null)
            {
                _heartTimer = new Timer();
                _heartTimer.Elapsed += TimeElapsed;
            }

            _heartTimer.AutoReset = true;     //循环执行
            _heartTimer.Interval = 3 * 1000; //每3秒执行一次
            _heartTimer.Enabled = true;
            _heartTimer.Start();

            //初始化心跳包
            _heartRes = new ApiResponse((int)ApiCode.HeartBeat)
            {
                Data = new Dictionary<string, object>()
            };

            _heartRes.Data.Add("beat", UserInfo.Nickname + ":"+UserInfo.UserId+" " + DateTime.Now.ToString("HH:mm:ss"));
        }

        /// <summary>
        /// 定时执行
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void TimeElapsed(object source, ElapsedEventArgs e)
        {
            _heartRes.Data.Clear();
            _heartRes.Data.Add("beat", UserInfo.Nickname + ":" + UserInfo.UserId+" " + DateTime.Now.ToString("HH:mm:ss"));

            Send(_heartRes);
        }
    }
}