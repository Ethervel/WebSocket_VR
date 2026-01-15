# Documentation Serveur - VR Meeting Platform

## Vue d'ensemble

Cette plateforme est une application de réunion VR multijoueur utilisant :
- **Client** : Unity 6000.2.14f1 (Quest, PCVR, Desktop)
- **Serveur** : Node.js + WebSocket (port 8080)
- **Base de données** : MariaDB (authentification, persistance utilisateurs)
- **Voix** : WebRTC (mesh P2P)

---

## 1. Architecture Réseau

```
┌─────────────────────────────────────────────────────────────────┐
│                        SERVEUR NODE.JS                          │
│                         (port 8080)                             │
├─────────────────────────────────────────────────────────────────┤
│  WebSocket Server                                               │
│  ├── Clients Map: clientId -> {ws, roomId, playerName}         │
│  ├── Rooms Map: roomId -> RoomInfo                             │
│  └── Message Router (switch sur type)                          │
├─────────────────────────────────────────────────────────────────┤
│  MariaDB Connection (auth.js)                                   │
│  └── Table: users (authentification)                           │
└─────────────────────────────────────────────────────────────────┘
          │                    │                    │
          ▼                    ▼                    ▼
    ┌─────────┐          ┌─────────┐          ┌─────────┐
    │ Client  │          │ Client  │          │ Client  │
    │  Unity  │◄────────►│  Unity  │◄────────►│  Unity  │
    │  (VR)   │  WebRTC  │(Desktop)│  WebRTC  │  (VR)   │
    └─────────┘  (Voix)  └─────────┘  (Voix)  └─────────┘
```

### Flux de données
1. **WebSocket** : Tous les messages passent par le serveur (routage, broadcast)
2. **WebRTC** : Connexions P2P directes pour la voix (mesh topology)
3. **MariaDB** : Authentification uniquement (login/register)

---

## 2. Format des Messages

Tous les messages WebSocket suivent ce format JSON :

```json
{
  "type": "message-type",
  "senderId": "client-uuid",
  "data": "{\"key\":\"value\"}"  // JSON stringifié
}
```

> **Important** : Le champ `data` est toujours une chaîne JSON (pas un objet imbriqué) pour compatibilité avec Unity JsonUtility.

---

## 3. Messages Client → Serveur

### 3.1 Connexion & Authentification

| Type | Payload | Description |
|------|---------|-------------|
| `auth-register` | `{username, email, password, displayName}` | Inscription nouvel utilisateur |
| `auth-login` | `{username, password}` | Connexion utilisateur |
| `auth-update-profile` | `{displayName, avatarColor}` | Mise à jour profil |

### 3.2 Gestion des Rooms

| Type | Payload | Description |
|------|---------|-------------|
| `room-available` | `{roomId, hostId, roomName, roomType, maxPlayers}` | Hôte annonce création room |
| `room-join` | `{roomId, playerId, playerName, colorR, colorG, colorB}` | Joueur rejoint room |
| `room-leave` | `{roomId, playerId}` | Joueur quitte room |
| `room-list-request` | `{}` | Demande liste des rooms |
| `room-teleport` | `{roomId, playerId, targetRoomType}` | Changement de zone |
| `room-closed` | `{roomId}` | Hôte ferme la room |

### 3.3 Synchronisation VR (30 Hz)

| Type | Payload | Description |
|------|---------|-------------|
| `vr-position` | Voir structure ci-dessous | Position corps/tête/mains |

```json
{
  "roomId": "ABCDEF",
  "roomType": 1,
  "posX": 0.0, "posY": 1.0, "posZ": 0.0,
  "rotY": 45.0,
  "headPosX": 0.0, "headPosY": 1.7, "headPosZ": 0.0,
  "headRotX": 0.0, "headRotY": 0.0, "headRotZ": 0.0, "headRotW": 1.0,
  "leftHandPosX": -0.3, "leftHandPosY": 1.0, "leftHandPosZ": 0.2,
  "leftHandRotX": 0.0, "leftHandRotY": 0.0, "leftHandRotZ": 0.0, "leftHandRotW": 1.0,
  "rightHandPosX": 0.3, "rightHandPosY": 1.0, "rightHandPosZ": 0.2,
  "rightHandRotX": 0.0, "rightHandRotY": 0.0, "rightHandRotZ": 0.0, "rightHandRotW": 1.0
}
```

