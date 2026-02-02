# Architecture Serveur WebSocket - VR Meeting Rooms

Description technique du serveur WebSocket et de son integration avec le client Unity.

> **Derniere mise a jour : 2026-02-02** - Synchronise avec le code source actuel.

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
| VRRoomManager    |                                     | filePresentation |
| VRGameManager    |                                     |   .js (PDF)      |
| VoiceChatManager |                                     | (optionnel)      |
+------------------+                                     +------------------+
```

### Stack Technique

| Composant | Technologie | Version | Fichier |
|-----------|-------------|---------|---------|
| Serveur WebSocket | Node.js + ws | 8.14.2 | `server.js` (887 lignes) |
| Client WebSocket | NativeWebSocket | Unity Package | `VRNetworkManager.cs` (460 lignes) |
| Voix (WebRTC) | Unity.WebRTC | 3.0.0 | `VoiceChatManager.cs` (1139 lignes) |
| PDF (optionnel) | pdf-poppler | 0.2.3 | `filePresentation.js` (257 lignes) |

---

## Structure du Serveur

### Fichiers

```
Server/
├── server.js           # Serveur principal (887 lignes)
├── filePresentation.js # Conversion PDF (257 lignes, optionnel)
├── package.json        # Dependances: ws, uuid, pdf-poppler
└── node_modules/       # Dependances installees
```

### Composants server.js

| Composant | Lignes (approx.) | Description |
|-----------|-------------------|-------------|
| Configuration | 1-27 | Imports, constantes, state global |
| Connection handling | 41-96 | Welcome, peer events, handlers |
| Message routing | 100-228 | Switch principal, dispatch (46 types) |
| Room management | 232-416 | Create, join, leave, close, update, kick |
| Whiteboard | 463-487 | State sync point-to-point |
| WebRTC signaling | 491-605 | Voice (3 types) + screen share (3 types) |
| File sharing | 609-661 | List response, present state |
| PDF conversion | 665-748 | Cache, page requests, filePresentation |
| Utilities | 752-833 | sendToClient, broadcast, broadcastToRoom, sendError |
| Maintenance | 838-887 | Heartbeat (30s), cache cleanup (5m), status log (60s), shutdown |

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
| Max players/room | 10 | Limite par defaut |

---

## Gestion des Connexions

### Flux de Connexion

```
Client                              Serveur
   |                                   |
   |-------- WebSocket Connect ------->|
   |                                   | Genere UUID (uuidv4)
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
    roomId: string,          // Code 6 caracteres (crypto-secure)
    hostId: string,          // UUID du createur
    roomName: string,        // Nom affiche
    roomType: number,        // 0=Lobby, 1=MeetingRoomA, 2=MeetingRoomB
    playerCount: number,     // Joueurs actuels
    maxPlayers: number,      // Limite (defaut: 10)
    createdAt: number        // Timestamp creation
}
```

---

## Routage des Messages

### Switch Principal (46 types explicites)

| Categorie | Types | Handler |
|-----------|-------|---------|
| Room Lifecycle | `room-available`, `room-closed`, `room-join`, `room-leave`, `room-update`, `room-list-request` | Fonctions dediees |
| Position VR | `vr-position`, `position` | `broadcastToRoom()` |
| Objets Interactifs | `obj-sync`, `obj-state` | `broadcastToRoom()` |
| Whiteboard | `whiteboard-draw`, `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request` | `broadcastToRoom()` |
| Whiteboard State | `whiteboard-state` | `handleWhiteboardState()` (point-to-point) |
| Room State | `room-welcome`, `room-teleport`, `player-name-update` | `broadcastToRoom()` |
| Admin | `kick-player` | `handleKickPlayer()` (host only) |
| WebRTC Voice | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Point-to-point |
| Screen Share | `screen-share-start/stop/frame/request/state` | `broadcastToRoom()` |
| Screen WebRTC | `screen-video-offer/answer/ice` | Point-to-point |
| File Share | `file-announce`, `file-chunk`, `file-complete`, `file-request`, `file-list-request` | `broadcastToRoom()` |
| File Share P2P | `file-list-response` | `handleFileListResponse()` (point-to-point ou broadcast) |
| File Presentation | `file-present-start/page/navigate/stop/request` | `broadcastToRoom()` |
| File Present P2P | `file-present-state` | `handleFilePresentState()` (point-to-point ou broadcast) |
| PDF | `pdf-convert-request`, `pdf-page-request` | Fonctions dediees (reponse directe) |
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

### Fonctions de Communication

| Fonction | Description |
|----------|-------------|
| `sendToClient(ws, message)` | Envoyer a 1 client |
| `broadcast(message, exceptId)` | Envoyer a TOUS les clients |
| `broadcastToRoom(senderId, message)` | Envoyer aux membres de la room |
| `sendRoomList(ws)` | Envoyer la liste des rooms a 1 client |
| `broadcastRoomList()` | Broadcast liste rooms a tous |
| `sendError(clientId, errorMessage)` | Envoyer une erreur a 1 client |

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

> **Important :** `senderId` est **force cote serveur** (`message.senderId = clientId`). La valeur envoyee par le client est ecrasee.

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
| `room-list-request` | Client -> Serveur | (vide) |
| `room-closed` | Host -> All | `{roomId}` |
| `room-update` | Host -> Serveur | `{roomId, ...}` (host only) |
| `room-teleport` | Client -> Room | `{roomId, roomType}` |
| `player-name-update` | Client -> Room | `{playerName}` |
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

> Mains a 0 = mode Desktop (pas de mains visibles)

### Messages Objets Interactifs

| Type | Direction | Data |
|------|-----------|------|
| `obj-sync` | Client -> Room | Position/rotation/etat de l'objet |
| `obj-state` | Client -> Room | Etat complet pour late joiners |

### Messages WebRTC (Point-to-Point)

| Type | Data |
|------|------|
| `webrtc-offer` | `{targetId, sdp}` |
| `webrtc-answer` | `{targetId, sdp}` |
| `webrtc-ice-candidate` | `{targetId, candidate, sdpMid, sdpMLineIndex}` |

### Messages Whiteboard

| Type | Data |
|------|------|
| `whiteboard-draw` | `{whiteboardId, roomId, ...strokeData}` |
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
| `screen-share-request` | `{sharerId}` |
| `screen-share-state` | `{targetId, ...}` |
| `screen-video-offer` | `{targetId, sdp}` (point-to-point) |
| `screen-video-answer` | `{targetId, sdp}` (point-to-point) |
| `screen-video-ice` | `{targetId, candidate}` (point-to-point) |

### Messages File Share

| Type | Data |
|------|------|
| `file-announce` | `{fileId, fileName, fileSize, fileType}` |
| `file-chunk` | `{fileId, chunkIndex, data}` |
| `file-complete` | `{fileId}` |
| `file-request` | `{fileId}` |
| `file-list-request` | `{roomId}` |
| `file-list-response` | `{targetId, files: [...]}` |

### Messages File Presentation

| Type | Data |
|------|------|
| `file-present-start` | `{fileId, fileName, totalPages}` |
| `file-present-page` | `{fileId, pageIndex, imageData}` |
| `file-present-navigate` | `{fileId, pageIndex}` |
| `file-present-stop` | `{fileId}` |
| `file-present-request` | `{roomId}` |
| `file-present-state` | `{targetId, fileId, currentPage, ...}` |

### Messages PDF

| Type | Direction | Data |
|------|-----------|------|
| `pdf-convert-request` | Client -> Serveur | `{fileId, data (base64)}` |
| `pdf-convert-response` | Serveur -> Client | `{fileId, totalPages, success}` |
| `pdf-page-request` | Client -> Serveur | `{fileId, pageIndex}` |
| `pdf-page-response` | Serveur -> Client | `{fileId, pageIndex, imageData}` |

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

### File Presentation

```
Presentateur                    Serveur                    Participants
  |                                |                          |
  |-- pdf-convert-request -------->|                          |
  |<-- pdf-convert-response -------|                          |
  |                                |                          |
  |-- file-present-start --------->|                          |
  |                                |--- file-present-start -->|
  |                                |                          |
  |-- file-present-page ---------->|                          |
  |                                |--- file-present-page --->|
  |                                |                          |
  |-- file-present-navigate ------>| (late joiner)            |
  |                                |<-- file-present-request -|
  |                                |                          |
  |<-- file-present-request -------|                          |
  |-- file-present-state --------->|                          |
  |                                |--- file-present-state -->|
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
| Pong client | Automatique (protocole WebSocket) |
| Timeout | 60s sans pong -> terminate + handleDisconnect |

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
| Room update authority | Verification `room.hostId === clientId` |
| Capacite rooms | Rejet si `playerCount >= maxPlayers` |
| Timeout | Deconnexion automatique apres 60s |
| Validation JSON | Try/catch sur tous les handlers |
| Etat WebSocket | Verification `readyState === OPEN` |
| Rate limiting (client) | Token bucket 60 msg/s dans Unity |

