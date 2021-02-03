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
        bool connectReceived;
        bool disconnectReceived;

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

        // for statistics. don't call Count and assume that it's the same after
        // the call.
        public int Count
        {
            // connect and disconnect count as messages too
            get
            {
                lock (this)
                {
                    return queue.Count +
                           (connectReceived ? 1 : 0) +
                           (disconnectReceived ? 1 : 0);
                }
            }
        }

        // pool count for testing
        public int PoolCount
        {
            get { lock (this) { return pool.Count(); } }
        }

        // enqueue a message
        // -> ArraySegment to avoid allocations later
        // -> parameters passed directly so it's more obvious that we don't just
        //    queue a passed 'Message', instead we copy the ArraySegment into
        //    a byte[] and store it internally, etc.)
        public void Enqueue(EventType eventType, ArraySegment<byte> message)
        {
            // pool & queue usage always needs to be locked
            lock (this)
            {
                switch (eventType)
                {
                    case EventType.Connected:
                    {
                        connectReceived = true;
                        break;
                    }
                    case EventType.Data:
                    {
                        // ArraySegment is only valid until returning.
                        // copy it into a byte[] that we can store.
                        // ArraySegment array is only valid until returning, so copy
                        // it into a byte[] that we can queue safely.

                        // get one from the pool first to avoid allocations
                        byte[] bytes = pool.Take();

                        // copy into it
                        Buffer.BlockCopy(message.Array, message.Offset, bytes, 0, message.Count);

                        // indicate which part is the message
                        ArraySegment<byte> segment = new ArraySegment<byte>(bytes, 0, message.Count);

                        // enqueue it
                        // IMPORTANT: pass the segment around pool byte[],
                        // NOT the 'message' that is only valid until returning!
                        queue.Enqueue(segment);
                        break;
                    }
                    case EventType.Disconnected:
                    {
                        disconnectReceived = true;
                        break;
                    }
                }
            }
        }

        // peek the next message
        // -> allows the caller to process it while pipe still holds on to the
        //    byte[]
        // -> TryDequeue should be called after processing, so that the message
        //    is actually dequeued and the byte[] is returned to pool!
        // => see TryDequeue comments!
        public bool TryPeek(out EventType eventType, out ArraySegment<byte> data)
        {
            eventType = EventType.Disconnected;
            data = default;

            // pool & queue usage always needs to be locked
            lock (this)
            {
                // IMPORTANT: need to Peek the same order as Dequeue!

                // always process connect message first (if any), then reset
                if (connectReceived)
                {
                    eventType = EventType.Connected;
                    data = default;
                    // DO NOT RESET THE FLAG. peek simply peeks.
                    return true;
                }
                // process any data message after
                else if (queue.Count > 0)
                {
                    eventType = EventType.Data;
                    data = queue.Peek();
                    return true;
                }
                // always process disconnect last, then reset
                // => AFTER everything else. disconnect is always last. don't
                //    want to process any data messages after disconnect.
                else if (disconnectReceived)
                {
                    eventType = EventType.Disconnected;
                    data = default;
                    // DO NOT RESET THE FLAG. peek simply peeks.
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
                // IMPORTANT: need to Dequeue same order as Peek!

                // always process connect message first (if any)
                if (connectReceived)
                {
                    connectReceived = false;
                    return true;
                }
                // process any data message after
                else if (queue.Count > 0)
                {
                    // dequeue and return byte[] to pool
                    pool.Return(queue.Dequeue().Array);
                    return true;
                }
                // always process disconnect last, then reset
                // => AFTER everything else. disconnect is always last. don't
                //    want to process any data messages after disconnect.
                else if (disconnectReceived)
                {
                    disconnectReceived = false;
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
                connectReceived = false;
                disconnectReceived = false;

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