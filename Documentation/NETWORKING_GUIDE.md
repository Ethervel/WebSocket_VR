# Guide Complet du Networking - Explication Simple

Ce document explique comment fonctionne le réseau dans ce projet VR, comme si on l'expliquait à quelqu'un qui n'y connaît absolument rien.

---

## Table des Matières

1. [C'est quoi le Networking ?](#1-cest-quoi-le-networking-)
2. [Les 3 Fichiers Importants](#2-les-3-fichiers-importants)
3. [VRNetworkManager.cs - Le Client Unity (ligne par ligne)](#3-vrnetworkmanagercs---le-client-unity)
4. [server.js - Le Serveur Node.js (ligne par ligne)](#4-serverjs---le-serveur-nodejs)
5. [db.js - La Base de Données (ligne par ligne)](#5-dbjs---la-base-de-données)
6. [Comment tout fonctionne ensemble](#6-comment-tout-fonctionne-ensemble)
7. [Pour passer en production (serveur distant)](#7-pour-passer-en-production)

---

## 1. C'est quoi le Networking ?

Imagine que tu joues avec des amis dans une salle virtuelle. Pour que tout le monde voit la même chose :

```
Toi (Unity)  ←──────→  Serveur (Node.js)  ←──────→  Ton ami (Unity)
     │                       │                           │
     │    "Je bouge !"       │                           │
     │ ──────────────────→   │                           │
     │                       │   "Il a bougé !"          │
     │                       │ ──────────────────────→   │
```

- **Client** = Le jeu Unity sur ton ordinateur/casque VR
- **Serveur** = Un ordinateur central qui reçoit les messages de tout le monde et les redistribue
- **WebSocket** = Un "tuyau" qui reste ouvert pour envoyer des messages instantanément

---

## 2. Les 3 Fichiers Importants

| Fichier | Où ? | Rôle |
|---------|------|------|
| `VRNetworkManager.cs` | Unity (Client) | Envoie et reçoit les messages |
| `server.js` | Node.js (Serveur) | Reçoit les messages et les redistribue |
| `db.js` | Node.js (Serveur) | Stocke les utilisateurs dans une base de données |

---

## 3. VRNetworkManager.cs - Le Client Unity

**Chemin :** `Assets/Scrips/Network/VRNetworkManager.cs`

Ce fichier est le "cerveau réseau" du jeu Unity. Il gère la connexion au serveur.

### Les imports (lignes 1-4)

```csharp
using System;                    // Pour utiliser des fonctions de base (comme les erreurs)
using System.Threading.Tasks;    // Pour faire des choses "en parallèle" (async/await)
using UnityEngine;               // Pour utiliser les fonctions Unity (MonoBehaviour, etc.)
using NativeWebSocket;           // La bibliothèque qui permet de faire du WebSocket
```

**Explication simple :**
- C'est comme dire "j'ai besoin de ces outils" avant de commencer à travailler
- `NativeWebSocket` est une bibliothèque externe qui permet de communiquer avec le serveur

---

### La classe et le Singleton (lignes 13-16)

```csharp
public class VRNetworkManager : MonoBehaviour
{
    public static VRNetworkManager Instance { get; private set; }
```

**Explication simple :**
- `MonoBehaviour` = Ce script peut être attaché à un objet Unity
- `Instance` = Il n'y aura qu'UN SEUL VRNetworkManager dans tout le jeu (c'est un "Singleton")
- C'est comme dire "il n'y a qu'un seul téléphone pour appeler le serveur"

---

### La configuration du serveur (lignes 17-32)

```csharp
[Header("Server Configuration")]
public string serverUrl = "ws://localhost:8080";    // L'adresse du serveur
public bool autoReconnect = true;                   // Se reconnecter si on perd la connexion ?
public float reconnectDelay = 3f;                   // Attendre combien de secondes avant de réessayer

[Header("Connection Timeout (P0 Fix)")]
public float welcomeTimeout = 5f;                   // Attendre max 5 secondes la réponse du serveur

[Header("Exponential Backoff (P0 Fix)")]
public float initialReconnectDelay = 1f;            // Premier essai : attendre 1 seconde
public float maxReconnectDelay = 30f;               // Maximum : attendre 30 secondes
public float backoffMultiplier = 2f;                // Multiplier le délai par 2 à chaque échec
```

**Explication simple :**
- `serverUrl` = L'adresse où se trouve le serveur. `ws://` c'est pour WebSocket, `localhost` c'est "sur mon propre ordinateur", `8080` c'est le numéro de la porte
- `autoReconnect` = Si la connexion se coupe, on réessaye automatiquement
- `Exponential Backoff` = Si ça ne marche pas, on attend de plus en plus longtemps avant de réessayer (1s, puis 2s, puis 4s, puis 8s...) pour ne pas "spammer" le serveur

**IMPORTANT pour serveur distant :**
```csharp
// Pour un serveur sur internet, change cette ligne :
public string serverUrl = "ws://ton-serveur.com:8080";

// Ou avec HTTPS (plus sécurisé) :
public string serverUrl = "wss://ton-serveur.com:8080";
```

---

### Les variables d'état (lignes 34-47)

```csharp
public static string LocalId { get; private set; }   // Mon identifiant unique (donné par le serveur)
public static bool IsConnected { get; private set; } // Suis-je connecté ? true/false

private WebSocket _websocket;           // L'objet qui gère la connexion
private bool _isReconnecting;           // Est-ce qu'on est en train de se reconnecter ?
private float _reconnectTimer;          // Compteur pour la reconnexion

private float _currentReconnectDelay;   // Délai actuel de reconnexion
private int _reconnectAttempts;         // Nombre de tentatives de reconnexion

private float _welcomeTimeoutTimer;     // Compteur pour le timeout du message "welcome"
private bool _waitingForWelcome;        // Est-ce qu'on attend le message "welcome" ?
```

**Explication simple :**
- `LocalId` = C'est comme ton numéro de téléphone dans le jeu. Le serveur te le donne quand tu te connectes.
- `IsConnected` = Un simple "oui" ou "non" pour savoir si on est connecté
- `_websocket` = C'est le "tuyau" de communication
- Les autres variables servent à gérer les reconnexions

---

### Les événements (lignes 52-60)

```csharp
public static event Action OnConnected;                    // Déclenché quand on se connecte
public static event Action OnDisconnected;                 // Déclenché quand on se déconnecte
public static event Action<string> OnPeerConnected;        // Déclenché quand quelqu'un d'autre se connecte
public static event Action<string> OnPeerDisconnected;     // Déclenché quand quelqu'un d'autre se déconnecte
public static event Action<NetworkMessage> OnMessageReceived;  // Déclenché quand on reçoit un message
public static event Action<string> OnConnectionError;      // Déclenché quand il y a une erreur
```

**Explication simple :**
- Les "events" c'est comme des "alertes" que d'autres scripts peuvent écouter
- Par exemple, `VRRoomManager` écoute `OnConnected` pour savoir quand il peut créer des salles
- C'est comme dire "Préviens-moi quand quelqu'un arrive !"

**Comment les utiliser dans un autre script :**
```csharp
void OnEnable() {
    VRNetworkManager.OnConnected += MaFonctionQuandConnecte;  // Je m'abonne
}

void OnDisable() {
    VRNetworkManager.OnConnected -= MaFonctionQuandConnecte;  // Je me désabonne
}

void MaFonctionQuandConnecte() {
    Debug.Log("Youpi, je suis connecté !");
}
```

---

### Awake - Création du Singleton (lignes 65-75)

```csharp
void Awake()
{
    // Si un VRNetworkManager existe déjà...
    if (Instance != null)
    {
        Destroy(gameObject);  // ...détruit ce nouveau (on n'en veut qu'un seul)
        return;
    }

    Instance = this;                    // Je suis LE VRNetworkManager
    DontDestroyOnLoad(gameObject);      // Ne me détruis pas quand on change de scène
}
```

**Explication simple :**
- `Awake()` est appelé quand l'objet est créé dans Unity
- On vérifie s'il existe déjà un VRNetworkManager. Si oui, on détruit le nouveau.
- `DontDestroyOnLoad` = "Garde-moi en vie même si on change de niveau"

---

### Start - Démarrage de la connexion (lignes 78-83)

```csharp
void Start()
{
    _currentReconnectDelay = initialReconnectDelay;  // Délai initial = 1 seconde
    _reconnectAttempts = 0;                          // Aucune tentative pour l'instant
    ConnectAsync();                                  // Lance la connexion !
}
```

**Explication simple :**
- `Start()` est appelé une fois au début du jeu
- On initialise les compteurs et on démarre la connexion

---

### ConnectAsync - Wrapper de connexion (lignes 86-98)

```csharp
private async void ConnectAsync()
{
    try
    {
        await Connect();  // Essaye de se connecter
    }
    catch (Exception e)   // Si ça plante...
    {
        Debug.LogError($"[VRNet] Connection failed: {e.Message}");
        OnConnectionError?.Invoke(e.Message);  // Préviens tout le monde de l'erreur
        HandleDisconnection();                  // Gère la déconnexion
    }
}
```

**Explication simple :**
- `async` = Cette fonction peut faire des choses "en parallèle" sans bloquer le jeu
- `try/catch` = "Essaye ça, et si ça ne marche pas, fais ceci"
- C'est une "enveloppe de sécurité" autour de la vraie fonction `Connect()`

---

### Update - La boucle principale (lignes 100-130)

```csharp
void Update()
{
    // Sur PC (pas sur navigateur web), il faut manuellement traiter les messages reçus
    #if !UNITY_WEBGL || UNITY_EDITOR
        _websocket?.DispatchMessageQueue();  // Traite les messages en attente
    #endif

    // Vérifie si le serveur met trop de temps à répondre "welcome"
    if (_waitingForWelcome)
    {
        _welcomeTimeoutTimer -= Time.deltaTime;  // Compte à rebours
        if (_welcomeTimeoutTimer <= 0f)          // Temps écoulé !
        {
            Debug.LogWarning("[VRNet] Welcome timeout - reconnecting");
            _waitingForWelcome = false;
            HandleDisconnection();  // Considère qu'on est déconnecté
        }
    }

    // Gère la reconnexion automatique
    if (_isReconnecting && autoReconnect)
    {
        _reconnectTimer -= Time.deltaTime;  // Compte à rebours
        if (_reconnectTimer <= 0f)          // C'est le moment de réessayer !
        {
            _isReconnecting = false;
            _reconnectAttempts++;
            Debug.Log($"[VRNet] Reconnect attempt #{_reconnectAttempts}");
            ConnectAsync();  // Réessaye de se connecter
        }
    }
}
```

**Explication simple :**
- `Update()` est appelé à chaque image du jeu (60 fois par seconde environ)
- On vérifie si le serveur a mis trop de temps à répondre
- On vérifie si c'est le moment de réessayer de se connecter

---

### Connect - La vraie connexion (lignes 160-207)

```csharp
public async Task Connect()
{
    // Si on est déjà connecté ou en train de se connecter, ne fait rien
    if (_websocket != null &&
        (_websocket.State == WebSocketState.Open ||
         _websocket.State == WebSocketState.Connecting))
        return;

    try
    {
        Debug.Log($"[VRNet] Connecting to {serverUrl}");

        // Crée un nouveau WebSocket vers le serveur
        _websocket = new WebSocket(serverUrl);

        // Quand la connexion s'ouvre...
        _websocket.OnOpen += () =>
        {
            Debug.Log("[VRNet] WebSocket opened");
            _waitingForWelcome = true;           // On attend le message "welcome"
            _welcomeTimeoutTimer = welcomeTimeout;  // Démarre le compteur (5 secondes)
        };

        // Quand on reçoit un message...
        _websocket.OnMessage += bytes =>
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);  // Convertit les bytes en texte
            HandleMessage(json);  // Traite le message
        };

        // Quand la connexion se ferme...
        _websocket.OnClose += code =>
        {
            Debug.Log($"[VRNet] Closed ({code})");
            HandleDisconnection();
        };

        // Quand il y a une erreur...
        _websocket.OnError += err =>
        {
            Debug.LogError($"[VRNet] Error: {err}");
            OnConnectionError?.Invoke(err);
            HandleDisconnection();
        };

        // Lance la connexion !
        await _websocket.Connect();
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] Connection exception: {e.Message}");
        HandleDisconnection();
    }
}
```

**Explication simple :**
- On crée un "tuyau" WebSocket vers le serveur
- On définit ce qui se passe dans différentes situations :
  - `OnOpen` = Le tuyau est ouvert ! On attend maintenant le message "welcome"
  - `OnMessage` = On a reçu un message ! On le traite.
  - `OnClose` = Le tuyau s'est fermé. On gère la déconnexion.
  - `OnError` = Il y a eu un problème. On gère l'erreur.
- `await _websocket.Connect()` = On attend que la connexion soit établie

---

### HandleDisconnection - Gestion de la déconnexion (lignes 224-253)

```csharp
private void HandleDisconnection()
{
    bool wasConnected = IsConnected;  // Est-ce qu'on était connecté avant ?

    _waitingForWelcome = false;  // On n'attend plus le "welcome"
    IsConnected = false;         // On n'est plus connecté
    LocalId = null;              // On n'a plus d'identifiant

    // Si on était connecté, préviens tout le monde
    if (wasConnected)
    {
        OnDisconnected?.Invoke();
        // Réinitialise le délai de reconnexion (on recommence à 1 seconde)
        _currentReconnectDelay = initialReconnectDelay;
        _reconnectAttempts = 0;
    }

    // Si la reconnexion automatique est activée...
    if (autoReconnect && !_isReconnecting)
    {
        _isReconnecting = true;
        _reconnectTimer = _currentReconnectDelay;  // Attendre ce délai avant de réessayer

        Debug.Log($"[VRNet] Reconnecting in {_currentReconnectDelay}s");

        // EXPONENTIAL BACKOFF : Double le délai pour la prochaine fois
        // 1s → 2s → 4s → 8s → 16s → 30s (max)
        _currentReconnectDelay = Mathf.Min(
            _currentReconnectDelay * backoffMultiplier,
            maxReconnectDelay
        );
    }
}
```

**Explication simple :**
- Quand on perd la connexion, on nettoie tout
- Si on était vraiment connecté avant, on prévient les autres scripts
- On programme une reconnexion automatique
- **Exponential Backoff** : On attend de plus en plus longtemps entre chaque essai pour ne pas "harceler" le serveur

---

### HandleMessage - Traitement des messages reçus (lignes 258-305)

```csharp
void HandleMessage(string json)
{
    try
    {
        // Convertit le texte JSON en objet NetworkMessage
        NetworkMessage msg = JsonUtility.FromJson<NetworkMessage>(json);

        // MESSAGE "welcome" = Le serveur nous dit bonjour et nous donne notre ID
        if (msg.type == "welcome")
        {
            _waitingForWelcome = false;  // On a reçu le welcome !

            // Réinitialise le backoff (la connexion a réussi)
            _currentReconnectDelay = initialReconnectDelay;
            _reconnectAttempts = 0;

            LocalId = msg.senderId;   // Sauvegarde notre identifiant unique
            IsConnected = true;       // On est officiellement connecté !

            Debug.Log($"[VRNet] Assigned ID: {LocalId}");
            OnConnected?.Invoke();    // Préviens tout le monde qu'on est connecté
            return;
        }

        // MESSAGE "peer-connected" = Quelqu'un d'autre s'est connecté
        if (msg.type == "peer-connected")
        {
            OnPeerConnected?.Invoke(msg.senderId);  // Préviens avec son ID
            return;
        }

        // MESSAGE "peer-disconnected" = Quelqu'un s'est déconnecté
        if (msg.type == "peer-disconnected")
        {
            OnPeerDisconnected?.Invoke(msg.senderId);
            return;
        }

        // Ignore les messages qu'on a envoyé nous-même (écho)
        if (msg.senderId == LocalId && msg.type != "whiteboard-history")
            return;

        // Tous les autres messages : les transmet aux autres scripts
        OnMessageReceived?.Invoke(msg);
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] JSON parse error: {e.Message}");
    }
}
```

**Explication simple :**
- Quand on reçoit un message, on regarde son `type` pour savoir quoi faire
- `welcome` = Le serveur nous accueille et nous donne notre ID. C'est le moment où on est "vraiment" connecté.
- `peer-connected` = Un autre joueur vient d'arriver
- `peer-disconnected` = Un autre joueur est parti
- Tous les autres messages sont transmis aux autres scripts via `OnMessageReceived`

---

### Send - Envoyer des messages (lignes 310-342)

```csharp
// Version simple : juste le type, pas de données
public void Send(string type)
{
    SendInternal(type, "{}");  // Envoie avec un objet vide
}

// Version complète : type + données
public void Send(string type, object payload)
{
    // Convertit l'objet en texte JSON
    string dataJson = payload is string s ? s : JsonUtility.ToJson(payload);
    SendInternal(type, dataJson);
}

// La vraie fonction d'envoi (privée)
private async void SendInternal(string type, string dataJson)
{
    // Si pas connecté, ne fait rien
    if (_websocket == null || _websocket.State != WebSocketState.Open)
        return;

    try
    {
        // Prépare le message
        _cachedOutgoingMessage.type = type;
        _cachedOutgoingMessage.senderId = LocalId;
        _cachedOutgoingMessage.data = dataJson;

        // Envoie le message au serveur
        await _websocket.SendText(JsonUtility.ToJson(_cachedOutgoingMessage));
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] Send failed for '{type}': {e.Message}");
    }
}
```

**Explication simple :**
- `Send("room-join", monObjet)` = Envoie un message au serveur
- Le message est converti en texte JSON avant d'être envoyé
- On utilise un objet "caché" (`_cachedOutgoingMessage`) pour éviter de créer des objets inutiles (optimisation mémoire)

**Comment l'utiliser dans ton code :**
```csharp
// Exemple : rejoindre une salle
var data = new RoomJoinData { roomId = "ABC123", playerName = "Jean" };
VRNetworkManager.Instance.Send("room-join", data);

// Exemple : envoyer ma position
var posData = new PositionData { x = 1.5f, y = 0, z = 3.2f };
VRNetworkManager.Instance.Send("vr-position", posData);
```

---

### Le format des messages (lignes 356-362)

```csharp
[Serializable]
public class NetworkMessage
{
    public string type;      // Le type de message (ex: "vr-position", "room-join")
    public string senderId;  // L'ID de celui qui envoie
    public string data;      // Les données (toujours en JSON texte)
}
```

**Explication simple :**
- Tous les messages ont la même structure
- `type` = C'est quoi comme message ?
- `senderId` = Qui l'envoie ?
- `data` = Les informations (position, nom de salle, etc.)

**Exemple de message en JSON :**
```json
{
    "type": "vr-position",
    "senderId": "abc123-def456-...",
    "data": "{\"x\":1.5,\"y\":0,\"z\":3.2,\"rotY\":45}"
}
```

---

## 4. server.js - Le Serveur Node.js

**Chemin :** `LocalServ/Server/server.js`

Le serveur est comme un "standard téléphonique" : il reçoit les appels et les redirige vers les bonnes personnes.

### Les imports et configuration (lignes 13-21)

```javascript
const WebSocket = require('ws');        // Bibliothèque WebSocket pour Node.js
const { v4: uuidv4 } = require('uuid'); // Pour générer des identifiants uniques
const { registerUser, loginUser, updateUserProfile } = require('./auth');  // Fonctions d'authentification

const PORT = process.env.PORT || 8080;  // Port du serveur (8080 par défaut)
const HEARTBEAT_INTERVAL = 30000;       // Vérifier les connexions toutes les 30 secondes

const clients = new Map();  // Liste des clients connectés : clientId → {ws, roomId, playerName}
const rooms = new Map();    // Liste des salles : roomId → RoomInfo
```

**Explication simple :**
- On charge les outils dont on a besoin
- `PORT` = Le numéro de la "porte" du serveur. Par défaut 8080, mais on peut le changer avec une variable d'environnement
- `clients` = Une liste de tous les joueurs connectés
- `rooms` = Une liste de toutes les salles de réunion

**IMPORTANT pour serveur distant :**
```bash
# Pour changer le port, définis la variable d'environnement AVANT de lancer :
export PORT=3000
node server.js
```

---

### Démarrage du serveur (ligne 23-25)

```javascript
const wss = new WebSocket.Server({ port: PORT });

console.log(`[SERVER] WebSocket server started on port ${PORT}`);
```

**Explication simple :**
- On crée le serveur WebSocket qui écoute sur le port configuré
- Le serveur affiche un message pour dire qu'il est prêt

---

### Quand un client se connecte (lignes 31-78)

```javascript
wss.on('connection', (ws) => {
    // Génère un identifiant unique pour ce client
    const clientId = uuidv4();  // Exemple: "550e8400-e29b-41d4-a716-446655440000"

    // Ajoute le client à notre liste
    clients.set(clientId, {
        ws: ws,              // La connexion WebSocket
        roomId: null,        // Pas encore dans une salle
        playerName: 'Player', // Nom par défaut
        lastHeartbeat: Date.now()  // Dernière activité
    });

    console.log(`[SERVER] Client connected: ${clientId}`);

    // Envoie le message "welcome" avec l'ID au client
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // Préviens tous les autres clients qu'un nouveau joueur est arrivé
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);  // Sauf le nouveau client lui-même

    // Envoie la liste des salles disponibles
    sendRoomList(ws);

    // Quand on reçoit un message de ce client...
    ws.on('message', (data) => {
        try {
            const message = JSON.parse(data.toString());  // Convertit le texte en objet
            handleMessage(clientId, message);              // Traite le message
        } catch (e) {
            console.error(`[SERVER] Parse error: ${e.message}`);
        }
    });

    // Quand ce client se déconnecte...
    ws.on('close', () => {
        handleDisconnect(clientId);
    });

    // Quand il y a une erreur avec ce client...
    ws.on('error', (error) => {
        console.error(`[SERVER] Client error (${clientId}): ${error.message}`);
    });

    // Réponse au ping (pour vérifier que le client est toujours là)
    ws.on('pong', () => {
        const client = clients.get(clientId);
        if (client) {
            client.lastHeartbeat = Date.now();
        }
    });
});
```

**Explication simple :**
1. Un nouveau joueur se connecte
2. On lui crée un ID unique (comme un numéro de badge)
3. On l'ajoute à notre liste de joueurs
4. On lui dit "Bienvenue ! Ton ID est XXX" (message `welcome`)
5. On dit aux autres "Hey, quelqu'un vient d'arriver !" (message `peer-connected`)
6. On lui envoie la liste des salles disponibles
7. On configure ce qui se passe quand il envoie des messages ou se déconnecte

---

### Le routage des messages (lignes 84-231)

```javascript
function handleMessage(clientId, message) {
    const { type, senderId, data } = message;
    message.senderId = clientId;  // On s'assure que l'ID est correct

    console.log(`[SERVER] Message from ${clientId}: ${type}`);

    switch (type) {
        // === GESTION DES SALLES ===
        case 'room-available':      // Créer une nouvelle salle
            handleRoomAvailable(clientId, data);
            break;

        case 'room-closed':         // Fermer une salle
            handleRoomClosed(clientId, data);
            break;

        case 'room-join':           // Rejoindre une salle
            handleRoomJoin(clientId, data);
            break;

        case 'room-leave':          // Quitter une salle
            handleRoomLeave(clientId, data);
            break;

        // === SYNCHRONISATION VR (position des joueurs) ===
        case 'vr-position':
        case 'position':
            broadcastToRoom(clientId, message);  // Envoie à tous dans la même salle
            break;

        // === TABLEAU BLANC ===
        case 'whiteboard-draw':
        case 'whiteboard-batch':
        case 'whiteboard-clear':
            broadcastToRoom(clientId, message);  // Envoie à tous dans la même salle
            break;

        // === SIGNALING WEBRTC (pour le chat vocal) ===
        case 'webrtc-offer':
            handleWebRTCOffer(clientId, data);   // Envoie à UN client spécifique
            break;

        case 'webrtc-answer':
            handleWebRTCAnswer(clientId, data);
            break;

        case 'webrtc-ice-candidate':
            handleWebRTCIceCandidate(clientId, data);
            break;

        // ... autres types de messages ...

        default:
            // Message inconnu : on l'envoie à la salle ou à tout le monde
            const client = clients.get(clientId);
            if (client && client.roomId) {
                broadcastToRoom(clientId, message);
            } else {
                broadcast(message, clientId);
            }
    }
}
```

**Explication simple :**
- Le serveur regarde le `type` du message pour savoir quoi en faire
- Certains messages vont à **tous les joueurs de la salle** (`broadcastToRoom`)
- Certains messages vont à **un joueur spécifique** (WebRTC)
- Certains messages vont à **tout le monde** (`broadcast`)

---

### Gestion des salles - Créer une salle (lignes 237-270)

```javascript
function handleRoomAvailable(clientId, dataStr) {
    try {
        // Convertit les données JSON en objet
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // Crée les informations de la salle
        const roomInfo = {
            roomId: data.roomId,           // ID unique de la salle (ex: "ABC123")
            hostId: clientId,               // Qui a créé la salle
            roomName: data.roomName || `Room ${data.roomId}`,
            roomType: data.roomType || 0,
            playerCount: 1,                 // Le créateur est dedans
            maxPlayers: data.maxPlayers || 10,
            createdAt: Date.now()
        };

        // Ajoute la salle à notre liste
        rooms.set(data.roomId, roomInfo);

        // Le créateur est maintenant dans cette salle
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
        }

        console.log(`[SERVER] Room created: ${data.roomId} by ${clientId}`);

        // Met à jour la liste des salles pour tout le monde
        broadcastRoomList();

        // Informe tout le monde qu'une nouvelle salle existe
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

**Explication simple :**
- Un joueur demande à créer une salle
- On crée un objet avec toutes les infos de la salle
- On ajoute la salle à notre liste
- On dit à tout le monde "Une nouvelle salle est disponible !"

---

### Gestion des salles - Rejoindre une salle (lignes 295-331)

```javascript
function handleRoomJoin(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);

        // Vérifie que la salle existe
        if (!room) {
            sendError(clientId, `Room ${data.roomId} not found`);
            return;
        }

        // Vérifie que la salle n'est pas pleine
        if (room.playerCount >= room.maxPlayers) {
            sendError(clientId, 'Room is full');
            return;
        }

        // Met à jour les infos du client
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
            client.playerName = data.playerName || 'Player';
        }

        // Incrémente le nombre de joueurs
        room.playerCount++;

        console.log(`[SERVER] Player ${clientId} joined room ${data.roomId}`);

        // Préviens les autres joueurs de la salle
        broadcastToRoom(clientId, {
            type: 'room-join',
            senderId: clientId,
            data: JSON.stringify(data)
        });

        // Met à jour la liste des salles
        broadcastRoomList();

    } catch (e) {
        console.error(`[SERVER] handleRoomJoin error: ${e.message}`);
    }
}
```

**Explication simple :**
- Un joueur veut rejoindre une salle
- On vérifie que la salle existe et qu'elle n'est pas pleine
- On ajoute le joueur à la salle
- On prévient les autres joueurs de la salle "Hey, quelqu'un arrive !"

---

### Gestion des salles - Quitter une salle (lignes 334-361)

```javascript
function handleRoomLeave(clientId, dataStr) {
    try {
        // Convertit les données JSON en objet
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // Trouve la salle
        const room = rooms.get(data.roomId);

        // Décrémente le nombre de joueurs (minimum 0)
        if (room) {
            room.playerCount = Math.max(0, room.playerCount - 1);
        }

        // Enlève le joueur de la salle
        const client = clients.get(clientId);
        if (client) {
            client.roomId = null;  // Le joueur n'est plus dans aucune salle
        }

        console.log(`[SERVER] Player ${clientId} left room ${data.roomId}`);

        // Préviens les autres joueurs de la salle
        broadcastToRoom(clientId, {
            type: 'room-leave',
            senderId: clientId,
            data: JSON.stringify(data)
        });

        // Met à jour la liste des salles pour tout le monde
        broadcastRoomList();

    } catch (e) {
        console.error(`[SERVER] handleRoomLeave error: ${e.message}`);
    }
}
```

**Explication simple :**
- Un joueur veut quitter une salle
- On décrémente le compteur de joueurs de la salle
- On met `roomId = null` pour indiquer qu'il n'est plus dans une salle
- On prévient les autres "Hey, quelqu'un est parti !"

---

### Gestion des salles - Mettre à jour une salle (lignes 363-377)

```javascript
function handleRoomUpdate(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);

        // Seul l'hôte (créateur) peut modifier la salle
        if (room && room.hostId === clientId) {
            room.playerCount = data.playerCount || room.playerCount;
            room.roomName = data.roomName || room.roomName;
            broadcastRoomList();  // Informe tout le monde du changement
        }

    } catch (e) {
        console.error(`[SERVER] handleRoomUpdate error: ${e.message}`);
    }
}
```

**Explication simple :**
- Seul le créateur de la salle (l'hôte) peut la modifier
- On vérifie `room.hostId === clientId` pour s'assurer que c'est bien l'hôte
- On met à jour les infos et on prévient tout le monde

---

### Gestion de la déconnexion d'un joueur (lignes 379-421)

```javascript
function handleDisconnect(clientId) {
    const client = clients.get(clientId);

    if (client) {
        // Si le joueur était dans une salle...
        if (client.roomId) {
            const room = rooms.get(client.roomId);

            if (room) {
                // Si c'était l'HÔTE de la salle...
                if (room.hostId === clientId) {
                    // ...on ferme la salle entière !
                    rooms.delete(client.roomId);

                    // On prévient tout le monde que la salle est fermée
                    broadcast({
                        type: 'room-closed',
                        senderId: clientId,
                        data: JSON.stringify({ roomId: client.roomId })
                    });
                } else {
                    // Si c'était un simple joueur, on décrémente le compteur
                    room.playerCount = Math.max(0, room.playerCount - 1);
                }
            }

            // Préviens SEULEMENT les joueurs de la même salle
            broadcastToRoom(clientId, {
                type: 'room-leave',
                senderId: clientId,
                data: JSON.stringify({
                    roomId: client.roomId,
                    playerId: clientId
                })
            });
        }
    }

    // Supprime le client de notre liste
    clients.delete(clientId);

    // Préviens TOUT LE MONDE qu'un joueur s'est déconnecté
    broadcast({
        type: 'peer-disconnected',
        senderId: clientId
    });

    // Met à jour la liste des salles
    broadcastRoomList();

    console.log(`[SERVER] Client disconnected: ${clientId}`);
}
```

**Explication simple :**
- Quand un joueur se déconnecte (ferme le jeu, perd internet, etc.)
- Si c'était l'**hôte** d'une salle → la salle est **fermée** pour tout le monde
- Si c'était un simple joueur → on le retire de la salle
- On prévient tout le monde de son départ
- C'est comme quand quelqu'un quitte une réunion Zoom

---

### Tableau blanc - Synchronisation de l'état (lignes 427-458)

```javascript
function handleWhiteboardState(clientId, dataStr) {
    try {
        const stateData = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // Si on a un destinataire spécifique (targetId)...
        if (stateData.targetId) {
            // Trouve ce destinataire
            const targetClient = clients.get(stateData.targetId);

            // Envoie SEULEMENT à lui
            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                sendToClient(targetClient.ws, {
                    type: 'whiteboard-state',
                    senderId: clientId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });

                // Log pour le debug (affiche la taille en KB)
                const sizeKB = stateData.textureData ?
                    (stateData.textureData.length * 0.75 / 1024).toFixed(2) : '0';

                console.log(`[Whiteboard] State sent ${clientId} → ${stateData.targetId} (${sizeKB} KB)`);
            }
        } else {
            // Sinon, envoie à toute la salle
            broadcastToRoom(clientId, {
                type: 'whiteboard-state',
                senderId: clientId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }

    } catch (e) {
        console.error(`[Whiteboard] handleWhiteboardState error: ${e.message}`);
    }
}
```

**Explication simple :**
- Quand un nouveau joueur rejoint, il demande "C'est quoi l'état actuel du tableau blanc ?"
- Un autre joueur lui répond avec l'image actuelle du tableau
- `targetId` = le joueur qui a demandé (on lui envoie directement, pas à tout le monde)
- C'est comme quand tu arrives en retard à une réunion et que quelqu'un te montre ce qui a été dessiné

---

### WebRTC - Signaling pour le chat vocal (lignes 464-521)

Le chat vocal utilise **WebRTC** (une technologie peer-to-peer). Le serveur ne transporte PAS la voix, il fait juste les présentations entre joueurs.

```javascript
// === ÉTAPE 1 : L'OFFRE ===
// Joueur A dit "Hey, je veux te parler" à Joueur B
function handleWebRTCOffer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;  // targetId = à qui je veux parler, sdp = infos techniques

        // Trouve le destinataire
        const targetClient = clients.get(targetId);
        if (!targetClient) return;  // Il n'existe pas ? On abandonne.

        // Transmet l'offre au destinataire
        sendToClient(targetClient.ws, {
            type: 'webrtc-offer',
            senderId: senderId,      // De la part de qui ?
            data: JSON.stringify({ sdp })
        });

        console.log(`[WebRTC] Offer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[WebRTC] handleWebRTCOffer error: ${e.message}`);
    }
}

// === ÉTAPE 2 : LA RÉPONSE ===
// Joueur B répond "OK, voici mes infos pour qu'on se connecte"
function handleWebRTCAnswer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) return;

        // Transmet la réponse
        sendToClient(targetClient.ws, {
            type: 'webrtc-answer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });

        console.log(`[WebRTC] Answer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[WebRTC] handleWebRTCAnswer error: ${e.message}`);
    }
}

// === ÉTAPE 3 : LES CANDIDATS ICE ===
// Échange d'infos réseau pour trouver le meilleur chemin
function handleWebRTCIceCandidate(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, candidate, sdpMid, sdpMLineIndex } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) return;

        // Transmet le candidat ICE
        sendToClient(targetClient.ws, {
            type: 'webrtc-ice-candidate',
            senderId: senderId,
            data: JSON.stringify({ candidate, sdpMid, sdpMLineIndex })
        });

    } catch (e) {
        console.error(`[WebRTC] handleWebRTCIceCandidate error: ${e.message}`);
    }
}
```

**Explication simple :**
- **WebRTC** = Technologie pour la voix/vidéo en direct (comme Zoom, Discord)
- Le serveur fait juste les **présentations** (signaling)
- Ensuite, les joueurs se parlent **directement** entre eux (peer-to-peer)
- C'est comme si le serveur était un entremetteur qui dit "Joueur A, voici le numéro de Joueur B"

**Le processus en 3 étapes :**
```
Joueur A                    Serveur                    Joueur B
    │                          │                           │
    │  "Je veux parler à B"    │                           │
    │  (webrtc-offer + SDP)    │                           │
    │ ────────────────────────>│                           │
    │                          │  "A veut te parler"       │
    │                          │ ─────────────────────────>│
    │                          │                           │
    │                          │  "OK, voici mes infos"    │
    │                          │<───────────────────────── │
    │  "B accepte, voici ses   │  (webrtc-answer + SDP)    │
    │   infos"                 │                           │
    │<─────────────────────────│                           │
    │                          │                           │
    │  (Échange de candidats ICE pour trouver le chemin)   │
    │<═════════════════════════════════════════════════════>│
    │                          │                           │
    │        CONNEXION DIRECTE (voix en peer-to-peer)      │
    │<═════════════════════════════════════════════════════>│
