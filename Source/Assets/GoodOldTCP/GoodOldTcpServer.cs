using System;
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

                        // store current message in here, not globally, so we can't
                        // access it and risk any deadlocks etc.
                        MemoryStream unprocessedBytes = new MemoryStream();

                        // keep reading
                        int length;
                        byte[] buffer = new byte[4096];
                        while ((length = stream.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            // resize so we don't have any dead bytes ever
                            //Array.Resize(ref buffer, length);

                            // add to unprocessed bytes
                            unprocessedBytes.Write(buffer, 0, length);
                            //Debug.Log("Server: added to unprocessed: " + length + " unprocessed size is now: " + unprocessedBytes.Position);

                            // did we just completely receive a message?
                            // (needs at least the 'ushort size' header
                            if (unprocessedBytes.Position >= 2)
                            {
                                // go to position 0, read the size, go back
                                long oldPosition = unprocessedBytes.Position;
                                unprocessedBytes.Position = 0;

                                byte[] lengthBytes = new byte[2];
                                unprocessedBytes.Read(lengthBytes, 0, 2);
                                ushort expectedLength = BitConverter.ToUInt16(lengthBytes, 0);
                                //Debug.Log("  Server: can now read expected size:" + expectedLength);

                                unprocessedBytes.Position = oldPosition;

                                // does unprocessed bytes contain size+message? (size is short, so +2)
                                if (unprocessedBytes.Position >= expectedLength + 2)
                                {
                                    //Debug.Log("  Server: unprocessed copying bytes: " + expectedLength);

                                    // set position to after size header, then copy the expected size
                                    unprocessedBytes.Position = 2;
                                    byte[] data = new byte[expectedLength];
                                    unprocessedBytes.Read(data, 0, expectedLength);
                                    messageQueue.Enqueue(new ConnectionMessage(connectionId, data));

                                    // show a warning if the message queue gets too big
                                    // because something is off then.
                                    if (messageQueue.Count > 10000)
                                    {
                                        Debug.LogWarning("Server: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
                                    }

                                    // reset unprocessed bytes
                                    unprocessedBytes.Position = 0;

                                    // TODO process message
                                    //Debug.Log("Server process msg: length=" + data.Length + " bytes=" + BitConverter.ToString(data));
                                }
                            }
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
                Debug.LogWarning("SocketException " + socketException.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("other exception: " + exception);
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