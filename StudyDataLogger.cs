using System;
using System.IO;
using UnityEngine;

public class StudyDataLogger : MonoBehaviour
{
    private StreamWriter writer;
    private int trialNumber = 0;
    private string logFilePath;

    private void Awake()
    {
        InitializeWriter();
    }

    private void Start()
    {
        // Ensure the writer is initialized before any external calls to LogEvent.
        InitializeWriter();
    }

    private bool InitializeWriter()
    {
        if (writer != null)
            return true;

        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = "StudyData_" + timestamp + ".csv";
            logFilePath = Path.Combine(Application.persistentDataPath, filename);
            writer = new StreamWriter(logFilePath, false)
            {
                AutoFlush = true
            };

            writer.WriteLine("TrialNumber,EventType,Timestamp,ObjectName");
            Debug.Log("Logger started. Saving to: " + logFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to initialize study data logger: " + ex.Message);
            writer = null;
            return false;
        }
    }

    public void LogEvent(string eventType, string objectName)
    {
        if (!InitializeWriter())
            return;

        string timestamp = Time.time.ToString("F3");
        string line = string.Join(",", trialNumber.ToString(), eventType, timestamp, objectName);
        writer.WriteLine(line);
    }

    public void NewTrial()
    {
        trialNumber++;
        LogEvent("TRIAL_START", "none");
    }

    private void CloseWriter()
    {
        if (writer == null)
            return;

        try
        {
            writer.Close();
            writer = null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to close study data logger: " + ex.Message);
        }
    }

    private void OnDestroy()
    {
        CloseWriter();
    }

    private void OnApplicationQuit()
    {
        CloseWriter();
    }
}