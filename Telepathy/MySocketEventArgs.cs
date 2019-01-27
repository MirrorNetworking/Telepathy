using System.Net.Sockets;

namespace Telepathy
{
    class MySocketEventArgs : SocketAsyncEventArgs
    {
        public bool IsUsing;
    }
}