using System;
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
                // TODO needs to construct his messages too!!!!!!!!
                /*while (reader != null)
                {
                    Message msg = Message.ReadFromStream(reader);
                    messageQueue.Enqueue(msg);
                }
                */

                Debug.Log("Client: started listener thread");

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
                            messageQueue.Enqueue(data);

                            // show a warning if the message queue gets too big
                            // because something is off then.
                            if (messageQueue.Count > 10000)
                            {
                                Debug.LogWarning("Client: messageQueue is getting big(" + messageQueue.Count + "), try calling GetNextMessage more often. You can call it more than once per frame!");
                            }

                            // reset unprocessed bytes
                            unprocessedBytes.Position = 0;

                            // TODO process message
                            //Debug.Log("Server process msg: length=" + data.Length + " bytes=" + BitConverter.ToString(data));
                        }
                    }
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

            // write size header and data
            writer.Write((ushort)data.Length);
            writer.Write(data);
            writer.Flush();
        }
        else Debug.LogWarning("Client.Send: not connected!");
    }
}