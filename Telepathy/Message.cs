// incoming message queue of <connectionId, message>
// (not a HashSet because one connection can have multiple new messages)
using System;

namespace Telepathy
{
    public struct Message : IDisposable
    {
        public int connectionId;
        public EventType eventType;
        public ArraySegment<byte> data;
        public byte[] buffer;
        public int size;
        public Message(int connectionId, EventType eventType, byte[] buffer, int size)
        {
            this.connectionId = connectionId;
            this.eventType = eventType;
            this.buffer = buffer;
            this.size = size;
            this.data = new ArraySegment<byte>(this.buffer, 0, size);
        }

        public void Dispose() 
        {
            if(buffer != null)
                ByteArrayPool.Return(buffer);
        }
    }
}