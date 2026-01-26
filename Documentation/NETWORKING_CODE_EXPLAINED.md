# Code Reseau - Analyse Technique

Analyse du code des scripts reseau pour VR Meeting Rooms.

---

## Vue d'Ensemble

| Script | Lignes | Role |
|--------|--------|------|
| `Server/server.js` | 888 | Hub WebSocket, routage messages, gestion rooms |
| `Assets/Scrips/Network/VRNetworkManager.cs` | ~460 | Client WebSocket, connexion, envoi/reception |
| `Assets/Scrips/Network/VRRoomManager.cs` | ~850 | Gestion rooms, joueurs, zones |
| `Assets/Scrips/Network/VRGameManager.cs` | ~1430 | Spawn joueurs, sync positions VR |
| `Assets/Scrips/WebRTC/VoiceChatManager.cs` | ~1085 | WebRTC voice chat, audio spatial |

---

## server.js (Node.js)

### Imports et Configuration (lignes 1-27)

```javascript
const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');

const PORT = process.env.PORT || 8080;
const HEARTBEAT_INTERVAL = 30000;  // 30 seconds
const PDF_CACHE_TTL = 30 * 60 * 1000;  // 30 minutes

const clients = new Map();  // clientId -> { ws, roomId, playerName, lastHeartbeat }
const rooms = new Map();    // roomId -> RoomInfo
const pdfCache = new Map(); // fileId -> { pages, totalPages, timestamp }
```

| Element | Description |
|---------|-------------|
| `ws` | Librairie WebSocket pour Node.js |
| `uuidv4` | Generateur d'identifiants uniques |
| `clients` | Map de tous les clients connectes |
| `rooms` | Map des rooms actives |
| `pdfCache` | Cache des PDF convertis |

### Module Optionnel (lignes 19-26)

```javascript
let filePresentation = null;
try {
    filePresentation = require('./filePresentation');
    console.log('[Server] filePresentation module loaded');
} catch (e) {
    console.log('[Server] filePresentation module not available');
}
```

Charge le module PDF si disponible, continue sans si absent.

### Demarrage Serveur (lignes 30-37)

```javascript
const wss = new WebSocket.Server({ port: PORT });

console.log('============================================');
console.log('  VR MEETING ROOMS - WebSocket Server');
console.log('============================================');
console.log(`  Port: ${PORT}`);
console.log(`  Heartbeat: ${HEARTBEAT_INTERVAL / 1000}s`);
console.log('============================================');
```

### Gestion Connexions (lignes 41-96)

```javascript
wss.on('connection', (ws) => {
    const clientId = uuidv4();

    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });

    // Welcome avec ID assigne
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // Notifier les autres
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);

    // Envoyer liste rooms
    sendRoomList(ws);

    // Handlers
    ws.on('message', (data) => { ... });
    ws.on('close', () => { handleDisconnect(clientId); });
    ws.on('error', (error) => { ... });
    ws.on('pong', () => { client.lastHeartbeat = Date.now(); });
});
```

| Etape | Action |
|-------|--------|
| 1 | Genere UUID unique |
| 2 | Stocke client dans Map |
| 3 | Envoie `welcome` avec ID |
| 4 | Broadcast `peer-connected` |
| 5 | Envoie liste des rooms |
| 6 | Configure handlers |

### Routage Messages (lignes 100-228)

```javascript
function handleMessage(clientId, message) {
    const { type, data } = message;
    message.senderId = clientId;  // Force l'ID correct

    switch (type) {
        case 'room-available':
            handleRoomAvailable(clientId, data);
            break;
        case 'vr-position':
        case 'position':
            broadcastToRoom(clientId, message);
            break;
        case 'webrtc-offer':
            handleWebRTCOffer(clientId, data);
            break;
        // ... autres cases
        default:
            // Broadcast intelligent
            const client = clients.get(clientId);
            if (client && client.roomId) {
                broadcastToRoom(clientId, message);
            } else {
                broadcast(message, clientId);
            }
    }
}
```

