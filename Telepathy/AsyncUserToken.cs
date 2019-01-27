using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Telepathy
{
    public class AsyncUserToken
    {
        /// <summary>
        /// 客户端IP地址
        /// </summary>
        public IPAddress IpAddress { get; set; }

        /// <summary>
        /// 远程地址
        /// </summary>
        public EndPoint Remote { get; set; }

        /// <summary>
        /// 通信Socket
        /// </summary>
        public Socket Socket { get; set; }

        /// <summary>
        /// 连接时间
        /// </summary>
        public DateTime ConnectTime { get; set; }

        /// <summary>
        /// 所属用户信息
        /// </summary>
        public string UserInfo { get; set; }

        /// <summary>
        /// 数据缓存区
        /// </summary>
        public List<byte> Buffer { get; set; }

        public AsyncUserToken()
        {
            Buffer = new List<byte>();
        }
    }
}