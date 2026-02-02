# Code Reseau - Analyse Technique

Analyse du code des scripts reseau pour VR Meeting Rooms.

> **Derniere mise a jour : 2026-02-02** - Synchronise avec le code source actuel.

---

## Vue d'Ensemble

| Script | Lignes | Role |
|--------|--------|------|
| `Server/server.js` | 887 | Hub WebSocket, routage 46 types de messages, gestion rooms |
| `Server/filePresentation.js` | 257 | Conversion PDF (optionnel, pdf-poppler) |
| `Assets/Scrips/Network/VRNetworkManager.cs` | 460 | Client WebSocket, connexion, rate limiting, backoff |
| `Assets/Scrips/Network/VRRoomManager.cs` | 931 | Gestion rooms, joueurs, zones, avatars |
| `Assets/Scrips/Network/VRGameManager.cs` | 1888 | Spawn joueurs, sync positions VR 30Hz, interpolation |
| `Assets/Scrips/WebRTC/VoiceChatManager.cs` | 1139 | WebRTC voice chat mesh, audio spatial 3D |
| `Assets/Scrips/VR/BootstrapManager.cs` | 292 | Scene flow, XR init, singletons |
| `Assets/Scrips/Avatar/AvatarCustomization.cs` | 315 | Couleur, username, persistance |
| `Assets/Scrips/Interaction/LaserPointer.cs` | 338 | Pointeur laser reseau 10Hz |
| `Assets/Scrips/Debug/DebugManager.cs` | 169 | Logging par categorie |

### Statistiques Globales

| Categorie | Fichiers | Lignes |
|-----------|----------|--------|
| Core Network | 3 | 3,279 |
| WebRTC Voice | 1 | 1,139 |
| Whiteboard | 12 | ~4,158 |
| Sharing | 8 | ~4,185 |
| UI/Menu | ~14 | ~2,000+ |
| VR Modules | ~10 | ~2,000+ |
| Autres | ~13 | ~2,000+ |
| **Total** | **~61** | **~28,765** |

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
| `pdfCache` | Cache des PDF convertis (TTL 30 min) |

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
| 6 | Configure handlers (message, close, error, pong) |

### Routage Messages (lignes 100-228)

