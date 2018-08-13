using System.IO;
using System.Net.Sockets;

public static class NetworkStreamExtensions
{
    // .Read returns '0' if remote closed the connection but throws an
    // IOException if we voluntarily closed our own connection.
    //
    // lets's add a ReadSafely method that returns '0' in both cases so we don't
    // have to worry about exceptions, since a disconnect is a disconnect...
    public static int ReadSafely(this NetworkStream stream, byte[] buffer, int offset, int size)
    {
        try
        {
            return stream.Read(buffer, offset, size);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    // helper function to read EXACTLY 'n' bytes
    // -> default .Read reads up to 'n' bytes. this function reads exactly 'n'
    //    bytes
    // -> this is blocking until 'n' bytes were received
    // -> immediately returns false in case of disconnects
    public static bool ReadExactly(this NetworkStream stream, byte[] buffer, int amount)
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
            // read up to 'remaining' bytes with the 'safe' read extension
            int remaining = amount - bytesRead;
            int result = stream.ReadSafely(buffer, bytesRead, remaining);

            // .Read returns 0 if disconnected
            if (result == 0)
                return false;

            // otherwise add to bytes read
            bytesRead += result;
        }
        return true;
    }

    // static helper functions /////////////////////////////////////////////
    // fast ushort to byte[] conversion and vice versa
    // -> test with 100k conversions:
    //    BitConverter.GetBytes(ushort): 144ms
    //    bit shifting: 11ms
    // -> 10x speed improvement makes this optimization actually worth it
    // -> this way we don't need to allocate BinaryWriter/Reader either
    public static int ReadUshort(this NetworkStream stream)
    {
        byte[] bytes = new byte[2];

        if (!stream.ReadExactly(bytes, 2))
            return -1;

        return (ushort)((bytes[1] << 8) + bytes[0]);
    }

    public static void Write(this NetworkStream stream, ushort value)
    {
        byte [] bytes =  new byte[]
        {
                (byte)value,
                (byte)(value >> 8)
        };

        stream.Write(bytes, 0, bytes.Length);
    }

}
