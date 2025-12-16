using System;
using UnityEngine;
using NativeWebSocket;

// Main network manager for VR / WebGL
// Handles WebSocket connection, reconnection, and message dispatching

public class VRNetworkManager : MonoBehaviour
{
    // Singleton instance (one network manager for the whole app)
    public static VRNetworkManager Instance { get; private set; }
    [Header("Server Configuration")]

    // Websocket signaling server URL
    public string serverUrl = "ws://localhost:8080/game";
    
    // Enable / disable automatic reconnection
    public bool autoReconnect = true;

    // Delay before attempting reconnection
    public float reconnectDelay = 3f;

    // Local client ID assigned by the server
    public static string LocalId {get; private set;}

    // True when handshake is completed and ID is assigned
    public static bool IsConnected { get; private set; }

    // WebSocket client instance
    private WebSocket _websocket;

    // Reconnection state
    private bool _isReconnecting;
    private float _reconnectTimer;

    // Public events for the rest of the app
    public static event Action OnConnected;
    public static event Action OnDisconnected;
    public static event Action<string> OnPeerConnected;
    public static event Action<string> OnPeerDisconnected;
    public static event Action<NetworkMessage> OnMessageReceived;
    public static event Action<string> OnConnectionError;

    void Awake()
    {
        //Enforce the singleton pattern
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
        // Automatically connect at startup
        await Connect();
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
                // Required for NativeWebSocket outside WebGL
                // Dispatches received messages on the main thread
                _websocket?.DispatchMessageQueue();
        #endif

        // Handle automatic reconnection timing
        if (_isReconnecting && autoReconnect)
        {
            _reconnectTimer -= Time.deltaTime;

            if (_reconnectTimer <= 0f)
            {
                _isReconnecting = false;
                _ = Connect(); // fire-and-forget reconnect
            }
        }
    }
    async void OnDestroy()
    {
        await Disconnect();
    }

    async void OnApplicationQuit()
    {
        await Disconnect();
    }

    // ============================
    // Connection Management
    // ============================

    // Opens a WebSocket connection to the server
    public async System.Threading.Tasks.Task Connect()
    {
        // Prevent double connections
        if (_websocket != null && _websocket.State == WebSocketState.Open)
        {
            Debug.Log("[VRNet] Already connected");
            return;
        }

        try
        {
            Debug.Log($"[VRNet] Connecting to {serverUrl}");

            _websocket = new WebSocket(serverUrl);

            // Register WebSocket callbacks
            _websocket.OnOpen += OnWebSocketOpen;
            _websocket.OnMessage += OnWebSocketMessage;
            _websocket.OnClose += OnWebSocketClose;
            _websocket.OnError += OnWebSocketError;

            // Async connection
            await _websocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRNet] Connection failed: {e.Message}");
            OnConnectionError?.Invoke(e.Message);
            ScheduleReconnect();
        }
    }
    // Closes the WebSocket connection cleanly
    public async System.Threading.Tasks.Task Disconnect()
    {
        autoReconnect = false;
        _isReconnecting = false;

        if (_websocket != null && _websocket.State == WebSocketState.Open)
        {
            await _websocket.Close();
        }

        _websocket = null;
        IsConnected = false;
    }

    // Starts a delayed reconnection attempt
    void ScheduleReconnect()
    {
        if (!autoReconnect || _isReconnecting)
            return;

        _isReconnecting = true;
        _reconnectTimer = reconnectDelay;

        Debug.Log($"[VRNet] Reconnecting in {reconnectDelay}s");
    }
    // ============================
    // WebSocket Callbacks
    // ============================

    // Called when the socket opens (TCP-level connection)
    void OnWebSocketOpen()
    {
        Debug.Log("[VRNet] WebSocket opened");
    }

    // Called when a message is received (raw bytes)
    void OnWebSocketMessage(byte[] data)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        HandleMessage(json);
    }

    // Called when the socket closes
    void OnWebSocketClose(WebSocketCloseCode closeCode)
    {
        Debug.Log($"[VRNet] WebSocket closed: {closeCode}");

        IsConnected = false;
        LocalId = null;

        OnDisconnected?.Invoke();
        ScheduleReconnect();
    }

    // Called on WebSocket error
    void OnWebSocketError(string errorMsg)
    {
        Debug.LogError($"[VRNet] WebSocket error: {errorMsg}");
        OnConnectionError?.Invoke(errorMsg);
    }
    // ============================
    // Message Handling
    // ============================

    // Parses JSON messages and routes them
    void HandleMessage(string json)
    {
        try
        {
            NetworkMessage msg = JsonUtility.FromJson<NetworkMessage>(json);

            // Server assigns our client ID
            if (msg.type == "welcome")
            {
                LocalId = msg.senderId;
                IsConnected = true;

                Debug.Log($"[VRNet] Assigned ID: {LocalId}");
                OnConnected?.Invoke();
                return;
            }

            // Another client joined
            if (msg.type == "peer-connected")
            {
                Debug.Log($"[VRNet] Peer connected: {msg.senderId}");
                OnPeerConnected?.Invoke(msg.senderId);
                return;
            }

            // Another client left
            if (msg.type == "peer-disconnected")
            {
                Debug.Log($"[VRNet] Peer disconnected: {msg.senderId}");
                OnPeerDisconnected?.Invoke(msg.senderId);
                return;
            }

            // Ignore messages sent by ourselves
            if (msg.senderId == LocalId)
                return;

            // Forward message to game logic
            OnMessageReceived?.Invoke(msg);
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRNet] JSON parse error: {e.Message}\n{json}");
        }
    }
    // ============================
    // Public API - Sending Messages
    // ============================

    // Sends a simple text payload
    public async void Send(string type, string data = "")
    {
        if (_websocket == null || _websocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("[VRNet] Send failed: not connected");
            return;
        }

        NetworkMessage msg = new NetworkMessage
        {
            type = type,
            senderId = LocalId,
            data = data
        };

        string json = JsonUtility.ToJson(msg);
        await _websocket.SendText(json);
    }

    // Sends a structured object serialized as JSON
    public void Send<T>(string type, T payload)
    {
        Send(type, JsonUtility.ToJson(payload));
    }

    // Returns true if WebSocket is open and usable
    public bool IsConnectionOpen()
    {
        return _websocket != null && _websocket.State == WebSocketState.Open;
    }
}


// Generic network message format
[Serializable]
public class NetworkMessage
{
    public string type;      // Message type (e.g. move, sync, chat, etc.)
    public string senderId; // Sender client ID
    public string data;     // JSON payload
}
