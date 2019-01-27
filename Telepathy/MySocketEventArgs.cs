using System.Net.Sockets;

namespace Telepathy
{
    class MySocketEventArgs : SocketAsyncEventArgs
    {
        public int ArgsTag;
        public bool IsUsing;
    }
}