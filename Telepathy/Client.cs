using System;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : Common
    {
        TcpClient client = new TcpClient();
        Thread thread;

        public bool Connecting
        {
            get { return thread != null && thread.IsAlive && !client.Connected; }
        }

        public bool Connected
        {
            get { return thread != null && thread.IsAlive && client.Connected; }
        }

        public void Connect(string ip, int port)
        {
            // not if already started
            if (Connecting || Connected) return;

            // client.Connect(ip, port) is blocking. let's call it in the thread and return immediately.
            // -> this way the application doesn't hang for 30s if connect takes too long, which is especially good
            //    in games
            // -> this way we don't async client.BeginConnect, which seems to fail sometimes if we connect too many
            //    clients too fast
            thread = new Thread(() =>
            {
                try
                {
                    // connect (blocking)
                    client.Connect(ip, port);

                    // run the receive loop
                    ReceiveLoop(messageQueue, 0, client);
                }
                catch (SocketException socketException)
                {
                    // this happens if (for example) the IP address is correct but there
                    // is no server running on that IP/Port
                    Logger.Log("Client: failed to connect to ip=" + ip + " port=" + port + " reason=" + socketException);
                    client.Close(); // clean up properly before exiting
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        public void Disconnect()
        {
            // only if started
            if (!Connecting && !Connected) return;

            Logger.Log("Client: disconnecting");

            // this is supposed to disconnect gracefully, but the blocking Read
            // calls throw a 'Read failure' exception instead of returning 0.
            // (maybe it's Unity? maybe Mono?)
            client.GetStream().Close();
            client.Close();

            // clear queue just to be sure that nothing old is processed when
            // starting again
            messageQueue.Clear();
        }

        public bool Send(byte[] data)
        {
            if (Connected)
            {
                return SendMessage(client.GetStream(), data);
            }
            Logger.LogWarning("Client.Send: not connected!");
            return false;
        }
    }
}