```javascript
function handleMessage(clientId, message) {
    const { type, data } = message;
    message.senderId = clientId;  // Force l'ID correct (securite)

    switch (type) {
        // Room Lifecycle (6 types)
        case 'room-available': handleRoomAvailable(clientId, data); break;
        case 'room-closed': handleRoomClosed(clientId, data); break;
        case 'room-join': handleRoomJoin(clientId, data); break;
        case 'room-leave': handleRoomLeave(clientId, data); break;
        case 'room-list-request': sendRoomList(clients.get(clientId).ws); break;
        case 'room-update': handleRoomUpdate(clientId, data); break;

        // Position VR (2 types)
        case 'vr-position':
        case 'position':
            broadcastToRoom(clientId, message); break;

        // Objets Interactifs (2 types)
        case 'obj-sync':
        case 'obj-state':
            broadcastToRoom(clientId, message); break;

        // Whiteboard (5 types)
        case 'whiteboard-draw':
        case 'whiteboard-batch':
        case 'whiteboard-clear':
        case 'whiteboard-request':
            broadcastToRoom(clientId, message); break;
        case 'whiteboard-state':
            handleWhiteboardState(clientId, data); break;

        // Room State (3 types)
        case 'room-welcome':
        case 'room-teleport':
        case 'player-name-update':
            broadcastToRoom(clientId, message); break;

        // Admin (1 type)
        case 'kick-player':
            handleKickPlayer(clientId, data); break;

        // WebRTC Voice (3 types)
        case 'webrtc-offer': handleWebRTCOffer(clientId, data); break;
        case 'webrtc-answer': handleWebRTCAnswer(clientId, data); break;
        case 'webrtc-ice-candidate': handleWebRTCIceCandidate(clientId, data); break;

        // Screen Share (5 types broadcast + 3 types P2P)
        case 'screen-share-start':
        case 'screen-share-stop':
        case 'screen-share-frame':
        case 'screen-share-request':
        case 'screen-share-state':
            broadcastToRoom(clientId, message); break;
        case 'screen-video-offer': handleScreenVideoOffer(clientId, data); break;
        case 'screen-video-answer': handleScreenVideoAnswer(clientId, data); break;
        case 'screen-video-ice': handleScreenVideoIce(clientId, data); break;

        // File Share (6 types)
        case 'file-announce':
        case 'file-chunk':
        case 'file-complete':
        case 'file-request':
        case 'file-list-request':
            broadcastToRoom(clientId, message); break;
        case 'file-list-response':
            handleFileListResponse(clientId, data); break;

        // File Presentation (7 types)
        case 'file-present-start':
        case 'file-present-page':
        case 'file-present-navigate':
        case 'file-present-stop':
        case 'file-present-request':
            broadcastToRoom(clientId, message); break;
        case 'file-present-state':
            handleFilePresentState(clientId, data); break;

        // PDF (2 types)
        case 'pdf-convert-request':
            handlePdfConvertRequest(clientId, data); break;
        case 'pdf-page-request':
            handlePdfPageRequest(clientId, data); break;

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

| Routage | Types | Nombre |
|---------|-------|--------|
| Fonctions dediees | Room lifecycle, WebRTC, kick, whiteboard-state, file responses, PDF | 20 |
| broadcastToRoom | Position, objets, whiteboard, screen share, file share, presentation | 22 |
| Point-to-point | WebRTC voice (3), screen WebRTC (3) | 6 |
| **Total explicite** | | **46** |

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

#### handleRoomUpdate (host only)

```javascript
function handleRoomUpdate(clientId, dataStr) {
    const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
    const room = rooms.get(data.roomId);

    // Verification autorite host
    if (!room || room.hostId !== clientId) return;

    // Mise a jour des proprietes
    if (data.roomName) room.roomName = data.roomName;
    if (data.roomType !== undefined) room.roomType = data.roomType;
    if (data.maxPlayers) room.maxPlayers = data.maxPlayers;

    broadcastRoomList();
}
```

#### handleKickPlayer (host only)

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

    // Envoyer kick au joueur cible (point-to-point)
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

Le serveur agit comme relais pour le signaling WebRTC. Les 6 fonctions WebRTC sont identiques dans leur structure :

```javascript
// Voice Chat (3 fonctions)
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
// handleWebRTCAnswer et handleWebRTCIceCandidate : meme structure

