using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using UnityEngine;

public static class GoodOldTCPClient
{
    static TcpClient socket;
    static Thread listenerThread;

    // stream (with BinaryWriter for easier sending)
    static NetworkStream stream;
    static BinaryWriter writer;


    // store incoming messages in a thread safe queue, so we can safely process
    // them from Unity's Update function
    static SafeQueue<byte[]> messageQueue = new SafeQueue<byte[]>();

    public static bool Connected { get { return socket != null && socket.Connected; } }

    // removes and returns the oldest message from the message queue.
    // (might want to call this until it doesn't return anything anymore)
    // only returns one message each time so it's more similar to LLAPI:
    // https://docs.unity3d.com/ScriptReference/Networking.NetworkTransport.Receive.html
    public static bool GetNextMessage(out byte[] data)
    {
        if (messageQueue.TryDequeue(out data))
        {
            return true;
        }
        data = null;
        return false;
    }

    public static void Connect(string ip, int port)
    {
        if (listenerThread == null)
        {
            Debug.Log("Client: connecting");
            socket = new TcpClient(ip, port);

            // Get a stream object for reading
            // note: 'using' sucks here because it will try to dispose after thread was started
            // but we still need it in the thread
            stream = socket.GetStream();
            writer = new BinaryWriter(stream);

            listenerThread = new Thread(() =>
            {
                Debug.Log("Client: started listener thread");

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
                    messageQueue.Enqueue(content);
                    if (messageQueue.Count > 10000)
                        Debug.LogWarning("Server: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
                }

                Debug.Log("Client: finished thread");

                // clean up
                stream.Close();
                lock(listenerThread)
                {
                    listenerThread = null;
                }
                // TODO call onDisconnect(conn) if we got here?
            });
            listenerThread.IsBackground = true;
            listenerThread.Start();
        }
    }

    public static void Send(byte[] data)
    {
        if (Connected)
        {
            if (data.Length > ushort.MaxValue)
            {
                Debug.LogError("Client.Send: message too big(" + data.Length + ") max=" + ushort.MaxValue);
                return;
            }

            //Debug.Log("Client.Send: " + BitConverter.ToString(data));
            // write size header and data
            writer.Write((ushort)data.Length);
            writer.Write(data);
            writer.Flush();
        }
        else Debug.LogWarning("Client.Send: not connected!");
    }
}