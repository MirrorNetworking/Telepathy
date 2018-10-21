using System;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : Common
    {
        TcpClient client;
        Thread thread;

        public bool Connected
        {
            get
            {
                // TcpClient.Connected doesn't check if socket != null, which
                // results in NullReferenceExceptions if connection was closed.
                // -> let's check it manually instead
                return client != null &&
                       client.Client != null &&
                       client.Client.Connected;
            }
        }

        // there is no easy way to check if TcpClient is connecting:
        // - TcpClient has no flag for that
        // - using a 'bool connecting' would require locks and lots of special
        //   cases in case of exceptions/disconect calls etc.
        // => checking if the thread is running (while not connected yet) is the
        //    easiest and 100% reliable solution. no race conditions or locks.
        public bool Connecting
        {
            get { return thread != null && thread.IsAlive && !Connected; }
        }

        // the thread function
        // (static to reduce state for maximum reliability)
        static void ThreadFunction(TcpClient client, string ip, int port, SafeQueue<Message> messageQueue)
        {
            // absolutely must wrap with try/catch, otherwise thread
            // exceptions are silent
            try
            {
                // connect (blocking)
                // (NoDelay disables nagle algorithm. lowers CPU% and latency)
                client.NoDelay = true;
                client.Connect(ip, port);

                // run the receive loop
                ReceiveLoop(0, client, messageQueue);
            }
            catch (SocketException exception)
            {
                // this happens if (for example) the ip address is correct
                // but there is no server running on that ip/port
                Logger.Log("Client: failed to connect to ip=" + ip + " port=" + port + " reason=" + exception);

                // add 'Disconnected' event to message queue so that the caller
                // knows that the Connect failed. otherwise they will never know
                messageQueue.Enqueue(new Message(0, EventType.Disconnected, null));
            }
            catch (Exception exception)
            {
                // something went wrong. probably important.
                Logger.LogError("Client Exception: " + exception);
            }

            // if we got here then we are done. ReceiveLoop cleans up already,
            // but we may never get there if connect fails. so let's clean up
            // here too.
            client.Close();
        }

        public void Connect(string ip, int port)
        {
            // not if already started
            if (Connecting || Connected) return;

            // TcpClient can only be used once. need to create a new one each
            // time.
            client = new TcpClient();

            // clear old messages in queue, just to be sure that the caller
            // doesn't receive data from last time and gets out of sync.
            // -> calling this in Disconnect isn't smart because the caller may
            //    still want to process all the latest messages afterwards
            messageQueue.Clear();

            // client.Connect(ip, port) is blocking. let's call it in the thread
            // and return immediately.
            // -> this way the application doesn't hang for 30s if connect takes
            //    too long, which is especially good in games
            // -> this way we don't async client.BeginConnect, which seems to
            //    fail sometimes if we connect too many clients too fast
            thread = new Thread(() => { ThreadFunction(client, ip, port, messageQueue); });
            thread.IsBackground = true;
            thread.Start();
        }

        public void Disconnect()
        {
            // only if started
            if (Connecting || Connected)
            {
                // close client
                client.Close();

                // wait until thread finished. this is the only way to guarantee
                // that we can call Connect() again immediately after Disconnect
                if (thread != null)
                    thread.Join();

                Logger.Log("Client: disconnected");
            }
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
