# Explication Ligne par Ligne des Scripts Reseau

Ce document fournit une analyse detaillee du code des scripts de networking pour VR Meeting Rooms.

## Table des Matieres

1. [server.js (Node.js)](#serverjs-nodejs)
2. [VRNetworkManager.cs (Unity)](#vrnetworkmanagercs-unity)
3. [VRRoomManager.cs (Unity)](#vrroommanagercs-unity)
4. [VRGameManager.cs (Unity)](#vrgamemanagercs-unity)
5. [VoiceChatManager.cs (Unity)](#voicechatmanagercs-unity)
6. [Fichiers Auxiliaires](#fichiers-auxiliaires)

---

## server.js (Node.js)

**Chemin :** `Server/server.js`
**Lignes :** 1047
**Role :** Hub WebSocket central, routage des messages, gestion des rooms

### Section 1 : Imports et Configuration (lignes 1-35)

```javascript
/**
 * WebSocket Server for VR Meeting Rooms Application
 * Version avec FILTRAGE PAR ROOM pour sync objets et whiteboard
 */

const WebSocket = require('ws');          // Librairie WebSocket pour Node.js
const { v4: uuidv4 } = require('uuid');   // Generation d'identifiants uniques
const { registerUser, loginUser, updateUserProfile } = require('./auth'); // Handlers auth
```

**Explication :**
- `ws` : Librairie WebSocket haute performance pour Node.js
- `uuid` : Genere des identifiants UUID v4 (ex: `550e8400-e29b-41d4-a716-446655440000`)
- Import des fonctions d'authentification depuis `auth.js`

```javascript
// Module presentation fichiers (optionnel)
let filePresentation = null;
try {
    filePresentation = require('./filePresentation');
} catch (e) {
    console.log('[SERVER] File presentation module not loaded');
}
```

**Explication :** Charge le module de presentation PDF s'il existe, sinon continue sans.

```javascript
const PORT = process.env.PORT || 8080;        // Port d'ecoute (env ou 8080)
const HEARTBEAT_INTERVAL = 30000;             // 30 secondes entre chaque ping

const clients = new Map();  // clientId -> { ws, roomId, playerName, lastHeartbeat }
const rooms = new Map();    // roomId -> RoomInfo
```

**Explication :**
- `clients` : Map stockant tous les clients connectes avec leurs metadonnees
- `rooms` : Map stockant les informations de chaque room active

```javascript
const wss = new WebSocket.Server({ port: PORT });
console.log(`[SERVER] WebSocket server started on port ${PORT}`);
```

**Explication :** Cree et demarre le serveur WebSocket.

---

### Section 2 : Gestion des Connexions (lignes 40-87)

```javascript
wss.on('connection', (ws) => {
    // Genere un identifiant unique pour ce client
    const clientId = uuidv4();

    // Stocke le client dans la Map
    clients.set(clientId, {
        ws: ws,                    // Socket WebSocket
        roomId: null,              // Pas dans une room au debut
        playerName: 'Player',      // Nom par defaut
        lastHeartbeat: Date.now()  // Timestamp pour detection timeout
    });

    console.log(`[SERVER] Client connected: ${clientId}`);
```

**Explication :** A chaque nouvelle connexion, genere un UUID et stocke les metadonnees du client.

```javascript
    // Envoyer le message de bienvenue avec l'ID assigne
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });
```

**Explication :** Le client recoit son ID assigne. C'est CRITIQUE - le client ne peut rien faire sans cet ID.

```javascript
    // Notifier tous les autres clients de cette nouvelle connexion
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);  // Exclure le nouveau client du broadcast

    // Envoyer la liste des rooms disponibles
    sendRoomList(ws);
```

**Explication :**
- Broadcast `peer-connected` a tous SAUF au nouveau client
- Envoie la liste des rooms pour que le client puisse voir les meetings existants

```javascript
    // Handler pour chaque message recu de ce client
    ws.on('message', (data) => {
        try {
            const message = JSON.parse(data.toString());
            handleMessage(clientId, message);  // Router vers le bon handler
        } catch (e) {
            console.error(`[SERVER] Parse error: ${e.message}`);
        }
    });
```

**Explication :** Chaque message recu est parse en JSON puis route vers `handleMessage`.

```javascript
    ws.on('close', () => {
        handleDisconnect(clientId);  // Nettoyer quand le client se deconnecte
    });

    ws.on('error', (error) => {
        console.error(`[SERVER] Client error (${clientId}): ${error.message}`);
    });

    // Handler ping/pong pour le heartbeat
    ws.on('pong', () => {
        const client = clients.get(clientId);
        if (client) {
            client.lastHeartbeat = Date.now();  // Met a jour le timestamp
        }
    });
});
```

**Explication :**
- `close` : Nettoie les ressources du client
- `error` : Log les erreurs
- `pong` : Repond aux pings du serveur pour confirmer que le client est vivant

---

### Section 3 : Routage des Messages (lignes 93-263)

```javascript
function handleMessage(clientId, message) {
    const { type, senderId, data } = message;
    message.senderId = clientId;  // IMPORTANT: Force l'ID correct (securite)

    console.log(`[SERVER] Message from ${clientId}: ${type}`);
```

**Explication :** Force `senderId` a l'ID du client authentifie (empeche l'usurpation d'identite).

```javascript
    switch (type) {
        // === ROOM LIFECYCLE ===
        case 'room-available':
            handleRoomAvailable(clientId, data);
            break;

        case 'room-closed':
            handleRoomClosed(clientId, data);
            break;

        case 'room-join':
            handleRoomJoin(clientId, data);
            break;

        case 'room-leave':
            handleRoomLeave(clientId, data);
            break;

        // ... autres cases
```

**Explication :** Switch geant qui route chaque type de message vers son handler specifique.

```javascript
        // === VR POSITION (PAR ROOM) ===
        case 'vr-position':
        case 'position':
            broadcastToRoom(clientId, message);  // Broadcast a la room seulement
            break;

        // === WHITEBOARD (PAR ROOM) ===
        case 'whiteboard-draw':
        case 'whiteboard-batch':
        case 'whiteboard-clear':
            broadcastToRoom(clientId, message);  // Ne va qu'aux joueurs de la meme room
            break;
```

**Explication :** Les messages de position et whiteboard ne sont envoyes qu'aux clients de la meme room.

```javascript
        // === WEBRTC SIGNALING (POINT-TO-POINT) ===
        case 'webrtc-offer':
            handleWebRTCOffer(clientId, data);  // Envoye a un seul client cible
            break;

        case 'webrtc-answer':
            handleWebRTCAnswer(clientId, data);
            break;

        case 'webrtc-ice-candidate':
            handleWebRTCIceCandidate(clientId, data);
            break;
```

**Explication :** WebRTC utilise le signaling point-a-point (pas broadcast).

```javascript
        default:
            // Messages inconnus: broadcast a la room si dans une room, sinon global
            const client = clients.get(clientId);
            if (client && client.roomId) {
                broadcastToRoom(clientId, message);
            } else {
                broadcast(message, clientId);
            }
    }
}
```

**Explication :** Les types de messages inconnus sont routes intelligemment.

---

### Section 4 : Gestion des Rooms (lignes 269-409)

```javascript
function handleRoomAvailable(clientId, dataStr) {
    try {
        // Supporte data en string ou object
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // Creer l'objet RoomInfo
        const roomInfo = {
            roomId: data.roomId,
            hostId: clientId,           // Le createur devient host
            roomName: data.roomName || `Room ${data.roomId}`,
            roomType: data.roomType || 0,
            playerCount: 1,             // Le host compte
            maxPlayers: data.maxPlayers || 10,
            createdAt: Date.now()
        };

        // Stocker la room
        rooms.set(data.roomId, roomInfo);

        // Associer le client a cette room
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
        }

        console.log(`[SERVER] Room created: ${data.roomId} by ${clientId}`);

        // Notifier tout le monde
        broadcastRoomList();
        broadcast({
            type: 'room-available',
            senderId: clientId,
            data: JSON.stringify(roomInfo)
        });

    } catch (e) {
        console.error(`[SERVER] handleRoomAvailable error: ${e.message}`);
    }
}
```

**Explication :** Quand un client cree une room :
1. Parse les donnees
2. Cree un objet RoomInfo
3. Stocke dans la Map `rooms`
4. Associe le client a cette room
5. Broadcast la nouvelle room a tous

```javascript
function handleRoomJoin(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);

        // Verification: la room existe?
        if (!room) {
            sendError(clientId, `Room ${data.roomId} not found`);
            return;
        }

        // Verification: la room n'est pas pleine?
        if (room.playerCount >= room.maxPlayers) {
            sendError(clientId, 'Room is full');
            return;
        }

        // Mettre a jour le client
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
            client.playerName = data.playerName || 'Player';
        }

        room.playerCount++;

        console.log(`[SERVER] Player ${clientId} joined room ${data.roomId}`);

        // Broadcast SEULEMENT a cette room
        broadcastToRoom(clientId, {
            type: 'room-join',
            senderId: clientId,
            data: JSON.stringify(data)
        });

        broadcastRoomList();  // Mettre a jour le compteur dans la liste

    } catch (e) {
        console.error(`[SERVER] handleRoomJoin error: ${e.message}`);
    }
}
```

**Explication :** Quand un client rejoint une room :
1. Verifie que la room existe et n'est pas pleine
2. Met a jour le `roomId` du client
3. Incremente le compteur de joueurs
4. Notifie les autres joueurs de cette room

---

### Section 5 : Broadcast Utilities (lignes 928-998)

```javascript
function sendToClient(ws, message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
    }
}
```

**Explication :** Envoie un message a UN client specifique (verifie que le socket est ouvert).

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

**Explication :** Broadcast a TOUS les clients (optionnellement exclure un client).

```javascript
/**
 * FONCTION CRITIQUE: Broadcast SEULEMENT aux clients de la meme room
 */
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);

    // Le sender doit etre dans une room
    if (!sender || !sender.roomId) {
        return;
    }

    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);

    let recipientCount = 0;

    clients.forEach((client, clientId) => {
        // Envoyer SEULEMENT si:
        // 1. Ce n'est pas l'expediteur
        // 2. Le client est dans la MEME room
        // 3. La connexion est ouverte
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
            recipientCount++;
        }
    });

    // Log pour debug whiteboard/objets
    if (message.type && (message.type.includes('whiteboard') || message.type.includes('obj-'))) {
        console.log(`[Room:${roomId}] ${message.type} from ${senderId} -> ${recipientCount} clients`);
    }
}
```

**Explication :** C'est LA fonction la plus importante - elle garantit que les messages restent dans la room.

---

### Section 6 : WebRTC Signaling (lignes 494-553)

```javascript
function handleWebRTCOffer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        // Trouver le client cible
        const targetClient = clients.get(targetId);
        if (!targetClient) return;

        // Relayer l'offre au client cible
        sendToClient(targetClient.ws, {
            type: 'webrtc-offer',
            senderId: senderId,      // L'ID de celui qui envoie l'offre
            data: JSON.stringify({ sdp })
        });

        console.log(`[WebRTC] Offer: ${senderId} -> ${targetId}`);
    } catch (e) {
        console.error(`[WebRTC] handleWebRTCOffer error: ${e.message}`);
    }
}
```

**Explication :** Le serveur agit comme RELAIS pour le signaling WebRTC :
1. Client A envoie une offre au serveur avec `targetId: clientB`
2. Le serveur trouve Client B et lui envoie l'offre
3. Meme processus pour `answer` et `ice-candidate`

---

### Section 7 : Heartbeat (lignes 1015-1032)

```javascript
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    // Envoyer un ping a tous les clients connectes
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();  // Envoi ping, attend pong
        }
    });

    // Detecter et deconnecter les clients morts
    clients.forEach((client, clientId) => {
        // Si pas de pong depuis 60 secondes (2x l'intervalle)
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            console.log(`[SERVER] Client timeout: ${clientId}`);
            client.ws.terminate();       // Force la fermeture
            handleDisconnect(clientId);  // Nettoie les ressources
        }
    });

}, HEARTBEAT_INTERVAL);  // Execute toutes les 30 secondes
```

**Explication :** Mecanisme keepalive :
1. Envoie `ping` a tous les clients toutes les 30 secondes
2. Les clients repondent automatiquement avec `pong`
3. Si pas de `pong` pendant 60 secondes, le client est considere mort

---

## VRNetworkManager.cs (Unity)

**Chemin :** `Assets/Scrips/Network/VRNetworkManager.cs`
**Lignes :** 461
**Role :** Client WebSocket, gestion connexion, envoi/reception messages

### Section 1 : Declaration et Configuration (lignes 1-76)

```csharp
public class VRNetworkManager : MonoBehaviour
{
    // Singleton accessible de partout
    public static VRNetworkManager Instance { get; private set; }

    [Header("Server Configuration")]
    public string serverUrl = "ws://localhost:8080";  // URL du serveur

    [Tooltip("Forcer wss:// en production")]
    public bool enforceSecureConnection = false;

    public bool autoReconnect = true;    // Reconnexion automatique
    public float reconnectDelay = 3f;    // Delai initial

    [Header("Connection Timeout (P0 Fix)")]
    public float welcomeTimeout = 5f;    // Timeout pour message welcome

    [Header("Exponential Backoff (P0 Fix)")]
    public float initialReconnectDelay = 1f;   // 1 seconde au debut
    public float maxReconnectDelay = 30f;      // Maximum 30 secondes
    public float backoffMultiplier = 2f;       // x2 a chaque echec
```

**Explication :** Configuration exposee dans l'Inspector Unity.

```csharp
    // Proprietes statiques accessibles de partout
    public static string LocalId { get; private set; }   // Notre ID assigne par le serveur
    public static bool IsConnected { get; private set; }  // Etat de connexion

    private WebSocket _websocket;         // Instance NativeWebSocket
    private bool _isReconnecting;         // Flag pour eviter reconnexions multiples
    private float _reconnectTimer;        // Timer pour le backoff
    private float _currentReconnectDelay; // Delai actuel (augmente avec backoff)

    // P0 FIX: Tracker le timeout du welcome
    private float _welcomeTimeoutTimer;
    private bool _waitingForWelcome;
```

**Explication :** Variables internes pour gerer l'etat de connexion.

```csharp
    // Cache pour eviter allocations GC (envoi a 30Hz)
    private readonly NetworkMessage _cachedOutgoingMessage = new NetworkMessage();

    [Header("Rate Limiting")]
    public int maxMessagesPerSecond = 60;  // Limite par seconde
    public int burstAllowance = 10;        // Messages autorises en rafale

    private float _rateLimitTokens;       // Token bucket
    private float _lastRateLimitRefill;   // Dernier refill
```

**Explication :** Optimisation GC avec cache et rate limiting pour eviter de surcharger le reseau.

```csharp
    // EVENTS - Souscrire dans OnEnable, desouscrire dans OnDisable
    public static event Action OnConnected;           // Serveur a assigne notre ID
    public static event Action OnDisconnected;        // Connexion perdue
    public static event Action<string> OnPeerConnected;     // Un autre client s'est connecte
    public static event Action<string> OnPeerDisconnected;  // Un autre client s'est deconnecte
    public static event Action<NetworkMessage> OnMessageReceived;  // Message gameplay
    public static event Action<string> OnConnectionError;   // Erreur de connexion
```

**Explication :** Systeme d'events pour communication decouple avec les autres managers.

---

### Section 2 : Lifecycle (lignes 80-214)

```csharp
void Awake()
{
    // Pattern Singleton
    if (Instance != null)
    {
        Destroy(gameObject);
        return;
    }

    Instance = this;
    DontDestroyOnLoad(gameObject);  // Persiste entre les scenes
}
```

**Explication :** Singleton classique Unity avec persistance.

```csharp
void Start()
{
    // Valider la securite de la connexion
    ValidateConnectionSecurity();

    _currentReconnectDelay = initialReconnectDelay;
    _reconnectAttempts = 0;

    // Initialiser le rate limiting
    _rateLimitTokens = burstAllowance;
    _lastRateLimitRefill = Time.unscaledTime;

    ConnectAsync();  // Demarrer la connexion
}
```

**Explication :** Initialisation au demarrage.

```csharp
private void ValidateConnectionSecurity()
{
    bool isSecure = serverUrl.StartsWith("wss://");
    bool isLocalhost = serverUrl.Contains("localhost") || serverUrl.Contains("127.0.0.1");

#if !UNITY_EDITOR
    // En build, avertir si connexion non securisee vers serveur distant
    if (!isSecure && !isLocalhost)
    {
        Debug.LogWarning("[VRNet] SECURITY WARNING: Using unencrypted ws:// connection!");
    }

    // Bloquer si enforceSecureConnection est active
    if (enforceSecureConnection && !isSecure)
    {
        Debug.LogError("[VRNet] SECURITY ERROR: Secure connection required!");
        enabled = false;
        return;
    }
#endif
}
```

**Explication :** Validation de securite - bloque les connexions non securisees en production si configure.

```csharp
void Update()
{
#if !UNITY_WEBGL || UNITY_EDITOR
    _websocket?.DispatchMessageQueue();  // Traiter les messages en file d'attente
#endif

    // Verifier le timeout du welcome
    if (_waitingForWelcome)
    {
        _welcomeTimeoutTimer -= Time.deltaTime;
        if (_welcomeTimeoutTimer <= 0f)
        {
            Debug.LogWarning("[VRNet] Welcome message timeout - reconnecting");
            _waitingForWelcome = false;
            HandleDisconnection();
        }
    }

    // Gerer la reconnexion avec exponential backoff
    if (_isReconnecting && autoReconnect)
    {
        _reconnectTimer -= Time.deltaTime;
        if (_reconnectTimer <= 0f)
        {
            _isReconnecting = false;
            _reconnectAttempts++;
            Debug.Log($"[VRNet] Reconnect attempt #{_reconnectAttempts}");
            ConnectAsync();
        }
    }
}
```

**Explication :** Chaque frame :
1. Dispatch les messages WebSocket (necessaire pour NativeWebSocket)
2. Verifie si le welcome a timeout
3. Gere la reconnexion si necessaire

---

### Section 3 : Connexion (lignes 218-311)

```csharp
public async Task Connect()
{
    // Ne pas reconnecter si deja connecte ou en cours
    if (_websocket != null &&
        (_websocket.State == WebSocketState.Open ||
         _websocket.State == WebSocketState.Connecting))
        return;

    try
    {
        Debug.Log($"[VRNet] Connecting to {serverUrl}");
        _websocket = new WebSocket(serverUrl);

        // Handler: socket ouvert
        _websocket.OnOpen += () =>
        {
            Debug.Log("[VRNet] WebSocket opened");
            _waitingForWelcome = true;
            _welcomeTimeoutTimer = welcomeTimeout;
        };

        // Handler: message recu
        _websocket.OnMessage += bytes =>
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            HandleMessage(json);
        };

        // Handler: socket ferme
        _websocket.OnClose += code =>
        {
            Debug.Log($"[VRNet] Closed ({code})");
            HandleDisconnection();
        };

        // Handler: erreur
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
```

**Explication :** Etablit la connexion WebSocket avec tous les handlers.

```csharp
private void HandleDisconnection()
{
    bool wasConnected = IsConnected;

    _waitingForWelcome = false;
    IsConnected = false;
    LocalId = null;

    if (wasConnected)
    {
        OnDisconnected?.Invoke();
        // Reset backoff apres une connexion reussie
        _currentReconnectDelay = initialReconnectDelay;
        _reconnectAttempts = 0;
    }

    if (autoReconnect && !_isReconnecting)
    {
        _isReconnecting = true;
        _reconnectTimer = _currentReconnectDelay;

        Debug.Log($"[VRNet] Reconnecting in {_currentReconnectDelay:F1}s");

        // Exponential backoff: 1s -> 2s -> 4s -> ... -> 30s max
        _currentReconnectDelay = Mathf.Min(
            _currentReconnectDelay * backoffMultiplier,
            maxReconnectDelay
        );
    }
}
```

**Explication :** Gestion propre de la deconnexion avec exponential backoff.

---

### Section 4 : Traitement des Messages (lignes 316-363)

```csharp
void HandleMessage(string json)
{
    try
    {
        NetworkMessage msg = JsonUtility.FromJson<NetworkMessage>(json);

        // 1. Handshake d'authentification
        if (msg.type == "welcome")
        {
            _waitingForWelcome = false;  // Annuler le timeout

            // Reset le backoff apres connexion reussie
            _currentReconnectDelay = initialReconnectDelay;
            _reconnectAttempts = 0;

            LocalId = msg.senderId;      // Stocker notre ID
            IsConnected = true;
            Debug.Log($"[VRNet] Assigned ID: {LocalId}");
            OnConnected?.Invoke();
            return;
        }

        // 2. Gestion des peers
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

        // 3. Ignorer les echos (sauf certains types)
        if (msg.senderId == LocalId && msg.type != "whiteboard-history")
            return;

        // 4. Transmettre aux systemes de jeu
        OnMessageReceived?.Invoke(msg);
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] JSON parse error: {e.Message}");
    }
}
```

**Explication :** Route les messages entrants vers les bons handlers.

---

### Section 5 : Envoi de Messages (lignes 368-440)

```csharp
public void Send(string type, object payload)
{
    string dataJson = payload is string s ? s : JsonUtility.ToJson(payload);
    SendInternal(type, dataJson);
}

private bool CheckRateLimit(string messageType)
{
    if (maxMessagesPerSecond <= 0)
        return true;  // Pas de limite

    // Token bucket algorithm
    float currentTime = Time.unscaledTime;
    float elapsed = currentTime - _lastRateLimitRefill;
    _lastRateLimitRefill = currentTime;

    // Refill des tokens
    _rateLimitTokens = Mathf.Min(burstAllowance, _rateLimitTokens + (elapsed * maxMessagesPerSecond));

    if (_rateLimitTokens >= 1f)
    {
        _rateLimitTokens -= 1f;
        return true;  // OK pour envoyer
    }

    // Rate limite - drop le message
    _messagesDropped++;
    if (_messagesDropped % 100 == 1)
    {
        Debug.LogWarning($"[VRNet] Rate limited '{messageType}' (total dropped: {_messagesDropped})");
    }
    return false;
}

private async void SendInternal(string type, string dataJson)
{
    if (_websocket == null || _websocket.State != WebSocketState.Open)
        return;

    // Skip rate limiting pour messages critiques
    bool isCritical = type == "welcome" || type.StartsWith("room-") || type.StartsWith("webrtc-");
    if (!isCritical && !CheckRateLimit(type))
        return;

    try
    {
        // Reutiliser le message cache (evite allocations GC)
        _cachedOutgoingMessage.type = type;
        _cachedOutgoingMessage.senderId = LocalId;
        _cachedOutgoingMessage.data = dataJson;

        await _websocket.SendText(JsonUtility.ToJson(_cachedOutgoingMessage));
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] Send failed: {e.Message}");
    }
}
```

**Explication :**
- `Send()` : API publique pour envoyer des messages
- `CheckRateLimit()` : Token bucket pour limiter les envois
- `SendInternal()` : Envoi effectif avec cache pour eviter GC

---

## VRRoomManager.cs (Unity)

**Chemin :** `Assets/Scrips/Network/VRRoomManager.cs`
**Lignes :** 854
**Role :** Gestion des rooms, joueurs, zones

### Points Cles

```csharp
// Generation de code de room SECURISEE
string GenerateRoomId()
{
    // Caracteres non ambigus (pas de O/0, I/1/L)
    const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    char[] id = new char[6];

    // IMPORTANT: Random cryptographique au lieu de System.Random
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

**Explication :** Genere des codes de room securises et lisibles.

```csharp
// Helper de deserialisation securisee
private T TryDeserialize<T>(string json, string context) where T : class
{
    if (string.IsNullOrEmpty(json))
    {
        Debug.LogWarning($"[VRRoom] Empty JSON for {context}");
        return null;
    }

    try
    {
        T result = JsonUtility.FromJson<T>(json);
        if (result == null)
        {
            Debug.LogWarning($"[VRRoom] Null result for {context}");
            return null;
        }
        return result;
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRRoom] JSON parse error for {context}: {e.Message}");
        return null;
    }
}
```

**Explication :** Evite les crashes sur JSON malformes.

---

## VRGameManager.cs (Unity)

**Chemin :** `Assets/Scrips/Network/VRGameManager.cs`
**Lignes :** 1430
**Role :** Spawn joueurs, sync positions VR

### Synchronisation Position (30Hz)

```csharp
void SendPositionUpdate()
{
    if (_localPlayer == null || !VRRoomManager.Instance.IsInRoom) return;

    Transform originTf = (_localXrOrigin != null) ? _localXrOrigin.transform : _localPlayer.transform;

    // Detection de mouvement (optimisation)
    float posChange = Vector3.Distance(_lastSyncPosition, originTf.position);
    float rotChange = Quaternion.Angle(_lastSyncRotation, originTf.rotation);

    bool headMoved = false;
    if (_localHead != null)
    {
        float headPosChange = Vector3.Distance(_lastSyncHeadPos, _localHead.position);
        float headRotChange = Quaternion.Angle(_lastSyncHeadRot, _localHead.rotation);
        headMoved = headPosChange > movementThreshold || headRotChange > rotationThreshold;
    }

    // Ne sync que si quelque chose a bouge
    if (posChange < movementThreshold && rotChange < rotationThreshold && !headMoved)
    {
        return;  // Pas d'envoi inutile
    }

    // Mise a jour des derniers positions
    _lastSyncPosition = originTf.position;
    _lastSyncRotation = originTf.rotation;

    // Reutiliser l'objet cache (evite GC)
    _cachedPositionData.roomId = VRRoomManager.Instance.CurrentRoomId;
    _cachedPositionData.roomType = VRRoomManager.Instance.CurrentRoomType;

    // Corps - arrondi a 3 decimales (mm)
    _cachedPositionData.posX = Round(originTf.position.x);
    _cachedPositionData.posY = Round(originTf.position.y);
    _cachedPositionData.posZ = Round(originTf.position.z);
    _cachedPositionData.rotY = Round(originTf.eulerAngles.y);

    // Tete en world space
    if (_localHead != null)
    {
        _cachedPositionData.headPosX = Round(_localHead.position.x);
        _cachedPositionData.headPosY = Round(_localHead.position.y);
        _cachedPositionData.headPosZ = Round(_localHead.position.z);
        _cachedPositionData.headRotX = Round(_localHead.rotation.x);
        // ... etc
    }

    // Mains en world space (VR mode seulement)
    if (!_isDesktopMode && syncHands)
    {
        // ... sync mains gauche et droite
    }

    VRNetworkManager.Instance.Send("vr-position", _cachedPositionData);
}
```

**Explication :**
1. Detecte si quelque chose a bouge (optimisation reseau)
2. Arrondit les valeurs a 3 decimales
3. Envoie seulement le necessaire

### Interpolation Remote Players

```csharp
void InterpolateRemotePlayers()
{
    float t = Time.deltaTime * interpolationSpeed;

    foreach (var remote in _remotePlayers.Values)
    {
        if (remote.gameObject == null || !remote.hasReceivedData)
            continue;

        if (!remote.gameObject.activeSelf)
            continue;

        // Corps : interpolation smooth
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

        // Tete : world space (detachee du corps)
        if (remote.head != null)
        {
            remote.head.position = Vector3.Lerp(
                remote.head.position,
                remote.targetHeadPosition,
                t
            );
            remote.head.rotation = Quaternion.Slerp(
                remote.head.rotation,
                remote.targetHeadRotation,
                t
            );

            // Name tag suit la tete + billboard
            if (remote.nameTag != null && _localHead != null)
            {
                remote.nameTag.position = remote.head.position + Vector3.up * 0.55f;

                // Billboard vers le joueur local
                Vector3 dirToViewer = remote.nameTag.position - _localHead.position;
                dirToViewer.y = 0;
                if (dirToViewer.sqrMagnitude > 0.001f)
                {
                    remote.nameTag.rotation = Quaternion.LookRotation(dirToViewer);
                }
            }
        }

        // Mains (VR mode)
        // ...
    }
}
```

**Explication :** Interpolation fluide des positions recues du reseau.

---

## VoiceChatManager.cs (Unity)

**Chemin :** `Assets/Scrips/WebRTC/VoiceChatManager.cs`
**Lignes :** 1085
**Role :** WebRTC voice chat, audio spatial

### Topologie Mesh

```csharp
void OnPlayerJoined(VRPlayerData player)
{
    if (player.playerId == VRNetworkManager.LocalId) return;

    // MESH TOPOLOGY: Regle deterministe pour eviter les doublons
    // Le joueur avec l'ID le plus petit initie la connexion
    string localId = VRNetworkManager.LocalId;
    if (string.Compare(localId, player.playerId, StringComparison.Ordinal) < 0)
    {
        // Notre ID est plus petit -> on initie
        LogDebug($"[VoiceChat] MESH: {localId} < {player.playerId} -> Initiating");
        StartCoroutine(CreatePeerConnection(player.playerId, true));
    }
    else
    {
        // Leur ID est plus petit -> ils vont initier
        LogDebug($"[VoiceChat] MESH: {localId} > {player.playerId} -> Waiting");
    }
}
```

**Explication :** Regle deterministe qui garantit une connexion par paire de joueurs.

### Configuration STUN/TURN

```csharp
private RTCConfiguration BuildRTCConfiguration()
{
    var iceServers = new List<RTCIceServer>();

    // STUN serveurs (gratuits, publics)
    iceServers.Add(new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } });
    iceServers.Add(new RTCIceServer { urls = new[] { "stun:stun.cloudflare.com:3478" } });

    // TURN serveurs (necessaires pour NAT/firewall)
    if (useCustomTurnServer && !string.IsNullOrEmpty(customTurnUrl))
    {
        // Serveur TURN prive (recommande en production)
        iceServers.Add(new RTCIceServer
        {
            urls = new[] { customTurnUrl },
            username = customTurnUsername,
            credential = customTurnCredential
        });
    }
    else
    {
        // TURN public (dev only - WARNING en production)
        iceServers.Add(new RTCIceServer
        {
            urls = new[] { "turn:openrelay.metered.ca:80" },
            username = "openrelayproject",
            credential = "openrelayproject"
        });
    }

    return new RTCConfiguration { iceServers = iceServers.ToArray() };
}
```

**Explication :**
- STUN : Pour decouvrir notre IP publique (gratuit)
- TURN : Pour relayer le traffic si connexion directe impossible (payant en prod)

---

## Fichiers Auxiliaires

### db.js (lignes 1-29)

```javascript
const mysql = require('mysql2/promise');

const pool = mysql.createPool({
    host: process.env.DB_HOST || 'localhost',
    port: process.env.DB_PORT || 3306,
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD || 'JJkk2812',
    database: process.env.DB_NAME || 'vr_meeting',
    waitForConnections: true,
    connectionLimit: 10,  // Pool de 10 connexions
    queueLimit: 0
});

// Test de connexion au demarrage
pool.getConnection()
    .then(conn => {
        console.log('[DB] Connected to MariaDB');
        conn.release();
    })
    .catch(err => {
        console.error('[DB] Connection failed:', err.message);
    });

module.exports = pool;
```

**Explication :** Pool de connexions MySQL/MariaDB.

### auth.js (lignes 1-97)

```javascript
const bcrypt = require('bcrypt');
const db = require('./db');

const SALT_ROUNDS = 10;  // Cout du hachage bcrypt

async function registerUser(username, email, password, displayName) {
    try {
        // Verifier si l'utilisateur existe deja
        const [existing] = await db.query(
            'SELECT id FROM users WHERE username = ? OR email = ?',
            [username, email]
        );

        if (existing.length > 0) {
            return { success: false, error: 'Username or email already exists' };
        }

        // Hacher le mot de passe
        const passwordHash = await bcrypt.hash(password, SALT_ROUNDS);

        // Inserer l'utilisateur
        const [result] = await db.query(
            'INSERT INTO users (username, email, password_hash, display_name) VALUES (?, ?, ?, ?)',
            [username, email, passwordHash, displayName || username]
        );

        return {
            success: true,
            userId: result.insertId,
            username: username,
            displayName: displayName || username
        };

    } catch (err) {
        console.error('[Auth] Register error:', err.message);
        return { success: false, error: 'Registration failed' };
    }
}

async function loginUser(username, password) {
    // ... verification mot de passe avec bcrypt.compare
}

async function updateUserProfile(userId, displayName, avatarColor) {
    // ... mise a jour profil
}

module.exports = { registerUser, loginUser, updateUserProfile };
```

**Explication :** Gestion authentification avec hachage bcrypt securise.

---

## Resume des Flux de Donnees

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
     |                               |-- webrtc-offer (point2point)->|
     |<-- webrtc-answer -------------|<-- webrtc-answer -------------|
     |                               |                               |
     |<========== P2P Audio via WebRTC (contourne le serveur) ======>|
```

---

## Conclusion

Cette architecture permet :
1. **Scalabilite** : Le serveur est un simple routeur, pas de logique complexe
2. **Faible latence** : Position sync a 30Hz, audio P2P via WebRTC
3. **Isolation** : Rooms independantes grace a `broadcastToRoom`
4. **Robustesse** : Reconnexion automatique avec exponential backoff
5. **Securite** : Rate limiting, validation JSON, options SSL
