using NUnit.Framework;

namespace Telepathy.Tests
{
    public class UtilsTest
    {
        [Test]
        public void IntToBytesBigNonAllocTest()
        {
            int number = 0x01020304;

            byte[] numberBytes = new byte[4];
            Utils.IntToBytesBigEndianNonAlloc(number, numberBytes);
            Assert.That(numberBytes[0], Is.EqualTo(0x01));
            Assert.That(numberBytes[1], Is.EqualTo(0x02));
            Assert.That(numberBytes[2], Is.EqualTo(0x03));
            Assert.That(numberBytes[3], Is.EqualTo(0x04));

            int converted = Utils.BytesToIntBigEndian(numberBytes);
            Assert.That(converted, Is.EqualTo(number));
        }

        [Test]
        public void IntToBytesBigNonAllocWithOffsetTest()
        {
            int number = 0x01020304;

            byte[] numberBytes = {0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00};
            Utils.IntToBytesBigEndianNonAlloc(number, numberBytes, 2);
            Assert.That(numberBytes[0], Is.EqualTo(0xFF));
            Assert.That(numberBytes[1], Is.EqualTo(0xFF));
            Assert.That(numberBytes[2], Is.EqualTo(0x01));
            Assert.That(numberBytes[3], Is.EqualTo(0x02));
            Assert.That(numberBytes[4], Is.EqualTo(0x03));
            Assert.That(numberBytes[5], Is.EqualTo(0x04));
        }
    }
}