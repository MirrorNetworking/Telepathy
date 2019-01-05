using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : Common
    {
        // connect done event:
        //   false while connecting
        //   true before/afterwards
        static ManualResetEvent connectDone = new ManualResetEvent(true);

        // the socket
        Socket socket;

        public bool Connected
        {
            get { return socket != null && socket.Connected; }
        }

        // check the connectDone event for connecting status:
        //   WaitOne(0) simply returns the internal state, which is false while
        //   connecting and true otherwise
        public bool Connecting { get { return !connectDone.WaitOne(0); }}

        public bool Connect(string ip, int port, int timeoutSeconds = 6)
        {
            // not if already started
            if (Connecting || Connected) return false;

            // Connect to a remote device.
            try
            {
                // localhost support so .Parse doesn't throw errors
                if (ip.ToLower() == "localhost") ip = "127.0.0.1";

                // Establish the remote endpoint for the socket.
                // The name of the
                // remote device is "host.contoso.com".
                //IPHostEntry ipHostInfo = Dns.GetHostEntry("host.contoso.com");
                IPAddress ipAddress = IPAddress.Parse(ip);
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, port);

                // Create a TCP/IP socket.
                socket = new Socket(ipAddress.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = NoDelay;

                // reset the event so we can wait for it again if needed
                connectDone.Reset();

                // Connect to the remote endpoint.
                // TODO timeout again. or return immediately and wait for the connect to finish
                socket.BeginConnect(remoteEP, ConnectCallback, socket);
                //connectDone.WaitOne(); <- don't wait. return immediately. we have Connecting() to check status

                return true;
            }
            catch (Exception e)
            {
                Logger.Log("Client Connect failed: " + e);
                connectDone.Set(); // set it, so we don't wait for it anywhere else (e.g. in Disconnect)
                return false;
            }
        }

        void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket) ar.AsyncState;

                // Complete the connection.
                client.EndConnect(ar);

                //Logger.Log("Socket connected to: " +client.RemoteEndPoint);

                // Signal that the connection has been made.
                connectDone.Set();

                // add connected event to queue
                messageQueue.Enqueue(new Message(0, EventType.Connected, null));

                // start receive loop
                Receive(socket);
            }
            catch (Exception e)
            {
                connectDone.Set(); // reset no matter what. we aren't connecting anymore.
                Logger.Log("Client ConnectCallback error: " + e);
            }
        }

        void Receive(Socket client)
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = client;

                // start receiving the 4 header bytes
                client.BeginReceive(state.header, 0, 4, 0,
                    ReadHeaderCallback, state);
            }
            catch (Exception e)
            {
                Logger.LogError(e.ToString());
            }
        }

        public bool Send(byte[] data)
        {
            if (Connected)
            {
                Send(socket, data);
                return true;
            }
            Logger.LogWarning("Client.Send: not connected!");
            return false;
        }

        public void Disconnect()
        {
            // only if started
            if (Connecting || Connected)
            {
                // is there a connect in progress? then wait until finished
                // this is the only way to guarantee that we can call Connect()
                // again immediately after Disconnect
                connectDone.WaitOne();

                // Release the socket.
                CloseSafely(socket);

                //Logger.Log("Client Disconnected");
            }
        }
    }
}