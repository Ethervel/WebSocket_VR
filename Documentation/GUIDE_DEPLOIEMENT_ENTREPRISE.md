# Architecture Serveur - VR Meeting Rooms

Document technique decrivant l'etat actuel du serveur WebSocket pour l'application VR Meeting Rooms.

---

## Vue d'Ensemble

### Stack Technique Actuelle

| Composant | Technologie | Version |
|-----------|-------------|---------|
| Runtime | Node.js | >= 16 (18 LTS recommande) |
| WebSocket | ws | 8.14.2 |
| UUID | uuid | 9.0.1 |
| PDF (optionnel) | pdf-poppler | 0.2.3 |

### Dependances Non Utilisees (Preparees)

| Module | Etat | Description |
|--------|------|-------------|
| `auth.js` | NON CONNECTE | Authentification bcrypt + MariaDB |
| `db.js` | NON CONNECTE | Pool de connexions MariaDB |
| `mysql2` | NON INSTALLE | Driver MariaDB |
| `bcrypt` | NON INSTALLE | Hachage mots de passe |
| `dotenv` | NON INSTALLE | Variables d'environnement |

---

## Architecture Serveur

### Structure des Fichiers

```
Server/
├── server.js           # Serveur principal (888 lignes)
├── package.json        # Dependances npm
├── auth.js             # Module auth (NON CONNECTE)
├── db.js               # Module DB (NON CONNECTE)
└── filePresentation.js # Conversion PDF (optionnel)
```

### Fonctionnalites Actives (server.js)

| Fonctionnalite | Implementation | Statut |
|----------------|----------------|--------|
| Connexion WebSocket | `ws` library, port 8080 | Actif |
| Gestion des rooms | Create, join, leave, close | Actif |
| Sync position VR | Broadcast 30Hz par room | Actif |
| Whiteboard | Batch drawing, clear, state sync | Actif |
| Screen sharing | WebSocket frames + WebRTC signaling | Actif |
| File sharing | Chunks, announce, complete | Actif |
| Voice chat | WebRTC offer/answer/ICE relay | Actif |
| Kick player | Host only | Actif |
| Heartbeat | Ping 30s, timeout 60s | Actif |
| Graceful shutdown | SIGINT handler | Actif |
| PDF presentation | Via filePresentation.js | Optionnel |
| Authentification | Via auth.js | NON CONNECTE |

### Configuration Actuelle

| Variable | Valeur par Defaut | Source |
|----------|-------------------|--------|
| `PORT` | 8080 | `process.env.PORT` |
| `HEARTBEAT_INTERVAL` | 30000 ms | Code en dur |
| `PDF_CACHE_TTL` | 30 min | Code en dur |

### Sortie Console au Demarrage

```
============================================
  VR MEETING ROOMS - WebSocket Server
============================================
  Port: 8080
  Heartbeat: 30s
============================================
```

### Logs Periodiques

Toutes les 60 secondes :
```
[Status] 3 clients | 2 rooms
```

---

## Protocole de Communication

### Format des Messages

```json
{
    "type": "message-type",
    "senderId": "uuid-client",
    "data": "{\"json\":\"serialise\"}"
}
```

### Types de Messages Geres

| Categorie | Types | Routage |
|-----------|-------|---------|
| Connexion | `welcome`, `peer-connected`, `peer-disconnected` | Global |
| Rooms | `room-available`, `room-join`, `room-leave`, `room-closed`, `room-list` | Global/Room |
| Position VR | `vr-position`, `position` | Room only |
| Whiteboard | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request`, `whiteboard-state` | Room only |
| Screen share | `screen-share-start/stop/frame/request/state` | Room only |
| File share | `file-announce`, `file-chunk`, `file-complete`, `file-request` | Room only |
| Voice WebRTC | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Point-to-point |
| Admin | `kick-player` | Point-to-point |

---

## Securite Actuelle

### Points Forts

| Aspect | Implementation |
|--------|----------------|
| Isolation rooms | Messages filtres par `roomId` |
| ID forge | `senderId` force cote serveur |
| Kick authority | Seul le host peut kick |
| Timeout clients | Deconnexion apres 60s sans pong |

### Points a Considerer

| Aspect | Etat Actuel | Risque |
|--------|-------------|--------|
| Chiffrement | Non (ws://) | Moyen - donnees en clair |
| Authentification | Aucune | Eleve - acces anonyme |
| Rate limiting | Aucun cote serveur | Moyen - spam possible |
| Validation donnees | Basique (JSON parse) | Faible |

---

## Exigences Production

### Minimum Requis

| Composant | Specification |
|-----------|---------------|
| Node.js | >= 16 |
| RAM | 512 MB minimum |
| CPU | 1 vCPU |
| Reseau | Port 8080 accessible |

### Recommande pour Production

| Composant | Specification | Raison |
|-----------|---------------|--------|
| Reverse proxy | Nginx | SSL termination, WebSocket upgrade |
| SSL/TLS | Certificat valide | Chiffrement `wss://` |
| Process manager | systemd ou PM2 | Restart automatique |
| Monitoring | journalctl / logs | Debug et audit |

