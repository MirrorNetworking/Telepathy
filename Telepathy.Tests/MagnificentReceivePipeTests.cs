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
            pipe.Enqueue(EventType.Connected, default);
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Enqueue(EventType.Data, new ArraySegment<byte>(new byte[]{0x42}));
            Assert.That(pipe.Count, Is.EqualTo(2));

            pipe.Enqueue(EventType.Disconnected, default);
            Assert.That(pipe.Count, Is.EqualTo(3));
        }

        [Test]
        public void TryPeek()
        {
            ArraySegment<byte> message = new ArraySegment<byte>(new byte[]{0x42});
            pipe.Enqueue(EventType.Data, message);
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryPeek(out EventType eventType, out ArraySegment<byte> peeked);
            Assert.That(result, Is.True);
            Assert.That(eventType, Is.EqualTo(EventType.Data));
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
            pipe.Enqueue(EventType.Connected, default);
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryDequeue();
            Assert.That(result, Is.True);
            Assert.That(pipe.Count, Is.EqualTo(0));
        }

        // ReceivePipe only sets flags for connected/disconnected messages
        // make sure the order is still respected
        // -> connect should always happen first
        // -> data inbetween
        // -> disconnect last
        [Test]
        public void EnqueueDequeueRespectsOrder()
        {
            pipe.Enqueue(EventType.Connected, default);
            pipe.Enqueue(EventType.Data, new ArraySegment<byte>(new byte[]{0x42}));
            pipe.Enqueue(EventType.Disconnected, default);

            // peek and dequeue first
            bool result = pipe.TryPeek(out EventType eventType, out ArraySegment<byte> message);
            Assert.That(result, Is.True);
            Assert.That(eventType, Is.EqualTo(EventType.Connected));
            pipe.TryDequeue();

            // peek and dequeue second
            result = pipe.TryPeek(out eventType, out message);
            Assert.That(result, Is.True);
            Assert.That(eventType, Is.EqualTo(EventType.Data));
            pipe.TryDequeue();

            // peek and dequeue third
            result = pipe.TryPeek(out eventType, out message);
            Assert.That(result, Is.True);
            Assert.That(eventType, Is.EqualTo(EventType.Disconnected));
            pipe.TryDequeue();
        }

        [Test]
        public void Clear()
        {
            pipe.Enqueue(EventType.Connected, default);
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
            pipe.Enqueue(EventType.Data, new ArraySegment<byte>(new byte[]{0x1}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // dequeue. should return the byte[] to the pool
            pipe.TryDequeue();
            Assert.That(pipe.PoolCount, Is.EqualTo(1));

            // enqueue one. should use the pooled entry
            pipe.Enqueue(EventType.Data, new ArraySegment<byte>(new byte[]{0x2}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // enqueue another one. pool is empty so it should allocate a new byte[]
            pipe.Enqueue(EventType.Data, new ArraySegment<byte>(new byte[]{0x3}));
            Assert.That(pipe.PoolCount, Is.EqualTo(0));

            // clear. should return both to pool.
            pipe.Clear();
            Assert.That(pipe.PoolCount, Is.EqualTo(2));
        }
    }
}