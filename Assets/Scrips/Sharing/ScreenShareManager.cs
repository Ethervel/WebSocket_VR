using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestionnaire de partage d'écran via WebSocket.
/// Supporte plusieurs whiteboards indépendants.
/// - Chaque whiteboard peut avoir son propre screen share
/// - Un utilisateur ne peut partager que vers un whiteboard à la fois
/// </summary>
public class ScreenShareManager : MonoBehaviour
{
    public static ScreenShareManager Instance { get; private set; }

    [Header("Capture Settings")]
    [Tooltip("Largeur de capture (plus petit = meilleure performance)")]
    public int captureWidth = 854;  // Réduit de 1280

    [Tooltip("Hauteur de capture")]
    public int captureHeight = 480; // Réduit de 720

    [Tooltip("Qualité JPEG (0-100, plus bas = plus rapide)")]
    [Range(0, 100)]
    public int jpegQuality = 50;    // Réduit de 70

    [Tooltip("Frames par seconde (plus bas = meilleure performance)")]
    [Range(1, 15)]
    public float captureFrameRate = 3f; // Réduit de 5

    [Header("Debug")]
    public bool showDebugInfo = true;
    [Tooltip("Activer les raccourcis clavier de test (F9=Start, F10=Stop)")]
    public bool enableTestShortcuts = true;

    // État local (ce qu'on partage)
    private bool _isSharing = false;
    private string _sharingToWhiteboardId = null;
    private Whiteboard _sharingToWhiteboard = null;
    private WindowCapture.WindowInfo _selectedWindow = null;  // null = capture jeu Unity

    // État de réception par whiteboard
    private class WhiteboardShareState
    {
        public string sharerId;
        public string sharerName;
        public Texture2D displayTexture;
    }
    private Dictionary<string, WhiteboardShareState> _receivingStates = new Dictionary<string, WhiteboardShareState>();

    // Capture resources
    private RenderTexture _captureRT;
    private Texture2D _captureTexture;
    private Texture2D _flippedTexture;
    private Coroutine _captureCoroutine;
    private int _frameIndex = 0;

    // Events
    public static event Action<string, string, string> OnScreenShareStarted;  // whiteboardId, sharerId, sharerName
    public static event Action<string, string> OnScreenShareStopped;          // whiteboardId, sharerId

    // Public state access
    public bool IsSharing => _isSharing;
    public string SharingToWhiteboardId => _sharingToWhiteboardId;
    public WindowCapture.WindowInfo SelectedWindow => _selectedWindow;
    public string SelectedWindowTitle => _selectedWindow?.Title ?? "Jeu Unity";

    // Event pour la liste des fenêtres
    public static event Action<List<WindowCapture.WindowInfo>> OnWindowListUpdated;

    /// <summary>
    /// Vérifie si un whiteboard spécifique reçoit un screen share
    /// </summary>
    public bool IsWhiteboardReceiving(string whiteboardId)
    {
        return _receivingStates.ContainsKey(whiteboardId);
    }

