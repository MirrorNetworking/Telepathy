using NUnit.Framework;

namespace Telepathy.Tests
{
    public class UtilsTest
    {
        [Test]
        public void IntToBytesBigTest()
        {
            int number = 0x01020304;

            byte[] numberBytes = Utils.IntToBytesBigEndian(number);
            Assert.That(numberBytes[0], Is.EqualTo(0x01));
            Assert.That(numberBytes[1], Is.EqualTo(0x02));
            Assert.That(numberBytes[2], Is.EqualTo(0x03));
            Assert.That(numberBytes[3], Is.EqualTo(0x04));

            int converted = Utils.BytesToIntBigEndian(numberBytes);
            Assert.That(converted, Is.EqualTo(number));
        }

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
    }
}