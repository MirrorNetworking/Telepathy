﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Server : Common
    {
        // listener
        TcpListener listener;
        Thread listenerThread;

        // clients with <connectionId, TcpClient>
        SafeDictionary<int, TcpClient> clients = new SafeDictionary<int, TcpClient>();

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
        public bool Active
        {
            get { return listenerThread != null && listenerThread.IsAlive; }
        }

        // the listener thread's listen function
        void Listen(int port, int maxConnections)
        {
            // absolutely must wrap with try/catch, otherwise thread
            // exceptions are silent
            try
            {
                // start listener
                // (NoDelay disables nagle algorithm. lowers CPU% and latency)
                listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
                listener.Server.NoDelay = true;
                listener.Start();
                Logger.Log("Server: listening port=" + port + " max=" + maxConnections);

                // keep accepting new clients
                while (true)
                {
                    // wait and accept new client
                    // note: 'using' sucks here because it will try to
                    // dispose after thread was started but we still need it
                    // in the thread
                    TcpClient client = listener.AcceptTcpClient();

                    // are more connections allowed?
                    if (clients.Count < maxConnections)
                    {
                        // generate the next connection id (thread safely)
                        int connectionId = NextConnectionId();

                        // spawn a thread for each client to listen to his
                        // messages
                        Thread thread = new Thread(() =>
                        {
                            // add to dict immediately
                            clients.Add(connectionId, client);

                            // run the receive loop
                            ReceiveLoop(connectionId, client, messageQueue);

                            // remove client from clients dict afterwards
                            clients.Remove(connectionId);
                        });
                        thread.IsBackground = true;
                        thread.Start();
                    }
                    // connection limit reached. disconnect the client and show
                    // a small log message so we know why it happened.
                    // note: no extra Sleep because Accept is blocking anyway
                    else
                    {
                        client.Close();
                        Logger.Log("Server too full, disconnected a client");
                    }
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
        public void Start(int port, int maxConnections = int.MaxValue)
        {
            // not if already started
            if (Active) return;

            // clear old messages in queue, just to be sure that the caller
            // doesn't receive data from last time and gets out of sync.
            // -> calling this in Stop isn't smart because the caller may
            //    still want to process all the latest messages afterwards
            messageQueue.Clear();

            // start the listener thread
            Logger.Log("Server: Start port=" + port + " max=" + maxConnections);
            listenerThread = new Thread(() => { Listen(port, maxConnections); });
            listenerThread.IsBackground = true;
            listenerThread.Start();
        }

        public void Stop()
        {
            // only if started
            if (!Active) return;

            Logger.Log("Server: stopping...");

            // stop listening to connections so that no one can connect while we
            // close the client connections
            listener.Stop();

            // close all client connections
            List<TcpClient> connections = clients.GetValues();
            foreach (TcpClient client in connections)
            {
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
            // find the connection
            TcpClient client;
            if (clients.TryGetValue(connectionId, out client))
            {
                // GetStream() might throw exception if client is disconnected
                try
                {
                    NetworkStream stream = client.GetStream();
                    return SendMessage(stream, data);
                }
                catch (Exception exception)
                {
                    Logger.LogWarning("Server.Send exception: " + exception);
                    return false;
                }
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
