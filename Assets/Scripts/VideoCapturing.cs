using System;
using UnityEngine;

public class VideoCapturing : MonoBehaviour
{
    public int framerate = 30;
    public int recordingLength;
    public GameObject label;

    private string folder;
    private bool recording;
    private int frameCount;

    public void StartCapture()
    {
        Time.captureFramerate = framerate;
        folder = System.IO.Path.Combine(Application.dataPath, "ScreenCaptures", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        System.IO.Directory.CreateDirectory(folder);
        frameCount = 0;
        label.SetActive(false);
        recording = true;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.ScrollLock) && !recording)
        {
            StartCapture();
        }

        if (recording)
        {
            if (frameCount < framerate * recordingLength)
            {
                string filename = string.Format("{0}/{1:D04}", folder, frameCount);

                ScreenCapture.CaptureScreenshot(filename);

                frameCount++;
            }
            else
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                        Application.Quit();
#endif
            }
        }
    }
}