// Screen Share (3 fonctions)
// handleScreenVideoOffer, handleScreenVideoAnswer, handleScreenVideoIce : meme structure
```

### File Presentation & PDF (lignes 609-748)

#### handleFilePresentState / handleFileListResponse

```javascript
// Point-to-point si targetId, sinon broadcastToRoom
function handleFilePresentState(clientId, dataStr) {
    const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

    if (data.targetId) {
        const targetClient = clients.get(data.targetId);
        if (targetClient) {
            sendToClient(targetClient.ws, { type: 'file-present-state', senderId: clientId, data: JSON.stringify(data) });
        }
    } else {
        broadcastToRoom(clientId, { type: 'file-present-state', senderId: clientId, data: JSON.stringify(data) });
    }
}
```

#### handlePdfConvertRequest

```javascript
function handlePdfConvertRequest(clientId, dataStr) {
    const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
    const { fileId } = data;

    // Verifier cache existant
    if (pdfCache.has(fileId)) {
        const cached = pdfCache.get(fileId);
        sendToClient(clients.get(clientId).ws, {
            type: 'pdf-convert-response',
            data: JSON.stringify({ fileId, totalPages: cached.totalPages, success: true })
        });
        return;
    }

    // Deleguer au module filePresentation si disponible
    if (filePresentation) {
        filePresentation.convertPdf(data, (result) => {
            pdfCache.set(fileId, { pages: result.pages, totalPages: result.totalPages, timestamp: Date.now() });
            sendToClient(clients.get(clientId).ws, {
                type: 'pdf-convert-response',
                data: JSON.stringify({ fileId, totalPages: result.totalPages, success: true })
            });
        });
    }
}
```

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

#### sendError

```javascript
function sendError(clientId, errorMessage) {
    const client = clients.get(clientId);
    if (client) {
        sendToClient(client.ws, {
            type: 'error',
            senderId: 'server',
            data: JSON.stringify({ error: errorMessage })
        });
    }
}
```

### Maintenance (lignes 838-887)

#### Heartbeat (30s)

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

#### Nettoyage Cache PDF (5 min)

```javascript
setInterval(() => {
    const now = Date.now();
    for (const [fileId, entry] of pdfCache) {
        if (now - entry.timestamp > PDF_CACHE_TTL) {
            pdfCache.delete(fileId);
        }
    }
}, 5 * 60 * 1000);
```

#### Status Log (60s)

```javascript
setInterval(() => {
    console.log(`[Status] ${clients.size} clients | ${rooms.size} rooms`);
}, 60000);
```

#### Graceful Shutdown

```javascript
process.on('SIGINT', () => {
    console.log('\n[Server] Shutting down...');
    clearInterval(heartbeatInterval);
    wss.clients.forEach((ws) => { ws.close(); });
    wss.close(() => {
        console.log('[Server] Goodbye!');
        process.exit(0);
    });
});
```

---

## VRNetworkManager.cs (Unity - 460 lignes)

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
    public float welcomeTimeout = 5f;  // P0 FIX: detection timeout welcome

    [Header("Exponential Backoff")]
    public float initialReconnectDelay = 1f;
    public float maxReconnectDelay = 30f;
    public float backoffMultiplier = 2f;

    [Header("Rate Limiting")]
    public int maxMessagesPerSecond = 60;  // Token bucket
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
    // P0 FIX: Validation securite wss://
    if (enforceSecureConnection && !serverUrl.StartsWith("wss://")) {
        OnConnectionError?.Invoke("Secure connection required");
        return;
    }

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

### Envoi avec Rate Limiting (Token Bucket)

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

    return false;  // Rate limited - message dropped
}
```

### Reconnexion Exponential Backoff

```csharp
private void HandleDisconnection()
{
    IsConnected = false;
    LocalId = null;
    OnDisconnected?.Invoke();

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

### P0 Fixes Implementes

- **Welcome timeout** : Deconnexion si pas de `welcome` apres 5s
- **Validation securite** : Verification `wss://` si `enforceSecureConnection`
- **Rate limiting** : Token bucket 60 msg/s avec burst 10
- **Backoff exponentiel** : 1s -> 30s max

---

## VRRoomManager.cs (Unity - 931 lignes)

### Generation Code Room (Crypto-Secure)

```csharp
string GenerateRoomId()
{
    // Caracteres non ambigus (pas O/0, I/1/L)
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    char[] id = new char[6];

    // P0 FIX: RandomNumberGenerator au lieu de System.Random
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
public static event Action<VRPlayerData> OnAvatarUpdated;
public static event Action<RoomType> OnRoomTypeChanged;
public static event Action<Dictionary<string, RoomInfo>> OnRoomListUpdated;
public static event Action<string> OnRoomError;
```

### Methodes Publiques

| Methode | Description |
|---------|-------------|
| `CreateRoom(RoomType, name)` | Creer une room avec code crypto-secure |
| `JoinRoom(roomId)` | Rejoindre une room existante |
| `LeaveRoom()` | Quitter proprement |
| `TeleportToRoomType(RoomType)` | Changement de zone |
| `KickPlayer(playerId)` | Host only |
| `SetPlayerName(name)` | Mise a jour nom |
| `RequestRoomList()` | Demander liste rooms |
| `BroadcastAvatarUpdate()` | Sync couleur + nom |

### Messages Geres

- **Envoyes** : `room-available`, `room-join`, `room-leave`, `room-list-request`, `room-teleport`, `player-name-update`, `avatar-update`, `kick-player`
- **Recus** : `room-join`, `room-welcome`, `room-leave`, `room-list`, `room-closed`, `room-teleport`, `player-name-update`, `avatar-update`, `kick-player`, `error`

