using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// UI de test pour File Sharing et Screen Sharing.
/// Appuyer sur F4 pour afficher/masquer.
/// </summary>
public class SharingTestUI : MonoBehaviour
{
    [Header("Settings")]
    public Key toggleKey = Key.F4;
    public bool showUI = false;

    [Header("Test File")]
    [Tooltip("Chemin vers un fichier de test (image PNG/JPG)")]
    public string testFilePath = "";

    private string _statusMessage = "";
    private Whiteboard _targetWhiteboard;

    void Update()
    {
        // New Input System
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            showUI = !showUI;
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        GUILayout.BeginArea(new Rect(10, 10, 350, 500));
        GUILayout.BeginVertical("box");

        GUILayout.Label("=== SHARING TEST (F4) ===", GUI.skin.box);

        // Status
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            GUILayout.Label(_statusMessage);
            GUILayout.Space(5);
        }

        // Connection status
        bool isConnected = VRNetworkManager.IsConnected;
        bool isInRoom = VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom;
        GUILayout.Label($"Connected: {(isConnected ? "YES" : "NO")}");
        GUILayout.Label($"In Room: {(isInRoom ? VRRoomManager.Instance.CurrentRoomId : "NO")}");

        GUILayout.Space(10);

        // ==================
        // FILE SHARING
        // ==================
        GUILayout.Label("--- FILE SHARING ---");

        // Test file path
        GUILayout.Label("Test file path:");
        testFilePath = GUILayout.TextField(testFilePath);

        if (GUILayout.Button("Browse (Editor Only)"))
        {
#if UNITY_EDITOR
            testFilePath = UnityEditor.EditorUtility.OpenFilePanel("Select test file", "", "png,jpg,jpeg,pdf,txt");
#else
            _statusMessage = "Browse only works in Editor";
#endif
        }

        // Share file button
        GUI.enabled = isInRoom && !string.IsNullOrEmpty(testFilePath);
        if (GUILayout.Button("Share File"))
        {
            if (FileShareManager.Instance != null)
            {
                FileShareManager.Instance.ShareFile(testFilePath);
                _statusMessage = $"Sharing: {System.IO.Path.GetFileName(testFilePath)}";
            }
            else
            {
                _statusMessage = "FileShareManager not found!";
            }
        }
        GUI.enabled = true;

        // Available files
        if (FileShareManager.Instance != null)
        {
            var files = FileShareManager.Instance.GetAvailableFiles();
            GUILayout.Label($"Available files: {files.Count}");

            foreach (var file in files)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"  {file.fileName} ({FormatSize(file.fileSize)})");

                if (file.isComplete && GUILayout.Button("Open", GUILayout.Width(50)))
                {
                    string localPath = FileShareManager.Instance.GetLocalPath(file.fileId);
                    if (!string.IsNullOrEmpty(localPath) && FileViewer.Instance != null)
                    {
                        FileViewer.Instance.OpenFile(localPath);
                        _statusMessage = $"Opening: {file.fileName}";
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(10);

        // ==================
        // SCREEN SHARING
        // ==================
        GUILayout.Label("--- SCREEN SHARING ---");

        bool isDesktop = VRGameManager.Instance == null || VRGameManager.Instance.IsDesktopMode;
        bool isSharing = ScreenShareManager.Instance != null && ScreenShareManager.Instance.IsSharing;
        bool isReceiving = ScreenShareManager.Instance != null && ScreenShareManager.Instance.IsReceiving;

        GUILayout.Label($"Mode: {(isDesktop ? "Desktop" : "VR")}");
        GUILayout.Label($"Sharing: {(isSharing ? "YES" : "NO")}");
        GUILayout.Label($"Receiving: {(isReceiving ? "YES" : "NO")}");

        GUI.enabled = isInRoom && isDesktop;
        if (GUILayout.Button(isSharing ? "Stop Screen Share" : "Start Screen Share"))
        {
            if (ScreenShareManager.Instance != null)
            {
                ScreenShareManager.Instance.ToggleSharing();
                _statusMessage = isSharing ? "Stopped sharing" : "Started sharing";
            }
            else
            {
                _statusMessage = "ScreenShareManager not found!";
            }
        }
        GUI.enabled = true;

        if (!isDesktop)
        {
            GUILayout.Label("(Screen share = Desktop only)");
        }

        GUILayout.Space(10);

        // ==================
        // WHITEBOARD
        // ==================
        GUILayout.Label("--- WHITEBOARD ---");

        // Find whiteboard
        if (_targetWhiteboard == null)
        {
            _targetWhiteboard = FindFirstObjectByType<Whiteboard>();
        }

        if (_targetWhiteboard != null)
        {
            GUILayout.Label($"Whiteboard: {_targetWhiteboard.id}");
            GUILayout.Label($"Presentation Mode: {(_targetWhiteboard.IsPresentationMode ? "YES" : "NO")}");

            if (_targetWhiteboard.IsPresentationMode)
            {
                GUILayout.Label($"  Title: {_targetWhiteboard.CurrentPresentationTitle}");

                if (GUILayout.Button("Stop Presentation"))
                {
                    _targetWhiteboard.StopPresentationMode();
                    _statusMessage = "Presentation stopped";
                }
            }
        }
        else
        {
            GUILayout.Label("No whiteboard found");
        }

        GUILayout.Space(10);

        // Close button
        if (GUILayout.Button("Close Test UI"))
        {
            showUI = false;
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.#} {sizes[order]}";
    }
}
