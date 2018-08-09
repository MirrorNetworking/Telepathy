using System;
using UnityEngine;
using System.Threading;

public class HUD : MonoBehaviour
{
    [Header("Stress test")]
    public int packetsPerTick = 1000;
    public byte[] stressBytes = new byte[]{0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01};
    bool stressTestRunning = false;

    void Awake()
    {
        // update even if window isn't focused, otherwise we don't receive.
        Application.runInBackground = true;

        // use Debug.Log functions for TCP so we can see it in the console
        Logger.LogMethod = Debug.Log;
        Logger.LogWarningMethod = Debug.LogWarning;
        Logger.LogErrorMethod = Debug.LogError;
    }

    void Update()
    {
        if (Telepathy.Client.Connected)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Telepathy.Client.Send(new byte[]{0xAF, 0xFE});
                Telepathy.Client.Send(new byte[]{0xBA, 0xBE});
                //Telepathy.Client.Send(stressBytes);
            }

            if (Input.GetKeyDown(KeyCode.S))
            {
                stressTestRunning = !stressTestRunning;
                if (stressTestRunning)
                    Debug.Log("client start stress test with: " + packetsPerTick + " packets per tick");
            }

            // SPAM
            if (stressTestRunning)
            {
                for (int i = 0; i < packetsPerTick; ++i)
                    Telepathy.Client.Send(stressBytes);
            }

            // any new message?
            Telepathy.EventType eventType;
            byte[] data;
            if (Telepathy.Client.GetNextMessage(out eventType, out data))
            {
                Debug.Log("received event=" + eventType + " msg: " + (data != null ? BitConverter.ToString(data) : "null"));
            }
        }

        if (Telepathy.Server.Active)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                Telepathy.Server.Send(0, new byte[]{0xAF, 0xFE});
                Telepathy.Server.Send(0, new byte[]{0xBA, 0xBE});
            }

            // any new message?
            // -> calling it once per frame is okay, but really why not just
            //    process all messages and make it empty..
            byte[] data;
            Telepathy.EventType eventType;
            uint connectionId;
            int receivedCount = 0;
            while (Telepathy.Server.GetNextMessage(out connectionId, out eventType, out data))
            {
                //Debug.Log("received connectionId=" + connectionId + " event=" + eventType + " msg: " + (data != null ? BitConverter.ToString(data) : "null"));
                ++receivedCount;
            }
            if (receivedCount > 0) Debug.Log("Server received " + receivedCount + " messages this frame."); // easier on CPU to log this way
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(0, 0, 300, 300));

        // client
        GUILayout.BeginHorizontal();
        GUI.enabled = !Telepathy.Client.Connected;
        if (GUILayout.Button("Connect Client"))
        {
            Telepathy.Client.Connect("localhost", 1337);
        }
        GUI.enabled = Telepathy.Client.Connected;
        if (GUILayout.Button("Disconnect Client"))
        {
            Telepathy.Client.Disconnect();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        // server
        GUILayout.BeginHorizontal();
        GUI.enabled = !Telepathy.Server.Active;
        if (GUILayout.Button("Start Server"))
        {
            Telepathy.Server.Start("localhost", 1337);
        }
        GUI.enabled = Telepathy.Server.Active;
        if (GUILayout.Button("Stop Server"))
        {
            Telepathy.Server.Stop();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    void OnApplicationQuit()
    {
        // the client/server threads won't receive the OnQuit info if we are
        // running them in the Editor. they would only quit when we press Play
        // again later. this is fine, but let's shut them down here for consistency
        Telepathy.Client.Disconnect();
        Telepathy.Server.Stop();
    }
}