---

## VRGameManager.cs (Unity - 1888 lignes)

### Configuration

| Parametre | Valeur | Description |
|-----------|--------|-------------|
| `syncRate` | 30 Hz | Frequence envoi position |
| `interpolationSpeed` | 15 | Vitesse interpolation reseau |
| `movementThreshold` | 0.01m | Seuil detection mouvement |
| `rotationThreshold` | 1 degre | Seuil detection rotation |

### Sync Position (30Hz)

```csharp
void SendPositionUpdate()
{
    if (_localPlayer == null || !VRRoomManager.Instance.IsInRoom) return;

    Transform originTf = _localXrOrigin?.transform ?? _localPlayer.transform;

    // Detection mouvement (optimisation)
    float posChange = Vector3.Distance(_lastSyncPosition, originTf.position);
    float rotChange = Quaternion.Angle(_lastSyncRotation, originTf.rotation);

    if (posChange < movementThreshold && rotChange < rotationThreshold)
        return;  // Pas d'envoi si pas de mouvement

    // Mise a jour cache (GC-friendly)
    _cachedPositionData.roomId = VRRoomManager.Instance.CurrentRoomId;
    _cachedPositionData.posX = Round(originTf.position.x);
    _cachedPositionData.posY = Round(originTf.position.y);
    _cachedPositionData.posZ = Round(originTf.position.z);
    _cachedPositionData.rotY = Round(originTf.eulerAngles.y);

    // Tete (world space)
    // Mains (world space, 0 = Desktop mode)

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
            remote.targetPosition, t);

        remote.gameObject.transform.rotation = Quaternion.Slerp(
            remote.gameObject.transform.rotation,
            remote.targetRotation, t);

        // Tete (world space, detachee de la hierarchie)
        if (remote.head != null)
        {
            remote.head.position = Vector3.Lerp(remote.head.position, remote.targetHeadPosition, t);
            remote.head.rotation = Quaternion.Slerp(remote.head.rotation, remote.targetHeadRotation, t);
        }

        // Mains (world space)
        // ...
    }
}
```

### Events

```csharp
public static event Action<GameObject> OnLocalPlayerSpawned;
public static event Action<string, GameObject> OnRemotePlayerSpawned;
public static event Action<string> OnRemotePlayerDespawned;
```

### Methodes Publiques

| Methode | Description |
|---------|-------------|
| `SpawnLocalPlayer(RoomType)` | Cree le joueur local (VR ou Desktop) |
| `GetLocalPlayer()` | Reference au joueur local |
| `TeleportLocalPlayer(RoomType)` | Teleportation avec gestion CharacterController |
| `SpawnRemotePlayer(VRPlayerData)` | Cree joueur distant (prefabs separes ou legacy) |
| `DespawnRemotePlayer(playerId)` | Supprime joueur distant |
| `GetRemotePlayer(id)` | Reference au joueur distant |
| `GetRemotePlayerHead(id)` | Reference a la tete du joueur distant |
| `ApplyAvatarColor(remote, color)` | Couleur via MaterialPropertyBlock |

### P0 Fixes

- **Thread-safe spawn** : Lock pour eviter les race conditions
- **Detached remote parts** : Container separe (pas de memory leak)
- **Canvas setup coroutine** : Spread across frames (pas de spike)
- **URP Unlit shader** : Stereo instancing VR (Sprites/Default incompatible)
- **MaterialPropertyBlock** : Pas de copie Material (pas de memory leak)
- **Cached FindObjectsByType** : Performance optimisation + invalidation au changement de scene

---

## VoiceChatManager.cs (Unity - 1139 lignes)

### Topologie Mesh