```

---

### Partage d'écran - Signaling WebRTC (lignes 527-590)

```javascript
// Même principe que le chat vocal, mais pour le flux vidéo de l'écran
function handleScreenVideoOffer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) {
            console.log(`[ScreenVideo] Target ${targetId} not found for offer`);
            return;
        }

        sendToClient(targetClient.ws, {
            type: 'screen-video-offer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });

        console.log(`[ScreenVideo] Offer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[ScreenVideo] handleScreenVideoOffer error: ${e.message}`);
    }
}

function handleScreenVideoAnswer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) {
            console.log(`[ScreenVideo] Target ${targetId} not found for answer`);
            return;
        }

        sendToClient(targetClient.ws, {
            type: 'screen-video-answer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });

        console.log(`[ScreenVideo] Answer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[ScreenVideo] handleScreenVideoAnswer error: ${e.message}`);
    }
}

function handleScreenVideoIce(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, candidate, sdpMid, sdpMLineIndex } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) return;

        sendToClient(targetClient.ws, {
            type: 'screen-video-ice',
            senderId: senderId,
            data: JSON.stringify({ candidate, sdpMid, sdpMLineIndex })
        });

    } catch (e) {
        console.error(`[ScreenVideo] handleScreenVideoIce error: ${e.message}`);
    }
}
```

**Explication simple :**
- C'est exactement comme le chat vocal, mais pour partager son écran
- Le présentateur envoie sa vidéo d'écran aux autres joueurs
- Même processus : Offre → Réponse → Candidats ICE → Connexion directe

---

### Partage de fichiers - Réponse à la liste (lignes 596-624)

```javascript
function handleFileListResponse(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // Si on a un destinataire spécifique...
        if (data.targetId) {
            const targetClient = clients.get(data.targetId);

            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                // Envoie la liste SEULEMENT à celui qui l'a demandée
                sendToClient(targetClient.ws, {
                    type: 'file-list-response',
                    senderId: senderId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });

                console.log(`[FileShare] List response: ${senderId} → ${data.targetId}`);
            }
        } else {
            // Sinon, envoie à toute la salle
            broadcastToRoom(senderId, {
                type: 'file-list-response',
                senderId: senderId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }

    } catch (e) {
        console.error(`[FileShare] handleFileListResponse error: ${e.message}`);
    }
}
```

**Explication simple :**
- Quand un nouveau joueur arrive, il demande "Quels fichiers ont été partagés ?"
- Un autre joueur lui répond avec la liste des fichiers
- Comme pour le tableau blanc, on envoie directement à celui qui a demandé

---

### Authentification - Inscription (lignes 630-662)

```javascript
async function handleAuthRegister(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { username, email, password, displayName } = data;

        // Vérifie que tous les champs obligatoires sont présents
        if (!username || !email || !password) {
            sendAuthResponse(clientId, 'auth-register-response', {
                success: false,
                error: 'Missing required fields'  // "Champs obligatoires manquants"
            });
            return;
        }

        // Appelle la fonction d'inscription (dans auth.js)
        // Elle hash le mot de passe et l'enregistre dans la base de données
        const result = await registerUser(username, email, password, displayName);

        // Envoie la réponse au client
        sendAuthResponse(clientId, 'auth-register-response', result);

        // Si l'inscription a réussi, on met à jour les infos du client
        if (result.success) {
            const client = clients.get(clientId);
            if (client) {
                client.userId = result.userId;
                client.playerName = result.displayName;
            }
        }

    } catch (e) {
        console.error('[Auth] handleAuthRegister error:', e.message);
        sendAuthResponse(clientId, 'auth-register-response', {
            success: false,
            error: 'Server error'
        });
    }
}
```

**Explication simple :**
- Un joueur veut créer un compte
- On vérifie qu'il a donné un nom d'utilisateur, email et mot de passe
- On enregistre dans la base de données (le mot de passe est "hashé" = crypté)
- On répond "OK ça a marché" ou "Erreur : ce nom existe déjà"

---

### Authentification - Connexion (lignes 664-696)

```javascript
async function handleAuthLogin(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { username, password } = data;

        // Vérifie que les identifiants sont fournis
        if (!username || !password) {
            sendAuthResponse(clientId, 'auth-login-response', {
                success: false,
                error: 'Missing credentials'  // "Identifiants manquants"
            });
            return;
        }

        // Appelle la fonction de connexion (dans auth.js)
        // Elle vérifie le mot de passe dans la base de données
        const result = await loginUser(username, password);

        // Envoie la réponse
        sendAuthResponse(clientId, 'auth-login-response', result);

        // Si la connexion a réussi...
        if (result.success) {
            const client = clients.get(clientId);
            if (client) {
                client.userId = result.userId;       // On note son ID utilisateur
                client.playerName = result.displayName;  // Et son nom d'affichage
            }
        }

    } catch (e) {
        console.error('[Auth] handleAuthLogin error:', e.message);
        sendAuthResponse(clientId, 'auth-login-response', {
            success: false,
            error: 'Server error'
        });
    }
}
```

**Explication simple :**
- Un joueur veut se connecter avec son compte
- On vérifie son nom d'utilisateur et mot de passe dans la base de données
- Si c'est bon → on lui dit "Bienvenue !" avec ses infos (nom, couleur d'avatar, etc.)
- Si c'est faux → on lui dit "Mot de passe incorrect"

---

### Authentification - Mise à jour du profil (lignes 698-727)

```javascript
async function handleAuthUpdateProfile(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { displayName, avatarColor } = data;

        const client = clients.get(clientId);

        // Le joueur doit être connecté (avoir un userId)
        if (!client || !client.userId) {
            sendAuthResponse(clientId, 'auth-update-response', {
                success: false,
                error: 'Not authenticated'  // "Non connecté"
            });
            return;
        }

        // Met à jour le profil dans la base de données
        const result = await updateUserProfile(client.userId, displayName, avatarColor);

        // Si ça a marché, on met aussi à jour côté serveur
        if (result.success && displayName) {
            client.playerName = displayName;
        }

        sendAuthResponse(clientId, 'auth-update-response', result);

    } catch (e) {
        console.error('[Auth] handleAuthUpdateProfile error:', e.message);
        sendAuthResponse(clientId, 'auth-update-response', {
            success: false,
            error: 'Server error'
        });
    }
}
```

**Explication simple :**
- Un joueur connecté veut changer son nom ou sa couleur d'avatar
- On vérifie qu'il est bien connecté (`client.userId` existe)
- On met à jour dans la base de données
- Maintenant son nouveau nom apparaîtra pour tout le monde

---

### Fonction utilitaire - Envoyer une réponse d'authentification (lignes 729-738)

```javascript
function sendAuthResponse(clientId, type, data) {
    const client = clients.get(clientId);
    if (client) {
        sendToClient(client.ws, {
            type: type,                    // Ex: 'auth-login-response'
            senderId: 'server',            // C'est le serveur qui répond
            data: JSON.stringify(data)     // Les données (success, userId, etc.)
        });
    }
}
```

**Explication simple :**
- Fonction helper pour répondre aux demandes d'authentification
- `senderId: 'server'` indique que c'est le serveur qui parle, pas un autre joueur

---

### Fonctions utilitaires - Envoi de messages (lignes 744-758)

```javascript
// Envoie un message à UN SEUL client
function sendToClient(ws, message) {
    // Vérifie que la connexion existe et est ouverte
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));  // Convertit en JSON et envoie
    }
}

