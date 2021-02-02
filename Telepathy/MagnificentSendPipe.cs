// a magnificent send pipe to shield us from all of life's complexities.
// safely sends messages from main thread to send thread.
// -> thread safety built in
// -> byte[] pooling coming in the future
//
// => hides all the complexity from telepathy
// => easy to switch between stack/queue/concurrentqueue/etc.
// => easy to test

using System;
using System.Collections.Generic;

namespace Telepathy
{
    public class MagnificentSendPipe
    {
        // message queue
        // ConcurrentQueue allocates. lock{} instead.
        readonly Queue<byte[]> queue = new Queue<byte[]>();

        // for statistics. don't call Count and assume that it's the same after
        // the call.
        public int Count
        {
            get { lock (this) { return queue.Count; } }
        }

        // enqueue a message
        // arraysegment for allocation free sends later.
        // -> the segment's array is only used until Enqueue() returns!
        public void Enqueue(ArraySegment<byte> message)
        {
            // ArraySegment array is only valid until returning, so copy
            // it into a byte[] that we can queue safely.
            // TODO byte[] pool later!
            byte[] data = new byte[message.Count];
            Buffer.BlockCopy(message.Array, message.Offset, data, 0, message.Count);

            // safely enqueue
            lock (this) { queue.Enqueue(data); }
        }

        // for when we want to dequeue and remove all of them at once without
        // locking every single TryDequeue.
        // -> COPIES all into a List, so we don't need to allocate via .ToArray
        // -> list passed as parameter to avoid allocations
        //
        // Net 4.X has ConcurrentQueue, but ConcurrentQueue has no TryDequeueAll
        // method, which makes SafeQueue twice as fast for the send thread.
        //
        // uMMORPG 450 CCU
        //   SafeQueue:       900-1440ms latency
        //   ConcurrentQueue:     2000ms latency
        //
        // It's also noticeable in the LoadTest project, which hardly handles
        // 300 CCU with ConcurrentQueue!
        public bool DequeueAll(List<byte[]> result)
        {
            // clear first, don't need to do that inside the lock.
            result.Clear();

            lock (this)
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
            lock (this) { queue.Clear(); }
        }
    }
}