using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Telepathy
{
    public class Common
    {
        // cache finished SocketAsyncEventArgs so we can reuse them without
        // reallocating in each Send
        readonly ConcurrentQueue<SocketAsyncEventArgs> sendEventArgsCache = new ConcurrentQueue<SocketAsyncEventArgs>();

        // return a previously used SocketAsyncEventArgs or create a new one
        protected SocketAsyncEventArgs MakeSendArgs()
        {
            SocketAsyncEventArgs args;
            if (!sendEventArgsCache.TryDequeue(out args))
            {
                args = new SocketAsyncEventArgs();
            }
            return args;
        }

        protected void RetireSendArgs(SocketAsyncEventArgs args)
        {
            // retire SocketAsyncEventArgs to cache so we can reuse them without
            // allocating again
            sendEventArgsCache.Enqueue(args);
        }
    }
}