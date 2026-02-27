using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// Central WebSocket manager for VR / WebGL
/// - Singleton
/// - Auto reconnect
/// - Server-driven authentication via "welcome"
/// - Clean event dispatch
/// </summary>
public class VRNetworkManager : MonoBehaviour
{
    public static VRNetworkManager Instance { get; private set; }

    [Header("Server Configuration")]
    [Tooltip("WebSocket server URL. SECURITY: Use wss:// (TLS) in production, ws:// only for local development")]
    public string serverUrl = "ws://localhost:8080";

    [Tooltip("Enable this in production builds to enforce secure connections (wss://)")]
    public bool enforceSecureConnection = false;

    public bool autoReconnect = true;
    public float reconnectDelay = 3f;

    [Header("Connection Timeout (P0 Fix)")]
    [Tooltip("Timeout in seconds waiting for 'welcome' message after connection")]
    public float welcomeTimeout = 5f;

    [Header("Exponential Backoff (P0 Fix)")]
    [Tooltip("Initial reconnect delay in seconds")]
    public float initialReconnectDelay = 1f;
    [Tooltip("Maximum reconnect delay in seconds")]
    public float maxReconnectDelay = 30f;
    [Tooltip("Multiplier for each retry")]
    public float backoffMultiplier = 2f;

    public static string LocalId { get; private set; }
    public static bool IsConnected { get; private set; }

    private WebSocket _websocket;
    private bool _isReconnecting;
    private float _reconnectTimer;
    private float _currentReconnectDelay;
    private int _reconnectAttempts;

    // P0 FIX: Track welcome message timeout
    private float _welcomeTimeoutTimer;
    private bool _waitingForWelcome;

    // Cache pour réduire les allocations GC lors de l'envoi fréquent (30Hz)
    private readonly NetworkMessage _cachedOutgoingMessage = new NetworkMessage();

    [Header("Debug / Offline Mode")]
    [Tooltip("Enable offline mode to test without server connection")]
    public bool offlineMode = false;

    [Tooltip("Auto-create a room when in offline mode")]
    public bool offlineAutoCreateRoom = true;

    [Tooltip("Room type to create in offline mode")]
    public RoomType offlineRoomType = RoomType.MeetingRoomA;

    [Header("Rate Limiting (IMPORTANT FIX)")]
    [Tooltip("Maximum messages per second (0 = unlimited)")]
    public int maxMessagesPerSecond = 60;

    [Tooltip("Burst allowance (messages allowed in quick succession)")]
    public int burstAllowance = 10;

    // IMPORTANT FIX: Rate limiting state
    private float _rateLimitTokens;
    private float _lastRateLimitRefill;
    private int _messagesDropped;

    // ============================
    // EVENTS
    // ============================
    public static event Action OnConnected;
    public static event Action OnDisconnected;
    public static event Action<string> OnPeerConnected;
    public static event Action<string> OnPeerDisconnected;
    public static event Action<NetworkMessage> OnMessageReceived;
    public static event Action<string> OnConnectionError;

    // ============================
    // LIFECYCLE
    // ============================
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

    // P0 FIX: Ne plus utiliser async void Start() qui swallow les exceptions
    void Start()
    {
        // IMPORTANT FIX: Initialize rate limiting tokens
        _rateLimitTokens = burstAllowance;
        _lastRateLimitRefill = Time.unscaledTime;
        _messagesDropped = 0;

        // OFFLINE MODE: Skip real connection and simulate
        if (offlineMode)
        {
            Debug.Log("[VRNet] <color=yellow>OFFLINE MODE ENABLED</color> - Simulating connection");
            StartCoroutine(SimulateOfflineConnection());
            return;
        }

        // SECURITY FIX: Validate secure connection requirements
        ValidateConnectionSecurity();

        _currentReconnectDelay = initialReconnectDelay;
        _reconnectAttempts = 0;

        ConnectAsync();
    }

