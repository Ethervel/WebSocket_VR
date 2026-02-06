# Documentation du Serveur WebSocket - VR Meeting Rooms

## Vue d'ensemble

Ce serveur Node.js gère toutes les communications temps réel entre les clients VR/Desktop. Il utilise WebSocket pour la communication bidirectionnelle.

```
Clients (Unity) ←──WebSocket──→ server.js ←──→ Mémoire (Map)
```

---

## Structure du fichier

| Section | Lignes | Description |
|---------|--------|-------------|
| Imports & Config | 1-26 | Chargement des modules |
| État global | 13-17 | Stockage en mémoire |
| Démarrage serveur | 28-37 | Création du WebSocket |
| Gestion connexions | 39-96 | Quand un client se connecte |
| Routage messages | 98-228 | Trier les messages par type |
| Gestion des rooms | 230-459 | Créer/rejoindre/quitter |
| Whiteboard | 461-487 | Synchronisation dessin |
| WebRTC Voice | 489-546 | Signaling voix |
| Screen Share | 548-605 | Partage d'écran |
| File Sharing | 607-633 | Partage de fichiers |
| File Presentation | 635-748 | Présentation PDF |
| Utilitaires | 750-833 | Fonctions d'envoi |
| Maintenance | 835-887 | Heartbeat, cleanup |

---

## Section par section

### 1. IMPORTS & CONFIGURATION (lignes 1-26)

```javascript
const WebSocket = require('ws');        // Librairie WebSocket
const { v4: uuidv4 } = require('uuid'); // Génère des IDs uniques

const PORT = process.env.PORT || 8080;  // Port du serveur (défaut: 8080)
const HEARTBEAT_INTERVAL = 30000;       // Vérifier si clients vivants toutes les 30s
const PDF_CACHE_TTL = 30 * 60 * 1000;   // Cache PDF expire après 30 min
```

**Explication simple:**
- On charge les outils dont on a besoin
- On définit les paramètres (port, intervalles)

---

### 2. ÉTAT GLOBAL (lignes 13-17)

```javascript
const clients = new Map();  // Tous les clients connectés
const rooms = new Map();    // Toutes les salles de réunion
const pdfCache = new Map(); // Cache des PDFs convertis
```

**Structure de `clients`:**
```javascript
clientId → {
    ws: WebSocket,        // Connexion du client
    roomId: "ABC123",     // Salle actuelle (ou null)
    playerName: "Jean",   // Nom du joueur
    lastHeartbeat: Date   // Dernière activité
}
```

**Structure de `rooms`:**
```javascript
roomId → {
    roomId: "ABC123",
    hostId: "uuid-xxx",   // ID du créateur
    roomName: "Réunion",
    roomType: 0,          // 0=Lobby, 1=RoomA, 2=RoomB
    playerCount: 3,
    maxPlayers: 10,
    createdAt: Date
}
```

---

### 3. DÉMARRAGE SERVEUR (lignes 28-37)

```javascript
const wss = new WebSocket.Server({ port: PORT });
```

**Ce que ça fait:**
- Crée un serveur WebSocket sur le port 8080
- Affiche un message de bienvenue dans la console

---

### 4. GESTION DES CONNEXIONS (lignes 39-96)

Quand un client se connecte:

```javascript
wss.on('connection', (ws) => {
    // 1. Créer un ID unique pour ce client
    const clientId = uuidv4();  // Ex: "a1b2c3d4-..."

    // 2. Enregistrer le client
    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });

    // 3. Envoyer "welcome" au client avec son ID
    sendToClient(ws, { type: 'welcome', senderId: clientId });

    // 4. Dire aux autres "quelqu'un est arrivé"
    broadcast({ type: 'peer-connected', senderId: clientId }, clientId);

    // 5. Envoyer la liste des rooms
    sendRoomList(ws);
});
```

**Événements écoutés sur chaque client:**

| Événement | Ce qui se passe |
|-----------|-----------------|
| `message` | Le client envoie un message → `handleMessage()` |
| `close` | Le client se déconnecte → `handleDisconnect()` |
| `error` | Erreur → Log dans la console |
| `pong` | Réponse au ping → Met à jour `lastHeartbeat` |

---

### 5. ROUTAGE DES MESSAGES (lignes 98-228)

Quand un message arrive, on regarde son `type` et on le traite:

