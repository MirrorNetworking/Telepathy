using System;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : Common
    {
        public TcpClient client;
        Thread connectThread;
        Thread sendThread;

        // can't check Client.Connected because it's only true after receiving
        // the first data. need to do it manually.
        // => bools are atomic according to
        //    https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/variables
        //    made volatile so the compiler does not reorder access to it
        volatile bool _Connected;
        public bool Connected => _Connected;

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

        // try to read without blocking.
        bool lastConnected;
        int contentSize = 0; // set after reading header
        public bool GetNextMessage(out Message message)
        {
            // lock so this never gets called simultaneously from multiple
            // threads, otherwise available->recv would get interfered.
            lock (this)
            {
                message = null;

                // first of all: not connected before, but now?
                if (Connected && !lastConnected)
                {
                    message = new Message(0, EventType.Connected, null);
                    lastConnected = true;
                    return true;
                }
                // connected and detected a disconnect?
                else if (Connected && WasDisconnected(client))
                {
                    message = new Message(0, EventType.Disconnected, null);
                    _Connected = false; // we detected it, we set it false
                    lastConnected = false;
                    client.Close(); // clean up
                    return true;
                }
                // still connected? then read a message
                else if (Connected)
                {
                    // header not read yet? then read if available
                    if (contentSize == 0)
                    {
                        contentSize = ReadHeaderIfAvailable(client);
                    }

                    // try to read content
                    if (contentSize > 0)
                    {
                        byte[] content = ReadContentIfAvailable(client, contentSize);
                        if (content != null)
                        {
                            message = new Message(0, EventType.Data, content);
                            contentSize = 0; // reset for next time
                            return true;
                        }
                    }
                }

                // nothing this time
                return false;
            }
        }

        // the thread function
        void ConnectThreadFunction(string ip, int port)
        {
            // absolutely must wrap with try/catch, otherwise thread
            // exceptions are silent
            try
            {
                // connect (blocking)
                client.Connect(ip, port);
                _Connected = true;

                // create send queue for this client
                SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
                sendQueues[0] = sendQueue;

                // start send thread only after connected
                sendThread = new Thread(() => { SendLoop(0, client, sendQueue); });
                sendThread.IsBackground = true;
                sendThread.Start();
            }
            catch (SocketException exception)
            {
                // this happens if (for example) the ip address is correct
                // but there is no server running on that ip/port
                Logger.Log("Client Connect: failed to connect to ip=" + ip + " port=" + port + " reason=" + exception);
            }
            catch (Exception exception)
            {
                // something went wrong. probably important.
                Logger.LogError("Client Connect Exception: " + exception);
            }
            finally
            {
                // we definitely aren't connecting anymore. either it worked or
                // it failed.
                _Connecting = false;
            }
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

            // reset state
            lastConnected = false;

            // clear old messages in queue, just to be sure that the caller
            // doesn't receive data from last time and gets out of sync.
            // -> calling this in Disconnect isn't smart because the caller may
            //    still want to process all the latest messages afterwards
            sendQueues.Clear();

            // client.Connect(ip, port) is blocking. let's call it in the thread
            // and return immediately.
            // -> this way the application doesn't hang for 30s if connect takes
            //    too long, which is especially good in games
            // -> this way we don't async client.BeginConnect, which seems to
            //    fail sometimes if we connect too many clients too fast
            connectThread = new Thread(() => { ConnectThreadFunction(ip, port); });
            connectThread.IsBackground = true;
            connectThread.Start();
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
                connectThread?.Join();

                // clear send queues. no need to hold on to them.
                // (unlike receiveQueue, which is still needed to process the
                //  latest Disconnected message, etc.)
                sendQueues.Clear();

                // let go of this one completely. the thread ended, no one uses
                // it anymore and this way Connected is false again immediately.
                _Connected = false;
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
