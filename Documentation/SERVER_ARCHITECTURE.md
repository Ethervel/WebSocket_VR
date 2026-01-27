# Architecture Serveur WebSocket - VR Meeting Rooms

Description technique du serveur WebSocket et de son integration avec le client Unity.

---

## Vue d'Ensemble

### Diagramme de Flux

```
+------------------+          WebSocket (ws/wss)         +------------------+
|                  | <----------------------------------> |                  |
|   Unity Client   |     Messages JSON bidirectionnels   |   Node.js Server |
|  (VRNetworkMgr)  |                                     |   (server.js)    |
|                  | <----------------------------------> |                  |
+------------------+                                     +------------------+
        |                                                        |
        v                                                        v
+------------------+                                     +------------------+
| VRRoomManager    |                                     | Modules          |
| VRGameManager    |                                     | (non connectes)  |
| VoiceChatManager |                                     | auth.js, db.js   |
+------------------+                                     +------------------+
```

### Stack Technique

| Composant | Technologie | Version | Fichier |
|-----------|-------------|---------|---------|
| Serveur WebSocket | Node.js + ws | 8.14.2 | `server.js` |
| Client WebSocket | NativeWebSocket | Unity Package | `VRNetworkManager.cs` |
| Voix (WebRTC) | Unity.WebRTC | 3.0.0 | `VoiceChatManager.cs` |
| Authentification | bcrypt + MariaDB | - | `auth.js` (NON CONNECTE) |

---

## Structure du Serveur

### Fichiers

```
Server/
├── server.js           # Serveur principal (888 lignes)
├── package.json        # Dependances: ws, uuid, pdf-poppler
├── auth.js             # Authentification (NON CONNECTE)
├── db.js               # Pool MariaDB (NON CONNECTE)
└── filePresentation.js # Conversion PDF (optionnel)
```

### Composants server.js

| Composant | Lignes | Description |
|-----------|--------|-------------|
| Configuration | 1-27 | Imports, constantes, state global |
| Connection handling | 41-96 | Welcome, peer events, handlers |
| Message routing | 100-228 | Switch principal, dispatch |
| Room management | 232-416 | Create, join, leave, close, kick |
| Whiteboard | 463-487 | State sync point-to-point |
| WebRTC signaling | 491-605 | Voice + screen share |
| File sharing | 609-661 | List response, present state |
| PDF conversion | 665-748 | Cache, page requests |
| Utilities | 752-833 | sendToClient, broadcast, broadcastToRoom |
| Maintenance | 838-887 | Heartbeat, cleanup, shutdown |

### State Global

```javascript
const clients = new Map();  // clientId -> { ws, roomId, playerName, lastHeartbeat }
const rooms = new Map();    // roomId -> RoomInfo
const pdfCache = new Map(); // fileId -> { pages, totalPages, timestamp }
```

### Configuration

| Constante | Valeur | Description |
|-----------|--------|-------------|
| `PORT` | 8080 | Port d'ecoute (env configurable) |
| `HEARTBEAT_INTERVAL` | 30000 ms | Intervalle ping |
| `PDF_CACHE_TTL` | 30 min | Duree cache PDF |

---

## Gestion des Connexions

### Flux de Connexion

```
Client                              Serveur
   |                                   |
   |-------- WebSocket Connect ------->|
   |                                   | Genere UUID
   |                                   | Stocke dans clients Map
   |<------- welcome {senderId} -------|
   |                                   |
   |                                   |--- peer-connected (broadcast) --->
   |<------- room-list ----------------|
   |                                   |
```

### Structure Client (Map)

```javascript
{
    ws: WebSocket,           // Instance socket
    roomId: null | string,   // Room actuelle
    playerName: 'Player',    // Nom affiche
    lastHeartbeat: Date.now() // Timestamp dernier pong
}
```

### Structure Room (Map)

```javascript
{
    roomId: string,          // Code 6 caracteres
    hostId: string,          // UUID du createur
    roomName: string,        // Nom affiche
    roomType: number,        // 0=Lobby, 1=RoomA, 2=RoomB
    playerCount: number,     // Joueurs actuels
    maxPlayers: number,      // Limite (defaut: 10)
    createdAt: number        // Timestamp creation
}
```

