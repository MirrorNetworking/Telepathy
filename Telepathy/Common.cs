// common code used by server and client
using System;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public abstract class Common
    {
        // IMPORTANT: DO NOT SHARE STATE ACROSS SEND/RECV LOOPS (DATA RACES)
        // (except receive pipe which is used for all threads)

        // thread safe pipe for received messages
        // (not a HashSet because one connection can have multiple new messages)
        protected readonly MagnificentReceivePipe receivePipe;

        // pipe count, useful for debugging / benchmarks
        public int ReceivePipeCount => receivePipe.Count;

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

        // Prevent allocation attacks. Each packet is prefixed with a length
        // header, so an attacker could send a fake packet with length=2GB,
        // causing the server to allocate 2GB and run out of memory quickly.
        // -> simply increase max packet size if you want to send around bigger
        //    files!
        // -> 16KB per message should be more than enough.
        public readonly int MaxMessageSize;

        // Send would stall forever if the network is cut off during a send, so
        // we need a timeout (in milliseconds)
        public int SendTimeout = 5000;

        // constructor /////////////////////////////////////////////////////////
        protected Common(int MaxMessageSize)
        {
            this.MaxMessageSize = MaxMessageSize;

            // create receive pipe with max message size for pooling
            receivePipe = new MagnificentReceivePipe(MaxMessageSize);
        }

        // helper functions ////////////////////////////////////////////////////
        // thread send function
        // note: we really do need one per connection, so that if one connection
        //       blocks, the rest will still continue to get sends
        protected void SendLoop(int connectionId, TcpClient client, MagnificentSendPipe sendPipe, ManualResetEvent sendPending)
        {
            // get NetworkStream from client
            NetworkStream stream = client.GetStream();

            // avoid payload[packetSize] allocations. size increases dynamically as
            // needed for batching.
            //
            // IMPORTANT: DO NOT make this a member, otherwise every connection
            //            on the server would use the same buffer simulatenously
            byte[] payload = null;

            try
            {
                while (client.Connected) // try this. client will get closed eventually.
                {
                    // reset ManualResetEvent before we do anything else. this
                    // way there is no race condition. if Send() is called again
                    // while in here then it will be properly detected next time
                    // -> otherwise Send might be called right after dequeue but
                    //    before .Reset, which would completely ignore it until
                    //    the next Send call.
                    sendPending.Reset(); // WaitOne() blocks until .Set() again

                    // dequeue & serialize all
                    // a locked{} TryDequeueAll is twice as fast as
                    // ConcurrentQueue, see SafeQueue.cs!
                    if (sendPipe.DequeueAndSerializeAll(ref payload, out int packetSize))
                    {
                        // send messages (blocking) or stop if stream is closed
                        if (!ThreadFunctions.SendMessagesBlocking(stream, payload, packetSize))
                            // break instead of return so stream close still happens!
                            break;
                    }

                    // don't choke up the CPU: wait until queue not empty anymore
                    sendPending.WaitOne();
                }
            }
            catch (ThreadAbortException)
            {
                // happens on stop. don't log anything.
            }
            catch (ThreadInterruptedException)
            {
                // happens if receive thread interrupts send thread.
            }
            catch (Exception exception)
            {
                // something went wrong. the thread was interrupted or the
                // connection closed or we closed our own connection or ...
                // -> either way we should stop gracefully
                Log.Info("SendLoop Exception: connectionId=" + connectionId + " reason: " + exception);
            }
            finally
            {
                // clean up no matter what
                // we might get SocketExceptions when sending if the 'host has
                // failed to respond' - in which case we should close the connection
                // which causes the ReceiveLoop to end and fire the Disconnected
                // message. otherwise the connection would stay alive forever even
                // though we can't send anymore.
                stream.Close();
                client.Close();
            }
        }
    }
}
