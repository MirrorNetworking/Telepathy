using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Telepathy.Tests
{
    public class MagnificentSendPipeTests
    {
        MagnificentSendPipe pipe;

        [SetUp]
        public void SetUp()
        {
            pipe = new MagnificentSendPipe();
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
        public void DequeueAll()
        {
            // enqueue two
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x1}));
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x2}));

            // create the helper list and add an old value to make sure
            // DequeueAll clears it before dequeuing
            List<byte[]> list = new List<byte[]>{new byte[]{0xFF}};

            // dequeue both
            bool result = pipe.DequeueAll(list);
            Assert.That(result, Is.True);
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list[0][0], Is.EqualTo(0x1));
            Assert.That(list[1][0], Is.EqualTo(0x2));

            // pipe should be empty now
            Assert.That(pipe.Count, Is.EqualTo(0));
        }

        [Test]
        public void Clear()
        {
            pipe.Enqueue(new ArraySegment<byte>(new byte[]{0x1}));
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Clear();
            Assert.That(pipe.Count, Is.EqualTo(0));
        }
    }
}