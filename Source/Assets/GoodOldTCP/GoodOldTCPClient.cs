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

                // store current message in here, not globally, so we can't
                // access it and risk any deadlocks etc.
                MemoryStream unprocessedBytes = new MemoryStream();

                // keep reading
                int length;
                byte[] buffer = new byte[4096];
                while ((length = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    // add to unprocessed bytes
                    unprocessedBytes.Write(buffer, 0, length);
                    //Debug.LogWarning("read raw. unprocessedBytes: " + BitConverter.ToString(unprocessedBytes.ToArray()));

                    // try processing as many messages as possible from unprocessed bytes
                    List<byte[]> extracted = GooldOldCommon.ExtractMessages(ref unprocessedBytes);
                    for (int i = 0; i < extracted.Count; ++i)
                        messageQueue.Enqueue(extracted[i]);

                    //  show warning if it gets too big. something is off then.
                    if (messageQueue.Count > 10000)
                        Debug.LogWarning("Client: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
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