```javascript
function handleMessage(clientId, message) {
    switch (message.type) {
        case 'room-join':
            handleRoomJoin(clientId, message.data);
            break;
        case 'vr-position':
            broadcastToRoom(clientId, message);  // Envoyer à la room
            break;
        // ... etc
    }
}
```

**Types de messages:**

| Catégorie | Types | Action |
|-----------|-------|--------|
| **Rooms** | `room-available`, `room-join`, `room-leave`, `room-closed` | Fonction spéciale |
| **VR Sync** | `vr-position`, `obj-sync` | Broadcast à la room |
| **Whiteboard** | `whiteboard-batch`, `whiteboard-clear` | Broadcast à la room |
| **Voice** | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Envoi direct au destinataire |
| **Screen** | `screen-share-start`, `screen-share-frame` | Broadcast à la room |
| **Files** | `file-announce`, `file-chunk` | Broadcast à la room |
| **PDF** | `pdf-convert-request`, `pdf-page-request` | Traitement serveur |

---

### 6. GESTION DES ROOMS (lignes 230-459)

#### Créer une room (`room-available`)
```javascript
function handleRoomAvailable(clientId, data) {
    // 1. Créer l'objet room
    const roomInfo = {
        roomId: data.roomId,
        hostId: clientId,        // Le créateur devient host
        roomName: data.roomName,
        playerCount: 1,
        maxPlayers: 10
    };

    // 2. Sauvegarder dans la Map
    rooms.set(data.roomId, roomInfo);

    // 3. Associer le client à cette room
    clients.get(clientId).roomId = data.roomId;

    // 4. Informer tout le monde
    broadcastRoomList();
}
```

#### Rejoindre une room (`room-join`)
```javascript
function handleRoomJoin(clientId, data) {
    // 1. Vérifier que la room existe
    const room = rooms.get(data.roomId);
    if (!room) return sendError(clientId, "Room not found");

    // 2. Vérifier qu'elle n'est pas pleine
    if (room.playerCount >= room.maxPlayers) return sendError(clientId, "Room full");

    // 3. Associer le client à la room
    clients.get(clientId).roomId = data.roomId;
    room.playerCount++;

    // 4. Informer les autres membres de la room
    broadcastToRoom(clientId, { type: 'room-join', ... });
}
```

#### Quitter une room (`room-leave`)
```javascript
function handleRoomLeave(clientId, data) {
    // 1. Décrémenter le compteur
    rooms.get(data.roomId).playerCount--;

    // 2. Dissocier le client
    clients.get(clientId).roomId = null;

    // 3. Informer les autres
    broadcastToRoom(clientId, { type: 'room-leave', ... });
}
```

#### Déconnexion (`handleDisconnect`)
```javascript
function handleDisconnect(clientId) {
    const client = clients.get(clientId);

    if (client.roomId) {
        const room = rooms.get(client.roomId);

        // Si c'était le host → fermer la room
        if (room.hostId === clientId) {
            rooms.delete(client.roomId);
            broadcast({ type: 'room-closed', ... });
        } else {
            // Sinon → juste décrémenter
            room.playerCount--;
        }
    }

    // Supprimer le client
    clients.delete(clientId);

    // Informer tout le monde
    broadcast({ type: 'peer-disconnected', senderId: clientId });
}
```

---

### 7. WEBRTC SIGNALING (lignes 489-546)

Le serveur ne gère PAS l'audio. Il fait juste passer les messages entre clients pour qu'ils établissent une connexion directe.

```
Client A                    Serveur                    Client B
    │                          │                          │
    │──webrtc-offer──────────►│──────────────────────────►│
    │                          │                          │
    │◄─────────────────────────│◄──webrtc-answer──────────│
    │                          │                          │
    │──webrtc-ice-candidate──►│──────────────────────────►│
    │◄─────────────────────────│◄──webrtc-ice-candidate───│
    │                          │                          │
    │◄═══════════════Connexion audio directe═════════════►│
```

```javascript
function handleWebRTCOffer(senderId, data) {
    const { targetId, sdp } = data;

    // Trouver le destinataire et lui envoyer l'offre
    const targetClient = clients.get(targetId);
    sendToClient(targetClient.ws, {
        type: 'webrtc-offer',
        senderId: senderId,
        data: JSON.stringify({ sdp })
    });
}
```

---

### 8. UTILITAIRES D'ENVOI (lignes 750-833)

#### `sendToClient(ws, message)`
Envoie un message à UN client spécifique.

```javascript
function sendToClient(ws, message) {
    if (ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
    }
}
```