```csharp
void OnPlayerJoined(VRPlayerData player)
{
    if (player.playerId == VRNetworkManager.LocalId) return;

    // Regle deterministe: ID plus petit initie la connexion
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

    // STUN publics (toujours inclus)
    iceServers.Add(new RTCIceServer { urls = new[] { STUN_GOOGLE_1 } });  // stun:stun.l.google.com:19302
    iceServers.Add(new RTCIceServer { urls = new[] { STUN_GOOGLE_2 } });  // stun:stun1.l.google.com:19302
    iceServers.Add(new RTCIceServer { urls = new[] { STUN_CLOUDFLARE } }); // stun:stun.cloudflare.com:3478

    // TURN (si configure, avec warning securite)
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

### Audio Spatial 3D

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
    audioSource.maxDistance = maxAudioDistance;  // 20m
    audioSource.rolloffMode = AudioRolloffMode.Linear;
}
```

### Push-to-Talk

- **VR** : Primary Button (A button)
- **Desktop** : Touche V
- Toggle avec `usePushToTalk`

### Methodes Publiques

| Methode | Description |
|---------|-------------|
| `StartMicrophone()` / `StopMicrophone()` / `ToggleMicrophone()` | Controle micro |
| `SetMicrophone(deviceName)` | Selection appareil |
| `GetAvailableMicrophones()` | Liste appareils |
| `SetPlaybackVolume(volume)` | Volume par joueur |
| `SetPlayerMuted(playerId, muted)` | Mute par joueur |
| `IsPlayerConnected(playerId)` | Etat connexion |
| `GetActiveConnectionCount()` | Nombre connexions actives |

### Events

```csharp
public static event Action OnVoiceChatReady;
public static event Action<string> OnPeerVoiceConnected;
public static event Action<string> OnPeerVoiceDisconnected;
public static event Action<bool> OnMicrophoneStateChanged;
```

### P0 Fixes

- **Connection timeout** : 15s checker pour nettoyer les connexions zombies
- **Constants STUN/TURN** : Plus de URLs hardcodees
- **Custom TURN** : Support avec warning securite

---

## Autres Scripts Cles

### BootstrapManager.cs (292 lignes)

- Scene flow : Bootstrap (persistent) -> Meet (additive)
- XR initialization manuelle pour builds
- XR Simulator desactive sur vrai hardware VR
- Frame rate lock 90fps pour VR
- EventSystem persistant cross-scene

### AvatarCustomization.cs (315 lignes)

- 8 couleurs predefinies
- Validation username requise
- Persistance PlayerPrefs
- Preview live couleur + nom

### LaserPointer.cs (338 lignes)

- VR : A button, Desktop : L key
- Sync reseau 10Hz quand actif
- URP Unlit shader (VR stereo instancing fix)
- Cached raycast mask

### DebugManager.cs (169 lignes)

- Categories : Network, VoiceChat, Whiteboard, Sharing, VR, UI, Avatar, Interaction, Game, General
- Master switch + per-category toggles
- Auto-disable in builds
- Conditional compilation (zero cost production)

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
     |                               |                               |
     |-- whiteboard-batch ---------->|                               |
     |                               |-- whiteboard-batch (room) --->|
     |                               |                               |
     |-- file-present-start -------->|                               |
     |                               |-- file-present-start (room) ->|
     |                               |                               |
     |-- pdf-convert-request ------->|                               |
     |<-- pdf-convert-response ------|                               |
```

---

## Changelog

| Date | Version | Description |
|------|---------|-------------|
| 2025-01-26 | 1.0 | Documentation initiale |
| 2026-02-02 | 2.0 | Mise a jour complete : correction line counts (server 887, VRRoomManager 931, VRGameManager 1888, VoiceChatManager 1139), ajout 46 types messages complets, ajout file presentation/PDF/objets interactifs/screen WebRTC, ajout P0 fixes, ajout statistiques globales |

---

## References

- [SERVER_ARCHITECTURE.md](./SERVER_ARCHITECTURE.md) - Architecture serveur
- [GUIDE_DEPLOIEMENT_ENTREPRISE.md](./GUIDE_DEPLOIEMENT_ENTREPRISE.md) - Etat actuel
- [SERVER_ARCHITECTURE_KO.md](./SERVER_ARCHITECTURE_KO.md) - Version coreenne
- [CLAUDE.md](../CLAUDE.md) - Instructions projet
