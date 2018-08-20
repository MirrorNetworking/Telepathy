using NUnit.Framework;
using System;
using System.Text;
using System.Threading;

namespace Telepathy.Tests
{
    [TestFixture]
    public class TransportTest
    {
        // just a random port that will hopefully not be taken
        const int port = 9587;

        Server server;

        [SetUp]
        public void Setup()
        {
            server = new Server();
            server.Start(port);

        }

        [TearDown]
        public void TearDown()
        {
            server.Stop();
        }

        [Test]
        public void ServerTest()
        {
            Encoding utf8 = Encoding.UTF8;
            Client client = new Client();

            client.Connect("127.0.0.1", port);

            // we  should first receive a connected message
            Message connectMsg = NextMessage(server);
            Assert.That(connectMsg.eventType, Is.EqualTo(EventType.Connected));


            // then we should receive the data
            client.Send(utf8.GetBytes("Hello world"));
            Message dataMsg = NextMessage(server);
            Assert.That(dataMsg.eventType, Is.EqualTo(EventType.Data));
            string str = utf8.GetString(dataMsg.data);
            Assert.That(str, Is.EqualTo("Hello world"));

            // finally when the client disconnect,  we should get a disconnected message
            client.Disconnect();
            Message disconnectMsg = NextMessage(server);
            Assert.That(disconnectMsg.eventType, Is.EqualTo(EventType.Disconnected));
        }

        [Test]
        public void ClientTest()
        {
            Encoding utf8 = Encoding.UTF8;
            Client client = new Client();

            client.Connect("127.0.0.1", port);

            // we  should first receive a connected message
            Message serverConnectMsg = NextMessage(server);
            int id = serverConnectMsg.connectionId;

            // we  should first receive a connected message
            Message clientConnectMsg = NextMessage(client);
            Assert.That(serverConnectMsg.eventType, Is.EqualTo(EventType.Connected));

            // Send some data to the client
            server.Send(id, utf8.GetBytes("Hello world"));
            Message dataMsg = NextMessage(client);
            Assert.That(dataMsg.eventType, Is.EqualTo(EventType.Data));
            string str = utf8.GetString(dataMsg.data);
            Assert.That(str, Is.EqualTo("Hello world"));

            // finally if the server stops,  the clients should get a disconnect error
            server.Stop();
            Message disconnectMsg = NextMessage(client);
            Assert.That(disconnectMsg.eventType, Is.EqualTo(EventType.Disconnected));

            client.Disconnect();
        }

        [Test]
        public void ConnectingTest()
        {
            Client client = new Client();

            // connect
            client.Connect("127.0.0.1", port);

            // we started a connect, so 'Connecting' should be true by
            // definition
            // (useful test because Connect's internal thread will take a while
            //  before it starts. this test makes sure that Connecting is true
            //  even before the thread started)
            Assert.That(client.Connecting, Is.EqualTo(true));
            client.Disconnect();
        }

        static Message NextMessage(Server server)
        {
            Message message;
            int count = 0;

            while (!server.GetNextMessage(out message))
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

        static Message NextMessage(Client client)
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