    /// <summary>
    /// Obtient le nom du présentateur pour un whiteboard
    /// </summary>
    public string GetSharerName(string whiteboardId)
    {
        if (_receivingStates.TryGetValue(whiteboardId, out var state))
            return state.sharerName;
        return null;
    }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
    }

    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
    }

    void OnDestroy()
    {
        StopSharing();
        CleanupResources();
    }

    void Update()
    {
        // Test shortcuts (F8=List windows, F9=Start, F10=Stop)
        if (enableTestShortcuts && Keyboard.current != null)
        {
            if (Keyboard.current.f8Key.wasPressedThisFrame)
            {
                ListWindowsAndSelectNext();
            }
            else if (Keyboard.current.f9Key.wasPressedThisFrame)
            {
                TestStartSharing();
            }
            else if (Keyboard.current.f10Key.wasPressedThisFrame)
            {
                StopSharing();
            }
        }
    }

    private int _testWindowIndex = -1;  // -1 = Unity game

    /// <summary>
    /// Test: Liste les fenêtres et sélectionne la suivante
    /// </summary>
    void ListWindowsAndSelectNext()
    {
        var windows = GetAvailableWindows();

        Debug.Log($"[ScreenShare] === Fenêtres disponibles ({windows.Count}) ===");
        Debug.Log($"  [-1] Jeu Unity {(_testWindowIndex == -1 ? "<-- SELECTED" : "")}");

        for (int i = 0; i < windows.Count; i++)
        {
            string selected = (i == _testWindowIndex) ? " <-- SELECTED" : "";
            Debug.Log($"  [{i}] {windows[i].Title} ({windows[i].Width}x{windows[i].Height}){selected}");
        }

        // Passer à la fenêtre suivante
        _testWindowIndex++;
        if (_testWindowIndex >= windows.Count)
        {
            _testWindowIndex = -1;
        }

        // Sélectionner
        if (_testWindowIndex == -1)
        {
            SelectWindow(null);
            Debug.Log("[ScreenShare] >>> Sélectionné: Jeu Unity");
        }
        else
        {
            SelectWindow(windows[_testWindowIndex]);
            Debug.Log($"[ScreenShare] >>> Sélectionné: {windows[_testWindowIndex].Title}");
        }

        Debug.Log("[ScreenShare] Appuie F8 pour changer, F9 pour partager");
    }

    /// <summary>
    /// Test: démarre le partage sur le premier whiteboard trouvé
    /// </summary>
    void TestStartSharing()
    {
        if (_isSharing)
        {
            Debug.Log("[ScreenShare] Already sharing. Press F10 to stop first.");
            return;
        }

        if (!CanShare())
        {
            Debug.Log("[ScreenShare] Cannot share right now");
            return;
        }

        // Find first whiteboard
        Whiteboard[] whiteboards = FindObjectsByType<Whiteboard>(FindObjectsSortMode.None);
        if (whiteboards.Length == 0)
        {
            Debug.LogWarning("[ScreenShare] No whiteboard found in scene!");
            return;
        }

        Debug.Log($"[ScreenShare] Test: Starting share on '{whiteboards[0].id}'");
        StartSharing(whiteboards[0]);
    }

    #region Public API

    /// <summary>
    /// Vérifie si on peut partager (Desktop ET VR supportés)
    /// En VR: partage la vue du casque
    /// En Desktop: partage une fenêtre ou le jeu Unity
    /// </summary>
    public bool CanShare()
    {
        // Toujours autoriser le partage (VR partage la vue casque, Desktop partage fenêtre/jeu)
        return true;
    }

    /// <summary>
    /// Vérifie si on est en mode VR
    /// </summary>
    public bool IsVRMode()
    {
        return VRGameManager.Instance != null && !VRGameManager.Instance.IsDesktopMode;
    }

    /// <summary>
    /// Obtient la liste des fenêtres disponibles pour le partage
    /// En VR, retourne une liste vide (seule la vue casque peut être partagée)
    /// </summary>
    public List<WindowCapture.WindowInfo> GetAvailableWindows()
    {
        // En VR, pas d'accès aux fenêtres Windows
        if (IsVRMode())
        {
            var emptyList = new List<WindowCapture.WindowInfo>();
            OnWindowListUpdated?.Invoke(emptyList);
            return emptyList;
        }

        var windows = WindowCapture.GetOpenWindows();
        OnWindowListUpdated?.Invoke(windows);
        return windows;
    }

    /// <summary>
    /// Sélectionne une fenêtre à partager
    /// </summary>
    public void SelectWindow(WindowCapture.WindowInfo window)
    {
        _selectedWindow = window;
        if (window != null)
        {
            LogDebug($"[ScreenShare] Window selected: {window.Title} ({window.Width}x{window.Height})");
        }
        else
        {
            LogDebug("[ScreenShare] Window selection cleared - will capture Unity game");
        }
    }

    /// <summary>
    /// Sélectionne une fenêtre par son index dans la liste
    /// </summary>
    public void SelectWindowByIndex(int index)
    {
        var windows = GetAvailableWindows();
        if (index >= 0 && index < windows.Count)
        {
            SelectWindow(windows[index]);
        }
        else
        {
            SelectWindow(null);
        }
    }

    /// <summary>
    /// Démarre le partage d'écran vers un whiteboard spécifique
    /// </summary>
    public void StartSharing(Whiteboard targetWhiteboard)
    {
        if (targetWhiteboard == null)
        {
            Debug.LogError("[ScreenShare] Target whiteboard is null");
            return;
        }

        if (_isSharing)
        {
            LogDebug("[ScreenShare] Already sharing. Stop current share first.");
            return;
        }

        if (!CanShare())
        {
            Debug.LogWarning("[ScreenShare] Screen sharing not available");
            return;
        }

        // En VR, forcer la capture de la vue casque (pas de fenêtres Windows)
        if (IsVRMode())
        {
            _selectedWindow = null;
            LogDebug("[ScreenShare] VR Mode: will share headset view");
        }

        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            Debug.LogWarning("[ScreenShare] Must be in a room to share");
            return;
        }

        // Vérifier si quelqu'un d'autre partage déjà sur ce whiteboard
        if (_receivingStates.ContainsKey(targetWhiteboard.id))
        {
            Debug.LogWarning($"[ScreenShare] Whiteboard {targetWhiteboard.id} already has an active share");
            return;
        }

        // Initialize
        _isSharing = true;
        _sharingToWhiteboardId = targetWhiteboard.id;
        _sharingToWhiteboard = targetWhiteboard;
        _frameIndex = 0;

        string sharerName = PlayerPrefs.GetString("PlayerName", "Player");

        // Setup capture resources
        InitializeCaptureResources();

        // Enter presentation mode on whiteboard
        targetWhiteboard.EnterPresentationMode(sharerName);

        // Notify room
        VRNetworkManager.Instance.Send("screen-share-start", new ScreenShareStartData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            whiteboardId = targetWhiteboard.id,
            sharerId = VRNetworkManager.LocalId,
            sharerName = sharerName,
            width = captureWidth,
            height = captureHeight
        });

        // Start capture loop
        _captureCoroutine = StartCoroutine(CaptureLoop());

        LogDebug($"[ScreenShare] Started sharing to {targetWhiteboard.id} ({captureWidth}x{captureHeight} @ {captureFrameRate} FPS)");
        OnScreenShareStarted?.Invoke(targetWhiteboard.id, VRNetworkManager.LocalId, sharerName);
    }

    /// <summary>
    /// Arrête le partage d'écran
    /// </summary>
    public void StopSharing()
    {
        if (!_isSharing) return;

        _isSharing = false;

        // Stop capture
        if (_captureCoroutine != null)
        {
            StopCoroutine(_captureCoroutine);
            _captureCoroutine = null;
        }

        // Notify room
        VRNetworkManager.Instance?.Send("screen-share-stop", new ScreenShareStopData
        {
            roomId = VRRoomManager.Instance?.CurrentRoomId ?? "",
            whiteboardId = _sharingToWhiteboardId,
            sharerId = VRNetworkManager.LocalId
        });

        // Exit presentation mode
        if (_sharingToWhiteboard != null)
        {
            _sharingToWhiteboard.ExitPresentationMode();
        }

        // Cleanup
        CleanupCaptureResources();

        string whiteboardId = _sharingToWhiteboardId;
        _sharingToWhiteboardId = null;
        _sharingToWhiteboard = null;

        LogDebug($"[ScreenShare] Stopped sharing to {whiteboardId}");
        OnScreenShareStopped?.Invoke(whiteboardId, VRNetworkManager.LocalId);
    }

    #endregion

    #region Capture

    void InitializeCaptureResources()
    {
        _captureRT = new RenderTexture(captureWidth, captureHeight, 0, RenderTextureFormat.ARGB32);
        _captureRT.Create();

        _captureTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        _flippedTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
    }

    void CleanupCaptureResources()
    {
        if (_captureRT != null)
        {
            _captureRT.Release();
            Destroy(_captureRT);
            _captureRT = null;
        }

        if (_captureTexture != null)
        {
            Destroy(_captureTexture);
            _captureTexture = null;
        }

        if (_flippedTexture != null)
        {
            Destroy(_flippedTexture);
            _flippedTexture = null;
        }
    }

    IEnumerator CaptureLoop()
    {
        WaitForSeconds frameDelay = new WaitForSeconds(1f / captureFrameRate);

        // Texture pour la capture de fenêtre
        Texture2D windowTexture = new Texture2D(16, 16, TextureFormat.RGB24, false);

        while (_isSharing && _sharingToWhiteboard != null)
        {
            yield return new WaitForEndOfFrame();

            Texture2D textureToSend = null;

            if (_selectedWindow != null)
            {
                // Capture de fenêtre Windows
                bool success = WindowCapture.CaptureWindow(_selectedWindow, windowTexture);
                if (success)
                {
                    // Flip horizontal pour les fenêtres Windows
                    FlipTextureHorizontal(windowTexture);
                    textureToSend = windowTexture;
                }
                else
                {
                    LogDebug("[ScreenShare] Window capture failed - window may be closed");
                    continue;
                }
            }
            else
            {
                // Capture du jeu Unity (comportement original)
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRT);

                RenderTexture.active = _captureRT;
                _captureTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                _captureTexture.Apply();
                RenderTexture.active = null;

                // Flip texture (nécessaire pour Unity capture)
                FlipTexture(_captureTexture, _flippedTexture);
                textureToSend = _flippedTexture;
            }

            if (textureToSend == null)
                continue;

            // Encode as JPEG
            byte[] jpegData = textureToSend.EncodeToJPG(jpegQuality);
            string base64Data = Convert.ToBase64String(jpegData);

            // Send frame
            VRNetworkManager.Instance.Send("screen-share-frame", new ScreenShareFrameData
            {
                roomId = VRRoomManager.Instance.CurrentRoomId,
                whiteboardId = _sharingToWhiteboardId,
                sharerId = VRNetworkManager.LocalId,
                imageData = base64Data,
                frameIndex = _frameIndex++,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Update local whiteboard display
            _sharingToWhiteboard.UpdatePresentationTexture(textureToSend);

            yield return frameDelay;
        }

        // Cleanup
        if (windowTexture != null)
        {
            Destroy(windowTexture);
        }
    }

    /// <summary>
    /// Flip texture horizontally (source -> destination)
    /// </summary>
    void FlipTexture(Texture2D source, Texture2D destination)
    {
        int width = source.width;
        int height = source.height;
        Color[] sourcePixels = source.GetPixels();
        Color[] destPixels = new Color[sourcePixels.Length];

        // Flip horizontal only
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int srcIndex = y * width + x;
                int dstIndex = y * width + (width - 1 - x);
                destPixels[dstIndex] = sourcePixels[srcIndex];
            }
        }

        destination.SetPixels(destPixels);
        destination.Apply();
    }

    /// <summary>
    /// Flip texture horizontally in-place
    /// </summary>
    void FlipTextureHorizontal(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        Color32[] pixels = texture.GetPixels32();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width / 2; x++)
            {
                int leftIndex = y * width + x;
                int rightIndex = y * width + (width - 1 - x);

                // Swap
                Color32 temp = pixels[leftIndex];
                pixels[leftIndex] = pixels[rightIndex];
                pixels[rightIndex] = temp;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
    }

    #endregion

    #region Network Handlers

    void HandleNetworkMessage(NetworkMessage msg)
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            return;

        switch (msg.type)
        {
            case "screen-share-start":
                HandleShareStart(msg);
                break;
            case "screen-share-stop":
                HandleShareStop(msg);
                break;
            case "screen-share-frame":
                HandleShareFrame(msg);
                break;
            case "screen-share-request":
                HandleShareRequest(msg);
                break;
            case "screen-share-state":
                HandleShareState(msg);
                break;
        }
    }

    void HandleShareStart(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<ScreenShareStartData>(msg.data);

        // Verify same room
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Ignore own message
        if (data.sharerId == VRNetworkManager.LocalId)
            return;

        LogDebug($"[ScreenShare] {data.sharerName} started sharing to {data.whiteboardId}");

        // Find the whiteboard
        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard == null)
        {
            Debug.LogWarning($"[ScreenShare] Whiteboard {data.whiteboardId} not found");
            return;
        }

        // Create receiving state
        var state = new WhiteboardShareState
        {
            sharerId = data.sharerId,
            sharerName = data.sharerName,
            displayTexture = new Texture2D(data.width, data.height, TextureFormat.RGB24, false)
        };
        _receivingStates[data.whiteboardId] = state;

        // Enter presentation mode on whiteboard
        targetWhiteboard.EnterPresentationMode(data.sharerName);

        OnScreenShareStarted?.Invoke(data.whiteboardId, data.sharerId, data.sharerName);
    }

    void HandleShareStop(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<ScreenShareStopData>(msg.data);

        // Verify same room
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Verify we're receiving from this sharer on this whiteboard
        if (!_receivingStates.TryGetValue(data.whiteboardId, out var state))
            return;
        if (state.sharerId != data.sharerId)
            return;

        LogDebug($"[ScreenShare] {state.sharerName} stopped sharing to {data.whiteboardId}");

        // Find whiteboard and exit presentation mode
        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard != null)
        {
            targetWhiteboard.ExitPresentationMode();
        }

        // Cleanup state
        if (state.displayTexture != null)
        {
            Destroy(state.displayTexture);
        }
        _receivingStates.Remove(data.whiteboardId);

        OnScreenShareStopped?.Invoke(data.whiteboardId, data.sharerId);
    }

    void HandleShareFrame(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<ScreenShareFrameData>(msg.data);

        // Verify same room
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Verify we're receiving from this sharer on this whiteboard
        if (!_receivingStates.TryGetValue(data.whiteboardId, out var state))
            return;
        if (state.sharerId != data.sharerId)
            return;

        try
        {
            // Decode JPEG
            byte[] jpegData = Convert.FromBase64String(data.imageData);
            state.displayTexture.LoadImage(jpegData);

            // Find whiteboard and update
            Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
            if (targetWhiteboard != null)
            {
                targetWhiteboard.UpdatePresentationTexture(state.displayTexture);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenShare] Failed to decode frame: {e.Message}");
        }
    }

    void HandleShareRequest(NetworkMessage msg)
    {
        // Only respond if we're sharing
        if (!_isSharing) return;

        var data = JsonUtility.FromJson<ScreenShareRequestData>(msg.data);
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // If request is for a specific whiteboard, check if it's ours
        if (!string.IsNullOrEmpty(data.whiteboardId) && data.whiteboardId != _sharingToWhiteboardId)
            return;

        LogDebug($"[ScreenShare] Sending state to late joiner {data.requesterId}");

        string sharerName = PlayerPrefs.GetString("PlayerName", "Player");

        // Send current state
        VRNetworkManager.Instance.Send("screen-share-state", new ScreenShareStateData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            whiteboardId = _sharingToWhiteboardId,
            isSharing = true,
            sharerId = VRNetworkManager.LocalId,
            sharerName = sharerName
        });
    }

    void HandleShareState(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<ScreenShareStateData>(msg.data);
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Ignore our own state
        if (data.sharerId == VRNetworkManager.LocalId)
            return;

        // If someone is sharing to a whiteboard we're not already receiving
        if (data.isSharing && !_receivingStates.ContainsKey(data.whiteboardId))
        {
            LogDebug($"[ScreenShare] Late joiner: {data.sharerName} is sharing to {data.whiteboardId}");

            Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
            if (targetWhiteboard == null)
            {
                Debug.LogWarning($"[ScreenShare] Whiteboard {data.whiteboardId} not found");
                return;
            }

            // Create receiving state
            var state = new WhiteboardShareState
            {
                sharerId = data.sharerId,
                sharerName = data.sharerName,
                displayTexture = new Texture2D(1280, 720, TextureFormat.RGB24, false)
            };
            _receivingStates[data.whiteboardId] = state;

            targetWhiteboard.EnterPresentationMode(data.sharerName);

            OnScreenShareStarted?.Invoke(data.whiteboardId, data.sharerId, data.sharerName);
        }
    }

    #endregion

    #region Room Events

    void OnRoomJoined(string roomId)
    {
        // Request current screen share state (for late joiners)
        StartCoroutine(RequestShareStateDelayed());
    }

    IEnumerator RequestShareStateDelayed()
    {
        yield return new WaitForSeconds(1.5f);

        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            // Request state for all whiteboards (empty whiteboardId)
            VRNetworkManager.Instance.Send("screen-share-request", new ScreenShareRequestData
            {
                roomId = VRRoomManager.Instance.CurrentRoomId,
                whiteboardId = "",  // Empty = request all
                requesterId = VRNetworkManager.LocalId
            });
        }
    }

    void OnRoomLeft()
    {
        // Stop sharing if we were sharing
        if (_isSharing)
        {
            StopSharing();
        }

        // Exit presentation mode on all whiteboards we were receiving
        foreach (var kvp in _receivingStates)
        {
            Whiteboard wb = FindWhiteboardById(kvp.Key);
            if (wb != null)
            {
                wb.ExitPresentationMode();
            }
            if (kvp.Value.displayTexture != null)
            {
                Destroy(kvp.Value.displayTexture);
            }
        }
        _receivingStates.Clear();
    }

    void OnPlayerLeft(string playerId)
    {
        // Check if any shares were from this player
        List<string> whiteboardsToStop = new List<string>();

        foreach (var kvp in _receivingStates)
        {
            if (kvp.Value.sharerId == playerId)
            {
                whiteboardsToStop.Add(kvp.Key);
            }
        }

        foreach (string wbId in whiteboardsToStop)
        {
            LogDebug($"[ScreenShare] Sharer {playerId} left, stopping share on {wbId}");

            Whiteboard wb = FindWhiteboardById(wbId);
            if (wb != null)
            {
                wb.ExitPresentationMode();
            }

            if (_receivingStates.TryGetValue(wbId, out var state))
            {
                if (state.displayTexture != null)
                {
                    Destroy(state.displayTexture);
                }
                OnScreenShareStopped?.Invoke(wbId, playerId);
            }
            _receivingStates.Remove(wbId);
        }
    }

    #endregion

    #region Helpers

    Whiteboard FindWhiteboardById(string whiteboardId)
    {
        // Find all whiteboards and match by ID
        Whiteboard[] whiteboards = FindObjectsByType<Whiteboard>(FindObjectsSortMode.None);
        foreach (var wb in whiteboards)
        {
            if (wb.id == whiteboardId)
                return wb;
        }
        return null;
    }

    void CleanupResources()
    {
        CleanupCaptureResources();

        foreach (var kvp in _receivingStates)
        {
            if (kvp.Value.displayTexture != null)
            {
                Destroy(kvp.Value.displayTexture);
            }
        }
        _receivingStates.Clear();
    }

    void LogDebug(string message)
    {
        if (showDebugInfo)
        {
            Debug.Log(message);
        }
    }

    #endregion
}
