using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

public static class GoodOldCommon
{
    // helper function to read EXACTLY 'n' bytes
    // -> default .Read reads up to 'n' bytes. this function reads exactly 'n'
    //    bytes
    // -> this is blocking obviously
    // -> immediately returns false in case of disconnects
    public static bool ReadExactly(NetworkStream stream, byte[] buffer, int amount)
    {
        // simple for loop, reading one byte after another
        // TODO read more at once later.
        for (int i = 0; i < amount; ++i)
            if (stream.Read(buffer, i, 1) == 0)
                return false;
        return true;
    }
}
