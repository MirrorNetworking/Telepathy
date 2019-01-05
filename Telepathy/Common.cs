// common code used by server and client
using System;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public abstract class Common
    {
        // common code /////////////////////////////////////////////////////////
        // State object for reading client data asynchronously
        public class StateObject
        {
            // Connection Id
            public int connectionId;
            // Client  socket.
            public Socket workSocket;
            // Receive buffer.
            public byte[] header = new byte[4];
            public int contentSize;
            public int contentPosition;
            public byte[] content;
            // keep track of last message queue warning
            public DateTime messageQueueLastWarning = DateTime.Now;
        }

        // incoming message queue of <connectionId, message>
        // (not a HashSet because one connection can have multiple new messages)
        protected SafeQueue<Message> messageQueue = new SafeQueue<Message>();

        // warning if message queue gets too big
        // if the average message is about 20 bytes then:
        // -   1k messages are   20KB
        // -  10k messages are  200KB
        // - 100k messages are 1.95MB
        // 2MB are not that much, but it is a bad sign if the caller process
        // can't call GetNextMessage faster than the incoming messages.
        public static int messageQueueSizeWarning = 100000;

        // NoDelay disables nagle algorithm. lowers CPU% and latency but
        // increases bandwidth
        public bool NoDelay = true;

        // removes and returns the oldest message from the message queue.
        // (might want to call this until it doesn't return anything anymore)
        // -> Connected, Data, Disconnected events are all added here
        // -> bool return makes while (GetMessage(out Message)) easier!
        // -> no 'is client connected' check because we still want to read the
        //    Disconnected message after a disconnect
        public bool GetNextMessage(out Message message)
        {
            return messageQueue.TryDequeue(out message);
        }

        // static helper functions /////////////////////////////////////////////
        // fast int to byte[] conversion and vice versa
        // -> test with 100k conversions:
        //    BitConverter.GetBytes(ushort): 144ms
        //    bit shifting: 11ms
        // -> 10x speed improvement makes this optimization actually worth it
        // -> this way we don't need to allocate BinaryWriter/Reader either
        // -> 4 bytes because some people may want to send messages larger than
        //    64K bytes
        public static byte[] IntToBytes(int value)
        {
            return new byte[] {
                (byte)value,
                (byte)(value >> 8),
                (byte)(value >> 16),
                (byte)(value >> 24)
            };
        }

        public static int BytesToInt(byte[] bytes )
        {
            return
                bytes[0] |
                (bytes[1] << 8) |
                (bytes[2] << 16) |
                (bytes[3] << 24);

        }

        // receive /////////////////////////////////////////////////////////////
        protected void ReadHeaderCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            // Read data from the client socket.
            int bytesRead = handler.EndReceive(ar);
            if (bytesRead == 4)
            {
                // convert to int and save it
                state.contentSize = BytesToInt(state.header);

                // read up to 'size' content bytes (it might read less if not all is there yet)
                state.contentPosition = 0;
                state.content = new byte[state.contentSize];
                handler.BeginReceive(state.content, 0, state.contentSize, 0,
                    new AsyncCallback(ReadContentCallback), state);
            }
            else
            {
                OnReadCallbackEnd(state);
            }
        }

        protected void ReadContentCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;

            // Read data from the client socket.
            int bytesRead = handler.EndReceive(ar);
            if (bytesRead > 0)
            {
                // we just read 'n' bytes, so update our content position
                state.contentPosition += bytesRead;

                // did we read the full content yet?
                if (state.contentPosition == state.contentSize)
                {
                    // queue it
                    messageQueue.Enqueue(new Message(state.connectionId, EventType.Data, state.content));

                    // and show a warning if the queue gets too big
                    // -> we don't want to show a warning every single time,
                    //    because then a lot of processing power gets wasted on
                    //    logging, which will make the queue pile up even more.
                    // -> instead we show it every 10s, so that the system can
                    //    use most it's processing power to hopefully process it.
                    if (messageQueue.Count > messageQueueSizeWarning)
                    {
                        TimeSpan elapsed = DateTime.Now - state.messageQueueLastWarning;
                        if (elapsed.TotalSeconds > 10)
                        {
                            Logger.LogWarning("ReceiveLoop: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
                            state.messageQueueLastWarning = DateTime.Now;
                        }
                    }

                    // start reading the next header
                    handler.BeginReceive(state.header, 0, 4, 0,
                        new AsyncCallback(ReadHeaderCallback), state);
                }
                // otherwise keep reading content
                else
                {
                    handler.BeginReceive(state.content, state.contentPosition, state.contentSize - state.contentPosition, 0,
                        new AsyncCallback(ReadContentCallback), state);
                }
            }
            else
            {
                OnReadCallbackEnd(state);
            }
        }

        // both read callbacks react the same way when ending. might as well
        // reuse code
        protected virtual void OnReadCallbackEnd(StateObject state)
        {
            //Logger.Log("ReadCallback ended for client: " + state.workSocket);

            // if we got here then either the client while loop ended, or an exception happened.
            // disconnect
            CloseSafely(state.workSocket);

            // add 'Disconnected' message after disconnecting properly.
            // -> always AFTER closing the streams to avoid a race condition
            //    where Disconnected -> Reconnect wouldn't work because
            //    Connected is still true for a short moment before the stream
            //    would be closed.
            messageQueue.Enqueue(new Message(state.connectionId, EventType.Disconnected, null));
        }

        // send ////////////////////////////////////////////////////////////////
        protected void Send(Socket socket, byte[] content)
        {
            // construct header (size)
            byte[] header = IntToBytes(content.Length);

            // write header+content at once via payload array. writing
            // header,payload separately would cause 2 TCP packets to be
            // sent if nagle's algorithm is disabled(2x TCP header overhead)
            byte[] payload = new byte[header.Length + content.Length];
            Array.Copy(header, payload, header.Length);
            Array.Copy(content, 0, payload, header.Length, content.Length);

            // Begin sending the payload to the remote device.
            // TODO check if previous send finished yet. otherwise we get errors
            socket.BeginSend(payload, 0, payload.Length, 0,
                new AsyncCallback(SendCallback), socket);

            //sendDone.WaitOne();
        }

        void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                //Logger.Log("Sent " + bytesSent + " bytes");

                // Signal that all bytes have been sent.
                //sendDone.Set();
            }
            catch (Exception e)
            {
                Logger.LogWarning("Server Send exception: " + e);
            }
        }

        // close ///////////////////////////////////////////////////////////////
        protected static void CloseSafely(Socket socket)
        {
            // C#'s built in TcpClient wraps this in try/finally too
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch {} // will get an exception if not connected anymore, etc.
            finally
            {
                socket.Close();
            }
        }
    }
}