    /// <summary>
    /// SECURITY FIX: Validates that the connection meets security requirements.
    /// Warns or blocks insecure connections based on configuration.
    /// </summary>
    private void ValidateConnectionSecurity()
    {
        bool isSecure = serverUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
        bool isLocalhost = serverUrl.Contains("localhost") || serverUrl.Contains("127.0.0.1");

#if !UNITY_EDITOR
        // In builds, always warn about insecure connections
        if (!isSecure && !isLocalhost)
        {
            Debug.LogWarning("[VRNet] SECURITY WARNING: Using unencrypted WebSocket (ws://) connection to remote server. " +
                           "This exposes all data to interception. Use wss:// in production!");
        }

        // Block if enforceSecureConnection is enabled
        if (enforceSecureConnection && !isSecure)
        {
            Debug.LogError("[VRNet] SECURITY ERROR: enforceSecureConnection is enabled but serverUrl uses ws://. " +
                          "Change to wss:// or disable enforceSecureConnection for local testing.");
            enabled = false;
            return;
        }
#else
        // In editor, just log a reminder
        if (!isSecure && !isLocalhost)
        {
            Debug.Log("[VRNet] Note: Using ws:// connection. Remember to use wss:// for production deployment.");
        }
#endif
    }

    private async void ConnectAsync()
    {
        try
        {
            await Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRNet] P0 FIX: Connection failed with exception: {e.Message}");
            OnConnectionError?.Invoke(e.Message);
            HandleDisconnection();
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        _websocket?.DispatchMessageQueue();
#endif

        if (_waitingForWelcome)
        {
            _welcomeTimeoutTimer -= Time.deltaTime;
            if (_welcomeTimeoutTimer <= 0f)
            {
                Debug.LogWarning($"[VRNet] P0 FIX: Welcome message timeout after {welcomeTimeout}s - reconnecting");
                _waitingForWelcome = false;
                HandleDisconnection();
            }
        }

        if (_isReconnecting && autoReconnect)
        {
            _reconnectTimer -= Time.deltaTime;
            if (_reconnectTimer <= 0f)
            {
                _isReconnecting = false;
                _reconnectAttempts++;
                Debug.Log($"[VRNet] P0 FIX: Reconnect attempt #{_reconnectAttempts} (delay was {_currentReconnectDelay:F1}s)");
                ConnectAsync();
            }
        }
    }

