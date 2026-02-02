# Architecture Serveur - VR Meeting Rooms

Document technique decrivant l'etat actuel du serveur WebSocket pour l'application VR Meeting Rooms.

> **Derniere mise a jour : 2026-02-02** - Synchronise avec le code source actuel.

---

## Vue d'Ensemble

### Stack Technique Actuelle

| Composant | Technologie | Version |
|-----------|-------------|---------|
| Runtime | Node.js | >= 16 (22 LTS recommande) |
| WebSocket | ws | 8.14.2 |
| UUID | uuid | 9.0.1 |
| PDF (optionnel) | pdf-poppler | 0.2.3 |
| Tests | Jest | 30.2.0 (dev) |

---

## Architecture Serveur

### Structure des Fichiers

```
Server/
├── server.js           # Serveur principal (887 lignes)
├── filePresentation.js # Conversion PDF (257 lignes, optionnel)
├── package.json        # Dependances npm
└── node_modules/       # Dependances installees
```

### Fonctionnalites Actives (server.js)

| Fonctionnalite | Implementation | Statut |
|----------------|----------------|--------|
| Connexion WebSocket | `ws` library, port 8080 | Actif |
| Gestion des rooms | Create, join, leave, close, update, kick | Actif |
| Sync position VR | Broadcast 30Hz par room | Actif |
| Sync objets interactifs | `obj-sync`, `obj-state` par room | Actif |
| Whiteboard | Batch drawing, clear, state sync | Actif |
| Screen sharing | WebSocket frames + WebRTC signaling | Actif |
| File sharing | Chunks, announce, complete, list | Actif |
| File presentation | Start, navigate, stop, state sync | Actif |
| PDF conversion | Via filePresentation.js + cache | Optionnel |
| Voice chat | WebRTC offer/answer/ICE relay | Actif |
| Kick player | Host only, verification autorite | Actif |
| Heartbeat | Ping 30s, timeout 60s | Actif |
| Status periodique | Log toutes les 60s | Actif |
| Nettoyage cache PDF | Toutes les 5 minutes, TTL 30 min | Actif |
| Graceful shutdown | SIGINT handler | Actif |

### Configuration Actuelle

| Variable | Valeur par Defaut | Source |
|----------|-------------------|--------|
| `PORT` | 8080 | `process.env.PORT` |
| `HEARTBEAT_INTERVAL` | 30000 ms | Code en dur |
| `PDF_CACHE_TTL` | 30 min | Code en dur |
| Max players/room | 10 | `handleRoomAvailable` |

### Sortie Console au Demarrage

```
============================================
  VR MEETING ROOMS - WebSocket Server
============================================
  Port: 8080
  Heartbeat: 30s
============================================
[Server] filePresentation module loaded    (ou "not available")
```

### Logs Periodiques

Toutes les 60 secondes :
```
[Status] 3 clients | 2 rooms
```

### Intervalles de Maintenance

| Intervalle | Frequence | Action |
|-----------|-----------|--------|
| Heartbeat | 30s | Ping clients, timeout 60s |
| Cache PDF | 5 min | Nettoyage TTL 30 min |
| Status log | 60s | Affichage clients/rooms |

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

### Types de Messages Geres (46 types explicites)