| Routage | Types |
|---------|-------|
| Fonctions dediees | `room-*`, `webrtc-*`, `whiteboard-state`, `kick-player` |
| broadcastToRoom | `vr-position`, `whiteboard-batch`, `screen-share-*`, `file-*` |
| Point-to-point | `webrtc-offer/answer/ice`, `screen-video-*` |

### Gestion Rooms (lignes 232-416)

#### handleRoomAvailable

```javascript
function handleRoomAvailable(clientId, dataStr) {
    const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

    const roomInfo = {
        roomId: data.roomId,
        hostId: clientId,
        roomName: data.roomName || `Room ${data.roomId}`,
        roomType: data.roomType || 0,
        playerCount: 1,
        maxPlayers: data.maxPlayers || 10,
        createdAt: Date.now()
    };

    rooms.set(data.roomId, roomInfo);
    clients.get(clientId).roomId = data.roomId;

    broadcastRoomList();
    broadcast({ type: 'room-available', senderId: clientId, data: JSON.stringify(roomInfo) });
}
```

#### handleRoomJoin

```javascript
function handleRoomJoin(clientId, dataStr) {
    const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
    const room = rooms.get(data.roomId);

    if (!room) {
        sendError(clientId, `Room ${data.roomId} not found`);
        return;
    }

    if (room.playerCount >= room.maxPlayers) {
        sendError(clientId, 'Room is full');
        return;
    }

    const client = clients.get(clientId);
    client.roomId = data.roomId;
    client.playerName = data.playerName || 'Player';
    room.playerCount++;

    broadcastToRoom(clientId, { type: 'room-join', senderId: clientId, data: JSON.stringify(data) });
    broadcastRoomList();
}
```

#### handleKickPlayer

```javascript
function handleKickPlayer(clientId, dataStr) {
    const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
    const room = rooms.get(data.roomId);

    // Verification host
    if (!room || room.hostId !== clientId) {
        console.warn(`[Kick] Rejected: not host`);
        return;
    }

    const targetClient = clients.get(data.playerId);
    if (!targetClient) return;

    // Envoyer kick au joueur cible
    targetClient.ws.send(JSON.stringify({
        type: 'kick-player',
        senderId: clientId,
        data: JSON.stringify(data)
    }));

    // Mettre a jour state
    targetClient.roomId = null;
    room.playerCount--;

    // Notifier la room
    broadcastToRoom(clientId, { type: 'room-leave', senderId: data.playerId, ... });
    broadcastRoomList();
}
```

### WebRTC Signaling (lignes 491-605)

```javascript
function handleWebRTCOffer(senderId, dataStr) {
    const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
    const { targetId, sdp } = data;

    const targetClient = clients.get(targetId);
    if (!targetClient) return;

    sendToClient(targetClient.ws, {
        type: 'webrtc-offer',
        senderId: senderId,
        data: JSON.stringify({ sdp })
    });
}
```

Le serveur agit comme relais pour le signaling WebRTC (offer, answer, ICE candidates).

### Utilities (lignes 752-833)

#### sendToClient

```javascript
function sendToClient(ws, message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
    }
}
```

#### broadcast

```javascript
function broadcast(message, exceptClientId = null) {
    const messageStr = JSON.stringify(message);
    clients.forEach((client, clientId) => {
        if (clientId !== exceptClientId && client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}
```

#### broadcastToRoom

```javascript
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    if (!sender || !sender.roomId) return;

    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);

    clients.forEach((client, clientId) => {
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}
```

### Heartbeat (lignes 838-855)

```javascript
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });

    clients.forEach((client, clientId) => {
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            console.log(`[Timeout] Client ${clientId.substring(0, 8)}...`);
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
}, HEARTBEAT_INTERVAL);
```

---

## VRNetworkManager.cs (Unity)

### Configuration

