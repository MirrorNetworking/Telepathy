﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Telepathy
{
    public class Server : Common
    {
        // listener
        public TcpListener listener;
        Thread listenerThread;

        volatile bool _Encrypted;
        public bool Encrypted => _Encrypted;

        volatile string _CertFile;
        public string CertFile => _CertFile;

        // class with all the client's data. let's call it Token for consistency
        // with the async socket methods.
        class ClientToken
        {
            public TcpClient client;

            // send queue
            // SafeQueue is twice as fast as ConcurrentQueue, see SafeQueue.cs!
            public SafeQueue<byte[]> sendQueue = new SafeQueue<byte[]>();

            // ManualResetEvent to wake up the send thread. better than Thread.Sleep
            // -> call Set() if everything was sent
            // -> call Reset() if there is something to send again
            // -> call WaitOne() to block until Reset was called
            public ManualResetEvent sendPending = new ManualResetEvent(false);

            public ClientToken(TcpClient client)
            {
                this.client = client;
            }
        }

        // clients with <connectionId, ClientData>
        readonly ConcurrentDictionary<int, ClientToken> clients = new ConcurrentDictionary<int, ClientToken>();

        // connectionId counter
        int counter;

        // public next id function in case someone needs to reserve an id
        // (e.g. if hostMode should always have 0 connection and external
        //  connections should start at 1, etc.)
        public int NextConnectionId()
        {
            int id = Interlocked.Increment(ref counter);

            // it's very unlikely that we reach the uint limit of 2 billion.
            // even with 1 new connection per second, this would take 68 years.
            // -> but if it happens, then we should throw an exception because
            //    the caller probably should stop accepting clients.
            // -> it's hardly worth using 'bool Next(out id)' for that case
            //    because it's just so unlikely.
            if (id == int.MaxValue)
            {
                throw new Exception("connection id limit reached: " + id);
            }

            return id;
        }

        // check if the server is running
        public bool Active => listenerThread != null && listenerThread.IsAlive;

        // the listener thread's listen function
        // note: no maxConnections parameter. high level API should handle that.
        //       (Transport can't send a 'too full' message anyway)
        void Listen(int port)
        {
            // absolutely must wrap with try/catch, otherwise thread
            // exceptions are silent
            try
            {
                // start listener on all IPv4 and IPv6 address via .Create
                listener = TcpListener.Create(port);
                listener.Server.NoDelay = NoDelay;
                listener.Server.SendTimeout = SendTimeout;
                listener.Start();
                Logger.Log("Server: listening port=" + port);

                X509Certificate cert = null;
                bool selfSignedCert = false;
                
                if (Encrypted)
                {
                    if (CertFile == null)
                    {
                        // Create a new self-signed certificate
                        selfSignedCert = true;
                        cert =
                            new X509Certificate2Builder
                            {
                                SubjectName = string.Format("CN={0}", ((IPEndPoint)listener.LocalEndpoint).Address.ToString())
                            }.Build();
                    }
                    else
                    {
                        cert = X509Certificate.CreateFromCertFile(CertFile);
                        selfSignedCert = false;
                    }
                }

                // keep accepting new clients
                while (true)
                {
                    // wait and accept new client
                    // note: 'using' sucks here because it will try to
                    // dispose after thread was started but we still need it
                    // in the thread
                    TcpClient client = listener.AcceptTcpClient();

                    // set socket options
                    client.NoDelay = NoDelay;
                    client.SendTimeout = SendTimeout;

                    // generate the next connection id (thread safely)
                    int connectionId = NextConnectionId();

                    // add to dict immediately
                    ClientToken token = new ClientToken(client);
                    clients[connectionId] = token;

                    Stream stream = client.GetStream();
                    
                    Thread sslAuthenticator = null;
                    if (Encrypted)
                    {
                        RemoteCertificateValidationCallback trustCert = (object sender, X509Certificate x509Certificate,
                            X509Chain x509Chain, SslPolicyErrors policyErrors) =>
                        {
                            if (selfSignedCert)
                            {
                                // All certificates are accepted
                                return true;
                            }
                            else
                            {
                                if (policyErrors == SslPolicyErrors.None)
                                {
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                        };

						SslStream sslStream = new SslStream(client.GetStream(), false, trustCert);
                        stream = sslStream;

                        sslAuthenticator = new Thread(() => {
                            try
                            {
                                // Using System.Security.Authentication.SslProtocols.None (the Microsoft recommended parameter which
                                // chooses the highest version of TLS) does not seem to work with Unity. Unity 2018.2 added support
                                // for TLS 1.2 when used with the .NET 4.x runtime, so use preprocessor directives to choose the right protocol
#if UNITY_2018_2_OR_NEWER && NET_4_6
                                System.Security.Authentication.SslProtocols protocol = System.Security.Authentication.SslProtocols.Tls12;
#else
                                System.Security.Authentication.SslProtocols protocol = System.Security.Authentication.SslProtocols.Default;
#endif

                                bool checkCertificateRevocation = !selfSignedCert;
                                sslStream.AuthenticateAsServer(cert, false, protocol, checkCertificateRevocation);
                            }
                            catch (Exception exception)
                            {
                                Logger.LogError("SSL Authenticator exception: " + exception);
                            }
                        });

                        sslAuthenticator.IsBackground = true;
                        sslAuthenticator.Start();
                    }

                    // spawn a send thread for each client
                    Thread sendThread = new Thread(() =>
                    {
                        // wrap in try-catch, otherwise Thread exceptions
                        // are silent
                        try
                        {
                            if (sslAuthenticator != null)
                            {
                                sslAuthenticator.Join();
                            }

                            // run the send loop
                            SendLoop(connectionId, client, stream, token.sendQueue, token.sendPending);
                        }
                        catch (ThreadAbortException)
                        {
                            // happens on stop. don't log anything.
                            // (we catch it in SendLoop too, but it still gets
                            //  through to here when aborting. don't show an
                            //  error.)
                        }
                        catch (Exception exception)
                        {
                            Logger.LogError("Server send thread exception: " + exception);
                        }
                    });
                    sendThread.IsBackground = true;
                    sendThread.Start();

                    // spawn a receive thread for each client
                    Thread receiveThread = new Thread(() =>
                    {
                        // wrap in try-catch, otherwise Thread exceptions
                        // are silent
                        try
                        {
                            if (sslAuthenticator != null)
                            {
                                sslAuthenticator.Join();
                            }

                            // run the receive loop
                            ReceiveLoop(connectionId, client, stream, receiveQueue, MaxMessageSize);

                            // remove client from clients dict afterwards
                            clients.TryRemove(connectionId, out ClientToken _);

                            // sendthread might be waiting on ManualResetEvent,
                            // so let's make sure to end it if the connection
                            // closed.
                            // otherwise the send thread would only end if it's
                            // actually sending data while the connection is
                            // closed.
                            sendThread.Interrupt();
                        }
                        catch (Exception exception)
                        {
                            Logger.LogError("Server client thread exception: " + exception);
                        }
                    });
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                }
            }
            catch (ThreadAbortException exception)
            {
                // UnityEditor causes AbortException if thread is still
                // running when we press Play again next time. that's okay.
                Logger.Log("Server thread aborted. That's okay. " + exception);
            }
            catch (SocketException exception)
            {
                // calling StopServer will interrupt this thread with a
                // 'SocketException: interrupted'. that's okay.
                Logger.Log("Server Thread stopped. That's okay. " + exception);
            }
            catch (Exception exception)
            {
                // something went wrong. probably important.
                Logger.LogError("Server Exception: " + exception);
            }
        }

        // start listening for new connections in a background thread and spawn
        // a new thread for each one.
        public bool Start(int port, bool encrypt, string certFile)
        {
            // not if already started
            if (Active) return false;

            _Encrypted = encrypt;
            _CertFile = certFile;

            // clear old messages in queue, just to be sure that the caller
            // doesn't receive data from last time and gets out of sync.
            // -> calling this in Stop isn't smart because the caller may
            //    still want to process all the latest messages afterwards
            receiveQueue = new ConcurrentQueue<Message>();

            // start the listener thread
            // (on low priority. if main thread is too busy then there is not
            //  much value in accepting even more clients)
            Logger.Log("Server: Start port=" + port);
            listenerThread = new Thread(() => { Listen(port); });
            listenerThread.IsBackground = true;
            listenerThread.Priority = ThreadPriority.BelowNormal;
            listenerThread.Start();
            return true;
        }

        public bool Start(int port)
        {
            return Start(port, false, null);
        }

        public void Stop()
        {
            // only if started
            if (!Active) return;

            Logger.Log("Server: stopping...");

            // stop listening to connections so that no one can connect while we
            // close the client connections
            // (might be null if we call Stop so quickly after Start that the
            //  thread was interrupted before even creating the listener)
            listener?.Stop();

            // kill listener thread at all costs. only way to guarantee that
            // .Active is immediately false after Stop.
            // -> calling .Join would sometimes wait forever
            listenerThread?.Interrupt();
            listenerThread = null;

            // close all client connections
            foreach (KeyValuePair<int, ClientToken> kvp in clients)
            {
                TcpClient client = kvp.Value.client;
                // close the stream if not closed yet. it may have been closed
                // by a disconnect already, so use try/catch
                try { client.GetStream().Close(); } catch {}
                client.Close();
            }

            // clear clients list
            clients.Clear();

            // reset the counter in case we start up again so
            // clients get connection ID's starting from 1
            counter = 0;
        }

        // send message to client using socket connection.
        public bool Send(int connectionId, byte[] data)
        {
            // respect max message size to avoid allocation attacks.
            if (data.Length <= MaxMessageSize)
            {
                // find the connection
                ClientToken token;
                if (clients.TryGetValue(connectionId, out token))
                {
                    // add to send queue and return immediately.
                    // calling Send here would be blocking (sometimes for long times
                    // if other side lags or wire was disconnected)
                    token.sendQueue.Enqueue(data);
                    token.sendPending.Set(); // interrupt SendThread WaitOne()
                    return true;
                }
                // sending to an invalid connectionId is expected sometimes.
                // for example, if a client disconnects, the server might still
                // try to send for one frame before it calls GetNextMessages
                // again and realizes that a disconnect happened.
                // so let's not spam the console with log messages.
                //Logger.Log("Server.Send: invalid connectionId: " + connectionId);
                return false;
            }
            Logger.LogError("Client.Send: message too big: " + data.Length + ". Limit: " + MaxMessageSize);
            return false;
        }

        // client's ip is sometimes needed by the server, e.g. for bans
        public string GetClientAddress(int connectionId)
        {
            // find the connection
            ClientToken token;
            if (clients.TryGetValue(connectionId, out token))
            {
                return ((IPEndPoint)token.client.Client.RemoteEndPoint).Address.ToString();
            }
            return "";
        }

        // disconnect (kick) a client
        public bool Disconnect(int connectionId)
        {
            // find the connection
            ClientToken token;
            if (clients.TryGetValue(connectionId, out token))
            {
                // just close it. client thread will take care of the rest.
                token.client.Close();
                Logger.Log("Server.Disconnect connectionId:" + connectionId);
                return true;
            }
            return false;
        }
    }
}
