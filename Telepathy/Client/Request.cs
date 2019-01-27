using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace Telepathy.Client
{
    internal class Request
    {
        public EventHandler<EventArgs<object>> OnReceiveData;
        public EventHandler OnServerClosed;

        //static Timer _heartTimer;

        static ApiResponse _heartRes;

        public static bool Connected => Manager != null && Manager.Connected;

        public UserInfoModel UserInfo;

        internal static SocketManager Manager;

        public SocketError Connect()
        {
            if (Connected) return SocketError.Success;

            string ip = "127.0.0.1";
            int port = 13909;
            if (string.IsNullOrWhiteSpace(ip) || port <= 1000) return SocketError.Fault;

            Manager = new SocketManager(ip, port);
            SocketError error = Manager.Connect();
            if (error == SocketError.Success)
            {
                Manager.ServerDataHandler += OnReceivedServerData;
                Manager.ServerStopEvent += OnServerStopEvent;
                //StartHeartbeat();
            }

            return error;
        }

        public static void Disconnect()
        {
            try
            {
                Manager.Disconnect();
            }
            catch (Exception) { }
        }

        static bool Send(byte[] buff)
        {
            if (!Connected) return false;

            Manager.Send(buff);
            return true;
        }

        void OnReceivedServerData(object sender, EventArgs<byte[]> e)
        {
            var str = Encoding.UTF8.GetString(e.Value);
            Console.WriteLine($"receive data {str}");

            OnReceiveData?.Invoke(null, OnReceiveData.CreateArgs(e.Value));
        }

        void OnServerStopEvent(object sender, EventArgs e)
        {
            OnServerClosed?.Invoke(sender, e);
        }

        /*void StartHeartbeat()
        {
            if (_heartTimer == null)
            {
                _heartTimer = new Timer();
                _heartTimer.Elapsed += TimeElapsed;
            }

            _heartTimer.AutoReset = true;
            _heartTimer.Interval = 3 * 1000;
            _heartTimer.Enabled = true;
            _heartTimer.Start();

            _heartRes = new ApiResponse((int)ApiCode.HeartBeat)
            {
                Data = new Dictionary<string, object>()
            };

            _heartRes.Data.Add("beat", UserInfo.Nickname + ":"+UserInfo.UserId+" " + DateTime.Now.ToString("HH:mm:ss"));
        }

        void TimeElapsed(object source, ElapsedEventArgs e)
        {
            _heartRes.Data.Clear();
            _heartRes.Data.Add("beat", UserInfo.Nickname + ":" + UserInfo.UserId+" " + DateTime.Now.ToString("HH:mm:ss"));

            Send(_heartRes);
        }*/
    }
}