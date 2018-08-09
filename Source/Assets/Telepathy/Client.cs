using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.IO;

namespace Telepathy
{
    public class Client : Common
    {
        TcpClient client;
        Thread listenerThread;

        // stream (with BinaryWriter for easier sending)
        NetworkStream stream;

        public bool Connected { get { return listenerThread != null && listenerThread.IsAlive; } }

        public bool Connect(string ip, int port, int timeoutSeconds = 6)
        {
            // not if already started
            if (Connected) return false;

            Logger.Log("Client: connecting to ip=" + ip + " port=" + port);

            // use async connect so we can specify a timeout. if we use
            // new TcpClient(ip, port) then we can't modify the timeout. deafult is
            // way too long there.
            try
            {
                client = new TcpClient();
                IAsyncResult result = client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds));

                // time elapsed for one reason or another. are we now connect, or not?
                if (!success || !client.Connected)
                {
                    Logger.Log("Client: failed to connect to ip=" + ip + " port=" + port + " after " + timeoutSeconds + "s");
                    client.Close(); // clean up properly before exiting, otherwise Unity freezes for 30s when rebuilding next time
                    return false;
                }
                client.EndConnect(result);
            }
            catch (SocketException socketException)
            {
                // this happens if (for example) the IP address is correct but there
                // is no server running on that IP/Port
                Logger.Log("Client: failed to connect to ip=" + ip + " port=" + port + " reason=" + socketException);
                client.Close(); // clean up properly before exiting
                return false;
            }

            // Get a stream object for reading
            // note: 'using' sucks here because it will try to dispose after thread was started
            // but we still need it in the thread
            stream = client.GetStream();

            listenerThread = new Thread(() =>
            {
                // run the receive loop
                ReceiveLoop(messageQueue, 0, client, stream);
            });
            listenerThread.IsBackground = true;
            listenerThread.Start();
            return true;
        }

        public void Disconnect()
        {
            // only if started
            if (!Connected) return;

            Logger.Log("Client: disconnecting");

            // this is supposed to disconnect gracefully, but the blocking Read
            // calls throw a 'Read failure' exception instead of returning 0.
            // (maybe it's Unity? maybe Mono?)
            stream.Close();
            client.Close();

            // clear queue just to be sure that nothing old is processed when
            // starting again
            messageQueue.Clear();
        }

        public void Send(byte[] data)
        {
            if (Connected)
            {
                SendMessage(stream, data);
            }
            else Logger.LogWarning("Client.Send: not connected!");
        }
    }
}