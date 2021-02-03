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
        public void ConnectedFlag_SetCheckReset()
        {
            pipe.SetConnected();
            Assert.That(pipe.CheckConnected(), Is.True);
            Assert.That(pipe.CheckConnected(), Is.False);
        }

        [Test]
        public void DisconnectedFlag_SetCheckReset()
        {
            pipe.SetDisconnected();
            Assert.That(pipe.CheckDisconnected(), Is.True);
            Assert.That(pipe.CheckDisconnected(), Is.False);
        }

        [Test]
        public void Enqueue()
        {
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x42}));
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x43}));
            Assert.That(pipe.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryPeek()
        {
            ArraySegment<byte> message = new ArraySegment<byte>(new byte[]{0x42});
            pipe.Enqueue(message);
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryPeek(out ArraySegment<byte> peeked);
            Assert.That(result, Is.True);
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
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x42}));
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryDequeue();
            Assert.That(result, Is.True);
            Assert.That(pipe.Count, Is.EqualTo(0));
        }

        // messages should always be processed in the order they were received
        [Test]
        public void EnqueueDequeueRespectsOrder()
        {
            // enqueue
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x42}));
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x43}));

            // peek and dequeue first
            bool result = pipe.TryPeek(out ArraySegment<byte> message);
            Assert.That(result, Is.True);
            Assert.That(message.Count, Is.EqualTo(1));
            Assert.That(message.Array[0], Is.EqualTo(0x42));
            pipe.TryDequeue();

            // peek and dequeue second
            result = pipe.TryPeek(out message);
            Assert.That(result, Is.True);
            Assert.That(message.Count, Is.EqualTo(1));
            Assert.That(message.Array[0], Is.EqualTo(0x43));
            pipe.TryDequeue();
        }

        [Test]
        public void Clear()
        {
            // set flags and enqueue an element
            pipe.SetConnected();
            pipe.SetDisconnected();
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x42}));
            Assert.That(pipe.Count, Is.EqualTo(1));

            // all should be reset
            pipe.Clear();
            Assert.That(pipe.CheckConnected, Is.False);
            Assert.That(pipe.CheckDisconnected, Is.False);
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