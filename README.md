![Telepathy Logo](https://i.imgur.com/2Dw1zx6.png)

[![Discord](https://img.shields.io/discord/343440455738064897.svg)](https://discordapp.com/invite/N9QVxbM)

Simple, message based, allocation free MMO Scale TCP networking in C#. And no magic.

Telepathy was designed with the [KISS Principle](https://en.wikipedia.org/wiki/KISS_principle) in mind.<br/>
Telepathy is fast and extremely reliable, designed for [MMO](https://assetstore.unity.com/packages/templates/systems/ummorpg-remastered-159401) scale Networking.<br/>
Telepathy uses framing, so anything sent will be received the same way.<br/>
Telepathy is raw C# and made for Unity & [Mirror](https://github.com/vis2k/Mirror) | [DOTSNET](https://u3d.as/YUi).<br/>

# What makes Telepathy special?
Telepathy was originally designed for [uMMORPG](https://assetstore.unity.com/packages/templates/systems/ummorpg-remastered-159401) after 3 years in UDP hell.

We needed a library that is:
* **Stable & Bug free:** Telepathy uses only 700 lines of code. There is no magic.
* **High performance:** Telepathy can handle thousands of connections and packages.
* **Concurrent:** Telepathy uses two threads per connection. It can make heavy use of multi core processors.
* **Allocation Free:** Telepathy has no allocations in hot path. Avoids GC spikes in Games.
* **Simple:** Telepathy wraps all the insanity behind Connect/Send/Disconnect/Tick.
* **Message based:** sending messages A,B,C are received as A,B,C. Never as AB,C or BC,A.

MMORPGs are insanely difficult to make and we created Telepathy so that we would never have to worry about low level Networking again.<br>

See also: [SyncThing article on KCP vs. TCP](https://forum.syncthing.net/t/connections-over-udp/9382).

# What about...
* Async Sockets: perform great in regular C# projects, but poorly when used in Unity.
* UDP vs. TCP: Minecraft and World of Warcraft are two of the biggest multiplayer games of all time and they both use TCP networking. There is a reason for that.

# Using the Telepathy Server
```C#
// create server & hook up events
// note that the message ArraySegment<byte> is only valid until returning (allocation free)
Telepathy.Server server = new Telepathy.Server();
server.OnConnected = (connectionId) => Console.WriteLine(msg.connectionId + " Connected");
server.OnData = (connectionId, message) => Console.WriteLine(msg.connectionId + " Data: " + BitConverter.ToString(message.Array, message.Offset, message.Count));
server.OnDisconnected = (connectionId) => Console.WriteLine(msg.connectionId + " Disconnected");

// start
server.Start(1337);

// tick to process incoming messages (do this in your update loop)
// => limit parameter to avoid deadlocks!
server.Tick(100);

// send a message to client with connectionId = 0 (first one)
byte[] message = new byte[]{0x42, 0x13, 0x37}
server.Send(0, new ArraySegment<byte>(message));

// stop the server when you don't need it anymore
server.Stop();
```

# Using the Telepathy Client
```C#
// create client & hook up events
// note that the message ArraySegment<byte> is only valid until returning (allocation free)
Telepathy.Client client = new Telepathy.Client();
client.OnConnected = () => Console.WriteLine("Client Connected");
client.OnData = (message) => Console.WriteLine("Client Data: " + BitConverter.ToString(message.Array, message.Offset, message.Count));
client.OnDisconnected = () => Console.WriteLine("Client Disconnected");

// connect
client.Connect("localhost", 1337);

// tick to process incoming messages (do this in your update loop)
// => limit parameter to avoid deadlocks!
client.Tick(100);

// send a message to server
byte[] message = new byte[]{0xFF}
client.Send(new ArraySegment<byte>(message));

// disconnect from the server when we are done
client.Disconnect();
```

# Unity Integration
Here is a very simple MonoBehaviour script for Unity. It's really just the above code with logging configured for Unity's Debug.Log:
```C#
using System;
using UnityEngine;

public class SimpleExample : MonoBehaviour
{
    Telepathy.Client client = new Telepathy.Client();
    Telepathy.Server server = new Telepathy.Server();

    void Awake()
    {
        // update even if window isn't focused, otherwise we don't receive.
        Application.runInBackground = true;

        // use Debug.Log functions for Telepathy so we can see it in the console
        Telepathy.Logger.Log = Debug.Log;
        Telepathy.Logger.LogWarning = Debug.LogWarning;
        Telepathy.Logger.LogError = Debug.LogError;

        // hook up events
        client.OnConnected = () => Debug.Log("Client Connected");
        client.OnData = (message) => Debug.Log("Client Data: " + BitConverter.ToString(message.Array, message.Offset, message.Count));
        client.OnDisconnected = () => Debug.Log("Client Disconnected");

        server.OnConnected = (connectionId) => Debug.Log(msg.connectionId + " Connected");
        server.OnData = (connectionId, message) => Debug.Log(msg.connectionId + " Data: " + BitConverter.ToString(message.Array, message.Offset, message.Count));
        server.OnDisconnected = (connectionId) => Debug.Log(msg.connectionId + " Disconnected");
    }

    void Update()
    {
        // client
        if (client.Connected)
        {
            // send message on key press
            if (Input.GetKeyDown(KeyCode.Space))
                client.Send(new ArraySegment<byte>(new byte[]{0x1}));
        }

        // tick to process messages
        // (even if not connected so we still process disconnect messages)
        client.Tick(100);

        // server
        if (server.Active)
        {
            if (Input.GetKeyDown(KeyCode.Space))
                server.Send(0, new ArraySegment<byte>(new byte[]{0x2}));

        }

        // tick to process messages
        // (even if not active so we still process disconnect messages)
        server.Tick(100);
    }

    void OnGUI()
    {
        // client
        GUI.enabled = !client.Connected;
        if (GUI.Button(new Rect(0, 0, 120, 20), "Connect Client"))
            client.Connect("localhost", 1337);

        GUI.enabled = client.Connected;
        if (GUI.Button(new Rect(130, 0, 120, 20), "Disconnect Client"))
            client.Disconnect();

        // server
        GUI.enabled = !server.Active;
        if (GUI.Button(new Rect(0, 25, 120, 20), "Start Server"))
            server.Start(1337);

        GUI.enabled = server.Active;
        if (GUI.Button(new Rect(130, 25, 120, 20), "Stop Server"))
            server.Stop();

        GUI.enabled = true;
    }

    void OnApplicationQuit()
    {
        // the client/server threads won't receive the OnQuit info if we are
        // running them in the Editor. they would only quit when we press Play
        // again later. this is fine, but let's shut them down here for consistency
        client.Disconnect();
        server.Stop();
    }
}
```
Make sure to enable 'run in Background' for your project settings, which is a must for all multiplayer games.
Then build it, start the server in the build and the client in the Editor and press Space to send a test message.

# Benchmarks
**Real World**<br/>
Telepathy is constantly tested in production with [uMMORPG](https://www.assetstore.unity3d.com/#!/content/51212).
We [recently tested](https://youtu.be/mDCNff1S9ZU) 500 players all broadcasting to each other in the worst case scenario, without issues.

We had to stop the test because we didn't have more players to spawn clients.<br/>

**Connections Test**<br/>
We also test only the raw Telepathy library by spawning 1 server and 1000 clients, each client sending 100 bytes 14 times per second and the server echoing the same message back to each client. This test should be a decent example for an MMORPG scenario and allows us to test if the raw Telepathy library can handle it.

Test Computer: 2015 Macbook Pro with a 2,2 GHz Intel Core i7 processor.<br/>
Test Results:<br/>

| Clients | CPU Usage | Ram Usage | Bandwidth Client+Server  | Result |
| ------- | ----------| --------- | ------------------------ | ------ |
|   128   |        7% |     26 MB |         1-2 MB/s         | Passed |
|   500   |       28% |     51 MB |         3-4 MB/s         | Passed |
|  1000   |       42% |     75 MB |         3-5 MB/s         | Passed |

You can run this test yourself by running the provided [LoadTest](LoadTest)
