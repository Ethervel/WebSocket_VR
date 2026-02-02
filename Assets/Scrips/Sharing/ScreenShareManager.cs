using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestionnaire de partage d'écran via WebSocket.
/// Supporte plusieurs whiteboards indépendants.
/// </summary>
public class ScreenShareManager : MonoBehaviour
{
    public static ScreenShareManager Instance { get; private set; }

    [Header("Capture Settings")]
    [Tooltip("Largeur de capture")]
    public int captureWidth = 1280;

    [Tooltip("Hauteur de capture")]
    public int captureHeight = 720;

    [Tooltip("Qualité JPEG (0-100)")]
    [Range(0, 100)]
    public int jpegQuality = 75;

    [Tooltip("Frames par seconde")]
    [Range(1, 15)]
    public float captureFrameRate = 5f;

    [Header("Debug")]
    [Tooltip("Activer les raccourcis clavier de test (F9=Start, F10=Stop)")]
    public bool enableTestShortcuts = true;

    // État local
    private bool _isSharing = false;
    private string _sharingToWhiteboardId = null;
    private Whiteboard _sharingToWhiteboard = null;
    private WindowCapture.WindowInfo _selectedWindow = null;

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

    // P2 FIX: Track pending coroutines and window texture for proper cleanup
    private Coroutine _pendingRequestCoroutine;
    private Texture2D _windowTexture;

    // IMPORTANT FIX: Timeout handling for late joiner state requests
    [Header("State Request Timeout")]
    [Tooltip("Timeout in seconds waiting for state response")]
    public float stateRequestTimeout = 10f;
    [Tooltip("Maximum retry attempts for state requests")]
    public int maxStateRequestRetries = 2;
    private int _stateRequestRetries = 0;
    private bool _waitingForStateResponse = false;

    // MINOR FIX: Constants for magic numbers
    private const int DEFAULT_DISPLAY_WIDTH = 1280;
    private const int DEFAULT_DISPLAY_HEIGHT = 720;
    private const int WINDOW_TEXTURE_INIT_SIZE = 16;
    private const float STATE_REQUEST_DELAY = 1.5f;

    // Events
    public static event Action<string, string, string> OnScreenShareStarted;
    public static event Action<string, string> OnScreenShareStopped;

    // Public state access
    public bool IsSharing => _isSharing;
    public string SharingToWhiteboardId => _sharingToWhiteboardId;
    public WindowCapture.WindowInfo SelectedWindow => _selectedWindow;
    public string SelectedWindowTitle => _selectedWindow?.Title ?? "Jeu Unity";

    public static event Action<List<WindowCapture.WindowInfo>> OnWindowListUpdated;

    public bool IsWhiteboardReceiving(string whiteboardId)
    {
        return _receivingStates.ContainsKey(whiteboardId);
    }

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

        // MINOR FIX: Ensure pending request coroutine is stopped on destroy
        if (_pendingRequestCoroutine != null)
        {
            StopCoroutine(_pendingRequestCoroutine);
            _pendingRequestCoroutine = null;
        }