---

## Logs Serveur

### Format

```
[Connect] Client a1b2c3d4...
[Room] Created: XYZ789
[Room] Join: e5f6g7h8 -> XYZ789
[Room] Leave: e5f6g7h8 <- XYZ789
[Room] Closed: XYZ789
[Room] Update: XYZ789 by a1b2c3d4
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

## Changelog

| Date | Version | Description |
|------|---------|-------------|
| 2025-01-26 | 1.0 | Documentation initiale |
| 2026-02-02 | 2.0 | Correction line counts (887), ajout 46 types de messages, ajout file presentation/PDF/objets interactifs/screen WebRTC, ajout fonctions communication |
| 2026-02-02 | 2.1 | Suppression des sections base de donnees (Phase 3 non implementee) |

---

## References

- [GUIDE_DEPLOIEMENT_ENTREPRISE.md](./GUIDE_DEPLOIEMENT_ENTREPRISE.md) - Etat actuel du deploiement
- [NETWORKING_CODE_EXPLAINED.md](./NETWORKING_CODE_EXPLAINED.md) - Code annote ligne par ligne
- [SERVER_ARCHITECTURE_KO.md](./SERVER_ARCHITECTURE_KO.md) - Version coreenne
- [CLAUDE.md](../CLAUDE.md) - Instructions projet
