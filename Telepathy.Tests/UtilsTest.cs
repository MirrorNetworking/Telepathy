using NUnit.Framework;

namespace Telepathy.Tests
{
    public class UtilsTest
    {
        [Test]
        public void IntToBytesBigNonAllocTest()
        {
            int number = 0x01020304;

            byte[] bytes = new byte[4];
            Utils.IntToBytesBigEndianNonAlloc(number, bytes);
            Assert.That(bytes[0], Is.EqualTo(0x01));
            Assert.That(bytes[1], Is.EqualTo(0x02));
            Assert.That(bytes[2], Is.EqualTo(0x03));
            Assert.That(bytes[3], Is.EqualTo(0x04));
        }

        [Test]
        public void IntToBytesBigNonAllocWithOffsetTest()
        {
            int number = 0x01020304;

            byte[] bytes = {0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00};
            Utils.IntToBytesBigEndianNonAlloc(number, bytes, 2);
            Assert.That(bytes[0], Is.EqualTo(0xFF));
            Assert.That(bytes[1], Is.EqualTo(0xFF));
            Assert.That(bytes[2], Is.EqualTo(0x01));
            Assert.That(bytes[3], Is.EqualTo(0x02));
            Assert.That(bytes[4], Is.EqualTo(0x03));
            Assert.That(bytes[5], Is.EqualTo(0x04));
        }
    }
}