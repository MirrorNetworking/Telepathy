using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

public static class GoodOldCommon
{
    // send bytes (via stream) with the <size,content> message structure
    public static void SendBytesAndSize(NetworkStream stream, byte[] data)
    {
        //Logger.Log("SendBytesAndSize: " + BitConverter.ToString(data));

        // can we still write to this socket (not disconnected?)
        if (!stream.CanWrite)
        {
            Logger.LogWarning("Send: stream not writeable: " + stream);
            return;
        }

        // check size
        if (data.Length > ushort.MaxValue)
        {
            Logger.LogError("Send: message too big(" + data.Length + ") max=" + ushort.MaxValue);
            return;
        }

        // write size header and data
        BinaryWriter writer = new BinaryWriter(stream); // TODO just use stream?
        writer.Write((ushort)data.Length);
        writer.Write(data);
        writer.Flush();
    }
}