```csharp
public class VRNetworkManager : MonoBehaviour
{
    public static VRNetworkManager Instance { get; private set; }

    [Header("Server Configuration")]
    public string serverUrl = "ws://localhost:8080";
    public bool enforceSecureConnection = false;
    public bool autoReconnect = true;

    [Header("Connection Timeout")]
    public float welcomeTimeout = 5f;

    [Header("Exponential Backoff")]
    public float initialReconnectDelay = 1f;
    public float maxReconnectDelay = 30f;
    public float backoffMultiplier = 2f;

    [Header("Rate Limiting")]
    public int maxMessagesPerSecond = 60;
    public int burstAllowance = 10;

    // Properties
    public static string LocalId { get; private set; }
    public static bool IsConnected { get; private set; }
}
```

### Events

```csharp
public static event Action OnConnected;
public static event Action OnDisconnected;
public static event Action<string> OnPeerConnected;
public static event Action<string> OnPeerDisconnected;
public static event Action<NetworkMessage> OnMessageReceived;
public static event Action<string> OnConnectionError;
```

### Connexion

```csharp
public async Task Connect()
{
    _websocket = new WebSocket(serverUrl);

    _websocket.OnOpen += () => {
        _waitingForWelcome = true;
        _welcomeTimeoutTimer = welcomeTimeout;
    };

    _websocket.OnMessage += bytes => {
        string json = System.Text.Encoding.UTF8.GetString(bytes);
        HandleMessage(json);
    };

    _websocket.OnClose += code => { HandleDisconnection(); };
    _websocket.OnError += err => { HandleDisconnection(); };

    await _websocket.Connect();
}
```

### Traitement Messages

```csharp
void HandleMessage(string json)
{
    NetworkMessage msg = JsonUtility.FromJson<NetworkMessage>(json);

    if (msg.type == "welcome") {
        _waitingForWelcome = false;
        LocalId = msg.senderId;
        IsConnected = true;
        OnConnected?.Invoke();
        return;
    }

    if (msg.type == "peer-connected") {
        OnPeerConnected?.Invoke(msg.senderId);
        return;
    }

    if (msg.type == "peer-disconnected") {
        OnPeerDisconnected?.Invoke(msg.senderId);
        return;
    }

    // Ignorer echos
    if (msg.senderId == LocalId) return;

    OnMessageReceived?.Invoke(msg);
}
```

### Envoi avec Rate Limiting

```csharp
public void Send(string type, object payload)
{
    string dataJson = payload is string s ? s : JsonUtility.ToJson(payload);
    SendInternal(type, dataJson);
}

private bool CheckRateLimit(string messageType)
{
    if (maxMessagesPerSecond <= 0) return true;

    // Token bucket algorithm
    float elapsed = Time.unscaledTime - _lastRateLimitRefill;
    _lastRateLimitRefill = Time.unscaledTime;
    _rateLimitTokens = Mathf.Min(burstAllowance, _rateLimitTokens + (elapsed * maxMessagesPerSecond));

    if (_rateLimitTokens >= 1f) {
        _rateLimitTokens -= 1f;
        return true;
    }

    return false;  // Rate limited
}
```

### Reconnexion Exponential Backoff

```csharp
private void HandleDisconnection()
{
    IsConnected = false;
    LocalId = null;

    if (autoReconnect && !_isReconnecting) {
        _isReconnecting = true;
        _reconnectTimer = _currentReconnectDelay;

        // 1s -> 2s -> 4s -> 8s -> ... -> 30s max
        _currentReconnectDelay = Mathf.Min(
            _currentReconnectDelay * backoffMultiplier,
            maxReconnectDelay
        );
    }
}
```

---

## VRRoomManager.cs (Unity)

### Generation Code Room

```csharp
string GenerateRoomId()
{
    // Caracteres non ambigus (pas O/0, I/1/L)
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    char[] id = new char[6];

    using (var rng = RandomNumberGenerator.Create())
    {
        byte[] randomBytes = new byte[6];
        rng.GetBytes(randomBytes);

        for (int i = 0; i < 6; i++)
        {
            id[i] = chars[randomBytes[i] % chars.Length];
        }
    }

    return new string(id);  // Ex: "XYZ789"
}
```

