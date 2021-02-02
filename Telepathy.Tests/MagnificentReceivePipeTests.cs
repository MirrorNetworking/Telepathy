using NUnit.Framework;

namespace Telepathy.Tests
{
    public class MagnificentReceivePipeTests
    {
        MagnificentReceivePipe pipe;

        [SetUp]
        public void SetUp()
        {
            pipe = new MagnificentReceivePipe();
        }

        [Test]
        public void Enqueue()
        {
            pipe.Enqueue(0, EventType.Connected, null);
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Enqueue(0, EventType.Connected, null);
            Assert.That(pipe.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryDequeue()
        {
            pipe.Enqueue(42, EventType.Connected, null);
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryDequeue(out int connectionId, out EventType eventType, out byte[] data);
            Assert.That(result, Is.True);
            Assert.That(connectionId, Is.EqualTo(42));
            Assert.That(eventType, Is.EqualTo(EventType.Connected));
            Assert.That(data, Is.EqualTo(null));
        }

        [Test]
        public void Clear()
        {
            pipe.Enqueue(0, EventType.Connected, null);
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Clear();
            Assert.That(pipe.Count, Is.EqualTo(0));
        }
    }
}