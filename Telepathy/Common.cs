// common code used by server and client
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public abstract class Common
    {
        // common code /////////////////////////////////////////////////////////

        // outgoing message queue of <connectionId, sendQueue>
        // (not a HashSet because one connection can have multiple new messages)
        protected ConcurrentDictionary<int, SafeQueue<byte[]>> sendQueues = new ConcurrentDictionary<int, SafeQueue<byte[]>>();

        // NoDelay disables nagle algorithm. lowers CPU% and latency but
        // increases bandwidth
        public bool NoDelay = true;

        // Send would stall forever if the network is cut off during a send, so
        // we need a timeout (in milliseconds)
        public int SendTimeout = 5000;

        // disconnect detection
        // we aren't blocking on receives, so we can't detect disconnects
        // via recv exceptions. instead we need to check for one special
        // case: if poll(SocketRead) is true but there is no actual data
        // available, then it must have been a disconnect
        protected static bool WasDisconnected(TcpClient client)
        {
            return client.Client.Poll(0, SelectMode.SelectRead) &&
                   client.Available == 0;
        }

        // reading /////////////////////////////////////////////////////////////
        protected static int ReadHeaderIfAvailable(TcpClient client)
        {
            // always use NetworkStream to support concurrent read/writes
            // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream?view=netframework-4.7.2
            NetworkStream stream = client.GetStream();

            // header not read yet, but can read it now?
            if (client.Available >= 4)
            {
                // create header buffer
                byte[] header = new byte[4];

                // read 4 bytes
                // -> we know how much is available, so this should not be
                //    blocking.
                int bytesRead = stream.Read(header, 0, 4);
                if (bytesRead == 4)
                {
                    // convert to int. don't return yet, we might be able to
                    // also read the message already
                    return Utils.BytesToIntBigEndian(header);
                }

                // reading failed. socket probably closed.
                Logger.LogError("Failed to read header: " + bytesRead + " / " + 4 + ". this should never happen." + Environment.StackTrace);
            }
            return 0;
        }

        protected static byte[] ReadContentIfAvailable(TcpClient client, int contentSize)
        {
            // always use NetworkStream to support concurrent read/writes
            // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream?view=netframework-4.7.2
            NetworkStream stream = client.GetStream();

            // try to read content
            if (client.Available >= contentSize)
            {
                // read 'contentSize' bytes
                // -> we know how much is available, so this should not be
                //    blocking.
                byte[] content = new byte[contentSize];
                int bytesRead = stream.Read(content, 0, contentSize);
                if (bytesRead == contentSize)
                {
                    return content;
                }

                // reading failed. socket probably closed.
                Logger.LogError("Failed to read content: " + bytesRead + " / " + contentSize + ". this should never happen." + Environment.StackTrace);
            }
            return null;
        }

        // static helper functions /////////////////////////////////////////////
        // send message (via stream) with the <size,content> message structure
        // this function is blocking sometimes!
        // (e.g. if someone has high latency or wire was cut off)
        protected static bool SendMessagesBlocking(NetworkStream stream, byte[][] messages)
        {
            // stream.Write throws exceptions if client sends with high
            // frequency and the server stops
            try
            {
                // we might have multiple pending messages. merge into one
                // packet to avoid TCP overheads and improve performance.
                int packetSize = 0;
                for (int i = 0; i < messages.Length; ++i)
                    packetSize += sizeof(int) + messages[i].Length; // header + content

                // create the packet
                byte[] payload = new byte[packetSize];
                int position = 0;
                for (int i = 0; i < messages.Length; ++i)
                {
                    // construct header (size)
                    byte[] header = Utils.IntToBytesBigEndian(messages[i].Length);

                    // copy header + message into buffer
                    Array.Copy(header, 0, payload, position, header.Length);
                    Array.Copy(messages[i], 0, payload, position + header.Length, messages[i].Length);
                    position += header.Length + messages[i].Length;
                }

                // write the whole thing
                stream.Write(payload, 0, payload.Length);

                return true;
            }
            catch (Exception exception)
            {
                // log as regular message because servers do shut down sometimes
                Logger.Log("Send: stream.Write exception: " + exception);
                return false;
            }
        }

        // thread send function
        // note: we really do need one per connection, so that if one connection
        //       blocks, the rest will still continue to get sends
        protected static void SendLoop(int connectionId, TcpClient client, SafeQueue<byte[]> sendQueue)
        {
            // get NetworkStream from client
            NetworkStream stream = client.GetStream();

            try
            {
                while (client.Connected) // try this. client will get closed eventually.
                {
                    // dequeue all
                    byte[][] messages;
                    if (sendQueue.TryDequeueAll(out messages))
                    {
                        // send message (blocking) or stop if stream is closed
                        if (!SendMessagesBlocking(stream, messages))
                            return;
                    }

                    // don't choke up the CPU: wait until queue not empty anymore
                    sendQueue.notEmpty.WaitOne();
                }
            }
            catch (ThreadAbortException)
            {
                // happens on stop. don't log anything.
            }
            catch (Exception exception)
            {
                // something went wrong. the thread was interrupted or the
                // connection closed or we closed our own connection or ...
                // -> either way we should stop gracefully
                Logger.Log("SendLoop Exception: connectionId=" + connectionId + " reason: " + exception);
            }
        }
    }
}
