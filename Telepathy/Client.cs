using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Telepathy
{
    public class Client : Common
    {
        public event Action OnConnected;
        public event Action<byte[]> OnReceivedData;
        public event Action OnDisconnected;
        public event Action<Exception> OnReceivedError;

        public TcpClient client;

        public bool Connecting { get; private set; }
        public bool Connected { get; private set; }

        public async void Connect(string host, int port)
        {
            // not if already started
            if (client != null)
            {
                // paul:  exceptions are better than silence
                OnReceivedError?.Invoke(new Exception("Client already connected"));
                return;
            }

            // We are connecting from now until Connect succeeds or fails
            Connecting = true;

            try
            {
                // TcpClient can only be used once. need to create a new one each
                // time.
                client = new TcpClient(AddressFamily.InterNetworkV6);
                // works with IPv6 and IPv4
                client.Client.DualMode = true;

                // NoDelay disables nagle algorithm. lowers CPU% and latency
                // but increases bandwidth
                client.NoDelay = NoDelay;

                await client.ConnectAsync(host, port);

                // now we are connected:
                Connected = true;
                Connecting = false;

                OnConnected?.Invoke();
                await ReceiveLoop(client);
            }
            catch (ObjectDisposedException)
            {
                // No error, the client got closed
            }
            catch (Exception ex)
            {
                OnReceivedError?.Invoke(ex);
            }
            finally
            {
                Disconnect();
                OnDisconnected?.Invoke();
            }
        }

        async Task ReceiveLoop(TcpClient client)
        {
            using (NetworkStream networkStream = client.GetStream())
            {
                while (true)
                {
                    byte[] data = await ReadMessageAsync(networkStream);

                    if (data == null)
                        break;

                    // we received some data, raise event
                    OnReceivedData?.Invoke(data);
                }
            }
        }

        public void Disconnect()
        {
            // only if started
            if (client != null)
            {
                // close client
                client.Close();
                client = null;
                Connecting = false;
                Connected = false;
            }
        }

        // send the data or throw exception
        public async void Send(byte[] data)
        {
            if (client == null)
            {
                OnReceivedError?.Invoke(new SocketException((int)SocketError.NotConnected));
                return;
            }

            try
            {
                await SendMessage(client.GetStream(), data);
            }
            catch (Exception ex)
            {
                Disconnect();
                OnReceivedError?.Invoke(ex);
            }
        }
    }
}
