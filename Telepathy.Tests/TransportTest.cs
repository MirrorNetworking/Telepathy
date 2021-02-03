using System;
using System.Collections.Generic;
using NUnit.Framework;
using System.Text;
using System.Threading;

namespace Telepathy.Tests
{
    // message struct to queue and test events more easily
    struct Message
    {
        public int connectionId;
        public EventType eventType;
        public byte[] data;
        public Message(int connectionId, EventType eventType, byte[] data)
        {
            this.connectionId = connectionId;
            this.eventType = eventType;
            this.data = data;
        }
    }

    [TestFixture]
    public class TransportTest
    {
        // just a random port that will hopefully not be taken
        const int port = 9587;
        const int MaxMessageSize = 16 * 1024;

        Server server;

        [SetUp]
        public void Setup()
        {
            server = new Server(MaxMessageSize);
            server.Start(port);
        }

        [TearDown]
        public void TearDown()
        {
            server.Stop();
            serverMessages.Clear();
            clientMessages.Clear();
        }

        [Test]
        public void NextConnectionIdTest()
        {
            // it should always start at '1', because '0' is reserved for
            // Mirror's local player
            int id = server.NextConnectionId();
            Assert.That(id, Is.EqualTo(1));
        }

        [Test]
        public void DisconnectImmediateTest()
        {
            Client client = new Client(MaxMessageSize);
            client.Connect("127.0.0.1", port);

            // I should be able to disconnect right away
            // if connection was pending,  it should just cancel
            client.Disconnect();

            Assert.That(client.Connected, Is.False);
            Assert.That(client.Connecting, Is.False);
        }

        [Test, Ignore("flaky")]
        public void SpamConnectTest()
        {
            Client client = new Client(MaxMessageSize);
            for (int i = 0; i < 1000; i++)
            {
                client.Connect("127.0.0.1", port);
                Assert.That(client.Connecting || client.Connected, Is.True);
                client.Disconnect();
                Assert.That(client.Connected, Is.False);
                Assert.That(client.Connecting, Is.False);
            }
        }

        [Test]
        public void SpamSendTest()
        {
            // BeginSend can't be called again after previous one finished. try
            // to trigger that case.
            Client client = new Client(MaxMessageSize);
            client.Connect("127.0.0.1", port);

            // wait for successful connection
            Message connectMsg = NextMessage(client);
            Assert.That(connectMsg.eventType, Is.EqualTo(EventType.Connected));
            Assert.That(client.Connected, Is.True);

            byte[] data = new byte[99999];
            for (int i = 0; i < 1000; i++)
            {
                client.Send(new ArraySegment<byte>(data));
            }

            client.Disconnect();
            Assert.That(client.Connected, Is.False);
            Assert.That(client.Connecting, Is.False);
        }

        [Test, Ignore("flaky")]
        public void ReconnectTest()
        {
            Client client = new Client(MaxMessageSize);
            client.Connect("127.0.0.1", port);

            // wait for successful connection
            Message connectMsg = NextMessage(client);
            Assert.That(connectMsg.eventType, Is.EqualTo(EventType.Connected));
            // disconnect and lets try again
            client.Disconnect();
            Assert.That(client.Connected, Is.False);
            Assert.That(client.Connecting, Is.False);

            // connecting should flush message queue  right?
            client.Connect("127.0.0.1", port);
            // wait for successful connection
            connectMsg = NextMessage(client);
            Assert.That(connectMsg.eventType, Is.EqualTo(EventType.Connected));
            client.Disconnect();
            Assert.That(client.Connected, Is.False);
            Assert.That(client.Connecting, Is.False);
        }

