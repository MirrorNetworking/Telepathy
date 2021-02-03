// a magnificent receive pipe to shield us from all of life's complexities.
// safely sends messages from receive thread to main thread.
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
    public class MagnificentReceivePipe
    {
        // message queue
        // ConcurrentQueue allocates. lock{} instead.
        //
        // IMPORTANT: lock{} all usages!
        readonly Queue<ArraySegment<byte>> queue = new Queue<ArraySegment<byte>>();

        // connect/disconnect messages can be simple flags
        // no need have a Message<EventType, data> queue if connect & disconnect
        // only happen once.
        bool connectedFlag;
        bool disconnectedFlag;

        // byte[] pool to avoid allocations
        // Take & Return is beautifully encapsulated in the pipe.
        // the outside does not need to worry about anything.
        // and it can be tested easily.
        //
        // IMPORTANT: lock{} all usages!
        Pool<byte[]> pool;

        // constructor
        public MagnificentReceivePipe(int MaxMessageSize)
        {
            // initialize pool to create max message sized byte[]s each time
            pool = new Pool<byte[]>(() => new byte[MaxMessageSize]);
        }

        // set connected/disconnected flags
        public void SetConnected() { lock (this) { connectedFlag = true; } }
        public void SetDisconnected() { lock (this) { disconnectedFlag = true; } }

        // check & reset connected/disconnected flags
        // => immediately resets them so Tick() doesn't process (dis)connected
        //    multiple times!
        public bool CheckConnected()
        {
            lock (this)
            {
                bool result = connectedFlag;
                connectedFlag = false;
                return result;
            }
        }

        public bool CheckDisconnected()
        {
            lock (this)
            {
                bool result = disconnectedFlag;
                disconnectedFlag = false;
                return result;
            }
        }

        // for statistics. don't call Count and assume that it's the same after
        // the call.
        // NOTE: only counts data messages. doesn't count connected/disconnected
        public int Count
        {
            get { lock (this) { return queue.Count; } }
        }

        // byte[] pool count for testing
        public int PoolCount
        {
            get { lock (this) { return pool.Count(); } }
        }

        // enqueue a message
        // arraysegment for allocation free sends later.
        // -> the segment's array is only used until Enqueue() returns!
        public void Enqueue(ArraySegment<byte> message)
        {
            // pool & queue usage always needs to be locked
            lock (this)
            {
                // ArraySegment array is only valid until returning, so copy
                // it into a byte[] that we can queue safely.

                // get one from the pool first to avoid allocations
                byte[] bytes = pool.Take();

                // copy into it
                Buffer.BlockCopy(message.Array, message.Offset, bytes, 0, message.Count);

                // indicate which part is the message
                ArraySegment<byte> segment = new ArraySegment<byte>(bytes, 0, message.Count);

                // now enqueue it
                queue.Enqueue(segment);
            }
        }

        // peek the next message
        // -> allows the caller to process it while pipe still holds on to the
        //    byte[]
        // -> TryDequeue should be called after processing, so that the message
        //    is actually dequeued and the byte[] is returned to pool!
        // => see TryDequeue comments!
        public bool TryPeek(out ArraySegment<byte> data)
        {
            data = default;

            // pool & queue usage always needs to be locked
            lock (this)
            {
                if (queue.Count > 0)
                {
                    data = queue.Peek();
                    return true;
                }
                return false;
            }
        }

        // dequeue the next message
        // -> simply dequeues and returns the byte[] to pool (if any)
        // -> use Peek to actually process the first element while the pipe
        //    still holds on to the byte[]
        // -> doesn't return the element because the byte[] needs to be returned
        //    to the pool in dequeue. caller can't be allowed to work with a
        //    byte[] that is already returned to pool.
        // => Peek & Dequeue is the most simple, clean solution for receive
        //    pipe pooling to avoid allocations!
        public bool TryDequeue()
        {
            // pool & queue usage always needs to be locked
            lock (this)
            {
                if (queue.Count > 0)
                {
                    // dequeue and return byte[] to pool
                    pool.Return(queue.Dequeue().Array);
                    return true;
                }
                return false;
            }
        }

        public void Clear()
        {
            // pool & queue usage always needs to be locked
            lock (this)
            {
                // clear flags
                connectedFlag = false;
                disconnectedFlag = false;

                // clear queue, but via dequeue to return each byte[] to pool
                while (queue.Count > 0)
                {
                    // dequeue and return byte[] to pool (if any).
                    pool.Return(queue.Dequeue().Array);
                }
            }
        }
    }
}