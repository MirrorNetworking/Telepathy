using System;
using NUnit.Framework;

namespace Telepathy.Tests
{
    public class MagnificentReceivePipeTests
    {
        const int MaxMessageSize = 64;
        MagnificentReceivePipe pipe;

        [SetUp]
        public void SetUp()
        {
            pipe = new MagnificentReceivePipe(MaxMessageSize);
        }

        [Test]
        public void Enqueue()
        {
            pipe.Enqueue(0, EventType.Connected, default);
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Enqueue(0, EventType.Connected, default);
            Assert.That(pipe.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryPeek()
        {
            ArraySegment<byte> message = new ArraySegment<byte>(new byte[]{0x42});
            pipe.Enqueue(42, EventType.Connected, message);
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryPeek(out int connectionId, out EventType eventType, out ArraySegment<byte> peeked);
            Assert.That(result, Is.True);
            Assert.That(connectionId, Is.EqualTo(42));
            Assert.That(eventType, Is.EqualTo(EventType.Connected));
            Assert.That(peeked.Offset, Is.EqualTo(message.Offset));
            Assert.That(peeked.Count, Is.EqualTo(message.Count));
            for (int i = 0; i < message.Count; ++i)
                Assert.That(peeked.Array[peeked.Offset + i], Is.EqualTo(message.Array[message.Offset + i]));

            // peek shouldn't remove anything
            Assert.That(pipe.Count, Is.EqualTo(1));
        }

        [Test]
        public void TryDequeue()
        {
            pipe.Enqueue(42, EventType.Connected, default);
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryDequeue();
            Assert.That(result, Is.True);
            Assert.That(pipe.Count, Is.EqualTo(0));
        }

        [Test]
        public void Clear()
        {
            pipe.Enqueue(0, EventType.Connected, default);
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
            pipe.Enqueue(42, EventType.Data, new ArraySegment<byte>(new byte[]{0x1}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // dequeue. should return the byte[] to the pool
            pipe.TryDequeue();
            Assert.That(pipe.PoolCount, Is.EqualTo(1));

            // enqueue one. should use the pooled entry
            pipe.Enqueue(42, EventType.Data, new ArraySegment<byte>(new byte[]{0x2}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // enqueue another one. pool is empty so it should allocate a new byte[]
            pipe.Enqueue(42, EventType.Data, new ArraySegment<byte>(new byte[]{0x3}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // clear. should return both to pool.
            pipe.Clear();
            Assert.That(pipe.PoolCount, Is.EqualTo(2));
        }
    }
}