| Categorie | Types | Routage |
|-----------|-------|---------|
| Connexion | `welcome`, `peer-connected`, `peer-disconnected` | Global (generes par serveur) |
| Room Lifecycle | `room-available`, `room-closed`, `room-join`, `room-leave`, `room-update`, `room-list-request` | Fonctions dediees |
| Room State | `room-welcome`, `room-teleport`, `player-name-update` | broadcastToRoom |
| Position VR | `vr-position`, `position` | broadcastToRoom |
| Objets Interactifs | `obj-sync`, `obj-state` | broadcastToRoom |
| Whiteboard | `whiteboard-draw`, `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request` | broadcastToRoom |
| Whiteboard State | `whiteboard-state` | Point-to-point (targetId) |
| Voice WebRTC | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Point-to-point |
| Screen Share | `screen-share-start`, `screen-share-stop`, `screen-share-frame`, `screen-share-request`, `screen-share-state` | broadcastToRoom |
| Screen WebRTC | `screen-video-offer`, `screen-video-answer`, `screen-video-ice` | Point-to-point |
| File Share | `file-announce`, `file-chunk`, `file-complete`, `file-request`, `file-list-request`, `file-list-response` | broadcastToRoom / Point-to-point |
| File Presentation | `file-present-start`, `file-present-page`, `file-present-navigate`, `file-present-stop`, `file-present-request`, `file-present-state` | broadcastToRoom / Point-to-point |
| PDF | `pdf-convert-request`, `pdf-page-request` | Fonctions dediees (reponse directe) |
| Admin | `kick-player` | Point-to-point (host only) |
| Default | Tout autre type | broadcastToRoom si en room, sinon broadcast global |

### Messages Generes par le Serveur

| Type | Declencheur | Destinataire |
|------|-------------|--------------|
| `welcome` | Connexion client | Client connecte |
| `peer-connected` | Connexion client | Tous sauf l'emetteur |
| `peer-disconnected` | Deconnexion client | Tous sauf l'emetteur |
| `room-list` | Changement rooms | Client specifique ou tous |
| `room-available` | Creation room | Tous |
| `room-closed` | Fermeture room | Tous |
| `error` | Requete invalide | Client concerne |
| `pdf-convert-response` | Conversion terminee | Client demandeur |
| `pdf-page-response` | Page PDF prete | Client demandeur |

---

## Securite Actuelle

### Points Forts

| Aspect | Implementation |
|--------|----------------|
| Isolation rooms | Messages filtres par `roomId` via `broadcastToRoom` |
| ID forge | `message.senderId` force cote serveur (ecrase la valeur client) |
| Kick authority | Verification `room.hostId === clientId` |
| Timeout clients | Deconnexion automatique apres 60s sans pong |
| Capacite rooms | Rejet si `room.playerCount >= room.maxPlayers` |
| Validation JSON | Try/catch sur tous les handlers de messages |
| Etat WebSocket | Verification `readyState === OPEN` avant chaque envoi |
| Rate limiting client | Token bucket 60 msg/s dans VRNetworkManager.cs (cote Unity) |

### Points a Considerer

