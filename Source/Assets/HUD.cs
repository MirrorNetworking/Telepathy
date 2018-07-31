using System;
using UnityEngine;
using System.Threading;

public class HUD : MonoBehaviour
{
    [Header("Stress test")]
    public int packetsPerTick = 1000;

    bool stressTestRunning = false;

    void Update()
    {
        if (GoodOldTcpClient.Connected)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                GoodOldTcpClient.Send(new byte[]{0xAF, 0xFE});
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
                    GoodOldTcpClient.Send(new byte[]{0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01, 0xAF, 0xFE, 0x01});
            }

            // any new message?
            byte[] data;
            if (GoodOldTcpClient.GetNextMessage(out data))
            {
                Debug.Log("received msg: " + BitConverter.ToString(data));
            }
        }

        if (GoodOldTcpServer.Active)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                GoodOldTcpServer.Send(new byte[]{0xBA, 0xBE});
            }

            // any new message?
            // -> calling it once per frame is okay, but really why not just
            //    process all messages and make it empty..
            byte[] data;
            uint connectionId;
            int receivedCount = 0;
            while (GoodOldTcpServer.GetNextMessage(out connectionId, out data))
            {
                Debug.Log("received connectionId=" + connectionId + " msg: " + BitConverter.ToString(data));
                ++receivedCount;
            }
            if (receivedCount > 0) Debug.Log("Server received " + receivedCount + " messages this frame."); // easier on CPU to log this way
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(0, 0, 300, 300));

        if (GUILayout.Button("Start Client"))
        {
            //client.Connect();
            GoodOldTcpClient.Connect("localhost", 1337);
        }

        if (GUILayout.Button("Start Server"))
        {
            GoodOldTcpServer.StartServer(1337);
        }

        GUILayout.EndArea();
    }
}