// Envoie un message à TOUS les clients (sauf un optionnel)
function broadcast(message, exceptClientId = null) {
    const messageStr = JSON.stringify(message);  // Convertit une seule fois (optimisation)

    // Parcourt tous les clients
    clients.forEach((client, clientId) => {
        // Envoie si :
        // - Ce n'est pas le client exclu
        // - La connexion est ouverte
        if (clientId !== exceptClientId && client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}
```

**Explication simple :**
- `sendToClient` = Envoie à UNE personne (comme un SMS)
- `broadcast` = Envoie à TOUT LE MONDE (comme un message sur un groupe WhatsApp)
- `exceptClientId` = Parfois on ne veut pas envoyer à quelqu'un (par exemple, ne pas renvoyer son propre message à l'expéditeur)

---

### Fonctions utilitaires - Liste des salles (lignes 793-824)

```javascript
// Envoie la liste des salles à UN client
function sendRoomList(ws) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;

    // Convertit notre Map en Array
    const roomList = Array.from(rooms.values());

    sendToClient(ws, {
        type: 'room-list',
        senderId: 'server',
        data: JSON.stringify({ rooms: roomList })
    });
}

// Envoie la liste des salles à TOUS les clients
function broadcastRoomList() {
    const roomList = Array.from(rooms.values());

    broadcast({
        type: 'room-list',
        senderId: 'server',
        data: JSON.stringify({ rooms: roomList })
    });
}

// Envoie un message d'erreur à un client
function sendError(clientId, errorMessage) {
    const client = clients.get(clientId);
    if (client) {
        sendToClient(client.ws, {
            type: 'error',
            senderId: 'server',
            data: errorMessage
        });
    }
}
```

**Explication simple :**
- `sendRoomList` = Donne la liste des salles à un nouveau joueur
- `broadcastRoomList` = Met à jour la liste pour tout le monde (quand une salle est créée/supprimée)
- `sendError` = Envoie un message d'erreur (ex: "Salle pleine", "Salle non trouvée")

---

### LA FONCTION CRITIQUE : broadcastToRoom (lignes 763-791)

```javascript
/**
 * Envoie un message SEULEMENT aux clients de la même salle
 * C'est LA fonction la plus importante pour la performance !
 */
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);

    // Si l'expéditeur n'est pas dans une salle, ne fait rien
    if (!sender || !sender.roomId) {
        return;
    }

    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);

    let recipientCount = 0;

    // Parcourt TOUS les clients
    clients.forEach((client, clientId) => {
        // Envoie SEULEMENT si :
        // 1. Ce n'est pas l'expéditeur lui-même
        // 2. Le client est dans la MÊME salle
        // 3. La connexion est ouverte
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {

            client.ws.send(messageStr);
            recipientCount++;
        }
    });

    // Log pour le debug
    if (message.type && (message.type.includes('whiteboard') || message.type.includes('obj-'))) {
        console.log(`[Room:${roomId}] ${message.type} from ${senderId} → ${recipientCount} clients`);
    }
}
```

**Explication simple :**
- Cette fonction est SUPER importante !
- Elle envoie un message UNIQUEMENT aux joueurs de la même salle
- Ça évite d'envoyer la position de tout le monde à tout le monde
- Imagine 100 joueurs dans 10 salles : sans ça, chaque joueur recevrait les positions de 99 autres joueurs au lieu de seulement 9 !

---

### Heartbeat - Vérification des connexions (lignes 830-847)

```javascript
// Toutes les 30 secondes, vérifie que les clients sont toujours là
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    // Envoie un "ping" à tous les clients
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();  // "T'es toujours là ?"
        }
    });

    // Vérifie si des clients n'ont pas répondu depuis longtemps
    clients.forEach((client, clientId) => {
        // Si pas de réponse depuis 60 secondes (2 x 30s)...
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            console.log(`[SERVER] Client timeout: ${clientId}`);
            client.ws.terminate();      // Ferme la connexion
            handleDisconnect(clientId); // Nettoie
        }
    });

}, HEARTBEAT_INTERVAL);  // Toutes les 30 secondes
```

**Explication simple :**
- Le serveur dit "Ping !" toutes les 30 secondes
- Les clients répondent "Pong !"
- Si un client ne répond pas pendant 60 secondes, on le considère déconnecté
- C'est comme appeler "T'es encore là ?" régulièrement

---

## 5. db.js - La Base de Données

**Chemin :** `LocalServ/Server/db.js`

Ce fichier gère la connexion à la base de données MariaDB pour stocker les utilisateurs.

```javascript
const mysql = require('mysql2/promise');  // Bibliothèque pour MySQL/MariaDB