### 3.4 WebRTC Signaling (Voix)

| Type | Payload | Description |
|------|---------|-------------|
| `webrtc-offer` | `{targetId, sdp}` | Offre SDP (initiateur) |
| `webrtc-answer` | `{targetId, sdp}` | Réponse SDP |
| `webrtc-ice-candidate` | `{targetId, candidate, sdpMid, sdpMLineIndex}` | Candidat ICE |

### 3.5 Whiteboard

| Type | Payload | Description |
|------|---------|-------------|
| `whiteboard-batch` | `{whiteboardId, roomId, r, g, b, a, penSize, pointsFlat[]}` | Traits de dessin |
| `whiteboard-clear` | `{whiteboardId, roomId, senderId}` | Effacer tableau |
| `whiteboard-request` | `{whiteboardId, roomId, requesterId}` | Demande état (late join) |

### 3.6 Screen Share

| Type | Payload | Description |
|------|---------|-------------|
| `screen-share-start` | `{roomId, whiteboardId, sharerId, sharerName, width, height}` | Début partage |
| `screen-share-frame` | `{roomId, whiteboardId, sharerId, imageData, frameIndex}` | Frame JPEG base64 |
| `screen-share-stop` | `{roomId, whiteboardId, sharerId}` | Fin partage |
| `screen-share-request` | `{roomId, whiteboardId, requesterId}` | Demande état |

---

## 4. Messages Serveur → Client

### 4.1 Connexion

| Type | Payload | Description |
|------|---------|-------------|
| `welcome` | `{senderId: "uuid"}` | Attribution ID client |
| `peer-connected` | `{senderId: "peer-uuid"}` | Nouveau peer connecté |
| `peer-disconnected` | `{senderId: "peer-uuid"}` | Peer déconnecté |

### 4.2 Authentification

| Type | Payload | Description |
|------|---------|-------------|
| `auth-register-response` | `{success, error, userId, username}` | Résultat inscription |
| `auth-login-response` | `{success, error, userId, username, email, displayName, avatarColor}` | Résultat connexion |
| `auth-update-response` | `{success, error}` | Résultat mise à jour |

### 4.3 Rooms

| Type | Payload | Description |
|------|---------|-------------|
| `room-list` | `{rooms: [{roomId, hostId, roomName, playerCount, maxPlayers}]}` | Liste rooms disponibles |
| `room-welcome` | `{roomId, roomType, players: [{playerId, playerName, isHost}]}` | Confirmation join + liste joueurs |

### 4.4 Whiteboard

| Type | Payload | Description |
|------|---------|-------------|
| `whiteboard-state` | `{whiteboardId, roomId, textureData, width, height}` | État complet (PNG base64) |

### 4.5 Screen Share

| Type | Payload | Description |
|------|---------|-------------|
| `screen-share-state` | `{roomId, whiteboardId, isSharing, sharerId, sharerName}` | État partage |

---

## 5. Routage des Messages

Le serveur route les messages selon leur portée :

| Portée | Comportement | Messages concernés |
|--------|--------------|-------------------|
| **Global** | Broadcast à tous les clients | `welcome`, `peer-*`, `room-available` |
| **Room** | Broadcast aux clients de la même room | `vr-position`, `whiteboard-*`, `screen-share-*` |
| **Point-to-Point** | Envoi à un client spécifique | `webrtc-*`, `auth-*-response` |

### Fonction de broadcast room (pseudo-code)

```javascript
function broadcastToRoom(senderId, message) {
  const senderRoom = clients.get(senderId).roomId;

  for (const [clientId, client] of clients) {
    if (clientId !== senderId &&
        client.roomId === senderRoom &&
        client.ws.readyState === OPEN) {
      client.ws.send(JSON.stringify(message));
    }
  }
}
```

---

## 6. Structure Base de Données (MariaDB)

### Table `users`

