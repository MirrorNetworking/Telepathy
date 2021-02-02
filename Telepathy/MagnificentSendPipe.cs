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

        // send threads need to dequeue each byte[] and write it into the socket
        // -> dequeueing one byte[] after another works, but it's WAY slower
        //    than dequeueing all immediately (locks only once)
        //    lock{} & DequeueAll is WAY faster than ConcurrentQueue & dequeue
        //    one after another:
        //
        //      uMMORPG 450 CCU
        //        SafeQueue:       900-1440ms latency
        //        ConcurrentQueue:     2000ms latency
        //
        // -> the most obvious solution is to just return a list with all byte[]
        //    (which allocates) and then write each one into the socket
        // -> a faster solution is to serialize each one into one payload buffer
        //    and pass that to the socket only once. fewer socket calls always
        //    give WAY better CPU performance(!)
        // -> to avoid allocating a new list of entries each time, we simply
        //    serialize all entries into the payload here already
        // => having all this complexity built into the pipe makes testing and
        //    modifying the algorithm super easy!
        //
        // IMPORTANT: serializing in here will allow us to return the byte[]
        //            entries back to a pool later to completely avoid
        //            allocations!
        public bool DequeueAndSerializeAll(ref byte[] payload, out int packetSize)
        {
            lock (this)
            {
                // do nothing if empty
                packetSize = 0;
                if (queue.Count == 0)
                    return false;

                // we might have multiple pending messages. merge into one
                // packet to avoid TCP overheads and improve performance.
                //
                // IMPORTANT: Mirror & DOTSNET already batch into MaxMessageSize
                //            chunks, but we STILL pack all pending messages
                //            into one large payload so we only give it to TCP
                //            ONCE. This is HUGE for performance so we keep it!
                packetSize = 0;
                foreach (byte[] message in queue)
                    packetSize += 4 + message.Length; // header + content

                // create payload buffer if not created yet or previous one is
                // too small
                // IMPORTANT: payload.Length might be > packetSize! don't use it!
                if (payload == null || payload.Length < packetSize)
                    payload = new byte[packetSize];

                // dequeue all byte arrays and serialize into the packet
                int position = 0;
                while (queue.Count > 0)
                {
                    // dequeue
                    byte[] message = queue.Dequeue();

                    // write header (size) into buffer at position
                    Utils.IntToBytesBigEndianNonAlloc(message.Length, payload, position);
                    position += 4;

                    // copy message into payload at position
                    Buffer.BlockCopy(message, 0, payload, position, message.Length);
                    position += message.Length;
                }

                // we did serialize something
                return true;
            }
        }

        public void Clear()
        {
            lock (this) { queue.Clear(); }
        }
    }
}