| Aspect | Etat Actuel | Risque |
|--------|-------------|--------|
| Chiffrement | Non (ws://) | Moyen - donnees en clair |
| Authentification | Aucune | Eleve - acces anonyme |
| Rate limiting serveur | Aucun cote serveur | Moyen - spam possible |
| Validation donnees | Basique (JSON parse) | Faible |
| Limite taille messages | Aucune | Moyen - gros payloads possibles |
| Validation origine | Aucune (CORS) | Moyen - toute origine acceptee |
| Persistance logs | Console uniquement | Moyen - perte au redemarrage |
| Sanitization input | Aucune (noms joueurs, etc.) | Faible |

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
| Serveur TURN | Prive (Twilio/Xirsys) | Voice chat en entreprise |

---

## WebRTC et Serveur TURN

### Pourquoi un Serveur TURN ?

Le voice chat et le screen share utilisent WebRTC pour les flux audio/video en peer-to-peer (P2P). Le serveur WebSocket (`server.js`) ne sert que de **relais de signaling** (offer/answer/ICE candidates) - les flux media ne passent jamais par lui.

Le processus de connexion WebRTC suit cet ordre :

```
Client A                    STUN Server                   Client B
   |                            |                             |
   |-- Requete STUN ---------->|                             |
   |<-- IP publique + port ----|                             |
   |                                                          |
   |=============== Connexion P2P directe ===================|
   |                    (UDP, cas ideal)                      |
```

Quand le P2P direct echoue (firewall, NAT symetrique, VPN), le TURN prend le relais :

```
Client A                    TURN Server                   Client B
   |                            |                             |
   |-- Flux media (relaye) --->|--- Flux media (relaye) ---->|
   |<-- Flux media (relaye) ---|<-- Flux media (relaye) -----|
   |                            |                             |
```

### Quand le TURN est-il Necessaire ?

| Scenario | P2P Direct | TURN Necessaire |
|----------|------------|-----------------|
| Meme reseau local (LAN) | Oui | Non |
| Reseau domestique standard | Generalement oui | Rarement |
| Reseau entreprise avec firewall | Rarement | **Oui** |
| NAT symetrique | Non | **Oui** |
| VPN corporatif | Rarement | **Oui** |
| Reseaux mobiles (4G/5G) | Variable | Souvent |

> **En entreprise, 20-30% des connexions WebRTC necessitent un TURN.** Sans lui, ces utilisateurs n'auront pas de voice chat ni de screen share WebRTC.

### Configuration Actuelle (Developpement)

Les serveurs ICE sont configures dans `VoiceChatManager.cs` :

```
STUN (publics, toujours inclus) :
  - stun:stun.l.google.com:19302
  - stun:stun1.l.google.com:19302
  - stun:stun.cloudflare.com:3478

TURN (dev only, NON FIABLE en production) :
  - turn:openrelay.metered.ca:443
```

### coturn (Self-Hosted)

Serveur TURN open source. Cout = cout serveur uniquement.

**Prerequis serveur :**

| Composant | Specification |
|-----------|---------------|
| OS | Ubuntu 22.04+ / Debian 12+ |
| RAM | 512 MB minimum (1 GB recommande) |
| CPU | 1 vCPU |
| Bande passante | 1 Mbps par connexion relayee |
| Ports | 3478 (UDP+TCP), 443 (TLS), 49152-65535 (relay UDP) |
| IP publique | Requise |

**Installation :**

```bash
# Installation
sudo apt update
sudo apt install coturn

# Activer le service
sudo sed -i 's/#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn
```

**Configuration (`/etc/turnserver.conf`) :**

```ini
# Reseau
listening-port=3478
tls-listening-port=5349
listening-ip=0.0.0.0
external-ip=YOUR_PUBLIC_IP
relay-ip=YOUR_PUBLIC_IP
min-port=49152
max-port=65535

# Authentification
realm=your-domain.com
use-auth-secret
static-auth-secret=YOUR_STRONG_SECRET_HERE

# TLS (recommande)
cert=/etc/letsencrypt/live/turn.your-domain.com/fullchain.pem
pkey=/etc/letsencrypt/live/turn.your-domain.com/privkey.pem

# Securite
no-multicast-peers
no-cli
denied-peer-ip=10.0.0.0-10.255.255.255
denied-peer-ip=172.16.0.0-172.31.255.255
denied-peer-ip=192.168.0.0-192.168.255.255

# Limites
total-quota=100
stale-nonce=600
max-bps=1048576

# Logs
log-file=/var/log/turnserver.log
simple-log
```

**Demarrage :**

```bash
sudo systemctl enable coturn
sudo systemctl start coturn
sudo systemctl status coturn

# Verifier que les ports sont ouverts
sudo ss -tulnp | grep turnserver
```

**Regles firewall :**

```bash
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 5349/udp
sudo ufw allow 49152:65535/udp
```

**Test avec Trickle ICE :**

Ouvrir https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/ et ajouter :
- `turn:YOUR_PUBLIC_IP:3478` avec username/credential

### Configuration Unity (Production)

Dans le Unity Inspector sur `VoiceChatManager` :

```csharp
useCustomTurnServer = true;
customTurnUrl = "turn:turn.your-domain.com:3478";
customTurnUsername = ""; // genere dynamiquement si use-auth-secret
customTurnCredential = ""; // genere dynamiquement si use-auth-secret
enableTurnTcp = true;  // Fallback TCP pour firewalls restrictifs
```

> **Note :** Avec `use-auth-secret` dans coturn, les credentials sont temporaires (HMAC-SHA1). Il faudra implementer la generation cote serveur Node.js et les transmettre aux clients via WebSocket. Pour un deploiement initial, utiliser `lt-cred-mech` avec un username/password fixe est plus simple.

**Alternative simple (credentials fixes) :**

Remplacer dans `/etc/turnserver.conf` :
```ini
# Remplacer use-auth-secret par :
lt-cred-mech
user=vrmeeting:YOUR_PASSWORD
```

Puis dans Unity :
```csharp
customTurnUsername = "vrmeeting";
customTurnCredential = "YOUR_PASSWORD";
```

### Monitoring coturn

```bash
# Logs en temps reel
sudo tail -f /var/log/turnserver.log

# Connexions actives
sudo ss -tunp | grep turnserver | wc -l

# Statistiques systeme
sudo systemctl status coturn
```

### Resume WebRTC

| Composant | Role | Necessaire en Production |
|-----------|------|--------------------------|
| STUN (public) | Decouverte IP publique | Oui (deja configure) |
| TURN (prive) | Relais media quand P2P impossible | **Oui pour entreprise** |
| Serveur WebSocket | Signaling uniquement (offer/answer/ICE) | Oui (deja en place) |

---

## Integration Unity

### Parametres VRNetworkManager (460 lignes)

| Parametre | Valeur Actuelle | Description |
|-----------|-----------------|-------------|
| `serverUrl` | `ws://localhost:8080` | URL du serveur |
| `enforceSecureConnection` | `false` | Force `wss://` si `true` |
| `autoReconnect` | `true` | Reconnexion automatique |
| `welcomeTimeout` | 5s | Timeout message welcome |
| `maxMessagesPerSecond` | 60 | Rate limiting client (token bucket) |
| `burstAllowance` | 10 | Rafale autorisee |
| `initialReconnectDelay` | 1s | Delai initial reconnexion |
| `maxReconnectDelay` | 30s | Delai max reconnexion |
| `backoffMultiplier` | 2x | Multiplicateur exponentiel |

### Parametres VoiceChatManager (1139 lignes)

| Parametre | Valeur Actuelle | Description |
|-----------|-----------------|-------------|
| `useCustomTurnServer` | `false` | TURN prive |
| `usePushToTalk` | `true` | Mode push-to-talk |
| `use3DAudio` | `true` | Audio spatial 3D |
| `maxAudioDistance` | 20m | Distance max audio |
| `peerConnectionTimeout` | 15s | Timeout connexion WebRTC |
| STUN servers | Google (x2), CloudFlare | Publics |
| TURN servers | openrelay.metered.ca | Public (dev only) |

### Parametres VRGameManager (1888 lignes)

| Parametre | Valeur Actuelle | Description |
|-----------|-----------------|-------------|
| `syncRate` | 30Hz | Frequence sync position |
| `interpolationSpeed` | 15 | Vitesse interpolation reseau |
| `movementThreshold` | 0.01m | Seuil mouvement (optimisation) |
| `rotationThreshold` | 1 degre | Seuil rotation (optimisation) |

---

## Statistiques du Projet Unity

### Scripts Principaux

| Script | Lignes | Role |
|--------|--------|------|
| VRNetworkManager.cs | 460 | Client WebSocket, connexion, rate limiting |
| VRRoomManager.cs | 931 | Rooms, joueurs, zones, avatar sync |
| VRGameManager.cs | 1888 | Spawn, sync 30Hz, interpolation, teleport |
| VoiceChatManager.cs | 1139 | WebRTC mesh, audio spatial, push-to-talk |
| BootstrapManager.cs | 292 | Scene flow, XR init, singletons |
| AvatarCustomization.cs | 315 | Couleurs, username, UI |
| DebugManager.cs | 169 | Logging par categorie |
| LaserPointer.cs | 338 | Pointeur laser reseau 10Hz |
| Whiteboard (12 fichiers) | ~4158 | Dessin, UI, reseau, gomme |
| Sharing (8 fichiers) | ~4185 | Ecran, fichiers, presentation |
| UI/Menu (~14 fichiers) | ~2000+ | Menu principal + menu VR in-game |
| VR Modules (~10 fichiers) | ~2000+ | Controllers, tracking, input |
| **Total** | **~61 fichiers** | **~28,765 lignes** |

---

## Metriques Serveur

### Statistiques Disponibles

| Metrique | Acces | Frequence |
|----------|-------|-----------|
| Nombre clients | Console log | 60s |
| Nombre rooms | Console log | 60s |
| Connexions/deconnexions | Console log | Temps reel |
| Room events | Console log | Temps reel |
| Kick events | Console log | Temps reel |
| Erreurs | Console error | Temps reel |

### Exemple de Logs

```
[Connect] Client a1b2c3d4...
[Room] Created: XYZ789
[Room] Join: e5f6g7h8 -> XYZ789
[Status] 2 clients | 1 rooms
[Kick] Host a1b2c3d4 kicked e5f6g7h8 from XYZ789
[Timeout] Client a1b2c3d4...
[Disconnect] Client a1b2c3d4...
```

---

## Resume

### Ce qui Fonctionne Actuellement

- Serveur WebSocket standalone
- Gestion complete des rooms (create, join, leave, close, update, kick)
- Sync position VR 30Hz avec interpolation
- Sync objets interactifs (`obj-sync`, `obj-state`)
- Whiteboard collaboratif (dessin, gomme, batch, clear, state sync)
- Partage d'ecran (WebSocket frames + WebRTC signaling)
- Partage de fichiers (chunks, announce, complete, list)
- Presentation de fichiers (navigation, state sync)
- Conversion PDF (cache 30 min, nettoyage automatique)
- Chat vocal WebRTC (mesh topology, audio spatial 3D)
- Pointeur laser reseau (10Hz)
- Avatar customization (couleur, nom)
- Rate limiting client (token bucket 60 msg/s)
- Heartbeat et gestion des timeouts (60s)
- Reconnexion automatique avec backoff exponentiel
- Graceful shutdown (SIGINT)

### Ce qui Manque pour Production

- Configuration SSL/TLS (wss://)
- Reverse proxy (Nginx)
- Service manager (systemd/PM2)
- Serveur TURN prive (voice chat entreprise)
- Rate limiting cote serveur
- Validation/sanitization des donnees
- Limite de taille des messages
- Monitoring/alerting
- Persistance des logs

---

## Changelog

| Date | Version | Description |
|------|---------|-------------|
| 2025-01-26 | 1.0 | Documentation initiale |
| 2026-02-02 | 2.0 | Mise a jour complete : correction line counts, ajout 46 types de messages, ajout file presentation/PDF/objets interactifs, stats Unity mises a jour |
| 2026-02-02 | 2.1 | Suppression des sections base de donnees (Phase 3 non implementee) |

---

## References

- [ENTERPRISE_DEPLOYMENT_GUIDE.md](./ENTERPRISE_DEPLOYMENT_GUIDE.md) - English version
- [LOCAL_TESTING_GUIDE.md](./LOCAL_TESTING_GUIDE.md) - Test local (WSL2, VM, LAN)
- [SERVER_ARCHITECTURE.md](./SERVER_ARCHITECTURE.md) - Details techniques serveur
- [NETWORKING_CODE_EXPLAINED.md](./NETWORKING_CODE_EXPLAINED.md) - Code annote
- [SERVER_ARCHITECTURE_KO.md](./SERVER_ARCHITECTURE_KO.md) - Version coreenne
- [CLAUDE.md](../CLAUDE.md) - Instructions projet
