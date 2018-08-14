﻿using System;
using System.Diagnostics;
using System.Threading;
using Telepathy;

namespace Telepathy.LoadTest
{
    public class RunServer
    {
        public static void StartServer(int port)
        {
            // start server
            Server server = new Server();
            server.Start(port);
            int serverFrequency = 60;
            Logger.Log("started server");

            long messagesReceived = 0;
            long dataReceived = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();

            while (true)
            {
                // reply to each incoming message
                Message msg;
                while (server.GetNextMessage(out msg))
                {
                    if (msg.eventType == EventType.Data)
                    {
                        server.Send(msg.connectionId, msg.data);

                        messagesReceived++;
                        dataReceived += msg.data.Length;
                    }
                }

                // sleep
                Thread.Sleep(1000 / serverFrequency);

                // report every 10 seconds
                if (stopwatch.ElapsedMilliseconds > 1000 * 10)
                {
                    Logger.Log(string.Format("Server in={0} ({1} KB/s)  out={0} ({1} KB/s)", messagesReceived, (dataReceived * 1000 / (stopwatch.ElapsedMilliseconds * 1024))));
                    stopwatch.Restart();
                    messagesReceived = 0;
                    dataReceived = 0;
                }

            }
        }


    }
}
