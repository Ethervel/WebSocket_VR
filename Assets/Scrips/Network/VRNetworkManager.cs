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
    public string serverUrl = "ws://localhost:8080";
    public bool autoReconnect = true;
    public float reconnectDelay = 3f;

    public static string LocalId { get; private set; }
    public static bool IsConnected { get; private set; }

    private WebSocket _websocket;
    private bool _isReconnecting;
    private float _reconnectTimer;

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

    async void Start()
    {
        await Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        _websocket?.DispatchMessageQueue();
#endif

        if (_isReconnecting && autoReconnect)
        {
            _reconnectTimer -= Time.deltaTime;
            if (_reconnectTimer <= 0f)
            {
                _isReconnecting = false;
                _ = Connect();
            }
        }
    }

    async void OnDestroy() => await Disconnect();
    async void OnApplicationQuit() => await Disconnect();

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
                // On attend explicitement le message "welcome"
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

        IsConnected = false;
        LocalId = null;

        if (wasConnected)
            OnDisconnected?.Invoke();

        if (autoReconnect && !_isReconnecting)
        {
            _isReconnecting = true;
            _reconnectTimer = reconnectDelay;
            Debug.Log($"[VRNet] Reconnecting in {reconnectDelay}s");
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

    private async void SendInternal(string type, string dataJson)
    {
        if (_websocket == null || _websocket.State != WebSocketState.Open)
            return;

        NetworkMessage msg = new NetworkMessage
        {
            type = type,
            senderId = LocalId,
            data = dataJson
        };

        await _websocket.SendText(JsonUtility.ToJson(msg));
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