// Crée un "pool" de connexions (plusieurs connexions réutilisables)
const pool = mysql.createPool({
    host: process.env.DB_HOST || 'localhost',        // Adresse du serveur de base de données
    port: process.env.DB_PORT || 3306,               // Port (3306 par défaut pour MySQL)
    user: process.env.DB_USER || 'root',             // Nom d'utilisateur
    password: process.env.DB_PASSWORD || 'JJkk2812', // Mot de passe
    database: process.env.DB_NAME || 'vr_meeting',   // Nom de la base de données
    waitForConnections: true,   // Attendre si toutes les connexions sont utilisées
    connectionLimit: 10,        // Maximum 10 connexions en même temps
    queueLimit: 0               // Pas de limite de file d'attente
});

// Teste la connexion au démarrage
pool.getConnection()
    .then(conn => {
        console.log('[DB] Connected to MariaDB');
        conn.release();  // Libère la connexion
    })
    .catch(err => {
        console.error('[DB] Connection failed:', err.message);
    });

module.exports = pool;  // Exporte le pool pour que d'autres fichiers puissent l'utiliser
```

**Explication simple :**
- Ce fichier crée une connexion à la base de données
- Un "pool" c'est comme avoir plusieurs lignes téléphoniques : on peut faire plusieurs appels en même temps
- Les variables d'environnement (`process.env.XXX`) permettent de changer les paramètres sans modifier le code

**IMPORTANT pour serveur distant :**
```bash
# Configure ces variables AVANT de lancer le serveur :
export DB_HOST=ton-serveur-db.com
export DB_PORT=3306
export DB_USER=ton_utilisateur
export DB_PASSWORD=ton_mot_de_passe_secret
export DB_NAME=vr_meeting

