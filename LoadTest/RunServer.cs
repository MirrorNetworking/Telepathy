using System;
using System.Diagnostics;
using System.Threading;

namespace Telepathy.LoadTest
{
    public class RunServer
    {
        public static void StartServer(int port)
        {
            long messagesReceived = 0;
            long dataReceived = 0;

            // start server
            Server server = new Server();

            // dispatch the events from the server
            server.OnConnected += (id) => { };
            server.OnDisconnected += (id) => { };
            server.OnReceivedData += (id, data) =>
            {
                server.Send(id, data); // reply
                ++messagesReceived;
                dataReceived += data.Length;
            };
            server.OnReceivedError += (id, exception) => { Logger.LogError("sv error:" + exception); };

            server.Start(port);
            int serverFrequency = 60;
            Logger.Log("started server");

            Stopwatch stopwatch = Stopwatch.StartNew();

            while (true)
            {
                // sleep
                Thread.Sleep(1000 / serverFrequency);

                // report every 2 seconds
                if (stopwatch.ElapsedMilliseconds > 1000 * 2)
                {
                    Logger.Log(string.Format("Server in={0} ({1} KB/s)  out={0} ({1} KB/s)", messagesReceived, (dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024))));
                    stopwatch.Stop();
                    stopwatch = Stopwatch.StartNew();
                    messagesReceived = 0;
                    dataReceived = 0;
                }
            }
        }
    }
}
