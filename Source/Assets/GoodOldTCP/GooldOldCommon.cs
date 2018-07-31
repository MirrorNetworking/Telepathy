using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class GooldOldCommon
{
    // try processing as many messages as possible from unprocessed bytes
    public static List<byte[]> ExtractMessages(ref MemoryStream unprocessedBytes)
    {
        List<byte[]> result = new List<byte[]>();

        while (true)
        {
            // size header fully received yet?
            if (unprocessedBytes.Length >= 2)
            {
                // read size header (2 bytes)
                unprocessedBytes.Position = 0;
                byte[] header = new byte[2];
                unprocessedBytes.Read(header, 0, 2);
                ushort size = BitConverter.ToUInt16(header, 0);
                //Debug.Log("read size header: " + size);

                // did we receive the full content yet? (header+content)
                if (unprocessedBytes.Length >= size + 2)
                {
                    // read the content
                    unprocessedBytes.Position = 2;
                    byte[] data = new byte[size];
                    unprocessedBytes.Read(data, 0, size);
                    result.Add(data);
                    //Debug.Log("read content: " + BitConverter.ToString(data));

                    // put remaining bytes into a new memory stream
                    long remainingBytes = unprocessedBytes.Length - (size + 2);
                    byte[] remainder = new byte[remainingBytes];
                    unprocessedBytes.Read(remainder, 0, (int)remainingBytes);
                    unprocessedBytes = new MemoryStream(); // empty constructor for variable sized buffer
                    unprocessedBytes.Write(remainder, 0 , remainder.Length);
                    //Debug.Log("remaining bytes: " + BitConverter.ToString(unprocessedBytes.ToArray()));
                }
                else break;
            }
            else break;
        }

        return result;
    }
}
