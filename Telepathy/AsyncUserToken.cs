using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Telepathy
{
    public class AsyncUserToken
    {
        public IPAddress IpAddress;
        public EndPoint Remote;
        public Socket Socket;
        public DateTime ConnectTime;
        public string UserInfo;
        public List<byte> Buffer = new List<byte>();
    }
}