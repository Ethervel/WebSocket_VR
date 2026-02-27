# Documentation de Fin de Stage - VR Meeting Rooms

## Table des matieres

1. [Presentation du projet](#presentation-du-projet)
2. [Historique de developpement](#historique-de-developpement)
3. [Architecture technique](#architecture-technique)
4. [Installation et execution](#installation-et-execution)
5. [Deploiement serveur](#deploiement-serveur)
6. [Configuration et parametres](#configuration-et-parametres)
7. [Fonctionnalites implementees](#fonctionnalites-implementees)
8. [Fonctionnalites non implementees / A faire](#fonctionnalites-non-implementees--a-faire)
9. [Pistes d'ameliorations futures](#pistes-dameliorations-futures)
10. [Structure du projet](#structure-du-projet)
11. [Ressources et documentation](#ressources-et-documentation)
12. [Troubleshooting / Depannage](#troubleshooting--depannage)

---

## Presentation du projet

**VR Meeting Rooms** est une application de salle de reunion virtuelle multijoueur developpee avec Unity 6000.2.14f1. Elle permet a plusieurs utilisateurs de se retrouver dans un environnement 3D immersif, que ce soit en VR (Quest, PCVR) ou en mode Desktop.

### Objectifs du projet
- Creer un espace de reunion virtuel accessible en VR et Desktop
- Permettre la communication vocale en temps reel
- Offrir des outils collaboratifs (tableau blanc, partage d'ecran, partage de fichiers)
- Supporter plusieurs salles de reunion simultanees

### Technologies utilisees
| Technologie | Utilisation |
|-------------|-------------|
| Unity 6000.2.14f1 | Moteur de jeu |
| WebSocket (NativeWebSocket) | Communication reseau temps reel |
| WebRTC | Chat vocal peer-to-peer |
| OpenXR | Support VR multi-plateforme |
| Node.js | Serveur backend |
| MariaDB | Base de donnees (non foncitonnel) |

---

## Historique de developpement

### Phase 1 : Fondations (16-17 Decembre 2025)
- Initialisation du projet Unity
- Configuration des packages XR Interaction Toolkit
- Creation du systeme reseau de base (VRNetworkManager)
- Implementation du systeme de rooms WebSocket
- Premiers prefabs joueur (local et remote)

### Phase 2 : Interface et Navigation (18-19 Decembre 2025)
- Creation du lobby et des salles de reunion (Room A, B)
- Implementation de la teleportation
- Interface utilisateur VR (panels, boutons)
- Systeme de spawn et gestion des positions

### Phase 3 : Communication (22-24 Decembre 2025)
- Integration WebRTC pour le chat vocal
- Implementation du tableau blanc basique
- Premiers tests de synchronisation

### Phase 4 : Tableau Blanc et Synchronisation (5-6 Janvier 2026)
- Correction de la synchronisation du tableau blanc
- Implementation du dessin multi-utilisateur
- Optimisation des performances reseau

### Phase 5 : Mode Desktop et Partage (12-14 Janvier 2026)
- Ajout du mode Desktop (clavier/souris)
- Partage d'ecran via WebRTC
- Partage de fichiers (PDF, images, documents)
- Personnalisation d'avatar

### Phase 6 : Optimisations et Polish (15-22 Janvier 2026)
- Optimisation des performances (batching, cache)
- Correction des fuites memoire
- Configuration TURN/STUN pour WebRTC
- Ajout du laser pointer

### Phase 7 : Menu Principal et Options (27-30 Janvier 2026)
- Creation du menu principal
- Systeme de parametres (audio, graphiques, VR)
- Ecran de chargement
- Nouveaux modeles d'avatar

### Phase 8 : Enregistrement et Authentification (Fevrier 2026)
- Systeme d'enregistrement des reunions
- Implementation de l'authentification (login/register)
- Optimisation VR pour eviter le motion sickness
- Sons ambiants et zones de mute
- Finalisation de l'interface utilisateur

---

## Architecture technique

### Structure des scenes
```
Bootstrap (Scene 0) - Persistante
    ├── Managers (DontDestroyOnLoad)
    │   ├── VRNetworkManager
    │   ├── VRRoomManager
    │   ├── VRGameManager
    │   ├── VoiceChatManager
    │   ├── SoundManager
    │   └── DebugManager
    └── MainMenuUI

Meet (Scene 1) - Chargee additivement
    ├── Lobby
    │   ├── SpawnPoint
    │   └── UI de teleportation
    ├── MeetingRoomA
    ├── MeetingRoomB
    └── Whiteboards
```

### Flux de connexion
```
Lancement → Bootstrap → Menu Principal → Authentification → Connexion WebSocket → Lobby → Salles
```

### Architecture reseau
```
┌─────────────┐     WebSocket      ┌─────────────┐     MariaDB      ┌─────────────┐
│   Client    │ ◄─────────────────►│   Serveur   │ ◄───────────────►│    BDD      │
│   Unity     │                    │   Node.js   │                  │  (optionnel)│
└─────────────┘                    └─────────────┘                  └─────────────┘
       │                                  │
       │           WebRTC (P2P)           │
       └──────────────────────────────────┘
              (Chat vocal direct)
```

---

## Installation et execution

### Prerequis
- Unity 6000.2.14f1 ou superieur
- Node.js >= 16.0.0
- (Optionnel) MariaDB pour l'authentification
- (Optionnel) FFmpeg pour l'enregistrement

### Installation du serveur

1. **Naviguer vers le dossier serveur :**
   ```bash
   cd Server/
   ```

2. **Installer les dependances :**
   ```bash
   npm install
   ```

3. **Configurer l'environnement (optionnel, pour l'authentification) :**
   ```bash
   cp .env.example .env
   # Editer .env avec vos parametres
   ```

4. **Lancer le serveur :**
   ```bash
   # Mode developpement (avec auto-reload)
   npm run dev

   # Mode production
   npm start
   ```

Le serveur ecoute sur `ws://localhost:8080` par defaut.

### Execution dans Unity

1. **Ouvrir le projet dans Unity 6000.2.14f1**

2. **Configurer l'URL du serveur :**
   - Ouvrir la scene `Bootstrap`
   - Selectionner `VRNetworkManager` dans la hierarchie
   - Dans l'Inspector, verifier `Server Url` (par defaut : `ws://localhost:8080`)

3. **Lancer le jeu :**
   - Appuyer sur Play dans l'editeur
   - Ou construire pour la plateforme ciblee

### Tests multi-utilisateurs (ParrelSync)
Pour tester avec plusieurs clients sur le meme PC :
1. Ouvrir le menu `ParrelSync > Clone Manager`
2. Creer un clone du projet
3. Ouvrir le clone dans une nouvelle instance Unity
4. Lancer les deux instances

---

## Deploiement serveur

Cette section explique comment deployer le serveur en environnement de production. Deux guides detailles sont disponibles dans le dossier `Documentation/` :
- `DEPLOYMENT_LAN_GUIDE.md` - Deploiement sur reseau local (VM VirtualBox)
- `DEPLOYMENT_PUBLIC_GUIDE.md` - Deploiement sur serveur public (Internet)

### Vue d'ensemble de l'architecture serveur

```
┌─────────────────────────────────────────────────────────────────┐
│                      SERVEUR (Ubuntu 24.04)                      │
│                                                                  │
│  ┌─────────────┐     ┌─────────────┐     ┌─────────────┐        │
│  │   nginx     │────►│   Node.js   │────►│  MariaDB    │        │
│  │  (port 443) │     │ (port 8080) │     │ (optionnel) │        │
│  │   SSL/TLS   │     │  WebSocket  │     │             │        │
│  └─────────────┘     └─────────────┘     └─────────────┘        │
│         │                                                        │
│  ┌─────────────┐                                                 │
│  │   coturn    │  ◄──  Serveur TURN pour WebRTC                  │
│  │ (port 3478) │      (traversee NAT pour le chat vocal)         │
│  └─────────────┘                                                 │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Composants installes

| Composant | Role | Port | Installation |
|-----------|------|------|--------------|
| **Node.js 22 LTS** | Runtime JavaScript pour le serveur WebSocket | 8080 | `curl -fsSL https://deb.nodesource.com/setup_22.x \| sudo bash - && sudo apt install nodejs` |
| **nginx** | Reverse proxy, SSL termination, WebSocket upgrade | 80, 443 | `sudo apt install nginx` |
| **coturn** | Serveur STUN/TURN pour WebRTC (chat vocal) | 3478, 5349 | `sudo apt install coturn` |
| **poppler-utils** | Conversion PDF en images | - | `sudo apt install poppler-utils` |
| **certbot** | Certificats SSL Let's Encrypt | - | `sudo apt install certbot python3-certbot-nginx` |
| **PM2** | Gestionnaire de processus Node.js | - | `sudo npm install -g pm2` |
| **fail2ban** | Protection anti brute-force SSH | - | `sudo apt install fail2ban` |
| **ufw** | Pare-feu | - | Pre-installe sur Ubuntu |

### Installation rapide (LAN - Test local)

**1. Preparer la VM ou le serveur (Ubuntu 24.04) :**

```bash
# Mettre a jour le systeme
sudo apt update && sudo apt upgrade -y

# Installer Node.js 22 LTS
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs poppler-utils

# Verifier les versions
node --version    # v22.x.x
npm --version
```

**2. Copier et configurer le serveur :**

```bash
# Creer le dossier
mkdir -p ~/vr-meeting

# Copier le dossier Server (via SCP depuis Windows)
# scp -r "D:\Test_project\WebSocket_VR\Server" user@IP_SERVEUR:~/vr-meeting/

# Installer les dependances
cd ~/vr-meeting/Server
npm install
```

**3. Creer le service systemd :**

```bash
sudo nano /etc/systemd/system/vr-meeting.service
```

Contenu :

```ini
[Unit]
Description=VR Meeting WebSocket Server
After=network.target

[Service]
Type=simple
User=vr-admin
WorkingDirectory=/home/vr-admin/vr-meeting/Server
ExecStart=/usr/bin/node server.js
Restart=always
RestartSec=5
Environment=PORT=8080

[Install]
WantedBy=multi-user.target
```

**4. Demarrer le service :**

```bash
sudo systemctl daemon-reload
sudo systemctl enable vr-meeting
sudo systemctl start vr-meeting
sudo systemctl status vr-meeting
```

**5. Ouvrir le pare-feu :**

```bash
sudo ufw allow 22/tcp    # SSH
sudo ufw allow 8080/tcp  # WebSocket
sudo ufw enable
```

### Installation production (Internet public)

**1. Prerequis serveur :**
- VPS ou serveur dedie avec IP publique fixe
- Nom de domaine (ex: `meeting.entreprise.com`)
- Ubuntu 24.04 LTS
- 4 Go RAM minimum, 2 vCPU

**2. Configurer le DNS :**

Creer les enregistrements A pointant vers l'IP du serveur :
- `meeting.entreprise.com` → IP_SERVEUR
- `turn.entreprise.com` → IP_SERVEUR (optionnel)

**3. Installer nginx avec SSL (Let's Encrypt) :**

```bash
sudo apt install -y nginx certbot python3-certbot-nginx

# Obtenir le certificat SSL
sudo certbot --nginx -d meeting.entreprise.com
```

**4. Configurer nginx pour WebSocket :**

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

```nginx
server {
    listen 80;
    server_name meeting.entreprise.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name meeting.entreprise.com;

    ssl_certificate /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/meeting.entreprise.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl restart nginx
```

**5. Configurer coturn (serveur TURN pour WebRTC) :**

```bash
sudo apt install -y coturn

# Activer coturn
sudo nano /etc/default/coturn
# Decommenter : TURNSERVER_ENABLED=1

# Configurer coturn
sudo nano /etc/turnserver.conf
```

```ini
realm=meeting.entreprise.com
listening-port=3478
tls-listening-port=5349
listening-ip=0.0.0.0
relay-ip=IP_PUBLIQUE
external-ip=IP_PUBLIQUE
min-port=49152
max-port=65535
cert=/etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem
pkey=/etc/letsencrypt/live/meeting.entreprise.com/privkey.pem
lt-cred-mech
user=vrmeeting:MotDePasseSecurise!
fingerprint
no-cli
```

```bash
sudo systemctl enable coturn
sudo systemctl start coturn
```

**6. Ouvrir les ports :**

```bash
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 49152:65535/udp
sudo ufw enable
```

**7. Utiliser PM2 pour la production :**

```bash
sudo npm install -g pm2

# Creer ecosystem.config.js
cd ~/vr-meeting/Server
nano ecosystem.config.js
```

```javascript
module.exports = {
  apps: [{
    name: 'vr-meeting',
    script: 'server.js',
    cwd: '/home/vr-admin/vr-meeting/Server',
    instances: 1,
    max_memory_restart: '500M',
    env: { NODE_ENV: 'production', PORT: 8080 },
    autorestart: true
  }]
};
```

```bash
pm2 start ecosystem.config.js
pm2 startup    # Suivre les instructions affichees
pm2 save
```

### Configurer Unity pour le deploiement

**VRNetworkManager (Inspector) :**

| Environnement | Server Url |
|---------------|------------|
| Local | `ws://localhost:8080` |
| LAN | `ws://192.168.1.X:8080` |
| Production | `wss://meeting.entreprise.com` |

**VoiceChatManager (Inspector) - Production uniquement :**

| Champ | Valeur |
|-------|--------|
| Use Custom Turn Server | `true` |
| Custom Turn Url | `turn:meeting.entreprise.com:3478` |
| Custom Turn Username | `vrmeeting` |
| Custom Turn Credential | `MotDePasseSecurise!` |

### Commandes de maintenance

```bash
# === Statut des services ===
sudo systemctl status vr-meeting nginx coturn

# === Logs en temps reel ===
journalctl -u vr-meeting -f          # Node.js
sudo tail -f /var/log/nginx/error.log # nginx
sudo tail -f /var/log/turnserver.log  # coturn

# === PM2 (si utilise) ===
pm2 status
pm2 logs vr-meeting
pm2 restart vr-meeting

# === Renouveler certificat SSL ===
sudo certbot renew

# === Redemarrer tous les services ===
sudo systemctl restart vr-meeting nginx coturn
```

### Checklist de deploiement

**Infrastructure :**
- [ ] Serveur Ubuntu 24.04 accessible
- [ ] Node.js 22 LTS installe
- [ ] Dossier Server copie et `npm install` execute
- [ ] Service systemd ou PM2 configure
- [ ] Pare-feu configure (ufw)

**Reseau (production) :**
- [ ] Domaine configure avec enregistrement DNS A
- [ ] nginx installe et configure
- [ ] Certificat SSL Let's Encrypt obtenu
- [ ] coturn installe et configure
- [ ] Ports ouverts : 80, 443, 3478, 5349, 49152-65535

**Unity :**
- [ ] Server Url mis a jour dans VRNetworkManager
- [ ] VoiceChatManager configure avec serveur TURN (production)
- [ ] Build teste avec connexion au serveur

> Pour plus de details, consultez les guides complets dans `Documentation/DEPLOYMENT_LAN_GUIDE.md` et `Documentation/DEPLOYMENT_PUBLIC_GUIDE.md`.

---

## Configuration et parametres

### Parametres du serveur (Server/server.js)

| Parametre | Valeur par defaut | Description |
|-----------|-------------------|-------------|
| `PORT` | 8080 | Port WebSocket |
| `HEARTBEAT_INTERVAL` | 30000 | Intervalle heartbeat (ms) |
| `PDF_CACHE_TTL` | 1800000 | Cache PDF (30 min) |

### Parametres reseau Unity (VRNetworkManager)

| Parametre | Valeur par defaut | Description |
|-----------|-------------------|-------------|
| `serverUrl` | ws://localhost:8080 | URL du serveur WebSocket |
| `reconnectDelay` | 2.0 | Delai de reconnexion (s) |
| `maxReconnectAttempts` | 5 | Tentatives max |
| `offlineMode` | false | Mode hors-ligne (debug) |

### Parametres VR (VRPlayerController)

| Parametre | Valeur par defaut | Description |
|-----------|-------------------|-------------|
| `sendRate` | 30 | Frequence d'envoi position (Hz) |
| `positionThreshold` | 0.01 | Seuil mouvement (m) |
| `rotationThreshold` | 1.0 | Seuil rotation (deg) |

### Parametres WebRTC (WebRTCConfiguration)

Les serveurs STUN/TURN sont configures dans `WebRTCConfiguration.cs` :
```csharp
public static readonly string[] IceServers = {
    "stun:stun.l.google.com:19302",
    // Ajouter serveurs TURN si necessaire
};
```

### Parametres du tableau blanc (Whiteboard)

| Parametre | Valeur | Description |
|-----------|--------|-------------|
| Texture | 2048x2048 | Resolution du tableau |
| Shader | Sprites/Default | Shader utilise |
| Send Rate | 33ms | Frequence de synchronisation |

### Parametres d'enregistrement (RecordingSettings)

| Parametre | Valeur | Description |
|-----------|--------|-------------|
| `width` | 1920 | Largeur video |
| `height` | 1080 | Hauteur video |
| `frameRate` | 30 | FPS cible |
| `outputFolder` | "Recordings" | Dossier de sortie |

### Parametres utilisateur (MainMenuSettings)

Accessibles depuis le menu Options :

**Audio :**
- Volume principal (0-100)
- Volume voix (0-100)
- Selection du microphone

**Graphiques :**
- Qualite (Low/Medium/High/Ultra)
- Resolution
- Plein ecran

**VR :**
- Mode de rotation (Snap/Smooth)
- Angle de snap (15/30/45/90)
- Vitesse de rotation smooth

**Desktop :**
- Sensibilite souris
- Inverser Y

---

## Fonctionnalites implementees

### Multijoueur
- [x] Connexion WebSocket avec reconnexion automatique
- [x] Systeme de rooms avec codes d'acces
- [x] Synchronisation position/rotation en temps reel (30 Hz)
- [x] Gestion des late joiners (synchronisation d'etat)
- [x] Kick de joueurs (host seulement)

### Communication
- [x] Chat vocal WebRTC (mesh topology)
- [x] Audio spatial 3D sur la tete des joueurs
- [x] Push-to-talk (touche V)
- [x] Indicateur visuel de qui parle

### Collaboration
- [x] Tableau blanc multi-utilisateur
- [x] Dessin synchronise en temps reel
- [x] Gomme pour le tableau blanc
- [x] Partage d'ecran (854x480 @ 3fps)
- [x] Partage de fichiers (PDF, images, documents)
- [x] Presentation de fichiers sur tableau blanc
- [x] Laser pointer (VR: bouton A, Desktop: touche L)

### VR
- [x] Support OpenXR (Quest, PCVR)
- [x] Teleportation
- [x] Tracking mains/tete
- [x] Menu VR attaché au poignet
- [x] Interaction avec objets (grab)
- [x] Clavier virtuel spatial

### Desktop
- [x] Controles clavier/souris (WASD + souris)
- [x] Mode spectateur
- [x] Interface adaptee

### Avatar
- [x] Personnalisation des couleurs
- [x] Nametag au-dessus de la tete
- [x] Synchronisation de l'apparence

### Audio
- [x] Gestionnaire de sons (SoundManager)
- [x] Ambiance sonore
- [x] Zones de mute (AudioMuteZone)
- [x] Sons de feedback UI

### Enregistrement
- [x] Capture video des reunions
- [x] Pipeline async pour eviter lag VR
- [x] Marqueurs temporels
- [x] Export via FFmpeg

### Authentification (code present mais non integre)
- [x] Interface login/register
- [x] Mode invite
- [x] Hash bcrypt des mots de passe
- [x] Tokens JWT

### Interface
- [x] Menu principal
- [x] Options (audio, graphiques, VR)
- [x] Ecran de chargement
- [x] Menu in-game VR

---

## Fonctionnalites non implementees / A faire

### Haute priorite
- [ ] Integration complete de l'authentification (actuellement bypass)
- [ ] Rooms privees (protegees par mot de passe)
- [ ] Persistance des avatars en base de donnees

### Moyenne priorite
- [ ] Historique des reunions
- [ ] Enregistrement audio dans la video
- [ ] XR Socket Interactor (snap zones pour objets)
- [ ] Preview des rooms au hover

### Basse priorite
- [ ] Chiffrement end-to-end (E2E)
- [ ] Support SSO (Single Sign-On)
- [ ] Panneau d'administration
- [ ] Conformite GDPR
- [ ] Calendrier de reunions
- [ ] Chat textuel

---

## Pistes d'ameliorations futures

### 1. Securite et Production
- **Deploiement HTTPS/WSS** : Configurer un reverse proxy (nginx) avec certificat SSL
- **Rate limiting** : Limiter les requetes par IP pour eviter les abus
- **Validation des entrees** : Ajouter une validation stricte cote serveur
- **Audit de securite** : Revoir les messages reseau pour eviter les injections

### 2. Scalabilite
- **Load balancing** : Plusieurs serveurs WebSocket avec Redis pour partager l'etat
- **Sharding des rooms** : Distribuer les rooms sur plusieurs serveurs
- **CDN pour les assets** : Heberger les fichiers partages sur un CDN

### 3. Fonctionnalites avancees
- **Avatars complets** : Integration Ready Player Me ou avatars personnalises
- **Objets interactifs** : Post-its, documents annotables, modeles 3D
- **Breakout rooms** : Sous-salles pour discussions en petit groupe
- **Interpreter AI** : Transcription et traduction en temps reel
- **Recording cloud** : Enregistrement et stockage automatique sur serveur

### 4. Qualite de vie
- **Tutoriel interactif** : Guide pour les nouveaux utilisateurs
- **Raccourcis clavier** : Plus d'options pour le mode Desktop
- **Accessibilite** : Sous-titres, options pour daltoniens
- **Mode economie de batterie** : Reduire le framerate quand inactif

### 5. Optimisations techniques
- **LOD pour avatars** : Reduire les details a distance
- **Culling intelligent** : Ne pas synchroniser les objets hors de vue
- **Compression des donnees** : Utiliser binary WebSocket au lieu de JSON
- **Prediction cote client** : Reduire la latence percue

### 6. Integration externe
- **API REST** : Pour integration avec d'autres services
- **Webhooks** : Notifications lors d'evenements (debut/fin reunion)
- **Plugins** : Architecture modulaire pour extensions

---

## Structure du projet

### Dossiers Unity
```
Assets/
├── Prefabs/Unity/          # Prefabs joueurs et objets
├── Scenes/                  # Bootstrap.unity, Meet.unity
├── Materials/               # Materiaux
├── images/                  # Textures et screenshots
└── Scrips/                  # Scripts C# (note: typo preservee)
    ├── Network/             # VRNetworkManager, VRRoomManager, VRGameManager
    ├── VR/                  # BootstrapManager, VRPlayerController
    ├── Desktop/             # DesktopPlayerController
    ├── WebRTC/              # VoiceChatManager, WebRTCPeerManager
    ├── WhiteBoard/          # Whiteboard, WhiteboardDrawingSurface
    ├── Sharing/             # ScreenShareManager, FileShareManager
    ├── Avatar/              # AvatarCustomization
    ├── Auth/                # AuthManager, AuthUI
    ├── Recording/           # RecordingManager, SpectatorCameraController
    ├── Audio/               # SoundManager, AmbienceManager
    ├── Interaction/         # LaserPointer, RoomBlocker
    ├── UI/                  # Interfaces utilisateur
    ├── Utils/               # Utilitaires (SceneLoader, ScreenFader)
    └── Debug/               # DebugManager
```

### Dossier serveur
```
Server/
├── server.js               # Serveur WebSocket principal
├── package.json            # Dependances Node.js
├── .env.example            # Template configuration
├── filePresentation.js     # Module presentation PDF
└── src/
    ├── database.js         # Connection MariaDB
    └── auth.js             # Authentification
```

### Scripts principaux et leur role

| Script | Role |
|--------|------|
| `VRNetworkManager.cs` | Gestion connexion WebSocket, envoi/reception messages |
| `VRRoomManager.cs` | Gestion des rooms (creation, join, leave) |
| `VRGameManager.cs` | Spawn/despawn des joueurs, references globales |
| `VRPlayerController.cs` | Controle joueur VR, envoi position |
| `DesktopPlayerController.cs` | Controle joueur clavier/souris |
| `VoiceChatManager.cs` | Orchestration WebRTC, gestion peers |
| `WhiteboardDrawingSurface.cs` | Synchronisation reseau du dessin |
| `ScreenShareManager.cs` | Capture et partage d'ecran |
| `RecordingManager.cs` | Pipeline d'enregistrement video |
| `AuthManager.cs` | Authentification login/register |
| `MainMenuManager.cs` | Menu principal et navigation |

---

## Ressources et documentation

### Documentation interne
- `CLAUDE.md` - Memo technique du projet (reference rapide)
- Ce document (`DOCUMENTATION_STAGE.md`) - Documentation complete

### Packages Unity utilises
| Package | Version | Usage |
|---------|---------|-------|
| com.endel.nativewebsocket | - | WebSocket |
| com.unity.webrtc | 3.0.0 | Chat vocal |
| com.unity.xr.interaction.toolkit | 3.2.2 | Interactions VR |
| com.unity.xr.openxr | 1.16.1 | Support OpenXR |
| com.unity.xr.hands | 1.7.2 | Hand tracking |
| com.unity.render-pipelines.universal | 17.2.0 | URP |
| com.veriorpies.parrelsync | - | Tests multi-instance |

### Liens utiles
- [Unity XR Interaction Toolkit](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.0/manual/index.html)
- [Unity WebRTC](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/index.html)
- [NativeWebSocket](https://github.com/endel/NativeWebSocket)
- [OpenXR](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.0/manual/index.html)

---

## Troubleshooting / Depannage

### Problemes de connexion

#### Le client ne se connecte pas au serveur

**Symptome :** Message d'erreur "Connection failed" ou timeout

**Causes possibles et solutions :**

| Cause | Verification | Solution |
|-------|--------------|----------|
| Serveur non demarre | `sudo systemctl status vr-meeting` | `sudo systemctl start vr-meeting` |
| Mauvaise URL | Verifier `serverUrl` dans VRNetworkManager | Corriger l'URL (ws:// ou wss://) |
| Pare-feu bloque | `sudo ufw status` | `sudo ufw allow 8080/tcp` |
| Port occupe | `ss -tlnp \| grep 8080` | Changer le port ou tuer le processus |

**Debug Unity :**
```
1. Ouvrir la Console Unity (Window > General > Console)
2. Chercher les messages [Network] ou [WebSocket]
3. Activer DebugManager si besoin
```

#### Deconnexions frequentes

**Symptome :** Les joueurs se deconnectent regulierement

**Solutions :**
1. Verifier la stabilite reseau (ping vers le serveur)
2. Augmenter `HEARTBEAT_INTERVAL` dans server.js (defaut: 30000ms)
3. Verifier les logs serveur : `journalctl -u vr-meeting -f`
4. En production, verifier les timeouts nginx (`proxy_read_timeout`)

---

### Problemes de chat vocal (WebRTC)

#### Pas de son entre les joueurs

**Symptome :** Connexion OK mais pas d'audio

**Checklist de diagnostic :**

```
[ ] Microphone autorise dans Windows/Quest ?
[ ] Push-to-talk active ? (touche V en Desktop)
[ ] VoiceChatManager present dans la scene Bootstrap ?
[ ] Les 2 joueurs sont dans la meme room ?
[ ] Logs Unity : chercher "WebRTC" ou "ICE"
```

**Causes courantes :**

| Probleme | Symptome dans les logs | Solution |
|----------|------------------------|----------|
| NAT symmetrique | "ICE failed" | Configurer un serveur TURN |
| Pas de STUN | "No ICE candidates" | Verifier acces internet |
| Firewall | "Connection timeout" | Ouvrir ports UDP 49152-65535 |

**Configurer un serveur TURN (obligatoire en production) :**
```csharp
// VoiceChatManager Inspector
Use Custom Turn Server = true
Custom Turn Url = turn:votre-serveur:3478
Custom Turn Username = vrmeeting
Custom Turn Credential = mot_de_passe
```

#### Audio saccade ou coupe

**Solutions :**
1. Reduire la qualite audio dans les parametres
2. Verifier la bande passante disponible
3. En LAN, desactiver le serveur TURN (direct P2P plus rapide)

---

### Problemes VR

#### Le casque n'est pas detecte

**Symptome :** Mode Desktop au lieu de VR

**Solutions :**
1. Verifier que le runtime OpenXR est configure :
   - Quest : Meta Quest Link ou Air Link actif
   - SteamVR : SteamVR lance et casque detecte
2. Dans Unity : Edit > Project Settings > XR Plug-in Management
3. Verifier que OpenXR est coche pour la plateforme cible

#### Teleportation ne fonctionne pas

**Causes courantes :**

| Probleme | Solution |
|----------|----------|
| Mauvais layer | Le sol doit etre sur le layer 31 (Teleport) |
| Pas de collider | Ajouter un MeshCollider sur le sol |
| XR Ray Interactor manquant | Verifier le prefab LocalPlayer |

**Verification rapide :**
```
1. Selectionner le sol dans la scene
2. Inspector > Layer = 31 (ou "Teleport")
3. Verifier qu'un Collider est present
```

#### Mains/controleurs invisibles ou mal positionnes

**Solutions :**
1. Verifier le tracking dans les parametres Quest/SteamVR
2. Recalibrer le guardian/play area
3. Dans Unity, verifier les XR Controllers dans le prefab LocalPlayer

---

### Problemes de tableau blanc

#### Le dessin ne se synchronise pas

**Symptome :** Un joueur dessine mais l'autre ne voit rien

**Checklist :**
```
[ ] Les 2 joueurs sont dans la meme room ?
[ ] WhiteboardDrawingSurface present sur le tableau ?
[ ] Logs serveur : messages "whiteboard-batch" recus ?
```

**Debug :**
1. Ouvrir la Console Unity sur les 2 clients
2. Chercher les messages `[Whiteboard]`
3. Verifier que `roomId` est identique dans les logs

#### Le dessin est decale ou imprecis

**Causes et solutions :**

| Cause | Solution |
|-------|----------|
| Mauvais UV mapping | Verifier les UVs du mesh whiteboard |
| Echelle incorrecte | Le tableau doit etre a l'echelle 1:1 |
| Collider decale | Aligner le collider avec le mesh |

---

### Problemes de partage d'ecran/fichiers

#### Ecran partage noir ou fige

**Solutions :**
1. Verifier les permissions de capture d'ecran (Windows)
2. Reduire la resolution de capture (854x480 recommande)
3. Certaines applications (Netflix, etc.) bloquent la capture

#### PDF ne s'affiche pas

**Cote serveur :**
```bash
# Verifier que poppler-utils est installe
which pdftoppm    # doit afficher /usr/bin/pdftoppm

# Verifier les logs
journalctl -u vr-meeting | grep PDF
```

**Cote Unity :**
- Verifier que le fichier fait moins de 10 Mo
- Extensions supportees : pdf, png, jpg, jpeg, gif

---

### Problemes d'enregistrement

#### L'enregistrement ne demarre pas

**Prerequis :**
```
[ ] FFmpeg installe et dans le PATH ?
[ ] SpectatorCamera presente dans la scene Meet ?
[ ] Espace disque suffisant ?
[ ] L'utilisateur est-il l'host de la room ?
```

**Verifier FFmpeg :**
```bash
# Windows (PowerShell)
ffmpeg -version

# Si non trouve, installer via chocolatey ou telecharger manuellement
```

#### Video saccadee ou frames manquantes

**Cause :** Le pipeline d'enregistrement est surcharge

**Solutions :**
1. Reduire la resolution (1280x720 au lieu de 1920x1080)
2. Reduire le framerate (24 fps au lieu de 30)
3. Utiliser un SSD pour le stockage temporaire
4. Fermer les applications en arriere-plan

---

### Problemes serveur

#### Le serveur crash au demarrage

**Diagnostic :**
```bash
# Voir les erreurs
journalctl -u vr-meeting --since "5 minutes ago"

# Ou lancer manuellement pour voir l'erreur
cd ~/vr-meeting/Server
node server.js
```

**Erreurs courantes :**

| Erreur | Cause | Solution |
|--------|-------|----------|
| `EADDRINUSE` | Port deja utilise | `kill $(lsof -t -i:8080)` ou changer le port |
| `MODULE_NOT_FOUND` | Dependance manquante | `npm install` |
| `EACCES` | Permission refusee | Verifier les droits du dossier |

#### Memoire serveur saturee

**Symptome :** Serveur lent ou crash apres plusieurs heures

**Solutions :**
1. Configurer PM2 avec limite memoire :
   ```javascript
   max_memory_restart: '500M'
   ```
2. Verifier les fuites memoire dans les logs
3. Redemarrer periodiquement : `pm2 restart vr-meeting`

#### Certificat SSL expire

**Symptome :** Erreur "Certificate expired" cote client

**Solution :**
```bash
# Renouveler manuellement
sudo certbot renew

# Verifier la date d'expiration
sudo certbot certificates

# Redemarrer les services
sudo systemctl restart nginx coturn
```

---

### Problemes de performance

#### Lag/latence elevee

**Diagnostic :**
```bash
# Mesurer la latence reseau
ping IP_SERVEUR

# Verifier la charge serveur
htop
```

**Optimisations :**
1. Rapprocher le serveur des utilisateurs (meme region)
2. Reduire la frequence de sync (30 Hz → 20 Hz dans VRPlayerController)
3. Activer la compression dans nginx

#### FPS bas en VR

**Causes et solutions :**

| Cause | Solution |
|-------|----------|
| Trop de joueurs | Limiter a 10 joueurs par room |
| Qualite graphique | Reduire dans les options |
| Whiteboard haute resolution | Utiliser 1024x1024 au lieu de 2048x2048 |
| Enregistrement actif | Desactiver ou reduire la qualite |

---

### Outils de diagnostic

#### Cote Unity

```csharp
// Activer les logs detailles
DebugManager.Instance.EnableCategory(DebugCategory.Network);
DebugManager.Instance.EnableCategory(DebugCategory.WebRTC);

// Ou dans l'Inspector de DebugManager, cocher les categories
```

#### Cote serveur

```bash
# Logs en temps reel
journalctl -u vr-meeting -f

# Statut des services
sudo systemctl status vr-meeting nginx coturn

# Connexions actives
ss -tn state established | grep -E ":(8080|443|3478)"

# Utilisation ressources
htop
df -h
```

#### Tests reseau

```bash
# Tester la connectivite
ping IP_SERVEUR
telnet IP_SERVEUR 8080

# Tester WebSocket (depuis un navigateur)
# Ouvrir la console (F12) et executer :
# new WebSocket('ws://IP_SERVEUR:8080')

# Tester TURN
# Utiliser https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
```

---

### Contacts et escalade

Si un probleme persiste apres avoir suivi ce guide :

1. **Verifier les logs** - 90% des problemes sont identifies dans les logs
2. **Reproduire le probleme** - Noter les etapes exactes
3. **Collecter les informations** :
   - Version Unity
   - Plateforme (Quest, PCVR, Desktop)
   - Logs client et serveur
   - Configuration reseau

---

## Notes importantes pour la reprise

1. **Typo dans le dossier** : Le dossier `Assets/Scrips/` contient une faute (devrait etre Scripts). Ce choix a ete preserve pour eviter de casser les references.

2. **Mode offline** : Pour tester sans serveur, activer `offlineMode` dans VRNetworkManager Inspector.

3. **XR Layers** : Le layer 31 est reserve a la teleportation. Ne pas l'ajouter aux objets Grabbable.

4. **Remote players** : Les mains/tete des joueurs distants sont detaches de la hierarchie (world-space) pour un meilleur tracking.

5. **Enregistrement** : Necessite FFmpeg dans le PATH. La SpectatorCamera doit etre dans la scene Meet.

6. **Base de donnees** : Le serveur fonctionne sans base de donnees. L'authentification est simplement desactivee.

---

*Document genere le 26 Fevrier 2026*
*Projet realise durant le stage*
