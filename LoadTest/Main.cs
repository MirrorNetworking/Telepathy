using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Telepathy.LoadTest
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Both();
            }
            else if (args[0] == "server")
            {
                Server(args);
            }
            else if (args[0] == "client")
            {
                Client(args);
            }
            else
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("   LoadTest");
                Console.WriteLine("   LoadTest server <port>");
                Console.WriteLine("   LoadTest client <host> <port> <clients>");
            }
        }

        public static void Both()
        {

            Thread serverThread = new Thread(() =>
            {

                RunServer.StartServer(1337);
            });
            serverThread.IsBackground = false;
            serverThread.Start();

            // test 500 clients, which means 500+500 = 1000 connections total.
            // this should be enough for any server or MMO.
            RunClients.StartClients("127.0.0.1", 1337, 500);

        }

        public static void Server(string [] args)
        {

            if (args.Length != 2)
            {
                Console.WriteLine("Usage: LoadTest server <port>");
                return;
            }
            int port = int.Parse(args[1]);

            RunServer.StartServer(port);

        }


        public static void Client(string[] args)
        {
            if (args.Length != 4)
            {
                Console.WriteLine("Usage: LoadTest client <host> <port> <clients>");
                return;
            }
            string ip = args[1];
            int port = int.Parse(args[2]);
            int clients = int.Parse(args[3]);

            RunClients.StartClients(ip, port, clients);
        }
    }
}
