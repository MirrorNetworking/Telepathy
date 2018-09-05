// common code used by server and client
using System;
using System.Collections.Generic;

namespace Telepathy
{
    /// <summary>
    /// Byte array pool allows recycling of arrays in order to minimise memory allocation.
    /// </summary>
    public static class ByteArrayPool
    {
        [ThreadStatic] static readonly Queue<byte[]> byteArrayPool = new Queue<byte[]>();

        /// <summary>
        /// Take an array from the pool.
        /// </summary>
        /// <returns>The array.</returns>
        static public byte[] Take()
        {
            if (byteArrayPool.Count == 0)
            {
                return new byte[ushort.MaxValue];
            }
            return byteArrayPool.Dequeue();
        }

        /// <summary>
        /// Return an array to the pool.
        /// </summary>
        /// <param name="bytes">The array.</param>
        static public void Return(byte[] bytes)
        {
            if (bytes.Length != ushort.MaxValue)
            {
                throw new System.ArgumentException("incorrect length", nameof(bytes));

            }
            byteArrayPool.Enqueue(bytes);
        }

    }
}
