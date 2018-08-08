using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.IO;

public static class GoodOldTCPClient
{
    static TcpClient client;
    static Thread listenerThread;

    // stream (with BinaryWriter for easier sending)
    static NetworkStream stream;

    // incoming message queue of <connectionId, message>
    // (not a HashSet because one connection can have multiple new messages)
    static SafeQueue<GoodOldMessage> messageQueue = new SafeQueue<GoodOldMessage>(); // accessed from getmessage and listener thread

    // removes and returns the oldest message from the message queue.
    // (might want to call this until it doesn't return anything anymore)
    // only returns one message each time so it's more similar to LLAPI:
    // https://docs.unity3d.com/ScriptReference/Networking.NetworkTransport.Receive.html
    // -> Connected, Data, Disconnected can all be detected with this function. simple and stupid.
    public static bool GetNextMessage(out GoodOldEventType eventType, out byte[] data)
    {
        GoodOldMessage message;
        if (messageQueue.TryDequeue(out message))
        {
            eventType = message.eventType;
            data = message.data;
            return true;
        }

        eventType = GoodOldEventType.Disconnected;
        data = null;
        return false;
    }

    public static bool Connected { get { return listenerThread != null && listenerThread.IsAlive; } }

    public static bool Connect(string ip, int port, int timeoutSeconds = 6)
    {
        // not if already started
        if (Connected) return false;

        Logger.Log("Client: connecting to ip=" + ip + " port=" + port);

        // use async connect so we can specify a timeout. if we use
        // new TcpClient(ip, port) then we can't modify the timeout. deafult is
        // way too long there.
        try
        {
            client = new TcpClient();
            IAsyncResult result = client.BeginConnect(ip, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds));

            // time elapsed for one reason or another. are we now connect, or not?
            if (!success || !client.Connected)
            {
                Logger.Log("Client: failed to connect to ip=" + ip + " port=" + port + " after " + timeoutSeconds + "s");
                client.Close(); // clean up properly before exiting, otherwise Unity freezes for 30s when rebuilding next time
                return false;
            }
            client.EndConnect(result);
        }
        catch (SocketException socketException)
        {
            // this happens if (for example) the IP address is correct but there
            // is no server running on that IP/Port
            Logger.Log("Client: failed to connect to ip=" + ip + " port=" + port + " reason=" + socketException);
            client.Close(); // clean up properly before exiting
            return false;
        }

        // Get a stream object for reading
        // note: 'using' sucks here because it will try to dispose after thread was started
        // but we still need it in the thread
        stream = client.GetStream();

        listenerThread = new Thread(() =>
        {
            // absolutely must wrap with try/catch, otherwise thread exceptions
            // are silent
            try
            {
                Logger.Log("Client: started listener thread");

                // add connected event to queue
                messageQueue.Enqueue(new GoodOldMessage(0, GoodOldEventType.Connected, null));

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
                    messageQueue.Enqueue(new GoodOldMessage(0, GoodOldEventType.Data, content));
                    if (messageQueue.Count > 10000)
                        Logger.LogWarning("Server: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
                }
            }
            catch (ThreadAbortException abortException)
            {
                // in the editor, this thread is only stopped via abort exception
                // after pressing play again the next time. and that's okay.
                Logger.Log("Client thread aborted. That's okay. " + abortException.ToString());
            }
            catch (SocketException socketException)
            {
                // happens because closing the client gracefully in Disconnect
                // doesn't seem to work with Unity/Mono. let's not throw an error,
                // a warning should do.
                Logger.LogWarning("Client SocketException " + socketException.ToString());
            }
            catch (Exception exception)
            {
                Logger.LogError("Client exception:" + exception);
            }
            Logger.Log("Client: finished thread");

            // if we got here then either the client while loop ended, or an exception happened.
            // disconnect and clean up no matter what
            messageQueue.Enqueue(new GoodOldMessage(0, GoodOldEventType.Disconnected, null));
            stream.Close();
            client.Close();
            lock(listenerThread)
            {
                listenerThread = null;
            }
        });
        listenerThread.IsBackground = true;
        listenerThread.Start();
        return true;
    }

    public static void Disconnect()
    {
        // only if started
        if (!Connected) return;

        Logger.Log("Client: disconnecting");

        // this is supposed to disconnect gracefully, but the blocking Read
        // calls throw a 'Read failure' exception instead of returning 0.
        // (maybe it's Unity? maybe Mono?)
        stream.Close();
        client.Close();

        // clear queue just to be sure that nothing old is processed when
        // starting again
        messageQueue.Clear();
    }

    public static void Send(byte[] data)
    {
        if (Connected)
        {
            GoodOldCommon.SendBytesAndSize(stream, data);
        }
        else Logger.LogWarning("Client.Send: not connected!");
    }
}