// IMPORTANT
// force all thread functions to be STATIC.
// => Common.Send/ReceiveLoop is EXTREMELY DANGEROUS because it's too easy to
//    accidentally share Common state between threads.
// => header buffer, payload etc. were accidentally shared once after changing
//    the thread functions from static to non static
// => C# does not automatically detect data races. best we can do is move all of
//    our thread code into static functions and pass all state into them
//
// let's even keep them in a STATIC CLASS so it's 100% obvious that this should
// NOT EVER be changed to non static!

using System;
using System.Net.Sockets;

namespace Telepathy
{
    public static class ThreadFunctions
    {
        // send message (via stream) with the <size,content> message structure
        // this function is blocking sometimes!
        // (e.g. if someone has high latency or wire was cut off)
        // -> payload is of multiple <<size, content, size, content, ...> parts
        public static bool SendMessagesBlocking(NetworkStream stream, byte[] payload, int packetSize)
        {
            // stream.Write throws exceptions if client sends with high
            // frequency and the server stops
            try
            {
                // write the whole thing
                stream.Write(payload, 0, packetSize);
                return true;
            }
            catch (Exception exception)
            {
                // log as regular message because servers do shut down sometimes
                Log.Info("Send: stream.Write exception: " + exception);
                return false;
            }
        }
        // read message (via stream) blocking.
        // writes into byte[] and returns bytes written to avoid allocations.
        public static bool ReadMessageBlocking(NetworkStream stream, int MaxMessageSize, byte[] headerBuffer, byte[] payloadBuffer, out int size)
        {
            size = 0;

            // buffer needs to be of Header + MaxMessageSize
            if (payloadBuffer.Length != 4 + MaxMessageSize)
            {
                Log.Error($"ReadMessageBlocking: payloadBuffer needs to be of size 4 + MaxMessageSize = {4 + MaxMessageSize} instead of {payloadBuffer.Length}");
                return false;
            }

            // read exactly 4 bytes for header (blocking)
            if (!stream.ReadExactly(headerBuffer, 4))
                return false;

            // convert to int
            size = Utils.BytesToIntBigEndian(headerBuffer);

            // protect against allocation attacks. an attacker might send
            // multiple fake '2GB header' packets in a row, causing the server
            // to allocate multiple 2GB byte arrays and run out of memory.
            //
            // also protect against size <= 0 which would cause issues
            if (size > 0 && size <= MaxMessageSize)
            {
                // read exactly 'size' bytes for content (blocking)
                return stream.ReadExactly(payloadBuffer, size);
            }
            Log.Warning("ReadMessageBlocking: possible header attack with a header of: " + size + " bytes.");
            return false;
        }
    }
}