node server.js
```

---

## 6. Comment tout fonctionne ensemble

### Schéma du flux complet

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            CONNEXION INITIALE                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Unity démarre                                                              │
│       │                                                                     │
│       ▼                                                                     │
│  VRNetworkManager.Start()                                                   │
│       │                                                                     │
│       ▼                                                                     │
│  Connect() ──── WebSocket ────────────────────────► Serveur                 │
│       │         ws://localhost:8080                    │                    │
│       │                                                │                    │
│       │                                                ▼                    │
│       │                                          Génère un ID unique        │
│       │                                          (ex: "abc-123-...")        │
│       │                                                │                    │
│       │         ◄────── message "welcome" ─────────────┘                    │
│       │         {type:"welcome", senderId:"abc-123-..."}                    │
│       ▼                                                                     │
│  LocalId = "abc-123-..."                                                    │
│  IsConnected = true                                                         │
│  OnConnected?.Invoke()  ──────► Autres scripts prévenus                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                            REJOINDRE UNE SALLE                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Joueur clique "Rejoindre salle ABC123"                                     │
│       │                                                                     │
│       ▼                                                                     │
│  VRRoomManager.JoinRoom("ABC123")                                           │
│       │                                                                     │
│       ▼                                                                     │
│  VRNetworkManager.Send("room-join", {roomId:"ABC123", playerName:"Jean"})   │
│       │                                                                     │
│       │         ──── message ────────────────────────► Serveur              │
│       │                                                    │                │
│       │                                                    ▼                │
│       │                                          handleRoomJoin()           │
│       │                                          - Vérifie salle existe     │
│       │                                          - Vérifie pas pleine       │
│       │                                          - Ajoute joueur            │
│       │                                                    │                │
│       │                                                    ▼                │
│       │                                          broadcastToRoom()          │
│       │                                          (vers autres de la salle)  │
│       │                                                    │                │
│       │         ◄──── message "room-join" ─────────────────┘                │
│       ▼                                                                     │
│  OnPlayerJoined?.Invoke(data)  ──────► VRGameManager spawn un avatar        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                          SYNCHRONISATION EN TEMPS RÉEL                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  VRGameManager.Update() (30 fois par seconde)                               │
│       │                                                                     │
│       ▼                                                                     │
│  Si position/rotation a changé :                                            │
│       │                                                                     │
│       ▼                                                                     │
│  VRNetworkManager.Send("vr-position", {                                     │
│      roomId: "ABC123",                                                      │
│      posX: 1.5, posY: 0, posZ: 3.2,                                        │
│      headPosX: 1.5, headPosY: 1.7, headPosZ: 3.2,                          │
│      headRotX: 0, headRotY: 0.7, headRotZ: 0, headRotW: 0.7,               │
│      ... (mains aussi)                                                      │
│  })                                                                         │
│       │                                                                     │
│       │         ──── message ────────────────────────► Serveur              │
│       │                                                    │                │
│       │                                                    ▼                │
│       │                                          broadcastToRoom()          │
│       │                                          (SEULEMENT salle ABC123)   │
│       │                                                    │                │
│       │                                          ┌─────────┴─────────┐      │
│       │                                          ▼                   ▼      │
│       │         ◄──── message ──────────────── Joueur2            Joueur3   │
│       │                                                                     │
│       ▼                                                                     │
│  OnMessageReceived  ──────► VRGameManager.HandlePositionMessage()           │
│                             - Trouve l'avatar du joueur                     │
│                             - Met à jour sa position                        │
│                             - Interpole pour fluidité                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 7. Pour passer en production

### Checklist pour déployer sur un serveur distant

#### 1. Serveur Node.js

```bash
# Sur ton serveur (VPS, AWS EC2, etc.)

