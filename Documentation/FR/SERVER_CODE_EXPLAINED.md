# Explication du Code server.js

Ce document explique le fonctionnement du serveur WebSocket ligne par ligne.

---

## Vue d'ensemble

Le serveur est un **relais de messages** WebSocket qui :
1. Accepte les connexions des clients Unity
2. Route les messages entre clients
3. Gère les rooms (salles de réunion)
4. Fait le signaling WebRTC pour la voix
5. Authentifie les utilisateurs via MariaDB

---

## Structure du fichier

```
server.js (868 lignes)
│
├── [1-25]     Configuration & Imports
├── [27-78]    Gestion des connexions
├── [80-231]   Routage des messages (switch principal)
├── [233-421]  Gestion des rooms
├── [423-458]  Handlers whiteboard
├── [460-590]  Signaling WebRTC (voix + vidéo)
├── [592-624]  Partage de fichiers
├── [626-738]  Authentification
├── [740-824]  Utilitaires de broadcast
└── [826-868]  Maintenance serveur
```

---

## 1. Configuration & Imports (lignes 1-25)

```javascript
const WebSocket = require('ws');           // Librairie WebSocket
const { v4: uuidv4 } = require('uuid');    // Génération d'IDs uniques
const { registerUser, loginUser, updateUserProfile } = require('./auth');  // Auth MariaDB

const PORT = process.env.PORT || 8080;     // Port serveur (configurable)
const HEARTBEAT_INTERVAL = 30000;          // Ping toutes les 30 secondes

const clients = new Map();  // Stocke tous les clients connectés
const rooms = new Map();    // Stocke toutes les rooms actives
```

### Structures de données

**clients Map** : `clientId → { ws, roomId, playerName, lastHeartbeat }`
| Champ | Description |
|-------|-------------|
| `ws` | Connexion WebSocket |
| `roomId` | Room actuelle (ou `null`) |
| `playerName` | Nom du joueur |
| `lastHeartbeat` | Timestamp du dernier ping |

**rooms Map** : `roomId → RoomInfo`
| Champ | Description |
|-------|-------------|
| `roomId` | Code de la room (ex: "ABCDEF") |
| `hostId` | ClientId de l'hôte |
| `playerCount` | Nombre de joueurs |
| `maxPlayers` | Limite (défaut: 10) |

---

## 2. Gestion des connexions (lignes 27-78)

```javascript
wss.on('connection', (ws) => {
    // 1. Génère un ID unique pour ce client
    const clientId = uuidv4();

    // 2. Enregistre le client dans la Map
    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });

    // 3. Envoie son ID au client
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // 4. Notifie les autres clients
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);

    // 5. Envoie la liste des rooms
    sendRoomList(ws);
```

### Événements WebSocket

| Événement | Action |
|-----------|--------|
| `message` | Parse JSON et appelle `handleMessage()` |
| `close` | Appelle `handleDisconnect()` |
| `error` | Log l'erreur |
| `pong` | Met à jour `lastHeartbeat` |

---

## 3. Routage des messages (lignes 80-231)

C'est le **cœur du serveur**. Un switch qui route chaque type de message.

```javascript
function handleMessage(clientId, message) {
    const { type, senderId, data } = message;
    message.senderId = clientId;  // Écrase le senderId par sécurité

    switch (type) {
        // ... tous les types de messages
    }
}
```

### Catégories de messages

| Catégorie | Types | Portée |
|-----------|-------|--------|
| **Room** | `room-available`, `room-join`, `room-leave`, `room-closed` | Global ou Room |
| **VR Sync** | `vr-position`, `position` | Room uniquement |
| **Objets** | `obj-sync`, `obj-state` | Room uniquement |
| **Whiteboard** | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request` | Room uniquement |
| **WebRTC** | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Point-to-point |
| **Screen Share** | `screen-share-start`, `screen-share-frame`, `screen-share-stop` | Room uniquement |
| **Auth** | `auth-register`, `auth-login`, `auth-update-profile` | Point-to-point |

### Trois modes de broadcast

```javascript
// 1. GLOBAL - À tous les clients
broadcast(message, exceptClientId);

// 2. ROOM - Seulement aux clients de la même room
broadcastToRoom(clientId, message);

