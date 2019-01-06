using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Timers;
using UnityEngine;


namespace Telepathy.LoadTest
{
    public class RunClients : MonoBehaviour
    {
        List<Client> clients = new List<Client>();

        long messagesSent = 0;
        long messagesReceived = 0;
        long dataReceived = 0;

        static string message = "Sometimes we just need a good networking library";
        static byte[] messageBytes = Encoding.ASCII.GetBytes(message);
        public int clientFrequency = 14;

        public string host = "127.0.0.1";
        public int port = 1337;
        public int clientAmount = 2;

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        void Start()
        {
            // give server some time to start...
            Invoke("StartDelayed", 2);
        }

        void StartDelayed()
        {
            StartCoroutine(StartClients());
        }

        IEnumerator StartClients()
        {
            Debug.Log("starting " + clientAmount + " clients");

            // start n clients and get queue messages all in this thread
            for (int i = 0; i < clientAmount; ++i)
            {
                Client client = new Client();

                // dispatch events from the client
                client.OnConnected += () => { /*Debug.Log("client connected @ Thread=" + Thread.CurrentThread.ManagedThreadId);*/ };
                client.OnDisconnected += () => { /*Debug.Log("client disconnected @ Thread=" + Thread.CurrentThread.ManagedThreadId);*/ };
                client.OnReceivedData += (data) =>
                {
                    ++messagesReceived;
                    dataReceived += data.Length;
                    //Debug.Log("client.Data @ Thread=" + Thread.CurrentThread.ManagedThreadId);
                };
                client.OnReceivedError += (exception) => { Debug.LogError("cl error: @ Thread=" + Thread.CurrentThread.ManagedThreadId + " " + exception); };
                client.Connect(host, port);
                clients.Add(client);

                Debug.Log("started client " + i);

                // can't connect that many at once. that's normal, not a unity thing.
                yield return new WaitForSeconds(0.01f);
            }
            Debug.Log("started all clients");

            float interval = 1f / clientFrequency;
            InvokeRepeating("Tick", interval, interval);

            InvokeRepeating("PrintStatus", 2, 2);
        }

        void Tick()
        {
            foreach (Client client in clients)
            {
                if (client.Connected)
                {
                    // send 2 messages each time
                    client.Send(messageBytes);
                    client.Send(messageBytes);
                    messagesSent += 2;
                    //Debug.Log("client sent
                }
            }
        }

        void PrintStatus()
        {
            long bandwithIn = dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024);
            long bandwithOut = messagesSent * messageBytes.Length * 1000 / (stopwatch.ElapsedMilliseconds * 1024);

            Debug.Log(string.Format("Client in={0} ({1} KB/s)  out={2} ({3} KB/s)",
                                     messagesReceived,
                                     bandwithIn,
                                     messagesSent,
                                     bandwithOut));
            stopwatch.Stop();
            stopwatch = System.Diagnostics.Stopwatch.StartNew();
            messagesSent = 0;
            dataReceived = 0;
            messagesReceived = 0;

        }

        void OnDestroy()
        {
            foreach (Client client in clients)
            {
                client.Disconnect();
            }
        }
    }
}
