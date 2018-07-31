using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

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
    struct ConnectionMessage
    {
        public uint connectionId;
        public byte[] data;
        public ConnectionMessage(uint connectionId, byte[] data)
        {
            this.connectionId = connectionId;
            this.data = data;
        }
    }
    static SafeQueue<ConnectionMessage> messageQueue = new SafeQueue<ConnectionMessage>(); // accessed from getmessage and listener thread

    // removes and returns the oldest message from the message queue.
    // (might want to call this until it doesn't return anything anymore)
    // only returns one message each time so it's more similar to LLAPI:
    // https://docs.unity3d.com/ScriptReference/Networking.NetworkTransport.ReceiveFromHost.html
    public static bool GetNextMessage(out uint connectionId, out byte[] data)
    {
        ConnectionMessage cm;
        if (messageQueue.TryDequeue(out cm))
        {
            connectionId = cm.connectionId;
            data = cm.data;
            return true;
        }

        connectionId = 0;
        data = null;
        return false;
    }

    public static bool Active { get { return listener != null; } }

    // Runs in background TcpServerThread; Handles incomming TcpClient requests
    // IMPORTANT: Debug.Log is only shown in log file, not in console

    public static void StartServer(int port)
    {
        // start the listener thread
        Debug.Log("Server: starting...");
        listenerThread = new Thread(() =>
        {
            try
            {
                // start listener
                listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
                listener.Start();
                Debug.Log("Server is listening");

                // keep accepting new clients
                while (true) // TODO while(listen)
                {
                    // wait and accept new client
                    // note: 'using' sucks here because it will try to dispose after
                    // thread was started but we still need it in the thread
                    TcpClient socket = listener.AcceptTcpClient();
                    if (nextId == uint.MaxValue)
                    {
                        Debug.LogError("Server can't accept any more clients, out of ids.");
                        break;
                    }
                    uint connectionId = nextId++; // TODo is this even thread safe?
                    Debug.Log("Server: client connected. connectionId=" + connectionId);

                    // Get a stream object for reading
                    // note: 'using' sucks here because it will try to dispose after thread was started
                    // but we still need it in the thread
                    NetworkStream stream = socket.GetStream();

                    // spawn a thread for each client to listen to his messages
                    // NOTE: Unity doesn't show compile errors in the thread. need
                    // to guess it. it only shows:
                    //   Delegate `System.Threading.ParameterizedThreadStart' does not take `0' arguments
                    // if there is any error below.
                    Thread thread = new Thread(() =>
                    {
                        Debug.Log("Server: started listener thread for connectionId=" + connectionId);

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
                            //Debug.Log("Received size header: " + size);

                            // read exactly 'size' bytes for content (blocking)
                            byte[] content = new byte[size];
                            if (!GoodOldCommon.ReadExactly(stream, content, size))
                                break;
                            //Debug.Log("Received content: " + BitConverter.ToString(content));

                            // queue it and show a warning if the queue starts to get big
                            messageQueue.Enqueue(new ConnectionMessage(connectionId, content));
                            if (messageQueue.Count > 10000)
                                Debug.LogWarning("Server: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
                        }

                        Debug.Log("Server: finished client thread for connectionId=" + connectionId);

                        // clean up
                        stream.Close();

                        // TODO call onDisconnect(conn) if we got here?
                    });
                    thread.Start();

                    // add to dict now
                    clients.Add(connectionId, socket);

                    // TODO when to dispose the client?
                }
            }
            catch (SocketException socketException)
            {
                Debug.LogWarning("Server SocketException " + socketException.ToString());
            }
            catch (ThreadAbortException abortException)
            {
                // in the editor, this thread is only stopped via abort exception
                // after pressing play again the next time. and that's okay.
                Debug.Log("Server thread aborted. That's okay. " + abortException.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Server Exception: " + exception);
            }
        });
        listenerThread.IsBackground = true;
        listenerThread.Start();
    }

    // Send message to client using socket connection.
    public static void Send(byte[] bytes)
    {
        Debug.Log("TODO: to which client?");
    }
}