---

## Routage des Messages

### Switch Principal

| Categorie | Types | Handler |
|-----------|-------|---------|
| Room Lifecycle | `room-available`, `room-closed`, `room-join`, `room-leave`, `room-update` | Fonctions dediees |
| Position VR | `vr-position`, `position` | `broadcastToRoom()` |
| Objets | `obj-sync`, `obj-state` | `broadcastToRoom()` |
| Whiteboard | `whiteboard-draw`, `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request` | `broadcastToRoom()` |
| Whiteboard State | `whiteboard-state` | `handleWhiteboardState()` |
| Room State | `room-welcome`, `room-teleport`, `player-name-update` | `broadcastToRoom()` |
| Admin | `kick-player` | `handleKickPlayer()` |
| WebRTC Voice | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Point-to-point |
| Screen Share | `screen-share-*`, `screen-video-*` | Mixte |
| File Share | `file-announce`, `file-chunk`, `file-complete`, `file-request` | `broadcastToRoom()` |
| PDF | `pdf-convert-request`, `pdf-page-request` | Fonctions dediees |
| Default | Autres | Room si dans room, sinon global |

### Fonction broadcastToRoom

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

---

## Protocole de Messages

### Format Standard

```json
{
    "type": "message-type",
    "senderId": "uuid-client",
    "data": "{\"json\":\"serialise\"}"
}
```

### Messages Connexion

| Type | Direction | Contenu |
|------|-----------|---------|
| `welcome` | Serveur -> Client | senderId = ID assigne |
| `peer-connected` | Serveur -> All | senderId = nouveau peer |
| `peer-disconnected` | Serveur -> All | senderId = peer parti |

### Messages Room

| Type | Direction | Data |
|------|-----------|------|
| `room-available` | Client -> Serveur | `{roomId, roomName, roomType, maxPlayers}` |
| `room-join` | Client -> Serveur | `{roomId, playerId, playerName, colorR/G/B}` |
| `room-welcome` | Host -> Room | `{roomId, roomType, players: [...]}` |
| `room-leave` | Client -> Room | `{roomId, playerId}` |
| `room-list` | Serveur -> Client | `{rooms: [RoomInfo...]}` |
| `room-closed` | Host -> All | `{roomId}` |
| `kick-player` | Host -> Target | `{roomId, playerId, reason}` |

### Messages Position VR (30Hz)

```json
{
    "roomId": "ABC123",
    "roomType": 1,
    "posX": 1.234, "posY": 0.0, "posZ": -5.678,
    "rotY": 45.0,
    "headPosX": 1.234, "headPosY": 1.7, "headPosZ": -5.678,
    "headRotX": 0.0, "headRotY": 0.707, "headRotZ": 0.0, "headRotW": 0.707,
    "leftHandPosX": ..., "leftHandRotX": ...,
    "rightHandPosX": ..., "rightHandRotX": ...
}
```

### Messages WebRTC (Point-to-Point)

| Type | Data |
|------|------|
| `webrtc-offer` | `{targetId, sdp}` |
| `webrtc-answer` | `{targetId, sdp}` |
| `webrtc-ice-candidate` | `{targetId, candidate, sdpMid, sdpMLineIndex}` |

### Messages Whiteboard

| Type | Data |
|------|------|
| `whiteboard-batch` | `{whiteboardId, roomId, r/g/b/a, penSize, pointsFlat: [u,v,...]}` |
| `whiteboard-clear` | `{whiteboardId, roomId}` |
| `whiteboard-request` | `{whiteboardId, roomId}` |
| `whiteboard-state` | `{targetId, textureData (base64 PNG)}` |

### Messages Screen Share

| Type | Data |
|------|------|
| `screen-share-start` | `{sharerId, sharerName}` |
| `screen-share-frame` | `{imageData (base64 JPEG)}` |
| `screen-share-stop` | `{sharerId}` |

---

## Flux de Donnees

