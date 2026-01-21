# Architecture Serveur WebSocket - VR Meeting Rooms

Ce document explique le fonctionnement du serveur WebSocket, comment il est connecte au projet Unity, les messages echanges, et comment le deployer sur un serveur distant.

## Table des Matieres

1. [Vue d'Ensemble](#vue-densemble)
2. [Architecture du Serveur](#architecture-du-serveur)
3. [Connexion Unity <-> Serveur](#connexion-unity---serveur)
4. [Protocole de Messages](#protocole-de-messages)
5. [Traitement des Messages](#traitement-des-messages)
6. [Deploiement en Production](#deploiement-en-production)
7. [Securite](#securite)
8. [Depannage](#depannage)

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
| VRRoomManager    |                                     | MariaDB          |
| VRGameManager    |                                     | (auth.js/db.js)  |
| VoiceChatManager |                                     |                  |
+------------------+                                     +------------------+
```

### Stack Technique

| Composant | Technologie | Fichier(s) |
|-----------|-------------|------------|
| Serveur WebSocket | Node.js + ws@8.14.2 | `server.js` |
| Client WebSocket | NativeWebSocket (Unity) | `VRNetworkManager.cs` |
| Authentification | bcrypt + MariaDB | `auth.js`, `db.js` |
| Voix (WebRTC) | Unity.WebRTC | `VoiceChatManager.cs` |

---

## Architecture du Serveur

### Structure des Fichiers Serveur

```
Server/
├── server.js           # Serveur principal WebSocket (1047 lignes)
├── auth.js             # Gestion authentification (bcrypt)
├── db.js               # Pool de connexions MariaDB
├── filePresentation.js # Conversion PDF (optionnel)
├── package.json        # Dependances npm
└── server.test.js      # Tests unitaires
```

### server.js - Composants Principaux

#### 1. Initialisation (lignes 1-35)

```javascript
const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');

const PORT = process.env.PORT || 8080;
const HEARTBEAT_INTERVAL = 30000;  // 30 secondes

// Stockage en memoire
const clients = new Map();  // clientId -> { ws, roomId, playerName, lastHeartbeat }
const rooms = new Map();    // roomId -> RoomInfo
```

#### 2. Gestion des Connexions (lignes 40-87)

Quand un client se connecte :
1. Genere un UUID unique (`clientId`)
2. Stocke le client dans `clients` Map
3. Envoie message `welcome` avec l'ID assigne
4. Broadcast `peer-connected` aux autres
5. Envoie la liste des rooms disponibles

```javascript
wss.on('connection', (ws) => {
    const clientId = uuidv4();

    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });

    // Message d'accueil avec ID assigne
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // Notifier les autres clients
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);

    // Envoyer la liste des rooms
    sendRoomList(ws);
});
```

#### 3. Routage des Messages (lignes 93-263)

Le `switch` principal route chaque type de message vers son handler :

| Categorie | Types de Messages | Action |
|-----------|-------------------|--------|
| **Room Lifecycle** | `room-available`, `room-closed`, `room-join`, `room-leave`, `room-update` | CRUD rooms |
| **Position VR** | `vr-position`, `position` | Broadcast a la room |
| **Objets** | `obj-sync`, `obj-state` | Broadcast a la room |
| **Whiteboard** | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request/state` | Broadcast ou point-a-point |
| **WebRTC Voice** | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Point-a-point |
| **Screen Share** | `screen-share-start/stop/frame` | Broadcast a la room |
| **File Share** | `file-announce`, `file-chunk`, `file-complete` | Broadcast a la room |
| **Auth** | `auth-register`, `auth-login`, `auth-update-profile` | Handler specifique |

#### 4. Fonction Critique : `broadcastToRoom` (lignes 948-976)

Cette fonction est ESSENTIELLE - elle filtre les messages par room :

```javascript
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    if (!sender || !sender.roomId) return;

    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);

    clients.forEach((client, clientId) => {
        // Envoyer SEULEMENT si:
        // 1. Ce n'est pas l'expediteur
        // 2. Le client est dans la MEME room
        // 3. La connexion est ouverte
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}
```

#### 5. Heartbeat et Timeout (lignes 1015-1032)

```javascript
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    // Envoyer ping a tous les clients
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });

    // Deconnecter les clients en timeout (60s)
    clients.forEach((client, clientId) => {
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            console.log(`[SERVER] Client timeout: ${clientId}`);
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
}, HEARTBEAT_INTERVAL);  // Toutes les 30 secondes
```

---

## Connexion Unity <-> Serveur

### Cote Unity : VRNetworkManager.cs

#### Configuration (Inspector Unity)

| Parametre | Defaut | Description |
|-----------|--------|-------------|
| `serverUrl` | `ws://localhost:8080` | URL du serveur WebSocket |
| `enforceSecureConnection` | `false` | Forcer wss:// en production |
| `autoReconnect` | `true` | Reconnexion automatique |
| `welcomeTimeout` | `5s` | Timeout pour message welcome |
| `maxMessagesPerSecond` | `60` | Rate limiting |

#### Flux de Connexion

```
Unity                                       Serveur
  |                                           |
  |-------- WebSocket Connect ---------------->|
  |                                           |
  |<------- welcome {senderId: "uuid"} -------|
  |                                           |
  |-------- room-available {roomId, name} --->|
  |                                           |
  |<------- room-list {rooms: [...]} ---------|
  |                                           |
  |-------- vr-position (30Hz) --------------->|
  |                                           |
```

#### Gestion des Erreurs (Exponential Backoff)

```csharp
// VRNetworkManager.cs - lignes 176-188
if (_isReconnecting && autoReconnect)
{
    _reconnectTimer -= Time.deltaTime;
    if (_reconnectTimer <= 0f)
    {
        _isReconnecting = false;
        _reconnectAttempts++;
        ConnectAsync();
    }
}

// Calcul du delai : 1s -> 2s -> 4s -> 8s -> ... -> 30s max
_currentReconnectDelay = Mathf.Min(
    _currentReconnectDelay * backoffMultiplier,
    maxReconnectDelay
);
```

---

## Protocole de Messages

### Format Standard

Tous les messages suivent ce format JSON :

```json
{
    "type": "message-type",
    "senderId": "client-uuid",
    "data": "{\"json\": \"serialized\"}"
}
```

### Messages par Categorie

#### 1. Connexion

| Type | Direction | Contenu `data` |
|------|-----------|----------------|
| `welcome` | Serveur -> Client | `null` (senderId = ID assigne) |
| `peer-connected` | Serveur -> All | `null` (senderId = nouveau peer) |
| `peer-disconnected` | Serveur -> All | `null` (senderId = peer parti) |

#### 2. Gestion des Rooms

| Type | Direction | Contenu `data` |
|------|-----------|----------------|
| `room-available` | Client -> Serveur | `{roomId, roomName, roomType, maxPlayers}` |
| `room-join` | Client -> Serveur | `{roomId, playerId, playerName, colorR/G/B}` |
| `room-welcome` | Host -> Room | `{roomId, roomType, players: [...]}` |
| `room-leave` | Client -> Room | `{roomId, playerId}` |
| `room-list` | Serveur -> Client | `{rooms: [RoomInfo...]}` |
| `room-closed` | Host -> All | `{roomId}` |

#### 3. Synchronisation VR (30Hz)

| Type | Direction | Contenu `data` |
|------|-----------|----------------|
| `vr-position` | Client -> Room | Voir structure VRPositionData |

```csharp
// VRPositionData (VRGameManager.cs)
{
    "roomId": "ABC123",
    "roomType": 1,  // 0=Lobby, 1=RoomA, 2=RoomB
    // Corps
    "posX": 1.234, "posY": 0.0, "posZ": -5.678,
    "rotY": 45.0,
    // Tete (world space)
    "headPosX": 1.234, "headPosY": 1.7, "headPosZ": -5.678,
    "headRotX": 0.0, "headRotY": 0.707, "headRotZ": 0.0, "headRotW": 0.707,
    // Mains (world space) - zeros = mode Desktop
    "leftHandPosX": ..., "leftHandRotX": ...,
    "rightHandPosX": ..., "rightHandRotX": ...
}
```

#### 4. WebRTC Signaling (Point-a-Point)

| Type | Direction | Contenu `data` |
|------|-----------|----------------|
| `webrtc-offer` | Client -> Client | `{targetId, sdp}` |
| `webrtc-answer` | Client -> Client | `{targetId, sdp}` |
| `webrtc-ice-candidate` | Client -> Client | `{targetId, candidate, sdpMid, sdpMLineIndex}` |

#### 5. Whiteboard

| Type | Direction | Contenu `data` |
|------|-----------|----------------|
| `whiteboard-batch` | Client -> Room | `{whiteboardId, roomId, r/g/b/a, penSize, pointsFlat: [u,v,...]}` |
| `whiteboard-clear` | Client -> Room | `{whiteboardId, roomId}` |
| `whiteboard-request` | Client -> Room | `{whiteboardId, roomId}` |
| `whiteboard-state` | Client -> Client | `{targetId, textureData (base64 PNG)}` |

#### 6. Screen Share

| Type | Direction | Contenu `data` |
|------|-----------|----------------|
| `screen-share-start` | Client -> Room | `{sharerId, sharerName}` |
| `screen-share-frame` | Client -> Room | `{imageData (base64 JPEG)}` |
| `screen-share-stop` | Client -> Room | `{sharerId}` |

---

## Traitement des Messages

### Exemple Complet : Rejoindre une Room

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
     |                             |                           |
```

### Handler `room-join` (server.js:327-364)

```javascript
function handleRoomJoin(clientId, dataStr) {
    const data = JSON.parse(dataStr);
    const room = rooms.get(data.roomId);

    // Verifications
    if (!room) {
        sendError(clientId, `Room ${data.roomId} not found`);
        return;
    }

    if (room.playerCount >= room.maxPlayers) {
        sendError(clientId, 'Room is full');
        return;
    }

    // Mettre a jour l'etat du client
    const client = clients.get(clientId);
    client.roomId = data.roomId;
    client.playerName = data.playerName;

    room.playerCount++;

    // Broadcast a la room SEULEMENT
    broadcastToRoom(clientId, {
        type: 'room-join',
        senderId: clientId,
        data: JSON.stringify(data)
    });

    // Mettre a jour la liste globale des rooms
    broadcastRoomList();
}
```

---

## Deploiement en Production

### Prerequis

- Node.js 18+ LTS
- MariaDB 10.5+ (pour l'authentification)
- Certificat SSL (pour wss://)
- Serveur avec IP publique

### Etape 1 : Preparation du Serveur

```bash
# Sur le serveur (Linux)
sudo apt update
sudo apt install nodejs npm mariadb-server nginx

# Cloner le projet
git clone <votre-repo>
cd WebSocket_VR/Server

# Installer les dependances
npm install
```

### Etape 2 : Configuration Base de Donnees

```sql
-- Creer la base de donnees
CREATE DATABASE vr_meeting;
USE vr_meeting;

-- Table utilisateurs
CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(50),
    avatar_color VARCHAR(20),
    last_login DATETIME,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Creer un utilisateur dedie
CREATE USER 'vr_app'@'localhost' IDENTIFIED BY 'votre_mot_de_passe_fort';
GRANT ALL PRIVILEGES ON vr_meeting.* TO 'vr_app'@'localhost';
FLUSH PRIVILEGES;
```

### Etape 3 : Variables d'Environnement

Creer un fichier `.env` :

```bash
# Server
PORT=8080

# Database
DB_HOST=localhost
DB_PORT=3306
DB_USER=vr_app
DB_PASSWORD=votre_mot_de_passe_fort
DB_NAME=vr_meeting
```

Modifier le script pour charger `.env` :

```javascript
// Ajouter en haut de server.js
require('dotenv').config();
```

### Etape 4 : Configuration SSL avec Nginx (Reverse Proxy)

```nginx
# /etc/nginx/sites-available/vr-meeting
server {
    listen 443 ssl;
    server_name votre-domaine.com;

    ssl_certificate /etc/letsencrypt/live/votre-domaine.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/votre-domaine.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}

# Redirect HTTP to HTTPS
server {
    listen 80;
    server_name votre-domaine.com;
    return 301 https://$server_name$request_uri;
}
```

```bash
# Activer le site
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx

# Obtenir certificat SSL (Let's Encrypt)
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d votre-domaine.com
```

### Etape 5 : Service Systemd

```ini
# /etc/systemd/system/vr-meeting.service
[Unit]
Description=VR Meeting WebSocket Server
After=network.target mariadb.service

[Service]
Type=simple
User=www-data
WorkingDirectory=/chemin/vers/WebSocket_VR/Server
ExecStart=/usr/bin/node server.js
Restart=always
RestartSec=10
Environment=NODE_ENV=production
EnvironmentFile=/chemin/vers/WebSocket_VR/Server/.env

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable vr-meeting
sudo systemctl start vr-meeting
sudo systemctl status vr-meeting
```

### Etape 6 : Configuration Unity

Dans Unity, modifier `VRNetworkManager` :

```csharp
// Changez serverUrl dans l'Inspector
serverUrl = "wss://votre-domaine.com";

// Activez la securite
enforceSecureConnection = true;
```

---

## Securite

### Checklist Production

| Element | Status | Action |
|---------|--------|--------|
| TLS/SSL | REQUIS | Utiliser `wss://` avec certificat valide |
| Mots de passe DB | REQUIS | Changer les valeurs par defaut |
| TURN server | RECOMMANDE | Utiliser serveur TURN prive (Twilio/Xirsys) |
| Rate limiting | INCLUS | 60 msg/s par client (configurable) |
| Validation JSON | INCLUS | `TryDeserialize` avec gestion erreurs |

### Configuration TURN Prive (VoiceChatManager.cs)

```csharp
// Dans l'Inspector Unity
useCustomTurnServer = true;
customTurnUrl = "turn:votre-turn.com:3478";
customTurnUsername = "votre_user";
customTurnCredential = "votre_secret";
enableTurnTcp = true;  // Pour firewalls restrictifs
```

---

## Depannage

### Problemes Courants

| Symptome | Cause Probable | Solution |
|----------|----------------|----------|
| "Welcome timeout" | Serveur non accessible | Verifier URL, port, firewall |
| Deconnexions frequentes | Heartbeat timeout | Verifier stabilite reseau |
| Pas d'audio entre joueurs | TURN server manquant | Ajouter serveur TURN |
| Messages non recus | Mauvais roomId | Verifier filtrage `broadcastToRoom` |

### Logs Serveur

```bash
# Voir les logs en temps reel
sudo journalctl -u vr-meeting -f

# Logs typiques
[SERVER] WebSocket server started on port 8080
[SERVER] Client connected: abc-123-def-456
[SERVER] Room created: XYZ789 by abc-123-def-456
[Room:XYZ789] whiteboard-batch from abc-123 -> 2 clients
```

### Test de Connexion

```javascript
// Test rapide avec wscat
npm install -g wscat
wscat -c ws://localhost:8080

// Envoyer un message de test
> {"type":"room-list-request","data":""}
< {"type":"room-list","senderId":"server","data":"{\"rooms\":[]}"}
```

---

## Monitoring Serveur

Le serveur affiche des statistiques toutes les 60 secondes :

```
[SERVER] 3 clients | Rooms: ABC123(2), XYZ789(1)
```

Pour un monitoring avance, considerez :
- PM2 (`pm2 monit`)
- Prometheus + Grafana
- Uptime monitoring (UptimeRobot, Pingdom)

---

## Resume

1. **Le serveur** est un hub WebSocket qui route les messages entre clients
2. **Les rooms** isolent les messages (whiteboard, position, screen share)
3. **WebRTC** est peer-to-peer mais le signaling passe par le serveur
4. **En production** : SSL obligatoire, TURN prive recommande, monitoring actif

Pour toute question, consultez le code source :
- Serveur : `Server/server.js`
- Client : `Assets/Scrips/Network/VRNetworkManager.cs`
