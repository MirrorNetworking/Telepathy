using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;

namespace Telepathy.LoadTest
{
    public class RunClients
    {
        public static void StartClients(string host, int port, int clientAmount)
        {
            Logger.LogError("starting " + clientAmount + " clients...");

            // start n clients and get queue messages all in this thread
            string message = "Sometimes we just need a good networking library";
            byte[] messageBytes = Encoding.ASCII.GetBytes(message);
            int clientFrequency = 14;
            List<Client> clients = new List<Client>();
            for (int i = 0; i < clientAmount; ++i)
            {
                Client client = new Client();
                client.Connect(host, port);
                clients.Add(client);
                Thread.Sleep(15);
            }
            Logger.Log("started all clients");

            // make sure that all clients connected successfully. otherwise
            // the sleep might be too small, or other reasons. no point in
            // load testing if the connect failed already.
            if (!clients.All(cl => cl.Connected))
            {
                Logger.Log("not all clients were connected successfully. aborting.");
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            long messagesSent = 0;
            long messagesReceived = 0;
            long dataReceived = 0;

            var timer = new System.Timers.Timer(1000.0 / clientFrequency);

            // THIS HAPPENS IN DIFFERENT THREADS.
            // so make sure that GetNextMessage is thread safe!
            timer.Elapsed += (object sender, ElapsedEventArgs e) =>
            {

                foreach (Client client in clients)
                {
                    if (client.Connected)
                    {
                        // send 2 messages each time
                        client.Send(messageBytes);
                        client.Send(messageBytes);

                        messagesSent += 2;
                        // get new messages from queue
                        Message msg;
                        while (client.GetNextMessage(out msg))
                        {
                            if (msg.eventType == EventType.Data)
                            {
                                messagesReceived++;
                                dataReceived += msg.data.Length;
                            }
                        }
                    }
                }


                // report every 10 seconds
                if (stopwatch.ElapsedMilliseconds > 1000 * 2)
                {
                    long bandwithIn = dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024);
                    long bandwithOut = messagesSent * messageBytes.Length * 1000 / (stopwatch.ElapsedMilliseconds * 1024);

                    Logger.Log(string.Format("Thread[" + Thread.CurrentThread.ManagedThreadId + "]: Client in={0} ({1} KB/s)  out={2} ({3} KB/s), ReceiveQueueAvg={4}",
                                             messagesReceived,
                                             bandwithIn,
                                             messagesSent,
                                             bandwithOut,
                                             (clients.Sum(cl => cl.ReceiveQueueCount) / clients.Count)));
                    stopwatch.Stop();
                    stopwatch = Stopwatch.StartNew();
                    messagesSent = 0;
                    dataReceived = 0;
                    messagesReceived = 0;
                }

            };

            timer.AutoReset = true;
            timer.Enabled = true;

            Console.ReadLine();
            timer.Stop();
            timer.Dispose();


            foreach (Client client in clients)
            {
                client.Disconnect();
            }
        }
    }
}
