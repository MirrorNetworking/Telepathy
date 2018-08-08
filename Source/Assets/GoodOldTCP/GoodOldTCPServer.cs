using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public static class GoodOldTCPServer
{
    // listener
    static TcpListener listener;
    static Thread listenerThread;

    // clients with <clientId, socket>
    // IMPORTANT: lock() while using!
    static SafeDictionary<uint, TcpClient> clients = new SafeDictionary<uint, TcpClient>();
    static uint nextId = 0;

    // incoming message queue of <connectionId, message>
    // (not a HashSet because one connection can have multiple new messages)
    static SafeQueue<GoodOldMessage> messageQueue = new SafeQueue<GoodOldMessage>(); // accessed from getmessage and listener thread

    // removes and returns the oldest message from the message queue.
    // (might want to call this until it doesn't return anything anymore)
    // only returns one message each time so it's more similar to LLAPI:
    // https://docs.unity3d.com/ScriptReference/Networking.NetworkTransport.ReceiveFromHost.html
    // -> Connected, Data, Disconnected can all be detected with this function. simple and stupid.
    public static bool GetNextMessage(out uint connectionId, out GoodOldEventType eventType, out byte[] data)
    {
        GoodOldMessage message;
        if (messageQueue.TryDequeue(out message))
        {
            connectionId = message.connectionId;
            eventType = message.eventType;
            data = message.data;
            return true;
        }

        connectionId = 0;
        eventType = GoodOldEventType.Disconnected;
        data = null;
        return false;
    }

    public static bool Active { get { return listenerThread != null && listenerThread.IsAlive; } }

    // Runs in background TcpServerThread; Handles incomming TcpClient requests
    // IMPORTANT: Logger.Log is only shown in log file, not in console

    public static void StartServer(string ip, int port)
    {
        // not if already started
        if (Active) return;

        // start the listener thread
        Logger.Log("Server: starting ip=" + ip + " port=" + port);
        listenerThread = new Thread(() =>
        {
            // absolutely must wrap with try/catch, otherwise thread exceptions
            // are silent
            try
            {
                // localhost support so .Parse doesn't throw errors
                if (ip.ToLower() == "localhost") ip = "127.0.0.1";

                // start listener
                listener = new TcpListener(IPAddress.Parse(ip), port);
                listener.Start();
                Logger.Log("Server is listening");

                // keep accepting new clients
                while (true) // TODO while(listen)
                {
                    // wait and accept new client
                    // note: 'using' sucks here because it will try to dispose after
                    // thread was started but we still need it in the thread
                    TcpClient client = listener.AcceptTcpClient();
                    if (nextId == uint.MaxValue)
                    {
                        Logger.LogError("Server can't accept any more clients, out of ids.");
                        break;
                    }
                    uint connectionId = nextId++; // TODo is this even thread safe?
                    Logger.Log("Server: client connected. connectionId=" + connectionId);

                    // Get a stream object for reading
                    // note: 'using' sucks here because it will try to dispose after thread was started
                    // but we still need it in the thread
                    NetworkStream stream = client.GetStream();

                    // add connected event to queue
                    messageQueue.Enqueue(new GoodOldMessage(connectionId, GoodOldEventType.Connected, null));

                    // spawn a thread for each client to listen to his messages
                    // NOTE: Unity doesn't show compile errors in the thread. need
                    // to guess it. it only shows:
                    //   Delegate `System.Threading.ParameterizedThreadStart' does not take `0' arguments
                    // if there is any error below.
                    Thread thread = new Thread(() =>
                    {
                        Logger.Log("Server: started listener thread for connectionId=" + connectionId);

                        // wrap each client in a try/catch, so that one client error won't bring the whole thing down
                        try
                        {
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
                            byte[] header = new byte[2]; // only create once to avoid allocations
                            while (true)
                            {
                                // read exactly 2 bytes for header (blocking)
                                if (!GoodOldCommon.ReadExactly(stream, header, 2))
                                    break;
                                ushort size = BitConverter.ToUInt16(header, 0);
                                //Logger.Log("Received size header: " + size);

                                // read exactly 'size' bytes for content (blocking)
                                byte[] content = new byte[size];
                                if (!GoodOldCommon.ReadExactly(stream, content, size))
                                    break;
                                //Logger.Log("Received content: " + BitConverter.ToString(content));

                                // queue it and show a warning if the queue starts to get big
                                messageQueue.Enqueue(new GoodOldMessage(connectionId, GoodOldEventType.Data, content));
                                if (messageQueue.Count > 10000)
                                    Logger.LogWarning("Server: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
                            }

                            Logger.Log("Server: finished client thread for connectionId=" + connectionId);
                        }
                        catch (Exception exception)
                        {
                            // just catch it. code below will handle it.
                            Logger.Log("Server: stopped client thread for connectionId=" + connectionId + " reason:" + exception);
                        }

                        // if we got here then either the client while loop ended, or an exception happened.
                        // disconnect and clean up no matter what
                        messageQueue.Enqueue(new GoodOldMessage(connectionId, GoodOldEventType.Disconnected, null));
                        stream.Close();
                        client.Close();
                        clients.Remove(connectionId);
                    });
                    thread.Start();

                    // add to dict now
                    clients.Add(connectionId, client);

                    // TODO when to dispose the client?
                }
            }
            catch (ThreadAbortException abortException)
            {
                // UnityEditor causes AbortException if thread is still running
                // when we press Play again next time. that's okay.
                Logger.Log("Server thread aborted. That's okay. " + abortException.ToString());
            }
            catch (SocketException socketException)
            {
                // calling StopServer will interrupt this thread with a
                // 'SocketException: interrupted'. that's okay.
                Logger.Log("Server Thread stopped. That's okay. " + socketException.ToString());
            }
            catch (Exception exception)
            {
                // something else went wrong. probably important.
                Logger.LogError("Server Exception: " + exception);
            }
        });
        listenerThread.IsBackground = true;
        listenerThread.Start();
    }

    public static void StopServer()
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
            // this is supposed to disconnect gracefully, but the blocking Read
            // calls throw a 'Read failure' exception instead of returning 0.
            // (maybe it's Unity? maybe Mono?)
            client.GetStream().Close();
            client.Close();
        }

        // clear clients list
        clients.Clear();
    }

    // Send message to client using socket connection.
    public static void Send(uint connectionId, byte[] data)
    {
        // find the connection
        TcpClient client;
        if (clients.TryGetValue(connectionId, out client))
        {
            GoodOldCommon.SendBytesAndSize(client.GetStream(), data);
        }
        else Logger.LogWarning("Server.Send: invalid connectionId: " + connectionId);
    }
}