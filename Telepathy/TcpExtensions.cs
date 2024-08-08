using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Telepathy
{
    public static class TcpExtensions
    {
        /// <summary>
        ///     Send
        /// </summary>
        /// <param name="stream">Flow</param>
        /// <param name="sendBufferSize">Send buffer size</param>
        /// <param name="sendBuffer">Send buffer</param>
        /// <param name="headerBuffer">Header buffer</param>
        /// <param name="outgoing">Outgoing</param>
        /// <param name="pool">Pool</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RawWrite(this NetworkStream stream, int sendBufferSize, byte[] sendBuffer, byte[] headerBuffer, Queue<ArraySegment<byte>> outgoing, Pool<byte[]> pool)
        {
            var count = 0;
            while (outgoing.Count > 0)
            {
                ArraySegment<byte> payload;
                lock (outgoing)
                {
                    payload = outgoing.Dequeue();
                }

                try
                {
                    var size = payload.Count;
                    var newCount = count + 4;
                    if (newCount < sendBufferSize)
                    {
                        FastBuffer.Write(sendBuffer, count, size);
                        count += 4;
                    }
                    else if (newCount > sendBufferSize)
                    {
                        FastBuffer.Write(headerBuffer, size);
                        var overflow1 = newCount - sendBufferSize;
                        var remaining1 = 4 - overflow1;
                        FastBuffer.BlockCopy(headerBuffer, 0, sendBuffer, count, remaining1);
                        stream.Write(sendBuffer, 0, sendBufferSize);
                        FastBuffer.BlockCopy(headerBuffer, remaining1, sendBuffer, 0, overflow1);
                        count = overflow1;
                    }
                    else
                    {
                        FastBuffer.Write(sendBuffer, count, size);
                        stream.Write(sendBuffer, 0, sendBufferSize);
                        count = 0;
                    }

                    newCount = count + size;
                    if (newCount < sendBufferSize)
                    {
                        FastBuffer.BlockCopy(in payload, 0, sendBuffer, count, size);
                        count += size;
                    }
                    else if (newCount > sendBufferSize)
                    {
                        var overflow = newCount - sendBufferSize;
                        var remaining = size - overflow;
                        FastBuffer.BlockCopy(in payload, 0, sendBuffer, count, remaining);
                        stream.Write(sendBuffer, 0, sendBufferSize);
                        FastBuffer.BlockCopy(in payload, remaining, sendBuffer, 0, overflow);
                        count = overflow;
                    }
                    else
                    {
                        FastBuffer.BlockCopy(in payload, 0, sendBuffer, count, size);
                        stream.Write(sendBuffer, 0, sendBufferSize);
                        count = 0;
                    }
                }
                finally
                {
                    lock (pool)
                    {
                        pool.Return(payload.Array);
                    }
                }
            }

            if (count == 0)
                return;
            stream.Write(sendBuffer, 0, count);
        }
    }

    public static class FastBuffer
    {
        /// <summary>
        ///     Write int value
        /// </summary>
        /// <param name="destination">Destination</param>
        /// <param name="value">Value</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(byte[] destination, int value) => Utils.IntToBytesBigEndianNonAlloc(value, destination);

        /// <summary>
        ///     Write int value
        /// </summary>
        /// <param name="destination">Destination</param>
        /// <param name="offset">Offset</param>
        /// <param name="value">Value</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(byte[] destination, int offset, int value) => Utils.IntToBytesBigEndianNonAlloc(value, destination, offset);

        /// <summary>
        ///     Copy bytes
        /// </summary>
        /// <param name="src">Source</param>
        /// <param name="srcOffset">Source offset</param>
        /// <param name="dst">Destination</param>
        /// <param name="dstOffset">Destination offset</param>
        /// <param name="count">Count</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlockCopy(byte[] src, int srcOffset, byte[] dst, int dstOffset, int count) => Buffer.BlockCopy(src, srcOffset, dst, dstOffset, count);

        /// <summary>
        ///     Copy bytes
        /// </summary>
        /// <param name="src">Source</param>
        /// <param name="srcOffset">Source offset</param>
        /// <param name="dst">Destination</param>
        /// <param name="dstOffset">Destination offset</param>
        /// <param name="count">Count</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlockCopy(byte[] src, int srcOffset, in ArraySegment<byte> dst, int dstOffset, int count) => BlockCopy(src, srcOffset, dst.Array, dst.Offset + dstOffset, count);

        /// <summary>
        ///     Copy bytes
        /// </summary>
        /// <param name="src">Source</param>
        /// <param name="srcOffset">Source offset</param>
        /// <param name="dst">Destination</param>
        /// <param name="dstOffset">Destination offset</param>
        /// <param name="count">Count</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlockCopy(in ArraySegment<byte> src, int srcOffset, byte[] dst, int dstOffset, int count) => BlockCopy(src.Array, src.Offset + srcOffset, dst, dstOffset, count);

        /// <summary>
        ///     Copy bytes
        /// </summary>
        /// <param name="src">Source</param>
        /// <param name="srcOffset">Source offset</param>
        /// <param name="dst">Destination</param>
        /// <param name="dstOffset">Destination offset</param>
        /// <param name="count">Count</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BlockCopy(in ArraySegment<byte> src, int srcOffset, in ArraySegment<byte> dst, int dstOffset, int count) => BlockCopy(src.Array, src.Offset + srcOffset, dst.Array, dst.Offset + dstOffset, count);
    }
}