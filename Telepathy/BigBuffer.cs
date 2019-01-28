// SocketAsyncEventArgs work best if all the receive args use a piece of one
// giant buffer.
// TODO find the source for that again.
//
// -> significant difference in load test:
//    - without BigBuffer: 225KB/s in
//    - with BigBuffer: 300-600KB/s in

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;

namespace Telepathy
{
    public class BigBuffer
    {
        // size per chunk. which is the buffer per SocketAsyncEventArg.
        // -> if we want to use BigBuffer for sends too, then we need to make
        //    the chunks big enough for max message size that we want to send.
        // -> this limits sending (can't send 2GB packets etc.), but greatly
        //    improves performance and reduces allocations.
        // -> PUBLIC in case someone needs to check max message size.
        public const int ChunkSize = 2048;

        // the big buffer. allocate enough chunks for 10k connections, so on
        // average it's 1 send + 1 recv chunk for 10k connections = 20k chunks.
        // -> no Unity game will run with >10k connections anyway.
        // -> chunk size seems to affect performance. 10k*2k works well.
        const int ChunkAmount = 100000;
        byte[] buffer = new byte[ChunkSize * ChunkAmount];

        // free chunks - by offset. threadsafe.
        // -> contains all the indices initially. e.g. 0, 1024, 2048, ...
        ConcurrentQueue<int> freeOffsets = new ConcurrentQueue<int>(Enumerable.Range(0, ChunkAmount).Select(idx => idx * ChunkSize));

        // assign a SocketAsyncEventArg's buffer without copying it to a return
        // value etc.
        public bool Assign(SocketAsyncEventArgs args)
        {
            // get free index from the queue
            int offset;
            if (freeOffsets.TryDequeue(out offset))
            {
                // assign to args
                args.SetBuffer(buffer, offset, ChunkSize);
                return true;
            }

            // no more indices left. should have allocated a bigger buffer.
            Logger.LogError("BigBuffer.Assign: out of free indices. Should've allocated more than " + ChunkAmount + ".");
            return false;
        }

        // assign a SocketAsyncEventArg's buffer without copying it to a return
        // value etc.
        //  -> can pass a byte[] to copy into the buffer for Send.
        public bool Assign(SocketAsyncEventArgs args, byte[] header, byte[] content)
        {
            // get free index from the queue
            int offset;
            if (freeOffsets.TryDequeue(out offset))
            {
                // does it fit into a chunk?
                int totalLength = header.Length + content.Length;
                if (totalLength <= ChunkSize)
                {
                    // copy it in
                    Array.Copy(header, 0, buffer, offset, header.Length);
                    Array.Copy(content, 0, buffer, offset + header.Length, content.Length);

                    // assign to args with length of copyIntoBuffer so we don't
                    // send more
                    args.SetBuffer(buffer, offset, totalLength);
                    return true;
                }
                else
                {
                    // log error and full message so we know which message
                    // was too big (should see the offset)
                    Logger.LogError("BigBuffer.Assign: can't copy " + totalLength + " bytes into chunk of max size " + ChunkSize + " for header=" + BitConverter.ToString(header) + " content=" + BitConverter.ToString(content));
                    return false;
                }
            }

            // no more indices left. should have allocated a bigger buffer.
            Logger.LogError("BigBuffer.Assign: out of free indices. Should've allocated more than " + ChunkAmount + ".");
            return false;
        }

        // free a SocketAsyncEventArg's chunk
        public void Free(SocketAsyncEventArgs args)
        {
            // free whatever offset we used
            freeOffsets.Enqueue(args.Offset);

            // clear it, just to be 100% sure
            args.SetBuffer(null, 0, 0);

            // helper
            if (freeOffsets.Count % 1000 == 0)
                Logger.Log("BigBuffer free indices: " + freeOffsets.Count);
        }
    }
}