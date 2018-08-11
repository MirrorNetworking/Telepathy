using System;
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
        SafeDictionary<uint, TcpClient> clients = new SafeDictionary<uint, TcpClient>();

        // check if the server is running
        public bool Active
        {
            get { return listenerThread != null && listenerThread.IsAlive; }
        }

        // start listening for new connections in a background thread and spawn
        // a new thread for each one.
        public void Start(string ip, int port)
        {
            // not if already started
            if (Active) return;

            // start the listener thread
            Logger.Log("Server: starting ip=" + ip + " port=" + port);
            listenerThread = new Thread(() =>
            {
                // absolutely must wrap with try/catch, otherwise thread
                // exceptions are silent
                try
                {
                    // localhost support so .Parse doesn't throw errors
                    if (ip.ToLower() == "localhost") ip = "127.0.0.1";

                    // start listener
                    listener = new TcpListener(IPAddress.Parse(ip), port);
                    listener.Start();
                    Logger.Log("Server is listening");

                    // keep accepting new clients
                    while (true)
                    {
                        // wait and accept new client
                        // note: 'using' sucks here because it will try to
                        // dispose after thread was started but we still need it
                        // in the thread
                        TcpClient client = listener.AcceptTcpClient();

                        // generate the next connection id (thread safely)
                        uint connectionId = counter.Next();

                        // spawn a thread for each client to listen to his
                        // messages
                        Thread thread = new Thread(() =>
                        {
                            // run the receive loop
                            ReceiveLoop(messageQueue, connectionId, client);

                            // remove client from clients dict afterwards
                            clients.Remove(connectionId);
                        });
                        thread.IsBackground = true;
                        thread.Start();

                        // add to dict now
                        clients.Add(connectionId, client);
                    }
                }
                catch (ThreadAbortException abortException)
                {
                    // UnityEditor causes AbortException if thread is still
                    // running when we press Play again next time. that's okay.
                    Logger.Log("Server thread aborted. That's okay. " + abortException);
                }
                catch (SocketException socketException)
                {
                    // calling StopServer will interrupt this thread with a
                    // 'SocketException: interrupted'. that's okay.
                    Logger.Log("Server Thread stopped. That's okay. " + socketException);
                }
                catch (Exception exception)
                {
                    // something went wrong. probably important.
                    Logger.LogError("Server Exception: " + exception);
                }
            });
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
                // this is supposed to disconnect gracefully, but the blocking
                // Read calls throw a 'Read failure' exception in Unity
                // sometimes (instead of returning 0)
                client.GetStream().Close();
                client.Close();
            }

            // clear clients list
            clients.Clear();
        }

        // send message to client using socket connection.
        public bool Send(uint connectionId, byte[] data)
        {
            // find the connection
            TcpClient client;
            if (clients.TryGetValue(connectionId, out client))
            {
                return SendMessage(client.GetStream(), data);
            }
            Logger.LogWarning("Server.Send: invalid connectionId: " + connectionId);
            return false;
        }
    }
}