---

## Integration Unity

### Parametres VRNetworkManager

| Parametre | Valeur Actuelle | Description |
|-----------|-----------------|-------------|
| `serverUrl` | `ws://localhost:8080` | URL du serveur |
| `enforceSecureConnection` | `false` | Force `wss://` si `true` |
| `autoReconnect` | `true` | Reconnexion automatique |
| `welcomeTimeout` | 5s | Timeout message welcome |
| `maxMessagesPerSecond` | 60 | Rate limiting client |

### Parametres VoiceChatManager (WebRTC)

| Parametre | Valeur Actuelle | Description |
|-----------|-----------------|-------------|
| `useCustomTurnServer` | `false` | TURN prive |
| STUN servers | Google, CloudFlare | Publics |
| TURN servers | openrelay.metered.ca | Public (dev only) |

---

## Modules Prepares Non Connectes

### auth.js

| Fonction | Description | Dependances Requises |
|----------|-------------|---------------------|
| `registerUser()` | Inscription avec bcrypt | mysql2, bcrypt |
| `loginUser()` | Connexion + verification | mysql2, bcrypt |
| `updateUserProfile()` | Mise a jour profil | mysql2 |

### db.js

| Configuration | Valeur par Defaut |
|---------------|-------------------|
| Host | localhost |
| Port | 3306 |
| User | root |
| Password | En dur dans le code |
| Database | vr_meeting |
| Connection pool | 10 connexions |

### Schema SQL Prevu (non cree)

```sql
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
```

---

## Metriques Serveur

### Statistiques Disponibles

| Metrique | Acces | Frequence |
|----------|-------|-----------|
| Nombre clients | Console log | 60s |
| Nombre rooms | Console log | 60s |
| Connexions/deconnexions | Console log | Temps reel |
| Erreurs | Console error | Temps reel |

### Exemple de Logs

```
[Connect] Client a1b2c3d4...
[Room] Created: XYZ789
[Room] Join: e5f6g7h8 -> XYZ789
[Status] 2 clients | 1 rooms
[Timeout] Client a1b2c3d4...
[Disconnect] Client a1b2c3d4...
```

---

## Resume

### Ce qui Fonctionne Actuellement

- Serveur WebSocket standalone (sans base de donnees)
- Gestion complete des rooms et sync VR
- Whiteboard collaboratif
- Partage d'ecran
- Partage de fichiers
- Chat vocal WebRTC (via signaling)
- Heartbeat et gestion des timeouts

### Ce qui est Prepare mais Non Actif

- Authentification utilisateurs (auth.js)
- Base de donnees MariaDB (db.js)
- Schema SQL pour users

### Ce qui Manque pour Production

- Configuration SSL/TLS (wss://)
- Reverse proxy (Nginx)
- Service manager (systemd/PM2)
- Serveur TURN prive (voice chat entreprise)
- Monitoring/alerting

---

## Changelog

| Date | Version | Auteur |
|------|---------|--------|
| 2025-01-26 | 1.0 | Documentation initiale |

---

## References

- [SERVER_ARCHITECTURE.md](./SERVER_ARCHITECTURE.md) - Details techniques
- [NETWORKING_CODE_EXPLAINED.md](./NETWORKING_CODE_EXPLAINED.md) - Code annote
- [CLAUDE.md](../CLAUDE.md) - Instructions projet
