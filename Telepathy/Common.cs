using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Telepathy
{
    public abstract class Common
    {
        // nagle: disabled by default
        public bool NoDelay = true;

        // the big buffer. static for maximum performance, so we can use one big
        // buffer even if we run 1k clients in one process.
        // (having it static also fixes our loadtest memory leak)
        protected static BigBuffer bigBuffer = new BigBuffer();

        protected abstract void ProcessReceive(SocketAsyncEventArgs e);

        protected void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;

                case SocketAsyncOperation.Send:
                    Logger.LogError("Client.IO_Completed: Send should never happen.");
                    break;

                default:
                    Logger.LogError("The last operation completed on the socket was not a receive or send");
                    break;
            }
        }
    }
}