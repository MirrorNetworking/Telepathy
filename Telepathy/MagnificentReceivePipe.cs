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
        // queue entry message. only used in here.
        struct Message
        {
            public int connectionId;
            public EventType eventType;
            public byte[] data;
            public Message(int connectionId, EventType eventType, byte[] data)
            {
                this.connectionId = connectionId;
                this.eventType = eventType;
                this.data = data;
            }
        }

        // message queue
        // ConcurrentQueue allocates. lock{} instead.
        readonly Queue<Message> queue = new Queue<Message>();

        // for statistics. don't call Count and assume that it's the same after
        // the call.
        public int Count
        {
            get { lock (this) { return queue.Count; } }
        }

        // enqueue a message
        // (parameters passed directly instead of using Message. this way we can
        //  pass ArraySegments and do pooling later)
        public void Enqueue(int connectionId, EventType eventType, byte[] data)
        {
            lock (this)
            {
                Message message = new Message(connectionId, eventType, data);
                queue.Enqueue(message);
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
                    Message message = queue.Dequeue();
                    connectionId = message.connectionId;
                    eventType = message.eventType;
                    data = message.data;
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