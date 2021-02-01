// a magnificent receive pipe to shield us from all of life's complexities.
// safely sends messages from receive thread to main thread.
// -> thread safety built in
// -> byte[] pooling coming in the future
//
// => hides all the complexity from telepathy
// => easy to switch between stack/queue/concurrentqueue/etc.
// => easy to test
using System.Collections.Generic;

namespace Telepathy
{
    public class MagnificentReceivePipe
    {
        // message queue
        // ConcurrentQueue allocates. lock{} instead.
        readonly Queue<Message> queue = new Queue<Message>();

        // for statistics. don't call Count and assume that it's the same after
        // the call.
        public int Count
        {
            get { lock (this) { return queue.Count; } }
        }

        public void Enqueue(Message message)
        {
            lock (this) { queue.Enqueue(message); }
        }

        public bool TryDequeue(out Message result)
        {
            lock (this)
            {
                result = default;
                if (queue.Count > 0)
                {
                    result = queue.Dequeue();
                    return true;
                }
                return false;
            }
        }

        public void Clear()
        {
            lock (this) { queue.Clear(); }
        }
    }
}