// 3. POINT-TO-POINT - À un seul client
sendToClient(targetClient.ws, message);
```

---

## 4. Gestion des rooms (lignes 233-421)

### Création de room (handleRoomAvailable)

```javascript
function handleRoomAvailable(clientId, dataStr) {
    const data = JSON.parse(dataStr);

    // Crée l'objet room
    const roomInfo = {
        roomId: data.roomId,
        hostId: clientId,           // Le créateur devient host
        roomName: data.roomName,
        playerCount: 1,
        maxPlayers: 10,
        createdAt: Date.now()
    };

    // Stocke la room
    rooms.set(data.roomId, roomInfo);

    // Associe le client à cette room
    clients.get(clientId).roomId = data.roomId;

    // Notifie tout le monde
    broadcastRoomList();
}
```

### Rejoindre une room (handleRoomJoin)

```javascript
function handleRoomJoin(clientId, dataStr) {
    const data = JSON.parse(dataStr);
    const room = rooms.get(data.roomId);

    // Vérifications
    if (!room) return sendError(clientId, 'Room not found');
    if (room.playerCount >= room.maxPlayers) return sendError(clientId, 'Room full');

    // Met à jour le client
    clients.get(clientId).roomId = data.roomId;

    // Incrémente le compteur
    room.playerCount++;

    // Notifie SEULEMENT la room
    broadcastToRoom(clientId, { type: 'room-join', ... });
}
```

### Déconnexion (handleDisconnect)

```javascript
function handleDisconnect(clientId) {
    const client = clients.get(clientId);

    if (client.roomId) {
        const room = rooms.get(client.roomId);

        if (room.hostId === clientId) {
            // L'hôte part → ferme la room
            rooms.delete(client.roomId);
            broadcast({ type: 'room-closed', ... });
        } else {
            // Joueur normal → décrémente
            room.playerCount--;
        }

        // Notifie la room
        broadcastToRoom(clientId, { type: 'room-leave', ... });
    }

    // Supprime le client
    clients.delete(clientId);

    // Notifie globalement
    broadcast({ type: 'peer-disconnected', senderId: clientId });
}
```

---

## 5. Whiteboard (lignes 423-458)

### handleWhiteboardState

Gère l'envoi de l'état du whiteboard (texture PNG) aux late joiners.

```javascript
function handleWhiteboardState(clientId, dataStr) {
    const stateData = JSON.parse(dataStr);

    if (stateData.targetId) {
        // Envoi ciblé à un seul client (late joiner)
        const targetClient = clients.get(stateData.targetId);
        sendToClient(targetClient.ws, {
            type: 'whiteboard-state',
            senderId: clientId,
            data: dataStr
        });
    } else {
        // Broadcast à toute la room
        broadcastToRoom(clientId, { type: 'whiteboard-state', ... });
    }
}
```

---

## 6. WebRTC Signaling (lignes 460-590)

Le serveur ne fait que **relayer** les messages SDP et ICE entre clients.

### Flux WebRTC (voix)

```
Client A                    Serveur                    Client B
    │                          │                          │
    │── webrtc-offer ─────────►│                          │
    │   {targetId: B, sdp}     │── webrtc-offer ─────────►│
    │                          │                          │
    │                          │◄── webrtc-answer ───────│
    │◄── webrtc-answer ───────│   {targetId: A, sdp}     │
    │                          │                          │
    │◄─────────────────────────┼── ICE candidates ───────►│