# 1. Installe Node.js
curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -
sudo apt-get install -y nodejs

# 2. Copie tes fichiers
scp -r LocalServ/Server/* user@ton-serveur:/home/user/vr-server/

# 3. Installe les dépendances
cd /home/user/vr-server
npm install

# 4. Configure les variables d'environnement
export PORT=8080
export DB_HOST=localhost
export DB_USER=vr_user
export DB_PASSWORD=mot_de_passe_securise
export DB_NAME=vr_meeting

# 5. Lance le serveur (avec pm2 pour qu'il reste actif)
npm install -g pm2
pm2 start server.js --name vr-server
pm2 save
pm2 startup
```

#### 2. Base de données MariaDB

```bash
# Installe MariaDB
sudo apt install mariadb-server

# Configure
sudo mysql_secure_installation

# Crée la base de données
sudo mysql -u root -p
```

```sql
CREATE DATABASE vr_meeting;
CREATE USER 'vr_user'@'localhost' IDENTIFIED BY 'mot_de_passe_securise';
GRANT ALL PRIVILEGES ON vr_meeting.* TO 'vr_user'@'localhost';
FLUSH PRIVILEGES;

-- Crée la table users
USE vr_meeting;
CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(100),
    avatar_color VARCHAR(20) DEFAULT '#3498db',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP NULL
);
```

#### 3. Firewall

```bash
# Ouvre le port 8080
sudo ufw allow 8080/tcp
```

#### 4. (Optionnel mais RECOMMANDÉ) HTTPS avec nginx

```bash
# Installe nginx et certbot
sudo apt install nginx certbot python3-certbot-nginx

# Configure nginx
sudo nano /etc/nginx/sites-available/vr-server
```

```nginx
server {
    listen 80;
    server_name ton-domaine.com;

    location / {
        proxy_pass http://localhost:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

```bash
# Active le site
sudo ln -s /etc/nginx/sites-available/vr-server /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx

# Ajoute HTTPS (gratuit avec Let's Encrypt)
sudo certbot --nginx -d ton-domaine.com
```

#### 5. Côté Unity

Dans `VRNetworkManager.cs`, change l'URL :

```csharp
// AVANT (développement local)
public string serverUrl = "ws://localhost:8080";

// APRÈS (production avec HTTPS)
public string serverUrl = "wss://ton-domaine.com";

// OU sans HTTPS (moins sécurisé mais plus simple)
public string serverUrl = "ws://123.456.789.012:8080";  // IP du serveur
```

---

## Résumé

| Composant | Fichier | Rôle | À modifier pour production |
|-----------|---------|------|---------------------------|
| Client Unity | `VRNetworkManager.cs` | Se connecte au serveur, envoie/reçoit messages | `serverUrl` |
| Serveur | `server.js` | Reçoit et redistribue les messages | `PORT` (variable d'env) |
| Base de données | `db.js` | Stocke les utilisateurs | `DB_HOST`, `DB_USER`, `DB_PASSWORD` |

**Les 3 choses à retenir :**

1. **WebSocket** = Un tuyau qui reste ouvert pour communiquer instantanément
2. **broadcastToRoom** = Envoie les messages SEULEMENT aux joueurs de la même salle (très important pour la performance !)
3. **Exponential Backoff** = Si la connexion échoue, on attend de plus en plus longtemps avant de réessayer

---

*Document généré pour le projet WebSocket_VR*