    async void OnDestroy()
    {
        try
        {
            await Disconnect();
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRNet] P0 FIX: Error during OnDestroy: {e.Message}");
        }
    }

    void OnApplicationQuit()
    {
        // Force synchronous cleanup - async doesn't work in OnApplicationQuit
        try
        {
            autoReconnect = false;
            _isReconnecting = false;

            if (_websocket != null)
            {
                // Close synchronously - don't await
                _websocket.Close();
                _websocket = null;
            }

            IsConnected = false;
            LocalId = null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRNet] Error during OnApplicationQuit: {e.Message}");
        }

        // Force kill any remaining threads after a short delay
        #if !UNITY_EDITOR
        System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        });
        #endif
    }

    // ============================
    // CONNECTION
    // ============================
    public async Task Connect()
    {
        if (_websocket != null &&
            (_websocket.State == WebSocketState.Open ||
             _websocket.State == WebSocketState.Connecting))
            return;

        try
        {
            Debug.Log($"[VRNet] Connecting to {serverUrl}");
            _websocket = new WebSocket(serverUrl);

            _websocket.OnOpen += () =>
            {
                Debug.Log("[VRNet] WebSocket opened");
                _waitingForWelcome = true;
                _welcomeTimeoutTimer = welcomeTimeout;
                Debug.Log($"[VRNet] P0 FIX: Waiting for welcome message (timeout: {welcomeTimeout}s)");
            };

            _websocket.OnMessage += bytes =>
            {
                string json = System.Text.Encoding.UTF8.GetString(bytes);
                HandleMessage(json);
            };

            _websocket.OnClose += code =>
            {
                Debug.Log($"[VRNet] Closed ({code})");
                HandleDisconnection();
            };

            _websocket.OnError += err =>
            {
                Debug.LogError($"[VRNet] Error: {err}");
                OnConnectionError?.Invoke(err);
                HandleDisconnection();
            };

            await _websocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRNet] Connection exception: {e.Message}");
            HandleDisconnection();
        }
    }

    public async Task Disconnect()
    {
        autoReconnect = false;
        _isReconnecting = false;

        if (_websocket != null)
        {
            await _websocket.Close();
            _websocket = null;
        }

        IsConnected = false;
        LocalId = null;
    }

    private void HandleDisconnection()
    {
        bool wasConnected = IsConnected;

        // P0 FIX: Reset welcome timeout state
        _waitingForWelcome = false;

        IsConnected = false;
        LocalId = null;

        if (wasConnected)
        {
            OnDisconnected?.Invoke();
            // P0 FIX: Reset backoff on clean disconnect (was connected before)
            _currentReconnectDelay = initialReconnectDelay;
            _reconnectAttempts = 0;
        }

        if (autoReconnect && !_isReconnecting)
        {
            _isReconnecting = true;

            // P0 FIX: Exponential backoff - increase delay each attempt
            _reconnectTimer = _currentReconnectDelay;
            Debug.Log($"[VRNet] P0 FIX: Reconnecting in {_currentReconnectDelay:F1}s (attempt #{_reconnectAttempts + 1})");

            // Calculate next delay with exponential backoff, capped at max
            _currentReconnectDelay = Mathf.Min(_currentReconnectDelay * backoffMultiplier, maxReconnectDelay);
        }
    }

    // ============================
    // OFFLINE MODE SIMULATION
    // ============================

    /// <summary>
    /// Simulates a server connection for offline testing.
    /// Generates a fake player ID and triggers connection events.
    /// </summary>
    private IEnumerator SimulateOfflineConnection()
    {
        // Small delay to let other managers initialize
        yield return new WaitForSeconds(0.3f);

        // Generate offline player ID
        LocalId = "offline-" + UnityEngine.Random.Range(1000, 9999);
        IsConnected = true;

        Debug.Log($"[VRNet] <color=cyan>OFFLINE</color> Assigned ID: {LocalId}");
        OnConnected?.Invoke();

        // Auto-create room if enabled
        if (offlineAutoCreateRoom)
        {
            yield return new WaitForSeconds(0.2f);

            // VRRoomManager should be ready by now, create room directly
            if (VRRoomManager.Instance != null)
            {
                Debug.Log($"[VRNet] <color=cyan>OFFLINE</color> Auto-creating room ({offlineRoomType})");
                VRRoomManager.Instance.CreateRoom(offlineRoomType, "Offline Test Room");
            }
            else
            {
                Debug.LogWarning("[VRNet] OFFLINE: VRRoomManager not found, cannot auto-create room");
            }
        }
    }

    /// <summary>
    /// Returns true if running in offline mode (no real server connection).
    /// </summary>
    public bool IsOfflineMode => offlineMode && IsConnected;

    // ============================
    // MESSAGE HANDLING
    // ============================
    void HandleMessage(string json)
    {
        try
        {
            NetworkMessage msg = JsonUtility.FromJson<NetworkMessage>(json);

            // 1. Auth handshake
            if (msg.type == "welcome")
            {
                // P0 FIX: Welcome received, cancel timeout
                _waitingForWelcome = false;

                // P0 FIX: Reset exponential backoff on successful connection
                _currentReconnectDelay = initialReconnectDelay;
                _reconnectAttempts = 0;

                LocalId = msg.senderId;
                IsConnected = true;
                Debug.Log($"[VRNet] Assigned ID: {LocalId}");
                OnConnected?.Invoke();
                return;
            }

            // 2. Peer management
            if (msg.type == "peer-connected")
            {
                OnPeerConnected?.Invoke(msg.senderId);
                return;
            }

            if (msg.type == "peer-disconnected")
            {
                OnPeerDisconnected?.Invoke(msg.senderId);
                return;
            }

            // 3. Ignore echo (except server-only payloads)
            if (msg.senderId == LocalId && msg.type != "whiteboard-history")
                return;

            // 4. Forward to gameplay systems
            OnMessageReceived?.Invoke(msg);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRNet] JSON parse error: {e.Message}\n{json}");
        }
    }

    // ============================
    // SEND API
    // ============================
    public void Send(string type)
    {
        SendInternal(type, "{}");
    }

    public void Send(string type, object payload)
    {
        string dataJson = payload is string s ? s : JsonUtility.ToJson(payload);
        SendInternal(type, dataJson);
    }

    /// <summary>
    /// IMPORTANT FIX: Check and consume rate limit tokens using token bucket algorithm.
    /// Returns true if the message can be sent, false if rate limited.
    /// </summary>
    private bool CheckRateLimit(string messageType)
    {
        // Skip rate limiting if disabled
        if (maxMessagesPerSecond <= 0)
            return true;

        // Refill tokens based on time elapsed
        float currentTime = Time.unscaledTime;
        float elapsed = currentTime - _lastRateLimitRefill;
        _lastRateLimitRefill = currentTime;

        // Add tokens based on time, capped at burst allowance
        _rateLimitTokens = Mathf.Min(burstAllowance, _rateLimitTokens + (elapsed * maxMessagesPerSecond));

        // Check if we have tokens available
        if (_rateLimitTokens >= 1f)
        {
            _rateLimitTokens -= 1f;
            return true;
        }

        // Rate limited - drop the message
        _messagesDropped++;
        if (_messagesDropped % 100 == 1) // Log every 100th dropped message
        {
            Debug.LogWarning($"[VRNet] IMPORTANT FIX: Rate limited message '{messageType}' (total dropped: {_messagesDropped})");
        }
        return false;
    }

    // P0 FIX: async void with proper exception handling
    private async void SendInternal(string type, string dataJson)
    {
        // OFFLINE MODE: Silently ignore sends (no websocket to send to)
        if (offlineMode)
        {
            // Log only non-spammy messages for debugging
            if (type != "vr-position")
            {
                Debug.Log($"[VRNet] <color=grey>OFFLINE SEND (ignored):</color> {type}");
            }
            return;
        }

        if (_websocket == null || _websocket.State != WebSocketState.Open)
            return;

        // IMPORTANT FIX: Apply rate limiting to prevent network flooding
        // Skip rate limiting for critical messages (welcome handshake, room management)
        bool isCritical = type == "welcome" || type.StartsWith("room-") || type.StartsWith("webrtc-");
        if (!isCritical && !CheckRateLimit(type))
            return;

        try
        {
            // Réutiliser le message caché pour éviter les allocations GC
            _cachedOutgoingMessage.type = type;
            _cachedOutgoingMessage.senderId = LocalId;
            _cachedOutgoingMessage.data = dataJson;

            await _websocket.SendText(JsonUtility.ToJson(_cachedOutgoingMessage));
        }
        catch (Exception e)
        {
            // P0 FIX: Don't let send errors go unnoticed
            Debug.LogError($"[VRNet] P0 FIX: Send failed for '{type}': {e.Message}");
            // Don't trigger reconnection on send failure - let the socket state handle it
        }
    }

    // ============================
    // HELPERS
    // ============================
    public bool IsConnectionOpen()
    {
        // OFFLINE MODE: Always return true if in offline mode and "connected"
        if (offlineMode && IsConnected)
            return true;

        return _websocket != null && _websocket.State == WebSocketState.Open;
    }
}

[Serializable]
public class NetworkMessage
{
    public string type;
    public string senderId;
    public string data; // Always JSON string (JsonUtility constraint)
}
