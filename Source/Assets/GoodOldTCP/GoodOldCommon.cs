using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

public static class GoodOldCommon
{
    // send message (via stream) with the <size,content> message structure
    public static void SendMessage(NetworkStream stream, byte[] content)
    {
        //Logger.Log("SendMessage: " + BitConverter.ToString(data));

        // can we still write to this socket (not disconnected?)
        if (!stream.CanWrite)
        {
            Logger.LogWarning("Send: stream not writeable: " + stream);
            return;
        }

        // check size
        if (content.Length > ushort.MaxValue)
        {
            Logger.LogError("Send: message too big(" + content.Length + ") max=" + ushort.MaxValue);
            return;
        }

        // write size header and content
        BinaryWriter writer = new BinaryWriter(stream); // TODO just use stream?
        writer.Write((ushort)content.Length);
        writer.Write(content);
        writer.Flush();
    }

    // read message (via stream) with the <size,content> message structure
    public static bool ReadMessageBlocking(NetworkStream stream, out byte[] content)
    {
        content = null;

        // read exactly 2 bytes for header (blocking)
        byte[] header = new byte[2];
        if (!stream.ReadExactly(header, 2))
            return false;
        ushort size = BitConverter.ToUInt16(header, 0);
        //Logger.Log("Received size header: " + size);

        // read exactly 'size' bytes for content (blocking)
        content = new byte[size];
        if (!stream.ReadExactly(content, size))
            return false;
        //Logger.Log("Received content: " + BitConverter.ToString(content));

        return true;
    }
}