        [Test]
        public void ServerTest()
        {
            Encoding utf8 = Encoding.UTF8;
            Client client = new Client(MaxMessageSize);

            client.Connect("127.0.0.1", port);

            // we should first receive a connected message
            Message connectMsg = NextMessage(server);
            Assert.That(connectMsg.eventType, Is.EqualTo(EventType.Connected));

            // then we should receive the data
            byte[] bytes = utf8.GetBytes("Hello world");
            client.Send(new ArraySegment<byte>(bytes));
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
            Client client = new Client(MaxMessageSize);

            client.Connect("127.0.0.1", port);

            // we  should first receive a connected message
            Message serverConnectMsg = NextMessage(server);
            int id = serverConnectMsg.connectionId;

            // we  should first receive a connected message
            Message clientConnectMsg = NextMessage(client);
            Assert.That(serverConnectMsg.eventType, Is.EqualTo(EventType.Connected));

            // Send some data to the client
            byte[] bytes = utf8.GetBytes("Hello world");
            server.Send(id, new ArraySegment<byte>(bytes));
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
        public void ServerDisconnectClientTest()
        {
            Client client = new Client(MaxMessageSize);

            client.Connect("127.0.0.1", port);

            // we  should first receive a connected message
            Message serverConnectMsg = NextMessage(server);
            int id = serverConnectMsg.connectionId;

            bool result = server.Disconnect(id);
            Assert.That(result, Is.True);
        }

        [Test]
        public void ClientKickedCleanupTest()
        {
            Client client = new Client(MaxMessageSize);

            client.Connect("127.0.0.1", port);

            // read connected message on client
            Message clientConnectedMsg = NextMessage(client);
            Assert.That(clientConnectedMsg.eventType, Is.EqualTo(EventType.Connected));

            // read connected message on server
            Message serverConnectMsg = NextMessage(server);
            int id = serverConnectMsg.connectionId;

            // server kicks the client
            bool result = server.Disconnect(id);
            Assert.That(result, Is.True);

            // wait for client disconnected message
            Message clientDisconnectedMsg = NextMessage(client);
            Assert.That(clientDisconnectedMsg.eventType, Is.EqualTo(EventType.Disconnected));

            // was everything cleaned perfectly?
            // if Connecting or Connected is still true then we wouldn't be able
            // to reconnect otherwise
            Assert.That(client.Connecting, Is.False);
            Assert.That(client.Connected, Is.False);
        }

        [Test]
        public void GetConnectionInfoTest()
        {
            // connect a client
            Client client = new Client(MaxMessageSize);
            client.Connect("127.0.0.1", port);

            // get server's connect message
            Message serverConnectMsg = NextMessage(server);
            Assert.That(serverConnectMsg.eventType, Is.EqualTo(EventType.Connected));

            // get server's connection info for that client
            string address = server.GetClientAddress(serverConnectMsg.connectionId);
            Assert.That(address == "127.0.0.1" || address == "::ffff:127.0.0.1");

            client.Disconnect();
        }

        // all implementations should be able to handle 'localhost' as IP too
        [Test]
        public void ParseLocalHostTest()
        {
            // connect a client
            Client client = new Client(MaxMessageSize);
            client.Connect("localhost", port);

            // get server's connect message
            Message serverConnectMsg = NextMessage(server);
            Assert.That(serverConnectMsg.eventType, Is.EqualTo(EventType.Connected));

            client.Disconnect();
        }

        // IPv4 needs to work
        [Test]
        public void ConnectIPv4Test()
        {
            // connect a client
            Client client = new Client(MaxMessageSize);
            client.Connect("127.0.0.1", port);

            // get server's connect message
            Message serverConnectMsg = NextMessage(server);
            Assert.That(serverConnectMsg.eventType, Is.EqualTo(EventType.Connected));

            client.Disconnect();
        }

        // IPv6 needs to work
        [Test]
        public void ConnectIPv6Test()
        {
            // connect a client
            Client client = new Client(MaxMessageSize);
            client.Connect("::ffff:127.0.0.1", port);

            // get server's connect message
            Message serverConnectMsg = NextMessage(server);
            Assert.That(serverConnectMsg.eventType, Is.EqualTo(EventType.Connected));

            client.Disconnect();
        }

        [Test]
        public void LargeMessageTest()
        {
            // connect a client
            Client client = new Client(MaxMessageSize);
            client.Connect("127.0.0.1", port);

            // we should first receive a connected message
            Message serverConnectMsg = NextMessage(server);
            int id = serverConnectMsg.connectionId;

            // Send largest allowed message
            byte[] bytes = new byte[server.MaxMessageSize];
            bool sent = client.Send(new ArraySegment<byte>(bytes));
            Assert.That(sent, Is.EqualTo(true));
            Message dataMsg = NextMessage(server);
            Assert.That(dataMsg.eventType, Is.EqualTo(EventType.Data));
            Assert.That(dataMsg.data.Length, Is.EqualTo(server.MaxMessageSize));

            // finally if the server stops,  the clients should get a disconnect error
            server.Stop();
            client.Disconnect();
        }

        [Test, Ignore("flaky")]
        public void AllocationAttackTest()
        {
            // connect a client
            // and allow client to send large message
            int attackSize = MaxMessageSize * 2;
            Client client = new Client(attackSize);
            client.Connect("127.0.0.1", port);

            // we should first receive a connected message
            Message serverConnectMsg = NextMessage(server);
            int id = serverConnectMsg.connectionId;

            // Send a large message, bigger thank max message size
            // -> this should disconnect the client
            byte[] bytes = new byte[attackSize];
            bool sent = client.Send(new ArraySegment<byte>(bytes));
            Assert.That(sent, Is.EqualTo(true));
            Message dataMsg = NextMessage(server);
            Assert.That(dataMsg.eventType, Is.EqualTo(EventType.Disconnected));

            // finally if the server stops,  the clients should get a disconnect error
            server.Stop();
            client.Disconnect();
        }

        [Test]
        public void ServerStartStopTest()
        {
            // create a server that only starts and stops without ever accepting
            // a connection
            Server sv = new Server(MaxMessageSize);
            Assert.That(sv.Start(port + 1), Is.EqualTo(true));
            Assert.That(sv.Active, Is.EqualTo(true));
            sv.Stop();
            Assert.That(sv.Active, Is.EqualTo(false));
        }

        [Test]
        public void ServerStartStopRepeatedTest()
        {
            // can we start/stop on the same port repeatedly?
            Server sv = new Server(MaxMessageSize);
            for (int i = 0; i < 10; ++i)
            {
                Assert.That(sv.Start(port + 1), Is.EqualTo(true));
                Assert.That(sv.Active, Is.EqualTo(true));
                sv.Stop();
                Assert.That(sv.Active, Is.EqualTo(false));
            }
        }

        [Test]
        public void ClientTickRespectsLimit()
        {
            // create & connect client
            Client client = new Client(MaxMessageSize);
            client.Connect("127.0.0.1", port);

            // eat server connected message
            Message serverConnectMsg = NextMessage(server);
            int id = serverConnectMsg.connectionId;

            // eat client connected message
            Message clientConnectMsg = NextMessage(client);
            Assert.That(serverConnectMsg.eventType, Is.EqualTo(EventType.Connected));

            // send 3 messages to the client
            server.Send(id, new ArraySegment<byte>(new byte[]{0x01}));
            server.Send(id, new ArraySegment<byte>(new byte[]{0x02}));
            server.Send(id, new ArraySegment<byte>(new byte[]{0x03}));

            // give it enough time to go over the thread -> network -> thread
            // until all 3 have DEFINITELY arrived
            Thread.Sleep(1000);

            // hook up to OnData
            // (need to do it before calling Tick because NextMessage(client)
            //  always overwrites it)
            int processed = 0;
            client.OnData = segment => ++processed;

            // process up to two messages
            client.Tick(2);
            Assert.That(processed, Is.EqualTo(2));

            // process the last one (pass a high limit just to see what happens)
            client.Tick(999);
            Assert.That(processed, Is.EqualTo(3));

            // cleanup
            client.Disconnect();
        }

        // Tick() might process more than one message, so we need to keep a list
        // and always return the next one in NextMessage. don't want to skip any.
        static Queue<Message> serverMessages = new Queue<Message>();
        static Message NextMessage(Server server)
        {
            // any remaining messages from last tick?
            if (serverMessages.Count > 0)
                return serverMessages.Dequeue();

            // otherwise tick for up to 10s

            // setup the events before we call tick
            // (they are only used in Tick, so it's fine if we just set them
            //  up before calling Tick)
            server.OnConnected = connectionId => { serverMessages.Enqueue(new Message(connectionId, EventType.Connected, null)); };
            server.OnData = (connectionId, data) => {
                // ArraySegment.Array is only available until returning. copy it
                // so we can return the content for tests.
                byte[] copy = new byte[data.Count];
                Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
                serverMessages.Enqueue(new Message(connectionId, EventType.Data, copy));
            };
            server.OnDisconnected = connectionId => { serverMessages.Enqueue(new Message(connectionId, EventType.Disconnected, null)); };

            // try tick for 10s until we receive a new message
            int count = 0;
            while (count < 100)
            {
                server.Tick();
                if (serverMessages.Count > 0)
                {
                    return serverMessages.Dequeue();
                }
                else
                {
                    count++;
                    Thread.Sleep(100);
                }
            }
            Assert.Fail("The message did not get to the server");
            return default;
        }

        // Tick() might process more than one message, so we need to keep a list
        // and always return the next one in NextMessage. don't want to skip any.
        static Queue<Message> clientMessages = new Queue<Message>();
        static Message NextMessage(Client client)
        {
            // any remaining messages from last tick?
            if (clientMessages.Count > 0)
                return clientMessages.Dequeue();

            // setup the events before we call tick
            // (they are only used in Tick, so it's fine if we just set them
            //  up before calling Tick)
            client.OnConnected = () => { clientMessages.Enqueue(new Message(0, EventType.Connected, null)); };
            client.OnData = (data) => {
                // ArraySegment.Array is only available until returning. copy it
                // so we can return the content for tests.
                byte[] copy = new byte[data.Count];
                Buffer.BlockCopy(data.Array, data.Offset, copy, 0, data.Count);
                clientMessages.Enqueue(new Message(0, EventType.Data, copy));
            };
            client.OnDisconnected = () => { clientMessages.Enqueue(new Message(0, EventType.Disconnected, null)); };

            // try tick for 10s until we receive a new message
            int count = 0;
            while (count < 100)
            {
                client.Tick(1);
                if (clientMessages.Count > 0)
                {
                    return clientMessages.Dequeue();
                }
                else
                {
                    count++;
                    Thread.Sleep(100);
                }
            }
            Assert.Fail("The message did not get to the client");
            return default;
        }

    }
}
