using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Telepathy
{
    public class Client : Common
    {
        // ManualResetEvent instances signal completion.
        static ManualResetEvent connectDone = new ManualResetEvent(false);
        //static ManualResetEvent sendDone =
        //    new ManualResetEvent(false);
        //static ManualResetEvent receiveDone =
        //    new ManualResetEvent(false);

        Socket socket;

        public bool Connected
        {
            get { return socket != null && socket.Connected; }
        }

        public bool Connecting
        {
            get { return socket != null && !socket.Connected; } // TODO does this work? maybe try connectDone event?
        }

        public bool Connect(string ip, int port, int timeoutSeconds = 6)
        {
            // not if already started
            if (Connected) return false;

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

                // Connect to the remote endpoint.
                // TODO timeout again. or return immediately and wait for the connect to finish
                socket.BeginConnect(remoteEP,
                    new AsyncCallback(ConnectCallback), socket);
                connectDone.WaitOne();

                // add connected event to queue
                messageQueue.Enqueue(new Message(0, EventType.Connected, null));

                // start receive loop
                Receive(socket);

                return true;
            }
            catch (Exception e)
            {
                Logger.Log("Client Connect failed: " + e);
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
            }
            catch (Exception e)
            {
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
                    new AsyncCallback(ReadHeaderCallback), state);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
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
            // Release the socket.
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
            Logger.Log("Disconnected");
        }
    }
}