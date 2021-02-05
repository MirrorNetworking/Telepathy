using System;
using NUnit.Framework;

namespace Telepathy.Tests
{
    public class MagnificentSendPipeTests
    {
        const int MaxMessageSize = 64;
        MagnificentSendPipe pipe;

        [SetUp]
        public void SetUp()
        {
            pipe = new MagnificentSendPipe(MaxMessageSize);
        }

        [Test]
        public void Enqueue()
        {
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x1}));
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x2}));
            Assert.That(pipe.Count, Is.EqualTo(2));
        }

        [Test]
        public void Clear()
        {
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x1}));
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Clear();
            Assert.That(pipe.Count, Is.EqualTo(0));
        }

        // make sure pooling works as intended
        [Test]
        public void Pooling()
        {
            // pool should be empty first
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // enqueue one. pool is empty so it should allocate a new byte[]
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x1}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // dequeue. should return the byte[] to the pool
            pipe.TryDequeue();
            Assert.That(pipe.PoolCount, Is.EqualTo(1));

            // enqueue one. should use the pooled entry
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x2}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // enqueue another one. pool is empty so it should allocate a new byte[]
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x3}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // clear. should return both to pool.
            pipe.Clear();
            Assert.That(pipe.PoolCount, Is.EqualTo(2));
        }
    }
}