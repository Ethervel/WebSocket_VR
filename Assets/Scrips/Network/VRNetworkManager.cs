using System;
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
        // SECURITY FIX: Validate secure connection requirements
        ValidateConnectionSecurity();

        _currentReconnectDelay = initialReconnectDelay;
        _reconnectAttempts = 0;

        // IMPORTANT FIX: Initialize rate limiting tokens
        _rateLimitTokens = burstAllowance;
        _lastRateLimitRefill = Time.unscaledTime;
        _messagesDropped = 0;

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

    // P0 FIX: Wrapper qui gère correctement les exceptions async
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

        // P0 FIX: Check for welcome message timeout
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

        // P0 FIX: Exponential backoff reconnection
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

    // P0 FIX: Proper async void with try-catch for Unity lifecycle methods
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
                // P0 FIX: Start welcome timeout - server must send "welcome" within timeout
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
        return _websocket != null && _websocket.State == WebSocketState.Open;
    }
}

// ============================
// MESSAGE FORMAT
// ============================
[Serializable]
public class NetworkMessage
{
    public string type;
    public string senderId;
    public string data; // Always JSON string (JsonUtility constraint)
}
