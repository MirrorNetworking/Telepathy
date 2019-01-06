using System;
using System.Threading;
using UnityEngine;

namespace Telepathy.LoadTest
{
    public class RunServer : MonoBehaviour
    {
        public int port = 1337;

        long messagesReceived = 0;
        long dataReceived = 0;

        public int serverFrequency = 60;

        Server server = new Server();

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        void Start()
        {
            // dispatch the events from the server
            server.OnConnected += (id) => { /*Debug.Log("server connected @ Thread=" + Thread.CurrentThread.ManagedThreadId);*/ };
            server.OnDisconnected += (id) => { };
            server.OnReceivedData += (id, data) =>
            {
                server.Send(id, data); // reply
                ++messagesReceived;
                dataReceived += data.Length;
                //Debug.Log("server.Data @ Thread=" + Thread.CurrentThread.ManagedThreadId);
            };
            server.OnReceivedError += (id, exception) => { Debug.LogError("sv error @ Thread=" + Thread.CurrentThread.ManagedThreadId + " " + exception); };

            server.Start(port);

            Debug.Log("started server");

            //float interval = 1f / serverFrequency;
            InvokeRepeating("PrintStatus", 2, 2);
        }

        void PrintStatus()
        {
            // report every 2 seconds
            Debug.Log(string.Format("Server in={0} ({1} KB/s)  out={0} ({1} KB/s)", messagesReceived, (dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024))));
            stopwatch.Stop();
            stopwatch = System.Diagnostics.Stopwatch.StartNew();
            messagesReceived = 0;
            dataReceived = 0;
        }

        void OnDestroy()
        {
            server.Stop();
        }
    }
}
