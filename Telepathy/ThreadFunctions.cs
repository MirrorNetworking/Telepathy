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
    }
}