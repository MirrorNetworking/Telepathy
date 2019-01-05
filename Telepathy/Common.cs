// common code used by server and client
using System;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public abstract class Common
    {
        // common code /////////////////////////////////////////////////////////
        // incoming message queue of <connectionId, message>
        // (not a HashSet because one connection can have multiple new messages)
        protected SafeQueue<Message> receiveQueue = new SafeQueue<Message>();

        // outgoing message queue of <connectionId, sendQueue>
        // (not a HashSet because one connection can have multiple new messages)
        protected SafeDictionary<int, SafeQueue<byte[]>> sendQueues = new SafeDictionary<int, SafeQueue<byte[]>>();

        // warning if message queue gets too big
        // if the average message is about 20 bytes then:
        // -   1k messages are   20KB
        // -  10k messages are  200KB
        // - 100k messages are 1.95MB
        // 2MB are not that much, but it is a bad sign if the caller process
        // can't call GetNextMessage faster than the incoming messages.
        public static int messageQueueSizeWarning = 100000;

        // removes and returns the oldest message from the message queue.
        // (might want to call this until it doesn't return anything anymore)
        // -> Connected, Data, Disconnected events are all added here
        // -> bool return makes while (GetMessage(out Message)) easier!
        // -> no 'is client connected' check because we still want to read the
        //    Disconnected message after a disconnect
        public bool GetNextMessage(out Message message)
        {
            return receiveQueue.TryDequeue(out message);
        }

        // calling GetNextMessage multiple times per tick will lock up the
        // queue multiple times per tick, which can be slow.
        // this version locks it only once
        // (note: no noticeable difference when testing 1000 connections though)
        public bool GetNextMessages(out Message[] messages)
        {
            // get all messages
            return receiveQueue.TryDequeueAll(out messages);
        }

        // NoDelay disables nagle algorithm. lowers CPU% and latency but
        // increases bandwidth
        public bool NoDelay = true;

        // Send would stall forever if the network is cut off during a send, so
        // we need a timeout (in milliseconds)
        public int SendTimeout = 5000;

        // SendLoop Sleep time so we don't choke up the CPU
        // probably wise not to change this. 10ms works good in benchmarks.
        public int SendInterval = 10;

        // static helper functions /////////////////////////////////////////////
        // fast int to byte[] conversion and vice versa
        // -> test with 100k conversions:
        //    BitConverter.GetBytes(ushort): 144ms
        //    bit shifting: 11ms
        // -> 10x speed improvement makes this optimization actually worth it
        // -> this way we don't need to allocate BinaryWriter/Reader either
        // -> 4 bytes because some people may want to send messages larger than
        //    64K bytes
        static byte[] IntToBytes(int value)
        {
            return new byte[] {
                (byte)value,
                (byte)(value >> 8),
                (byte)(value >> 16),
                (byte)(value >> 24)
            };
        }

        static int BytesToInt(byte[] bytes )
        {
            return
                bytes[0] |
                (bytes[1] << 8) |
                (bytes[2] << 16) |
                (bytes[3] << 24);

        }

        // send message (via stream) with the <size,content> message structure
        // this function is blocking sometimes, whether we like it or not.
        // (if someone has high latency or wire was cut off then this blocks)
        // (private so we only call it from Common.SendLoop (no blocking))
        static bool SendMessageBlocking(NetworkStream stream, byte[] content)
        {
            // can we still write to this socket (not disconnected?)
            if (!stream.CanWrite)
            {
                Logger.LogWarning("Send: stream not writeable: " + stream);
                return false;
            }

            // stream.Write throws exceptions if client sends with high
            // frequency and the server stops
            try
            {
                // construct header (size)
                byte[] header = IntToBytes(content.Length);

                // write header+content at once via payload array. writing
                // header,payload separately would cause 2 TCP packets to be
                // sent if nagle's algorithm is disabled(2x TCP header overhead)
                byte[] payload = new byte[header.Length + content.Length];
                Array.Copy(header, payload, header.Length);
                Array.Copy(content, 0, payload, header.Length, content.Length);
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

        // read message (via stream) with the <size,content> message structure
        protected static bool ReadMessageBlocking(NetworkStream stream, out byte[] content)
        {
            content = null;

            // read exactly 4 bytes for header (blocking)
            byte[] header = new byte[4];
            if (!stream.ReadExactly(header, 4))
                return false;

            int size = BytesToInt(header);

            // read exactly 'size' bytes for content (blocking)
            content = new byte[size];
            if (!stream.ReadExactly(content, size))
                return false;

            return true;
        }

        // thread receive function is the same for client and server's clients
        // (static to reduce state for maximum reliability)
        protected static void ReceiveLoop(int connectionId, TcpClient client, SafeQueue<Message> receiveQueue)
        {
            // get NetworkStream from client
            NetworkStream stream = client.GetStream();

            // keep track of last message queue warning
            DateTime messageQueueLastWarning = DateTime.Now;

            // absolutely must wrap with try/catch, otherwise thread exceptions
            // are silent
            try
            {
                // add connected event to queue with ip address as data in case
                // it's needed
                receiveQueue.Enqueue(new Message(connectionId, EventType.Connected, null));

                // let's talk about reading data.
                // -> normally we would read as much as possible and then
                //    extract as many <size,content>,<size,content> messages
                //    as we received this time. this is really complicated
                //    and expensive to do though
                // -> instead we use a trick:
                //      Read(2) -> size
                //        Read(size) -> content
                //      repeat
                //    Read is blocking, but it doesn't matter since the
                //    best thing to do until the full message arrives,
                //    is to wait.
                // => this is the most elegant AND fast solution.
                //    + no resizing
                //    + no extra allocations, just one for the content
                //    + no crazy extraction logic
                while (true)
                {
                    // read the next message (blocking) or stop if stream closed
                    byte[] content;
                    if (!ReadMessageBlocking(stream, out content))
                        break;

                    // queue it
                    receiveQueue.Enqueue(new Message(connectionId, EventType.Data, content));

                    // and show a warning if the queue gets too big
                    // -> we don't want to show a warning every single time,
                    //    because then a lot of processing power gets wasted on
                    //    logging, which will make the queue pile up even more.
                    // -> instead we show it every 10s, so that the system can
                    //    use most it's processing power to hopefully process it.
                    if (receiveQueue.Count > messageQueueSizeWarning)
                    {
                        TimeSpan elapsed = DateTime.Now - messageQueueLastWarning;
                        if (elapsed.TotalSeconds > 10)
                        {
                            Logger.LogWarning("ReceiveLoop: receiveQueue is getting big(" + receiveQueue.Count + "), try calling GetNextMessages more often!");
                            messageQueueLastWarning = DateTime.Now;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                // something went wrong. the thread was interrupted or the
                // connection closed or we closed our own connection or ...
                // -> either way we should stop gracefully
                Logger.Log("ReceiveLoop: finished receive function for connectionId=" + connectionId + " reason: " + exception);
            }

            // clean up no matter what
            stream.Close();
            client.Close();

            // add 'Disconnected' message after disconnecting properly.
            // -> always AFTER closing the streams to avoid a race condition
            //    where Disconnected -> Reconnect wouldn't work because
            //    Connected is still true for a short moment before the stream
            //    would be closed.
            receiveQueue.Enqueue(new Message(connectionId, EventType.Disconnected, null));
        }

        // thread send function
        // note: we really do need one per connection, so that if one connection
        //       blocks, the rest will still continue to get sends
        protected static void SendLoop(int connectionId, TcpClient client, SafeQueue<byte[]> sendQueue, int sendInterval)
        {
            // get NetworkStream from client
            NetworkStream stream = client.GetStream();

            // keep track of last message queue warning
            DateTime messageQueueLastWarning = DateTime.Now;

            try
            {
                while (client.Connected) // try this. client will get closed eventually.
                {
                    // show a warning if the queue gets too big
                    // -> we don't want to show a warning every single time,
                    //    because then a lot of processing power gets wasted on
                    //    logging, which will make the queue pile up even more.
                    // -> instead we show it every 10s, so that the system can
                    //    use most it's processing power to hopefully process it.
                    if (sendQueue.Count > messageQueueSizeWarning)
                    {
                        TimeSpan elapsed = DateTime.Now - messageQueueLastWarning;
                        if (elapsed.TotalSeconds > 10)
                        {
                            Logger.LogWarning("SendLoop: sendQueue is getting big(" + sendQueue.Count + ")! Try calling Send less often so the thread can keep up with sends.");
                            messageQueueLastWarning = DateTime.Now;
                        }
                    }

                    // dequeue all
                    byte[][] messages;
                    if (sendQueue.TryDequeueAll(out messages))
                    {
                        // TODO combine them? but Send is the one that generates
                        // the header and we need one for each message.
                        for (int i = 0; i < messages.Length; ++i) // foreach would copy the array
                        {
                            // send message (blocking) or stop if stream is closed
                            if (!SendMessageBlocking(stream, messages[i]))
                                return;
                        }
                    }

                    // don't choke up the CPU: wait until queue not empty anymore
                    sendQueue.notEmpty.WaitOne();
                }
            }
            catch (Exception exception)
            {
                // something went wrong. the thread was interrupted or the
                // connection closed or we closed our own connection or ...
                // -> either way we should stop gracefully
                Logger.Log("SendLoop: finished send function for connectionId=" + connectionId + " reason: " + exception);
            }
        }
    }
}