### Deserialisation Securisee

```csharp
private T TryDeserialize<T>(string json, string context) where T : class
{
    if (string.IsNullOrEmpty(json)) {
        Debug.LogWarning($"[VRRoom] Empty JSON for {context}");
        return null;
    }

    try {
        T result = JsonUtility.FromJson<T>(json);
        if (result == null) {
            Debug.LogWarning($"[VRRoom] Null result for {context}");
            return null;
        }
        return result;
    }
    catch (Exception e) {
        Debug.LogError($"[VRRoom] JSON parse error for {context}: {e.Message}");
        return null;
    }
}
```

### Events Room

```csharp
public static event Action<string> OnRoomCreated;
public static event Action<string> OnRoomJoined;
public static event Action OnRoomLeft;
public static event Action<VRPlayerData> OnPlayerJoined;
public static event Action<string> OnPlayerLeft;
public static event Action<RoomType> OnRoomTypeChanged;
public static event Action<Dictionary<string, RoomInfo>> OnRoomListUpdated;
public static event Action<string> OnRoomError;
```

---

## VRGameManager.cs (Unity)

### Sync Position (30Hz)

```csharp
void SendPositionUpdate()
{
    if (_localPlayer == null || !VRRoomManager.Instance.IsInRoom) return;

    Transform originTf = _localXrOrigin?.transform ?? _localPlayer.transform;

    // Detection mouvement
    float posChange = Vector3.Distance(_lastSyncPosition, originTf.position);
    float rotChange = Quaternion.Angle(_lastSyncRotation, originTf.rotation);

    if (posChange < movementThreshold && rotChange < rotationThreshold)
        return;  // Pas d'envoi si pas de mouvement

    // Mise a jour cache
    _cachedPositionData.roomId = VRRoomManager.Instance.CurrentRoomId;
    _cachedPositionData.posX = Round(originTf.position.x);
    _cachedPositionData.posY = Round(originTf.position.y);
    _cachedPositionData.posZ = Round(originTf.position.z);
    _cachedPositionData.rotY = Round(originTf.eulerAngles.y);

    // Tete et mains...

    VRNetworkManager.Instance.Send("vr-position", _cachedPositionData);
}
```

### Interpolation Remote Players

```csharp
void InterpolateRemotePlayers()
{
    float t = Time.deltaTime * interpolationSpeed;

    foreach (var remote in _remotePlayers.Values)
    {
        if (remote.gameObject == null || !remote.hasReceivedData) continue;

        // Corps
        remote.gameObject.transform.position = Vector3.Lerp(
            remote.gameObject.transform.position,
            remote.targetPosition,
            t
        );

        remote.gameObject.transform.rotation = Quaternion.Slerp(
            remote.gameObject.transform.rotation,
            remote.targetRotation,
            t
        );

        // Tete (world space)
        if (remote.head != null)
        {
            remote.head.position = Vector3.Lerp(remote.head.position, remote.targetHeadPosition, t);
            remote.head.rotation = Quaternion.Slerp(remote.head.rotation, remote.targetHeadRotation, t);
        }
    }
}
```

---

## VoiceChatManager.cs (Unity)

### Topologie Mesh

```csharp
void OnPlayerJoined(VRPlayerData player)
{
    if (player.playerId == VRNetworkManager.LocalId) return;

    // Regle deterministe: ID plus petit initie
    string localId = VRNetworkManager.LocalId;
    if (string.Compare(localId, player.playerId, StringComparison.Ordinal) < 0)
    {
        // Notre ID plus petit -> on initie
        StartCoroutine(CreatePeerConnection(player.playerId, true));
    }
    // Sinon on attend que l'autre initie
}
```

### Configuration ICE