#### `broadcast(message, exceptClientId)`
Envoie à TOUS les clients (sauf un).

```javascript
function broadcast(message, exceptClientId = null) {
    clients.forEach((client, clientId) => {
        if (clientId !== exceptClientId) {
            client.ws.send(JSON.stringify(message));
        }
    });
}
```

#### `broadcastToRoom(senderId, message)`
Envoie uniquement aux clients de la MÊME ROOM.

```javascript
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    const roomId = sender.roomId;

    clients.forEach((client, clientId) => {
        if (clientId !== senderId && client.roomId === roomId) {
            client.ws.send(JSON.stringify(message));
        }
    });
}
```

---

### 9. MAINTENANCE (lignes 835-887)

#### Heartbeat (vérifier si clients vivants)
```javascript
setInterval(() => {
    // Envoyer ping à tous
    wss.clients.forEach((ws) => ws.ping());

    // Vérifier qui n'a pas répondu depuis longtemps
    clients.forEach((client, clientId) => {
        if (Date.now() - client.lastHeartbeat > 60000) {  // 60s sans réponse
            client.ws.terminate();  // Déconnecter de force
            handleDisconnect(clientId);
        }
    });
}, 30000);  // Toutes les 30 secondes
```

#### Nettoyage cache PDF
```javascript
setInterval(() => {
    // Supprimer les PDFs convertis depuis plus de 30 min
    pdfCache.forEach((entry, fileId) => {
        if (Date.now() - entry.timestamp > PDF_CACHE_TTL) {
            pdfCache.delete(fileId);
        }
    });
}, 5 * 60 * 1000);  // Toutes les 5 minutes
```

#### Arrêt propre (Ctrl+C)
```javascript
process.on('SIGINT', () => {
    // 1. Arrêter le heartbeat
    clearInterval(heartbeatInterval);

    // 2. Fermer toutes les connexions
    wss.clients.forEach((ws) => ws.close());

    // 3. Fermer le serveur
    wss.close(() => process.exit(0));
});
```

---

## Format des messages

Tous les messages ont cette structure:
```javascript
{
    type: "room-join",      // Type du message
    senderId: "uuid-xxx",   // ID de l'envoyeur
    data: "{...}"           // Données (JSON stringifié)
}
```

---

## Flux typiques

### Connexion d'un client
```
1. Client se connecte
2. Serveur génère UUID
3. Serveur envoie "welcome" avec UUID
4. Serveur envoie "room-list"
5. Serveur broadcast "peer-connected"
```

### Création de room
```
1. Client envoie "room-available"
2. Serveur crée la room en mémoire
3. Serveur broadcast "room-list" à tous
4. Serveur broadcast "room-available" à tous
```

### Rejoindre une room
```
1. Client envoie "room-join"
2. Serveur vérifie (existe? pas pleine?)
3. Serveur associe client → room
4. Serveur broadcast "room-join" à la room
5. Host envoie "room-welcome" avec liste joueurs
```

### Synchronisation VR (30 fois/seconde)
```
1. Client envoie "vr-position" avec position/rotation
2. Serveur broadcast à tous dans la même room
3. Autres clients reçoivent et mettent à jour l'avatar
```

---

## Résumé des fonctions

| Fonction | Rôle |
|----------|------|
| `handleMessage` | Trier les messages par type |
| `handleRoomAvailable` | Créer une room |
| `handleRoomJoin` | Rejoindre une room |
| `handleRoomLeave` | Quitter une room |
| `handleRoomClosed` | Fermer une room (host) |
| `handleKickPlayer` | Expulser un joueur (host) |
| `handleDisconnect` | Gérer déconnexion |
| `handleWebRTCOffer/Answer/Ice` | Signaling voix |
| `handleWhiteboardState` | Sync whiteboard |
| `handlePdfConvertRequest` | Convertir PDF |
| `sendToClient` | Envoyer à 1 client |
| `broadcast` | Envoyer à tous |
| `broadcastToRoom` | Envoyer à 1 room |
| `sendRoomList` | Envoyer liste rooms |
| `sendError` | Envoyer erreur |

---

## Pour tester

```bash
cd Server
npm install
npm run dev
```

Le serveur affiche:
```
============================================
  VR MEETING ROOMS - WebSocket Server
============================================
  Port: 8080
  Heartbeat: 30s
============================================
```

Puis toutes les 60 secondes:
```
[Status] 3 clients | 1 rooms
```
