using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Server : Common
    {
        // listener
        public TcpListener listener;
        Thread listenerThread;

        // clients with <connectionId, TcpClient>
        ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();

        // connectionId counter
        // (right now we only use it from one listener thread, but we might have
        //  multiple threads later in case of WebSockets etc.)
        // -> static so that another server instance doesn't start at 0 again.
        static int counter = 0;

        // public next id function in case someone needs to reserve an id
        // (e.g. if hostMode should always have 0 connection and external
        //  connections should start at 1, etc.)
        public static int NextConnectionId()
        {
            int id = Interlocked.Increment(ref counter);

            // it's very unlikely that we reach the uint limit of 2 billion.
            // even with 1 new connection per second, this would take 68 years.
            // -> but if it happens, then we should throw an exception because
            //    the caller probably should stop accepting clients.
            // -> it's hardly worth using 'bool Next(out id)' for that case
            //    because it's just so unlikely.
            if (id == int.MaxValue)
            {
                throw new Exception("connection id limit reached: " + id);
            }

            return id;
        }

        // check if the server is running
        public bool Active => listenerThread != null && listenerThread.IsAlive;

        // the listener thread's listen function
        // note: no maxConnections parameter. high level API should handle that.
        //       (Transport can't send a 'too full' message anyway)
        void Listen(int port)
        {
            // absolutely must wrap with try/catch, otherwise thread
            // exceptions are silent
            try
            {
                // start listener
                listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
                listener.Server.NoDelay = NoDelay;
                listener.Server.SendTimeout = SendTimeout;
                listener.Start();
                Logger.Log("Server: listening port=" + port);

                // keep accepting new clients
                while (true)
                {
                    // wait and accept new client
                    // note: 'using' sucks here because it will try to
                    // dispose after thread was started but we still need it
                    // in the thread
                    TcpClient client = listener.AcceptTcpClient();

                    // generate the next connection id (thread safely)
                    int connectionId = NextConnectionId();

                    // spawn a send thread for each client
                    Thread sendThread = new Thread(() =>
                    {
                        // wrap in try-catch, otherwise Thread exceptions
                        // are silent
                        try
                        {
                            // create send queue immediately
                            SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();
                            sendQueues[connectionId] = sendQueue;

                            // run the send loop
                            SendLoop(connectionId, client, sendQueue);

                            // remove queue from queues afterwards
                            sendQueues.TryRemove(connectionId, out SafeQueue<byte[]> _);
                        }
                        catch (ThreadAbortException)
                        {
                            // happens on stop. don't log anything.
                            // (we catch it in SendLoop too, but it still gets
                            //  through to here when aborting. don't show an
                            //  error.)
                        }
                        catch (Exception exception)
                        {
                            Logger.LogError("Server send thread exception: " + exception);
                        }
                    });
                    sendThread.IsBackground = true;
                    sendThread.Start();

                    // spawn a receive thread for each client
                    Thread receiveThread = new Thread(() =>
                    {
                        // wrap in try-catch, otherwise Thread exceptions
                        // are silent
                        try
                        {
                            // add to dict immediately
                            clients[connectionId] = client;

                            // run the receive loop
                            ReceiveLoop(connectionId, client, receiveQueue);

                            // remove client from clients dict afterwards
                            clients.TryRemove(connectionId, out TcpClient _);
                        }
                        catch (Exception exception)
                        {
                            Logger.LogError("Server client thread exception: " + exception);
                        }
                    });
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                }
            }
            catch (ThreadAbortException exception)
            {
                // UnityEditor causes AbortException if thread is still
                // running when we press Play again next time. that's okay.
                Logger.Log("Server thread aborted. That's okay. " + exception);
            }
            catch (SocketException exception)
            {
                // calling StopServer will interrupt this thread with a
                // 'SocketException: interrupted'. that's okay.
                Logger.Log("Server Thread stopped. That's okay. " + exception);
            }
            catch (Exception exception)
            {
                // something went wrong. probably important.
                Logger.LogError("Server Exception: " + exception);
            }
        }

        // start listening for new connections in a background thread and spawn
        // a new thread for each one.
        public bool Start(int port)
        {
            // not if already started
            if (Active) return false;

            // clear old messages in queue, just to be sure that the caller
            // doesn't receive data from last time and gets out of sync.
            // -> calling this in Stop isn't smart because the caller may
            //    still want to process all the latest messages afterwards
            receiveQueue = new ConcurrentQueue<Message>();

            // start the listener thread
            // (on low priority. if main thread is too busy then there is not
            //  much value in accepting even more clients)
            Logger.Log("Server: Start port=" + port);
            listenerThread = new Thread(() => { Listen(port); });
            listenerThread.IsBackground = true;
            listenerThread.Priority = ThreadPriority.BelowNormal;
            listenerThread.Start();
            return true;
        }

        public void Stop()
        {
            // only if started
            if (!Active) return;

            Logger.Log("Server: stopping...");

            // stop listening to connections so that no one can connect while we
            // close the client connections
            // (might be null if we call Stop so quickly after Start that the
            //  thread was interrupted before even creating the listener)
            listener?.Stop();

            // kill listener thread at all costs. only way to guarantee that
            // .Active is immediately false after Stop.
            // -> calling .Join would sometimes wait forever
            listenerThread?.Interrupt();
            listenerThread = null;

            // close all client connections
            foreach (KeyValuePair<int, TcpClient> kvp in clients)
            {
                TcpClient client = kvp.Value;
                // close the stream if not closed yet. it may have been closed
                // by a disconnect already, so use try/catch
                try { client.GetStream().Close(); } catch {}
                client.Close();
            }

            // clear clients list
            clients.Clear();
        }

        // send message to client using socket connection.
        public bool Send(int connectionId, byte[] data)
        {
            // was the sendqueue created yet?
            SafeQueue<byte[]> sendQueue;
            if (sendQueues.TryGetValue(connectionId, out sendQueue))
            {
                // add to send queue and return immediately.
                // calling Send here would be blocking (sometimes for long times
                // if other side lags or wire was disconnected)
                sendQueue.Enqueue(data);
                return true;
            }
            Logger.LogWarning("Server.Send: invalid connectionId: " + connectionId);
            return false;
        }

        // get connection info in case it's needed (IP etc.)
        // (we should never pass the TcpClient to the outside)
        public bool GetConnectionInfo(int connectionId, out string address)
        {
            // find the connection
            TcpClient client;
            if (clients.TryGetValue(connectionId, out client))
            {
                address = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                return true;
            }
            address = null;
            return false;
        }

        // disconnect (kick) a client
        public bool Disconnect(int connectionId)
        {
            // find the connection
            TcpClient client;
            if (clients.TryGetValue(connectionId, out client))
            {
                // just close it. client thread will take care of the rest.
                client.Close();
                Logger.Log("Server.Disconnect connectionId:" + connectionId);
                return true;
            }
            return false;
        }
    }
}