```sql
CREATE TABLE users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  username VARCHAR(50) UNIQUE NOT NULL,
  email VARCHAR(100) UNIQUE NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  display_name VARCHAR(100),
  avatar_color VARCHAR(20) DEFAULT '#3498db',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  last_login TIMESTAMP NULL,

  INDEX idx_username (username),
  INDEX idx_email (email)
);
```

### Table `rooms` (proposée - non implémentée)

```sql
CREATE TABLE rooms (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_code VARCHAR(6) UNIQUE NOT NULL,
  room_name VARCHAR(100),
  host_id INT NOT NULL,
  room_type ENUM('Lobby', 'MeetingRoomA', 'MeetingRoomB') DEFAULT 'Lobby',
  max_players INT DEFAULT 10,
  is_active BOOLEAN DEFAULT TRUE,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  closed_at TIMESTAMP NULL,

  FOREIGN KEY (host_id) REFERENCES users(id),
  INDEX idx_room_code (room_code),
  INDEX idx_active (is_active)
);
```

### Table `room_participants` (proposée - non implémentée)

```sql
CREATE TABLE room_participants (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  user_id INT NOT NULL,
  joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  left_at TIMESTAMP NULL,

  FOREIGN KEY (room_id) REFERENCES rooms(id),
  FOREIGN KEY (user_id) REFERENCES users(id),
  INDEX idx_room_user (room_id, user_id)
);
```

### Table `meetings` (proposée - pour historique)

```sql
CREATE TABLE meetings (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  title VARCHAR(200),
  started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  ended_at TIMESTAMP NULL,
  recording_url VARCHAR(500) NULL,

  FOREIGN KEY (room_id) REFERENCES rooms(id)
);
```

---

## 7. Configuration Serveur

### Variables d'environnement

```env
# Server
PORT=8080
NODE_ENV=production

# Database
DB_HOST=localhost
DB_PORT=3306
DB_USER=vr_meeting_user
DB_PASSWORD=secure_password_here
DB_NAME=vr_meeting

# Security
BCRYPT_SALT_ROUNDS=10
```

### Dépendances Node.js

```json
{
  "dependencies": {
    "ws": "^8.x",
    "mysql2": "^3.x",
    "bcrypt": "^5.x",
    "uuid": "^9.x",
    "dotenv": "^16.x"
  }
}
```

---

## 8. Déploiement

### Prérequis
- Node.js 18+ LTS
- MariaDB 10.6+
- Ports ouverts : 8080 (WebSocket), 3306 (MariaDB)

### Étapes

1. **Cloner le repository**
```bash
git clone <repo-url>
cd Server
```

2. **Installer les dépendances**
```bash
npm install
```

3. **Configurer la base de données**
```bash
mysql -u root -p < schema.sql
```

4. **Configurer les variables d'environnement**
```bash
cp .env.example .env
# Éditer .env avec les valeurs de production
```

5. **Démarrer le serveur**
```bash
# Développement
npm run dev

# Production (avec PM2)
pm2 start server.js --name vr-meeting-server
```

### Configuration Nginx (reverse proxy)

```nginx
server {
    listen 443 ssl;
    server_name meeting.entreprise.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location /ws {
        proxy_pass http://localhost:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400;
    }
}
```

---

## 9. Sécurité

### Implémenté
- Mots de passe hashés avec bcrypt (10 rounds)
- WebSocket sur WSS en production (via reverse proxy)

### À implémenter (Phase 3)
- [ ] JWT pour sessions authentifiées
- [ ] Rate limiting sur les messages
- [ ] Validation des payloads côté serveur
- [ ] Chiffrement E2E pour la voix (SRTP)
- [ ] CORS configuration
- [ ] Audit logs

---

## 10. Monitoring

### Logs recommandés
- Connexions/déconnexions clients
- Création/fermeture rooms
- Erreurs d'authentification
- Métriques de latence

### Métriques à surveiller
- Nombre de clients connectés
- Nombre de rooms actives
- Bande passante WebSocket
- Latence moyenne des messages

---

## Annexe : Codes Room

Les codes de room sont générés avec :
- 6 caractères alphanumériques
- Charset : `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (sans O/0, I/1)
- Exemple : `ABCDEF`, `X7K9M2`

---

*Document généré le 15/01/2026 - Version 1.0*
