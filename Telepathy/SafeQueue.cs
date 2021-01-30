// ConcurrentQueue allocates.
// Let's wrap a queue with lock{} statements instead.\
//
// Also...
// Net 4.X has ConcurrentQueue, but ConcurrentQueue has no TryDequeueAll method,
// which makes SafeQueue twice as fast for the send thread.
//
// uMMORPG 450 CCU
//   SafeQueue:       900-1440ms latency
//   ConcurrentQueue:     2000ms latency
//
// It's also noticeable in the LoadTest project, which hardly handles 300 CCU
// with ConcurrentQueue!
using System.Collections.Generic;

namespace Telepathy
{
    public class SafeQueue<T>
    {
        readonly Queue<T> queue = new Queue<T>();

        // for statistics. don't call Count and assume that it's the same after the
        // call.
        public int Count
        {
            get
            {
                lock (queue)
                {
                    return queue.Count;
                }
            }
        }

        public void Enqueue(T item)
        {
            lock (queue)
            {
                queue.Enqueue(item);
            }
        }

        // can't check .Count before doing Dequeue because it might change inbetween,
        // so we need a TryDequeue
        public bool TryDequeue(out T result)
        {
            lock (queue)
            {
                result = default(T);
                if (queue.Count > 0)
                {
                    result = queue.Dequeue();
                    return true;
                }
                return false;
            }
        }

        // for when we want to dequeue and remove all of them at once without
        // locking every single TryDequeue.
        // -> COPIES all into a List, so we don't need to allocate via .ToArray
        // -> list passed as parameter to avoid allocations
        public bool TryDequeueAll(List<T> result)
        {
            // clear first, don't need to do that inside the lock.
            result.Clear();

            lock (queue)
            {
                // IMPORTANT: this transfers ownership of the internal array to
                //            whoever calls this function.
                //            the out array can safely be used from the caller,
                //            while nobody else will use it anymore since we
                //            clear it here.

                // Linq.ToList allocates a new list, which we don't want.
                // Note: this COULD be way faster if we make our own queue where
                //       we can Array.Copy the internal array to the new list.
                while (queue.Count > 0)
                {
                    result.Add(queue.Dequeue());
                }
                queue.Clear();
            }

            // return amount copied. don't need to do that inside the lock.
            return result.Count > 0;
        }

        public void Clear()
        {
            lock (queue)
            {
                queue.Clear();
            }
        }
    }
}