        CleanupResources();
    }

    void Update()
    {
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

    private int _testWindowIndex = -1;

    void ListWindowsAndSelectNext()
    {
        var windows = GetAvailableWindows();

        _testWindowIndex++;
        if (_testWindowIndex >= windows.Count)
        {
            _testWindowIndex = -1;
        }

        if (_testWindowIndex == -1)
        {
            SelectWindow(null);
        }
        else
        {
            SelectWindow(windows[_testWindowIndex]);
        }
    }

    void TestStartSharing()
    {
        if (_isSharing) return;
        if (!CanShare()) return;

        Whiteboard[] whiteboards = FindObjectsByType<Whiteboard>(FindObjectsSortMode.None);
        if (whiteboards.Length == 0) return;

        StartSharing(whiteboards[0]);
    }

    #region Public API

    public bool CanShare()
    {
        return true;
    }

    public bool IsVRMode()
    {
        return VRGameManager.Instance != null && !VRGameManager.Instance.IsDesktopMode;
    }

    public List<WindowCapture.WindowInfo> GetAvailableWindows()
    {
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

    public void SelectWindow(WindowCapture.WindowInfo window)
    {
        _selectedWindow = window;
    }

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

    public void StartSharing(Whiteboard targetWhiteboard)
    {
        if (targetWhiteboard == null)
        {
            Debug.LogError("[ScreenShare] Target whiteboard is null");
            return;
        }

        if (_isSharing) return;
        if (!CanShare()) return;

        if (IsVRMode())
        {
            _selectedWindow = null;
        }

        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        if (_receivingStates.ContainsKey(targetWhiteboard.id)) return;

        _isSharing = true;
        _sharingToWhiteboardId = targetWhiteboard.id;
        _sharingToWhiteboard = targetWhiteboard;
        _frameIndex = 0;

        string sharerName = PlayerPrefs.GetString("PlayerName", "Player");

        InitializeCaptureResources();
        targetWhiteboard.EnterPresentationMode(sharerName);

        // MINOR FIX: Add null check before sending
        if (VRNetworkManager.Instance != null)
        {
            VRNetworkManager.Instance.Send("screen-share-start", new ScreenShareStartData
            {
                roomId = VRRoomManager.Instance.CurrentRoomId,
                whiteboardId = targetWhiteboard.id,
                sharerId = VRNetworkManager.LocalId,
                sharerName = sharerName,
                width = captureWidth,
                height = captureHeight
            });
        }

        _captureCoroutine = StartCoroutine(CaptureLoop());

        OnScreenShareStarted?.Invoke(targetWhiteboard.id, VRNetworkManager.LocalId, sharerName);
    }

    public void StopSharing()
    {
        if (!_isSharing) return;

        _isSharing = false;

        if (_captureCoroutine != null)
        {
            StopCoroutine(_captureCoroutine);
            _captureCoroutine = null;
        }

        VRNetworkManager.Instance?.Send("screen-share-stop", new ScreenShareStopData
        {
            roomId = VRRoomManager.Instance?.CurrentRoomId ?? "",
            whiteboardId = _sharingToWhiteboardId,
            sharerId = VRNetworkManager.LocalId
        });

        if (_sharingToWhiteboard != null)
        {
            _sharingToWhiteboard.ExitPresentationMode();
        }

        CleanupCaptureResources();

        string whiteboardId = _sharingToWhiteboardId;
        _sharingToWhiteboardId = null;
        _sharingToWhiteboard = null;

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

        // P2 FIX: Also cleanup window texture to prevent leaks on early termination
        if (_windowTexture != null)
        {
            Destroy(_windowTexture);
            _windowTexture = null;
        }
    }

    IEnumerator CaptureLoop()
    {
        WaitForSeconds frameDelay = new WaitForSeconds(1f / captureFrameRate);

        // P2 FIX: Use class-level texture for proper cleanup on early termination
        // MINOR FIX: Use constant for initial texture size
        if (_windowTexture == null)
            _windowTexture = new Texture2D(WINDOW_TEXTURE_INIT_SIZE, WINDOW_TEXTURE_INIT_SIZE, TextureFormat.RGB24, false);

        while (_isSharing && _sharingToWhiteboard != null)
        {
            yield return new WaitForEndOfFrame();

            Texture2D textureToSend = null;

            if (_selectedWindow != null)
            {
                bool success = WindowCapture.CaptureWindow(_selectedWindow, _windowTexture);
                if (success)
                {
                    FlipTextureHorizontal(_windowTexture);
                    textureToSend = _windowTexture;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRT);

                RenderTexture.active = _captureRT;
                _captureTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                _captureTexture.Apply();
                RenderTexture.active = null;

                FlipTexture(_captureTexture, _flippedTexture);
                textureToSend = _flippedTexture;
            }

            if (textureToSend == null)
                continue;

            byte[] jpegData = textureToSend.EncodeToJPG(jpegQuality);
            string base64Data = Convert.ToBase64String(jpegData);

            // MINOR FIX: Add null check before sending
            if (VRNetworkManager.Instance != null && VRRoomManager.Instance != null)
            {
                VRNetworkManager.Instance.Send("screen-share-frame", new ScreenShareFrameData
                {
                    roomId = VRRoomManager.Instance.CurrentRoomId,
                    whiteboardId = _sharingToWhiteboardId,
                    sharerId = VRNetworkManager.LocalId,
                    imageData = base64Data,
                    frameIndex = _frameIndex++,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }

            _sharingToWhiteboard.UpdateScreenShare(textureToSend, rotate180: false);

            yield return frameDelay;
        }

        // P2 FIX: Cleanup is now handled in CleanupCaptureResources() instead of here
        // This ensures cleanup happens even on early StopCoroutine()
    }

    void FlipTexture(Texture2D source, Texture2D destination)
    {
        int width = source.width;
        int height = source.height;
        Color[] sourcePixels = source.GetPixels();
        Color[] destPixels = new Color[sourcePixels.Length];

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

    /// <summary>
    /// IMPORTANT FIX: Safe JSON deserialization with validation.
    /// </summary>
    private T TryDeserialize<T>(string json, string context) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning($"[ScreenShare] Empty JSON data for {context}");
            return null;
        }

        try
        {
            T result = JsonUtility.FromJson<T>(json);
            if (result == null)
            {
                Debug.LogWarning($"[ScreenShare] Null result from JSON for {context}");
                return null;
            }
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenShare] JSON parse error for {context}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// IMPORTANT FIX: Safe Base64 decode for image data.
    /// </summary>
    private byte[] TryDecodeBase64(string base64Data, string context)
    {
        if (string.IsNullOrEmpty(base64Data))
            return null;

        try
        {
            return Convert.FromBase64String(base64Data);
        }
        catch (FormatException e)
        {
            Debug.LogError($"[ScreenShare] Base64 decode error for {context}: {e.Message}");
            return null;
        }
    }

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
        var data = TryDeserialize<ScreenShareStartData>(msg.data, "screen-share-start");
        if (data == null || string.IsNullOrEmpty(data.whiteboardId)) return;

        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;
        if (data.sharerId == VRNetworkManager.LocalId) return;

        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard == null) return;

        var state = new WhiteboardShareState
        {
            sharerId = data.sharerId,
            sharerName = data.sharerName,
            displayTexture = new Texture2D(data.width, data.height, TextureFormat.RGB24, false)
        };
        _receivingStates[data.whiteboardId] = state;

        targetWhiteboard.EnterPresentationMode(data.sharerName);

        OnScreenShareStarted?.Invoke(data.whiteboardId, data.sharerId, data.sharerName);
    }

    void HandleShareStop(NetworkMessage msg)
    {
        var data = TryDeserialize<ScreenShareStopData>(msg.data, "screen-share-stop");
        if (data == null || string.IsNullOrEmpty(data.whiteboardId)) return;

        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        if (!_receivingStates.TryGetValue(data.whiteboardId, out var state)) return;
        if (state.sharerId != data.sharerId) return;

        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard != null)
        {
            targetWhiteboard.ExitPresentationMode();
        }

        if (state.displayTexture != null)
        {
            Destroy(state.displayTexture);
        }
        _receivingStates.Remove(data.whiteboardId);

        OnScreenShareStopped?.Invoke(data.whiteboardId, data.sharerId);
    }

    void HandleShareFrame(NetworkMessage msg)
    {
        var data = TryDeserialize<ScreenShareFrameData>(msg.data, "screen-share-frame");
        if (data == null || string.IsNullOrEmpty(data.whiteboardId)) return;

        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        if (!_receivingStates.TryGetValue(data.whiteboardId, out var state)) return;
        if (state.sharerId != data.sharerId) return;

        // IMPORTANT FIX: Safe Base64 decode
        byte[] jpegData = TryDecodeBase64(data.imageData, "frame-image");
        if (jpegData == null || jpegData.Length == 0) return;

        // IMPORTANT FIX: Validate texture before loading
        // MINOR FIX: Use constants for default dimensions
        if (state.displayTexture == null)
        {
            Debug.LogWarning("[ScreenShare] Display texture is null, recreating...");
            state.displayTexture = new Texture2D(DEFAULT_DISPLAY_WIDTH, DEFAULT_DISPLAY_HEIGHT, TextureFormat.RGB24, false);
        }

        state.displayTexture.LoadImage(jpegData);

        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard != null)
        {
            targetWhiteboard.UpdateScreenShare(state.displayTexture, rotate180: false);
        }
    }

    void HandleShareRequest(NetworkMessage msg)
    {
        if (!_isSharing) return;

        var data = TryDeserialize<ScreenShareRequestData>(msg.data, "screen-share-request");
        if (data == null) return;

        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        if (!string.IsNullOrEmpty(data.whiteboardId) && data.whiteboardId != _sharingToWhiteboardId)
            return;

        // IMPORTANT FIX: Check connection before sending
        if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null) return;

        string sharerName = PlayerPrefs.GetString("PlayerName", "Player");

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
        var data = TryDeserialize<ScreenShareStateData>(msg.data, "screen-share-state");
        if (data == null || string.IsNullOrEmpty(data.whiteboardId)) return;

        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;
        if (data.sharerId == VRNetworkManager.LocalId) return;

        // IMPORTANT FIX: Mark state response as received for timeout handling
        _waitingForStateResponse = false;

        if (data.isSharing && !_receivingStates.ContainsKey(data.whiteboardId))
        {
            Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
            if (targetWhiteboard == null) return;

            // MINOR FIX: Use constants for default dimensions
            var state = new WhiteboardShareState
            {
                sharerId = data.sharerId,
                sharerName = data.sharerName,
                displayTexture = new Texture2D(DEFAULT_DISPLAY_WIDTH, DEFAULT_DISPLAY_HEIGHT, TextureFormat.RGB24, false)
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
        // P2 FIX: Cancel any pending request coroutine before starting a new one
        if (_pendingRequestCoroutine != null)
            StopCoroutine(_pendingRequestCoroutine);
        _pendingRequestCoroutine = StartCoroutine(RequestShareStateDelayed());
    }

    IEnumerator RequestShareStateDelayed()
    {
        // MINOR FIX: Use constant for delay
        yield return new WaitForSeconds(STATE_REQUEST_DELAY);

        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            _stateRequestRetries = 0;
            yield return StartCoroutine(RequestStateWithTimeout());
        }

        // P2 FIX: Clear coroutine reference when complete
        _pendingRequestCoroutine = null;
    }

    // IMPORTANT FIX: Request state with timeout and retry logic
    IEnumerator RequestStateWithTimeout()
    {
        while (_stateRequestRetries < maxStateRequestRetries)
        {
            _stateRequestRetries++;
            _waitingForStateResponse = true;

            Debug.Log($"[ScreenShare] IMPORTANT FIX: Requesting share state (attempt {_stateRequestRetries}/{maxStateRequestRetries})");

            // IMPORTANT FIX: Check connection before sending
            if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null)
            {
                Debug.LogWarning("[ScreenShare] Cannot request state - not connected");
                _waitingForStateResponse = false;
                yield break;
            }

            VRNetworkManager.Instance.Send("screen-share-request", new ScreenShareRequestData
            {
                roomId = VRRoomManager.Instance.CurrentRoomId,
                whiteboardId = "",
                requesterId = VRNetworkManager.LocalId
            });

            // Wait for response or timeout
            float timer = 0f;
            while (_waitingForStateResponse && timer < stateRequestTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            if (!_waitingForStateResponse)
            {
                // Response received (or no active share in room)
                Debug.Log("[ScreenShare] IMPORTANT FIX: State response received or no active share");
                yield break;
            }

            // Timeout - retry if attempts remaining
            Debug.LogWarning($"[ScreenShare] IMPORTANT FIX: State request timeout after {stateRequestTimeout}s (attempt {_stateRequestRetries}/{maxStateRequestRetries})");
        }

        // All retries exhausted - no screen share active or network issue
        _waitingForStateResponse = false;
        Debug.Log("[ScreenShare] IMPORTANT FIX: State request completed - no active screen share detected or timeout");
    }

    void OnRoomLeft()
    {
        // P2 FIX: Cancel any pending request coroutine on room leave
        if (_pendingRequestCoroutine != null)
        {
            StopCoroutine(_pendingRequestCoroutine);
            _pendingRequestCoroutine = null;
        }

        if (_isSharing)
        {
            StopSharing();
        }

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

    #endregion
}