```

```javascript
function handleWebRTCOffer(senderId, dataStr) {
    const { targetId, sdp } = JSON.parse(dataStr);

    // Trouve le client cible
    const targetClient = clients.get(targetId);

    // Relaie l'offre
    sendToClient(targetClient.ws, {
        type: 'webrtc-offer',
        senderId: senderId,
        data: JSON.stringify({ sdp })
    });
}
```

---

## 7. Authentification (lignes 626-738)

Utilise le module `auth.js` qui communique avec MariaDB.

### Inscription

```javascript
async function handleAuthRegister(clientId, dataStr) {
    const { username, email, password, displayName } = JSON.parse(dataStr);

    // Appelle la fonction d'auth (hash bcrypt + INSERT SQL)
    const result = await registerUser(username, email, password, displayName);

    // Renvoie le résultat au client
    sendAuthResponse(clientId, 'auth-register-response', result);

    // Si succès, met à jour le client
    if (result.success) {
        clients.get(clientId).userId = result.userId;
        clients.get(clientId).playerName = result.displayName;
    }
}
```

### Connexion

```javascript
async function handleAuthLogin(clientId, dataStr) {
    const { username, password } = JSON.parse(dataStr);

    // Vérifie credentials (SELECT + bcrypt.compare)
    const result = await loginUser(username, password);

    sendAuthResponse(clientId, 'auth-login-response', result);
}
```

---

## 8. Utilitaires de broadcast (lignes 740-824)

### sendToClient - Envoi à un client

```javascript
function sendToClient(ws, message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
    }
}
```

### broadcast - Envoi global

```javascript
function broadcast(message, exceptClientId = null) {
    clients.forEach((client, clientId) => {
        if (clientId !== exceptClientId && client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(JSON.stringify(message));
        }
    });
}
```

### broadcastToRoom - Envoi à une room (CRITIQUE)

```javascript
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    if (!sender || !sender.roomId) return;

    const roomId = sender.roomId;

    clients.forEach((client, clientId) => {
        // Conditions:
        // 1. Pas l'expéditeur
        // 2. Même room
        // 3. Connexion ouverte
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(JSON.stringify(message));
        }
    });
}
```

> **Important** : Cette fonction garantit l'isolation entre rooms. Les messages VR, whiteboard et screen share ne vont qu'aux membres de la même room.

---

## 9. Maintenance serveur (lignes 826-868)

### Heartbeat (ping/pong)

```javascript
const heartbeatInterval = setInterval(() => {
    // Envoie ping à tous les clients
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });

    // Déconnecte les clients inactifs (2x l'intervalle = 60s)
    clients.forEach((client, clientId) => {
        if (Date.now() - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
}, HEARTBEAT_INTERVAL);  // Toutes les 30 secondes
```

### Arrêt propre (SIGINT)

```javascript
process.on('SIGINT', () => {
    clearInterval(heartbeatInterval);

    // Ferme toutes les connexions
    wss.clients.forEach((ws) => ws.close());

    // Arrête le serveur
    wss.close(() => process.exit(0));
});
```

### Logs périodiques

```javascript
setInterval(() => {
    console.log(`[SERVER] ${clients.size} clients | Rooms: ...`);
}, 60000);  // Toutes les minutes
```

---

## Résumé du flux

```
┌─────────────────────────────────────────────────────────────────┐
│                        SERVEUR NODE.JS                          │
│                                                                 │
│  1. Client se connecte                                          │
│     └─► Génère UUID, envoie 'welcome', broadcast 'peer-connected'│
│                                                                 │
│  2. Client crée/rejoint room                                    │
│     └─► Met à jour clients Map et rooms Map                     │
│                                                                 │
│  3. Client envoie message                                       │
│     └─► handleMessage() route selon le type                     │
│         ├─► broadcastToRoom() pour VR/whiteboard/screen        │
│         ├─► sendToClient() pour WebRTC signaling               │
│         └─► broadcast() pour events globaux                    │
│                                                                 │
│  4. Client se déconnecte                                        │
│     └─► handleDisconnect() nettoie, notifie room               │
└─────────────────────────────────────────────────────────────────┘
```

---

## Dépendances

| Package | Version | Rôle |
|---------|---------|------|
| `ws` | ^8.x | Serveur WebSocket |
| `uuid` | ^9.x | Génération d'IDs |
| `bcrypt` | ^5.x | Hash des mots de passe (via auth.js) |
| `mysql2` | ^3.x | Connexion MariaDB (via db.js) |

---

## Points clés pour la sécurité

1. **senderId écrasé** (ligne 86) : Le serveur remplace toujours le senderId par l'ID réel du client pour éviter l'usurpation.

2. **Vérification host** (ligne 277, 368) : Seul l'hôte peut fermer une room ou la modifier.

3. **Isolation des rooms** : `broadcastToRoom()` garantit que les messages ne fuient pas entre rooms.

4. **Heartbeat** : Les clients inactifs sont déconnectés après 60 secondes.

---

*Document généré pour présentation interne*
