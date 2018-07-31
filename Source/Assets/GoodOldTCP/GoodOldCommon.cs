using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

public static class GoodOldCommon
{
    // send bytes (via stream) with the <size,content> message structure
    public static void SendBytesAndSize(NetworkStream stream, byte[] data)
    {
        //Debug.Log("SendBytesAndSize: " + BitConverter.ToString(data));
        if (data.Length > ushort.MaxValue)
        {
            Debug.LogError("Client.Send: message too big(" + data.Length + ") max=" + ushort.MaxValue);
            return;
        }

        // write size header and data
        BinaryWriter writer = new BinaryWriter(stream); // TODO just use stream?
        writer.Write((ushort)data.Length);
        writer.Write(data);
        writer.Flush();
    }

    // helper function to read EXACTLY 'n' bytes
    // -> default .Read reads up to 'n' bytes. this function reads exactly 'n'
    //    bytes
    // -> this is blocking obviously
    // -> immediately returns false in case of disconnects
    public static bool ReadExactly(NetworkStream stream, byte[] buffer, int amount)
    {
        // there might not be enough bytes in the TCP buffer for .Read to read
        // the whole amount at once, so we need to keep trying until we have all
        // the bytes (blocking)
        //
        // note: this just is a faster version of reading one after another:
        //     for (int i = 0; i < amount; ++i)
        //         if (stream.Read(buffer, i, 1) == 0)
        //             return false;
        //     return true;
        int bytesRead = 0;
        while (bytesRead < amount)
        {
            // read up to 'remaining' bytes
            int remaining = amount - bytesRead;
            int result = stream.Read(buffer, bytesRead, remaining);

            // .Read returns null if disconnected
            if (result == 0)
                return false;

            // otherwise add to bytes read
            bytesRead += result;
        }
        return true;
    }
}
