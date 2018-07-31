// People should be able to see and report errors to the developer very easily.
//
// Unity's Developer Console only works in development builds and it only shows
// errors. This class provides a console that works in all builds and also shows
// log and warnings in development builds.
//
// Note: we don't include the stack trace, because that can also be grabbed from
// the log files if needed.
//
// Note: there is no 'hide' button because we DO want people to see those errors
// and report them back to us.
//
// Note: normal Debug.Log messages can be shown by building in Debug/Development
//       mode.
using UnityEngine;
using System.Collections.Generic;

class LogEntry
{
    public string message;
    public LogType type;
    public LogEntry(string message, LogType type)
    {
        this.message = message;
        this.type = type;
    }
}

public class GUIConsole : MonoBehaviour
{
    public int height = 25;
    List<LogEntry> log = new List<LogEntry>();
    Vector2 scroll = Vector2.zero;

#if !UNITY_EDITOR
    void Awake()
    {
        Application.logMessageReceived += OnLog;
    }
#endif

    void OnLog(string message, string stackTrace, LogType type)
    {
        // show everything in debug builds and only errors/exceptions in release
        if (Debug.isDebugBuild || type == LogType.Error || type == LogType.Exception)
        {
            log.Add(new LogEntry(message, type));
            scroll.y = 99999f; // autoscroll
        }
    }

    void OnGUI()
    {
        if (log.Count == 0) return;

        scroll = GUILayout.BeginScrollView(scroll, "Box", GUILayout.Width(Screen.width), GUILayout.Height(height));
        foreach (LogEntry entry in log)
        {
            if (entry.type == LogType.Error || entry.type == LogType.Exception)
                GUI.color = Color.red;
            else if (entry.type == LogType.Warning)
                GUI.color = Color.yellow;
            GUILayout.Label(entry.message);
            GUI.color = Color.white;
        }
        GUILayout.EndScrollView();
    }
}
