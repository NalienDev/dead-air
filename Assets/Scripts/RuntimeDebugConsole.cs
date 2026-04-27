using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class RuntimeDebugConsole : MonoBehaviour
{
    [Header("UI")]
    public GameObject panel;
    public GameObject scrollRoot;
    public TMP_Text logText;
    public ScrollRect scrollRect;

    [Header("Settings")]
    public int maxLogs = 200;
    public KeyCode toggleKey = KeyCode.F1;

    Queue<string> logs = new Queue<string>();
    bool collapsed = false;

    void Awake()
    {
        Application.logMessageReceived += HandleLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            panel.SetActive(!panel.activeSelf);
    }

    void HandleLog(string message, string stackTrace, LogType type)
    {
        string color = "white";

        switch (type)
        {
            case LogType.Warning:
                color = "yellow";
                break;

            case LogType.Error:
            case LogType.Exception:
                color = "red";
                break;

            case LogType.Assert:
                color = "cyan";
                break;
        }

        string time = System.DateTime.Now.ToString("HH:mm:ss");

        logs.Enqueue($"<color=grey>[{time}]</color> <color={color}>{message}</color>");

        while (logs.Count > maxLogs)
            logs.Dequeue();

        logText.text = string.Join("\n", logs);

        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f; // newest log
    }

    public void ClearLogs()
    {
        logs.Clear();
        logText.text = "";
    }

    public void ToggleCollapse()
    {
        collapsed = !collapsed;
        scrollRoot.SetActive(!collapsed);
    }
}