```csharp
private RTCConfiguration BuildRTCConfiguration()
{
    var iceServers = new List<RTCIceServer>();

    // STUN publics
    iceServers.Add(new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } });
    iceServers.Add(new RTCIceServer { urls = new[] { "stun:stun.cloudflare.com:3478" } });

    // TURN (si configure)
    if (useCustomTurnServer && !string.IsNullOrEmpty(customTurnUrl))
    {
        iceServers.Add(new RTCIceServer {
            urls = new[] { customTurnUrl },
            username = customTurnUsername,
            credential = customTurnCredential
        });
    }

    return new RTCConfiguration { iceServers = iceServers.ToArray() };
}
```

### Audio Spatial

```csharp
void SetupSpatialAudio(string peerId, AudioSource audioSource)
{
    // Attacher a la tete du remote player
    Transform head = VRGameManager.Instance.GetRemotePlayerHead(peerId);
    if (head != null)
    {
        audioSource.transform.SetParent(head);
        audioSource.transform.localPosition = Vector3.zero;
    }

    // Configuration 3D
    audioSource.spatialBlend = 1.0f;  // Full 3D
    audioSource.maxDistance = 20f;
    audioSource.rolloffMode = AudioRolloffMode.Linear;
}
```

---

## Fichiers Auxiliaires

### auth.js (NON CONNECTE)

```javascript
const bcrypt = require('bcrypt');
const db = require('./db');

const SALT_ROUNDS = 10;

async function registerUser(username, email, password, displayName) {
    const [existing] = await db.query(
        'SELECT id FROM users WHERE username = ? OR email = ?',
        [username, email]
    );

    if (existing.length > 0) {
        return { success: false, error: 'Username or email already exists' };
    }

    const passwordHash = await bcrypt.hash(password, SALT_ROUNDS);

    const [result] = await db.query(
        'INSERT INTO users (username, email, password_hash, display_name) VALUES (?, ?, ?, ?)',
        [username, email, passwordHash, displayName || username]
    );

    return { success: true, userId: result.insertId };
}

async function loginUser(username, password) {
    const [users] = await db.query(
        'SELECT * FROM users WHERE username = ? OR email = ?',
        [username, username]
    );

    if (users.length === 0) return { success: false, error: 'User not found' };

    const passwordMatch = await bcrypt.compare(password, users[0].password_hash);
    if (!passwordMatch) return { success: false, error: 'Invalid password' };

    return { success: true, userId: users[0].id, ... };
}
```

### db.js (NON CONNECTE)

```javascript
const mysql = require('mysql2/promise');

const pool = mysql.createPool({
    host: process.env.DB_HOST || 'localhost',
    port: process.env.DB_PORT || 3306,
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD || 'JJkk2812',  // En dur
    database: process.env.DB_NAME || 'vr_meeting',
    connectionLimit: 10
});

module.exports = pool;
```

---

## Resume Flux de Donnees

```
Unity Client                     Node.js Server                  Other Clients
     |                                |                               |
     |-- Connect ------------------->|                               |
     |<-- welcome {id} --------------|                               |
     |-- room-available ------------>|                               |
     |                               |-- room-list ----------------->|
     |<-- room-list -----------------|                               |
     |                               |                               |
     |-- vr-position (30Hz) -------->|                               |
     |                               |-- vr-position (room filter) ->|
     |                               |                               |
     |-- webrtc-offer {target} ----->|                               |
     |                               |-- webrtc-offer (p2p) -------->|
     |<-- webrtc-answer -------------|<-- webrtc-answer -------------|
     |                               |                               |
     |<========== Audio P2P via WebRTC (contourne serveur) =========>|
```

---

## References

- [SERVER_ARCHITECTURE.md](./SERVER_ARCHITECTURE.md) - Architecture serveur
- [GUIDE_DEPLOIEMENT_ENTREPRISE.md](./GUIDE_DEPLOIEMENT_ENTREPRISE.md) - Etat actuel
