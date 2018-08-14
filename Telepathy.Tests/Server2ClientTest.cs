using NUnit.Framework;
using System;
using System.Threading;

namespace Telepathy.Tests
{
    [TestFixture]
    public class Server2ClientTest
    {
        // just a random port that will hopefully not be taken
        private const int port = 9587;

        Server server;
        Client client;


        [Test]
        public void TestServer()
        {

            Server server = new Server();
            server.Start(port);

            Encoding utf8 = Encoding.UTF8;

            Client client = new Client();

            client.Connect("127.0.0.1", port);

            // we  should first receive a connected message
            Message connectMsg = NextMessage(client);
            Assert.That(connectMsg.eventType, Is.EqualTo(EventType.Connected));

            uint id = connectMsg.connectionId;

            // then we should receive the data
            server.Send(id, utf8.GetBytes("Hello world"));
            Message dataMsg = NextMessage(client);
            Assert.That(dataMsg.eventType, Is.EqualTo(EventType.Data));
            string str = utf8.GetString(dataMsg.data);
            Assert.That(str, Is.EqualTo("Hello world"));

            // finally when the client disconnect,  we should get a disconnected message
            client.Disconnect();
            Message disconnectMsg = NextMessage(client);
            Assert.That(disconnectMsg.eventType, Is.EqualTo(EventType.Disconnected));



            server.Stop();
        }


        private static Message NextMessage(Client client)
        {
            Message message;
            int count = 0;

            while (!client.GetNextMessage(out message))
            {
                count++;
                Thread.Sleep(100);

                if (count >= 100)
                {
                    Assert.Fail("The message did not get to the server");
                }
            }

            return message;
        }
    }
}
