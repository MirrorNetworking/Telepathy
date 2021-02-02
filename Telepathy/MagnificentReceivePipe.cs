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
        // queue entry message. only used in here.
        struct Entry
        {
            public int connectionId;
            public EventType eventType;
            public byte[] data;
            public Entry(int connectionId, EventType eventType, byte[] data)
            {
                this.connectionId = connectionId;
                this.eventType = eventType;
                this.data = data;
            }
        }

        // message queue
        // ConcurrentQueue allocates. lock{} instead.
        readonly Queue<Entry> queue = new Queue<Entry>();

        // for statistics. don't call Count and assume that it's the same after
        // the call.
        public int Count
        {
            get { lock (this) { return queue.Count; } }
        }

        // enqueue a message
        // -> ArraySegment to avoid allocations later
        // -> parameters passed directly so it's more obvious that we don't just
        //    queue a passed 'Message', instead we copy the ArraySegment into
        //    a byte[] and store it internally, etc.)
        public void Enqueue(int connectionId, EventType eventType, ArraySegment<byte> data)
        {
            lock (this)
            {
                // does this message have a data array content?
                byte[] bytes = null;
                if (data != default)
                {
                    // ArraySegment is only valid until returning.
                    // copy it into a byte[] that we can store.
                    // TODO pooling later
                    bytes = new byte[data.Count];
                    Buffer.BlockCopy(data.Array, data.Offset, bytes, 0, data.Count);
                }

                // queue it
                Entry entry = new Entry(connectionId, eventType, bytes);
                queue.Enqueue(entry);
            }
        }

        // dequeue a message
        // (parameters passed directly instead of using Message. this way we can
        //  pass ArraySegments and do pooling later)
        public bool TryDequeue(out int connectionId, out EventType eventType, out byte[] data)
        {
            connectionId = 0;
            eventType = EventType.Disconnected;
            data = null;

            lock (this)
            {
                if (queue.Count > 0)
                {
                    Entry entry = queue.Dequeue();
                    connectionId = entry.connectionId;
                    eventType = entry.eventType;
                    data = entry.data;
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