### Rejoindre une Room

```
Joueur A (Host)                 Serveur                    Joueur B
     |                             |                           |
     |-- room-available ---------->|                           |
     |                             |--- room-list (broadcast)->|
     |                             |                           |
     |                             |<----- room-join ----------|
     |                             |                           |
     |<--- room-join --------------|--- room-join ------------>|
     |                             |                           |
     |--- room-welcome (players)-->|                           |
     |                             |--- room-welcome --------->|
     |                             |                           |
     |<======= vr-position (30Hz bidirectionnel) =============>|
```

### Kick Player

```
Host                            Serveur                    Target
  |                                |                          |
  |-- kick-player {playerId} ----->|                          |
  |                                | Verifie hostId           |
  |                                |--- kick-player --------->|
  |                                | Update room.playerCount  |
  |<-- room-leave -----------------|--- room-leave (autres)-->|
  |                                |--- room-list (broadcast) |
```

---

## Heartbeat et Timeout

### Mecanisme

```javascript
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    // Ping tous les clients
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });

    // Timeout clients morts (60s sans pong)
    clients.forEach((client, clientId) => {
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
}, HEARTBEAT_INTERVAL);
```

### Timing

| Evenement | Delai |
|-----------|-------|
| Ping serveur | Toutes les 30s |
| Pong client | Automatique (WebSocket) |
| Timeout | 60s sans pong |

---

## Deconnexion

### Flux handleDisconnect

1. Recuperer le client de la Map
2. Si dans une room :
   - Si host : supprimer la room, broadcast `room-closed`
   - Sinon : decrementer `playerCount`
   - Broadcast `room-leave` a la room
3. Supprimer de `clients` Map
4. Broadcast `peer-disconnected` global
5. Broadcast `room-list` mis a jour

---

## Securite Implementee

| Aspect | Implementation |
|--------|----------------|
| ID force | `message.senderId = clientId` dans handleMessage |
| Isolation rooms | `broadcastToRoom` filtre par roomId |
| Kick authority | Verification `room.hostId === clientId` |
| Timeout | Deconnexion automatique apres 60s |

---

## Logs Serveur

### Format

```
[Connect] Client a1b2c3d4...
[Room] Created: XYZ789
[Room] Join: e5f6g7h8 -> XYZ789
[Room] Leave: e5f6g7h8 <- XYZ789
[Room] Closed: XYZ789
[Kick] Host a1b2c3d4 kicked e5f6g7h8 from XYZ789
[Timeout] Client a1b2c3d4...
[Disconnect] Client a1b2c3d4...
[Status] 3 clients | 2 rooms
[Error] handleRoomJoin: ...
```

### Periodicite

| Log | Frequence |
|-----|-----------|
| Status | 60 secondes |
| Connect/Disconnect | Temps reel |
| Room events | Temps reel |
| Errors | Temps reel |

---

## Graceful Shutdown

```javascript
process.on('SIGINT', () => {
    console.log('\n[Server] Shutting down...');
    clearInterval(heartbeatInterval);

    wss.clients.forEach((ws) => {
        ws.close();
    });

    wss.close(() => {
        console.log('[Server] Goodbye!');
        process.exit(0);
    });
});
```

---

## Modules Non Connectes

### auth.js

| Fonction | Parametres | Retour |
|----------|------------|--------|
| `registerUser` | username, email, password, displayName | `{success, userId, error}` |
| `loginUser` | username, password | `{success, userId, username, email, displayName, avatarColor, error}` |
| `updateUserProfile` | userId, displayName, avatarColor | `{success, error}` |

### db.js

| Config | Valeur |
|--------|--------|
| Host | localhost |
| Port | 3306 |
| Database | vr_meeting |
| Pool size | 10 connexions |

---

## References

- [GUIDE_DEPLOIEMENT_ENTREPRISE.md](./GUIDE_DEPLOIEMENT_ENTREPRISE.md) - Etat actuel du deploiement
- [NETWORKING_CODE_EXPLAINED.md](./NETWORKING_CODE_EXPLAINED.md) - Code annote ligne par ligne
