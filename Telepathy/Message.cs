// incoming message queue of <connectionId, message>
// (not a HashSet because one connection can have multiple new messages)
using System;

namespace Telepathy
{
    public struct Message : IDisposable
    {
        public int connectionId;
        public EventType eventType;
        public byte[] buffer;
        public ArraySegment<byte> segment;
        [Obsolete("Use segment instead, and Dispose messages.")]
        public byte[] data {
            get {
                var array = new byte[segment.Count - segment.Offset];
                Array.Copy(segment.Array, segment.Offset, array, 0, segment.Count);
                return array;
            }
        }

        public Message(int connectionId, EventType eventType, byte[] buffer, int size=0)
        {
            this.connectionId = connectionId;
            this.eventType = eventType;
            this.buffer = buffer;
            this.segment = new ArraySegment<byte>(this.buffer, 0, size);
        }

        public void Dispose() 
        {
            if(buffer != null)
                ByteArrayPool.Return(buffer);
        }
    }
}