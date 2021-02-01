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
            pipe.Enqueue(new Message());
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Enqueue(new Message());
            Assert.That(pipe.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryDequeue()
        {
            Message message = new Message(42, EventType.Connected, null);
            pipe.Enqueue(message);
            Assert.That(pipe.Count, Is.EqualTo(1));

            bool result = pipe.TryDequeue(out Message dequeued);
            Assert.That(result, Is.True);
            Assert.That(dequeued, Is.EqualTo(message));
        }

        [Test]
        public void Clear()
        {
            pipe.Enqueue(new Message());
            Assert.That(pipe.Count, Is.EqualTo(1));

            pipe.Clear();
            Assert.That(pipe.Count, Is.EqualTo(0));
        }
    }
}