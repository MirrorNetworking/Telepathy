using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : Common
    {
        public TcpClient client;
        Thread receiveThread;
        Thread sendThread;

        // TcpClient.Connected doesn't check if socket != null, which
        // results in NullReferenceExceptions if connection was closed.
        // -> let's check it manually instead
        public bool Connected => client != null &&
                                 client.Client != null &&
                                 client.Client.Connected;

        // TcpClient has no 'connecting' state to check. We need to keep track
        // of it manually.
        // -> checking 'thread.IsAlive && !Connected' is not enough because. the
        //    thread is alive and connected is false for a short moment after
        //    disconnecting, so this would cause race conditions.
        // -> we use a threadsafe bool wrapper so that ThreadFunction can remain
        //    static (it needs a common lock)
        // => Connecting is true from first Connect() call in here, through the
        //    thread start, until TcpClient.Connect() returns. Simple and clear.
        // => bools are atomic according to
        //    https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/variables
        //    made volatile so the compiler does not reorder access to it
        volatile bool _Connecting;
        public bool Connecting => _Connecting;

        // the thread function
        void ReceiveThreadFunction(string ip, int port)
        {
            // absolutely must wrap with try/catch, otherwise thread
            // exceptions are silent
            try
            {
                // connect (blocking)
                client.Connect(ip, port);
                _Connecting = false;

                // create send queue for this client
                SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
                sendQueues[0] = sendQueue;

                // start send thread only after connected
                sendThread = new Thread(() => { SendLoop(0, client, sendQueue); });
                sendThread.IsBackground = true;
                sendThread.Start();

                // run the receive loop
                ReceiveLoop(0, client, receiveQueue);
            }
            catch (SocketException exception)
            {
                // this happens if (for example) the ip address is correct
                // but there is no server running on that ip/port
                Logger.Log("Client Recv: failed to connect to ip=" + ip + " port=" + port + " reason=" + exception);

                // add 'Disconnected' event to message queue so that the caller
                // knows that the Connect failed. otherwise they will never know
                receiveQueue.Enqueue(new Message(0, EventType.Disconnected, null));
            }
            catch (Exception exception)
            {
                // something went wrong. probably important.
                Logger.LogError("Client Recv Exception: " + exception);
            }

            // try interrupting send thread after receive thread
            // ends, just to be sure
            sendThread?.Interrupt();

            // Connect might have failed. thread might have been closed.
            // let's reset connecting state no matter what.
            _Connecting = false;

            // if we got here then we are done. ReceiveLoop cleans up already,
            // but we may never get there if connect fails. so let's clean up
            // here too.
            client.Close();
        }

        public void Connect(string ip, int port)
        {
            // not if already started
            if (Connecting || Connected) return;

            // We are connecting from now until Connect succeeds or fails
            _Connecting = true;

            // TcpClient can only be used once. need to create a new one each
            // time.
            client = new TcpClient();
            client.NoDelay = NoDelay;
            client.SendTimeout = SendTimeout;

            // clear old messages in queue, just to be sure that the caller
            // doesn't receive data from last time and gets out of sync.
            // -> calling this in Disconnect isn't smart because the caller may
            //    still want to process all the latest messages afterwards
            receiveQueue = new ConcurrentQueue<Message>();
            sendQueues.Clear();

            // client.Connect(ip, port) is blocking. let's call it in the thread
            // and return immediately.
            // -> this way the application doesn't hang for 30s if connect takes
            //    too long, which is especially good in games
            // -> this way we don't async client.BeginConnect, which seems to
            //    fail sometimes if we connect too many clients too fast
            receiveThread = new Thread(() => { ReceiveThreadFunction(ip, port); });
            receiveThread.IsBackground = true;
            receiveThread.Start();
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
                receiveThread?.Join();

                // clear send queues. no need to hold on to them.
                // (unlike receiveQueue, which is still needed to process the
                //  latest Disconnected message, etc.)
                sendQueues.Clear();

                // let go of this one completely. the thread ended, no one uses
                // it anymore and this way Connected is false again immediately.
                client = null;
            }
        }

        public bool Send(byte[] data)
        {
            if (Connected)
            {
                // was the sendqueue created yet?
                SafeQueue<byte[]> sendQueue;
                if (sendQueues.TryGetValue(0, out sendQueue))
                {
                    // add to send queue and return immediately.
                    // calling Send here would be blocking (sometimes for long times
                    // if other side lags or wire was disconnected)
                    sendQueue.Enqueue(data);
                    return true;
                }
            }
            Logger.LogWarning("Client.Send: not connected!");
            return false;
        }
    }
}
