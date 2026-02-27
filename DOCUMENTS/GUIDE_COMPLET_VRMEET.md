# Guide Complet - VRMeet
## Application de Salles de Reunion Virtuelles

---

## Table des matieres

1. [Presentation du projet](#1-presentation-du-projet)
2. [Fonctionnalites implementees](#2-fonctionnalites-implementees)
3. [Architecture technique](#3-architecture-technique)
4. [Installation et lancement](#4-installation-et-lancement)
   - [Prerequis](#41-prerequis)
   - [Lancement sur PC (Desktop)](#42-lancement-sur-pc-desktop)
   - [Lancement sur PC VR (PCVR)](#43-lancement-sur-pc-vr-pcvr)
   - [Lancement sur Meta Quest](#44-lancement-sur-meta-quest)
5. [Utilisation de l'application](#5-utilisation-de-lapplication)
6. [Controles](#6-controles)
7. [Configuration et parametres](#7-configuration-et-parametres)
8. [Depannage](#8-depannage)

---

## 1. Presentation du projet

### Technologies utilisees

| Composant | Technologie |
|-----------|-------------|
| Moteur de jeu | Unity 6000.2.14f1 |
| Communication temps reel | WebSocket (NativeWebSocket) |
| Chat vocal | WebRTC (peer-to-peer) |
| Support VR | OpenXR (multi-casques) |
| Serveur backend | Node.js |
| Base de donnees | MariaDB (optionnel) |

### Plateformes supportees

- **Windows Desktop** - Clavier/souris, sans casque VR
- **PCVR** - Meta Quest Link, SteamVR (Valve Index, HTC Vive, etc.)
- **Meta Quest** - Standalone (Quest 2, Quest 3, Quest Pro)

---

## 2. Fonctionnalites implementees

### Communication et Multiplayer

| Fonctionnalite | Description |
|----------------|-------------|
| **Connexion WebSocket** | Connexion temps reel avec reconnexion automatique |
| **Systeme de rooms** | Creation et rejoindre des salles avec code d'acces (6 caracteres) |
| **Synchronisation VR** | Position et rotation synchronisees a 30 Hz |
| **Chat vocal** | Communication vocale WebRTC avec audio spatial 3D |
| **Push-to-talk** | Option d'activer le micro avec la touche V |

### Outils de collaboration

| Fonctionnalite | Description |
|----------------|-------------|
| **Tableau blanc** | Dessin multi-utilisateur synchronise en temps reel |
| **Partage d'ecran** | Diffusion de votre ecran (854x480 @ 3fps) |
| **Partage de fichiers** | Envoi de PDF, images, documents (max 10 Mo) |
| **Presentation** | Affichage de fichiers sur le tableau blanc |
| **Laser pointer** | Pointeur pour indiquer des elements (VR: bouton A, Desktop: L) |

### Fonctionnalites VR

| Fonctionnalite | Description |
|----------------|-------------|
| **Teleportation** | Deplacement par teleportation |
| **Hand tracking** | Tracking des mains et controllers |
| **Menu VR** | Interface attachee au poignet |
| **Grab objets** | Saisie d'objets interactifs |

### Personnalisation et Interface

| Fonctionnalite | Description |
|----------------|-------------|
| **Avatar personnalisable** | Choix des couleurs de l'avatar |
| **Nametag** | Nom affiche au-dessus de la tete |
| **Menu principal** | Interface de demarrage avec options |
| **Parametres** | Audio, graphiques, VR, controles |
| **Authentification** | Login/Register/Mode invite |

### Enregistrement

| Fonctionnalite | Description |
|----------------|-------------|
| **Capture video** | Enregistrement des reunions (1920x1080 @ 30fps) |
| **Marqueurs** | Points de repere temporels |
| **Export MP4** | Conversion via FFmpeg |

---

## 3. Architecture technique

### Structure des scenes

```
Bootstrap (Scene 0) - Persistante
    ├── VRNetworkManager     → Gestion WebSocket
    ├── VRRoomManager        → Gestion des rooms
    ├── VRGameManager        → Spawn des joueurs
    ├── VoiceChatManager     → Chat vocal WebRTC
    ├── SoundManager         → Sons et ambiance
    ├── AuthManager          → Authentification
    └── MainMenuUI           → Interface menu

Meet (Scene 1) - Chargee additivement
    ├── Lobby                → Zone d'accueil
    ├── MeetingRoomA         → Salle de reunion A
    ├── MeetingRoomB         → Salle de reunion B
    ├── Whiteboards          → Tableaux blancs
    └── SpectatorCamera      → Camera d'enregistrement
```

### Flux de connexion

```
Lancement → Menu Principal → Authentification → Connexion Serveur → Lobby → Salle de reunion
```

### Architecture reseau

```
┌─────────────────┐                      ┌─────────────────┐
│  Client Unity   │◄────WebSocket───────►│  Serveur Node.js │
│  (VR/Desktop)   │                      │   (port 8080)    │
└────────┬────────┘                      └─────────────────┘
         │
         │ WebRTC (P2P)
         │
┌────────▼────────┐
│  Autre Client   │  ← Chat vocal direct entre clients
└─────────────────┘
```

---

## 4. Installation et lancement

### 4.1 Prerequis

#### Logiciels requis

| Logiciel | Version | Obligatoire | Usage |
|----------|---------|-------------|-------|
| Unity Hub + Unity | 6000.2.14f1 | Oui | Editeur/Build |
| Node.js | >= 16.0.0 | Oui | Serveur |
| Visual Studio | 2022 | Recommande | IDE C# |
| FFmpeg | Latest | Pour enregistrement | Encodage video |
| SteamVR | Latest | Pour PCVR | Runtime VR |
| Oculus App | Latest | Pour Quest Link | Runtime VR |

#### Ports reseau a ouvrir

| Port | Protocol | Usage |
|------|----------|-------|
| 8080 | TCP | WebSocket |
| 3478 | TCP/UDP | STUN/TURN (voice) |
| 49152-65535 | UDP | WebRTC media |

---

### 4.2 Lancement sur PC (Desktop)

#### Etape 1 : Demarrer le serveur (LAN + PM2)

Lancer le serveur sur une machine du meme reseau LAN.
Les clients se connectent via l'IP du serveur.

```
┌─────────────────┐         ┌─────────────────┐
│  Serveur Ubuntu │         │  Client         │
│  192.168.1.100  │◄────────│  (PC/Quest)     │
│  PM2 + Node.js  │   LAN   │                 │
│  Port 8080      │         │                 │
└─────────────────┘         └─────────────────┘
```

##### Configuration du serveur (Ubuntu/Linux)

**1. Installer Node.js :**
```bash
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs poppler-utils
```

**2. Copier le projet :**
```bash
# Depuis Windows (PowerShell)
scp -r "D:\Test_project\WebSocket_VR\Server" user@192.168.1.100:~/vr-meeting/

# Ou git clone sur le serveur
git clone <repo-url> ~/vr-meeting
```

**3. Installer les dependances :**
```bash
cd ~/vr-meeting/Server
npm install
```

**4. Installer et configurer PM2 :**
```bash
# Installation globale de PM2
sudo npm install -g pm2

# Creer ecosystem.config.js
nano ecosystem.config.js
```

```javascript
module.exports = {
  apps: [{
    name: 'vr-meeting',
    script: 'server.js',
    cwd: '/home/user/vr-meeting/Server',  // Adapter le chemin
    instances: 1,
    max_memory_restart: '500M',
    env: { NODE_ENV: 'production', PORT: 8080 },
    autorestart: true,
    watch: false
  }]
};
```

**5. Demarrer avec PM2 :**
```bash
# Lancer le serveur
pm2 start ecosystem.config.js

# Verifier le statut
pm2 status

# Demarrage auto au boot
pm2 startup
# Executer la commande sudo affichee

pm2 save
```

**6. Ouvrir le pare-feu :**
```bash
sudo ufw allow 8080/tcp
sudo ufw enable
```

**7. Trouver l'IP du serveur :**
```bash
ip addr show | grep "inet "
# Ex: 192.168.1.100
```

##### Commandes PM2 utiles

```bash
pm2 status              # Statut
pm2 logs vr-meeting     # Logs
pm2 restart vr-meeting  # Redemarrer
pm2 stop vr-meeting     # Arreter
pm2 start vr-meeting    # Demarrer
```

##### Configuration Unity

Dans `VRNetworkManager` (Inspector) :
- `Server Url` : `ws://192.168.1.100:8080` (remplacer par l'IP du serveur)

#### Etape 2 : Lancer depuis Unity Editor

1. Ouvrir le projet dans **Unity 6000.2.14f1**
2. Ouvrir la scene `Assets/Scenes/Bootstrap.unity`
3. Verifier que `VRNetworkManager` a le bon `Server Url` (IP du serveur)
4. Appuyer sur **Play**
5. L'application demarre automatiquement en mode Desktop (pas de casque detecte)

#### Etape 3 : Construire un executable Windows

1. **File → Build Settings**
2. Plateforme : **Windows, Mac, Linux**
3. Architecture : **x86_64**
4. Cliquer **Build**
5. Choisir un dossier de destination
6. Lancer l'executable genere

---

### 4.3 Lancement sur PC VR (PCVR)

#### Casques compatibles

- Meta Quest (via Link/Air Link)
- Valve Index
- HTC Vive / Vive Pro
- Windows Mixed Reality
- Tout casque compatible OpenXR

#### Etape 1 : Configurer le runtime VR

**Pour Meta Quest avec Link :**
1. Installer l'application **Oculus** sur PC
2. Connecter le Quest via cable USB-C (Link) ou Wi-Fi (Air Link)
3. Dans l'app Oculus : Settings → General → OpenXR Runtime → **Set Oculus as active**

**Pour SteamVR :**
1. Installer **SteamVR** via Steam
2. Connecter votre casque
3. Dans SteamVR : Settings → Developer → **Set SteamVR as OpenXR Runtime**

#### Etape 2 : Configurer Unity pour PCVR

1. **Edit → Project Settings → XR Plug-in Management**
2. Onglet **Windows** :
   - Cocher **OpenXR**
   - Cliquer sur OpenXR pour configurer les Interaction Profiles

#### Etape 3 : Lancer le serveur

```bash
cd Server
npm run dev
```

#### Etape 4 : Lancer l'application

**Depuis Unity Editor :**
1. Mettre votre casque VR
2. Appuyer sur **Play** dans Unity
3. L'application se lance dans le casque

**Build standalone :**
1. **File → Build Settings**
2. Plateforme : **Windows**
3. Cliquer **Build and Run**
4. L'application se lance dans le casque

---

### 4.4 Lancement sur Meta Quest

#### Prerequis Quest

- Meta Quest 2, Quest 3, ou Quest Pro
- Compte Meta Developer (gratuit)
- Cable USB-C ou Wi-Fi pour le deploiement

#### Etape 1 : Configurer Unity pour Android/Quest

1. **File → Build Settings**
2. Selectionner **Android**
3. Cliquer **Switch Platform**

4. **Edit → Project Settings → XR Plug-in Management**
5. Onglet **Android** :
   - Cocher **OpenXR**
   - Ajouter **Meta Quest Support** dans les features

6. **Edit → Project Settings → Player**
7. Onglet **Android** :
   - Company Name : Votre nom
   - Minimum API Level : **Android 10.0 (API level 29)**
   - Target API Level : **Automatic**
   - Scripting Backend : **IL2CPP**
   - Target Architectures : **ARM64**

#### Etape 2 : Activer le mode developpeur sur Quest

1. Sur votre telephone, ouvrir l'app **Meta Quest**
2. Aller dans **Devices → [Votre Quest] → Settings → Developer Mode**
3. Activer **Developer Mode**
4. Redemarrer le Quest

#### Etape 3 : Connecter le Quest au PC

1. Connecter le Quest au PC via cable USB-C
2. Mettre le Quest et accepter la demande de **debogage USB**
3. Dans Unity : **File → Build Settings → Refresh** pour voir le Quest dans la liste

#### Etape 4 : Configurer le serveur pour Quest

Le Quest doit pouvoir acceder au serveur. Options :

**Option A - Reseau local (recommande pour tests) :**

1. Trouver l'IP de votre PC :
   ```bash
   # Windows
   ipconfig
   # Chercher "IPv4 Address" (ex: 192.168.1.100)
   ```

2. Dans Unity, modifier `VRNetworkManager` :
   - `Server Url` : `ws://192.168.1.100:8080`

3. S'assurer que le PC et le Quest sont sur le meme reseau Wi-Fi

4. Ouvrir le pare-feu Windows pour le port 8080 :
   ```powershell
   # PowerShell (Admin)
   netsh advfirewall firewall add rule name="VRMeet" dir=in action=allow protocol=TCP localport=8080
   ```

**Option B - Serveur public (production) :**

1. Deployer le serveur sur un VPS (voir DOCUMENTATION_STAGE.md)
2. Configurer `wss://votre-domaine.com`

#### Etape 5 : Build et deploiement

1. **File → Build Settings**
2. Verifier que le Quest est dans **Run Device**
3. Cliquer **Build and Run**
4. Unity compile et installe sur le Quest
5. L'application demarre automatiquement

#### Etape 6 : Lancer l'application sur Quest

Apres installation :
1. Sur le Quest : **App Library → Unknown Sources**
2. Trouver et lancer **VRMeet**

---

## 5. Utilisation de l'application

### Demarrage

1. **Ecran de chargement** - Initialisation des systemes
2. **Menu principal** - Options : Start, Options, Quit
3. **Authentification** - Login, Register, ou mode Invite
4. **Connexion au serveur** - Automatique
5. **Lobby** - Zone d'accueil, choix de la salle

### Rejoindre/Creer une room

**Creer une room :**
1. Se teleporter vers une porte de salle (Room A ou B)
2. La room est creee automatiquement
3. Un code de 6 caracteres est genere
4. Partager ce code aux autres participants

**Rejoindre une room :**
1. Entrer le code de room dans l'interface
2. Se teleporter vers la salle correspondante

### Utiliser le tableau blanc

**En VR :**
1. Prendre un marqueur (Grab avec le controller)
2. Approcher la pointe du tableau
3. Dessiner en maintenant le marqueur contre le tableau

**En Desktop :**
1. Viser le tableau avec la souris
2. Clic gauche pour dessiner
3. Utiliser l'interface pour changer la couleur

### Chat vocal

- **Automatique** : Le micro est actif par defaut
- **Push-to-talk** : Maintenir V (Desktop) pour parler
- L'audio est spatial (3D) - le son vient de la position du joueur

### Partage d'ecran

1. Ouvrir le menu VR ou appuyer sur la touche de menu
2. Selectionner "Partage d'ecran"
3. Choisir la fenetre a partager
4. L'ecran s'affiche sur le tableau blanc

### Enregistrement de reunion

> **Note :** L'enregistrement est reserve a l'**hote**. FFmpeg doit etre installe sur le systeme.

**Demarrer l'enregistrement :**
1. Ouvrir le menu VR (ou touche menu en Desktop)
2. Aller dans l'onglet **Recording**
3. Cliquer sur **Demarrer l'enregistrement**
4. Un indicateur d'enregistrement apparait

**Ajouter des marqueurs :**
Pendant l'enregistrement, ajoutez des marqueurs pour les moments importants :
- **Important** - Contenu important
- **Question** - Question posee
- **Todo** - A faire
- **Idea** - Idee

Les marqueurs sont sauvegardes dans la timeline pour y revenir facilement.

**Arreter l'enregistrement :**
1. Cliquer sur **Arreter l'enregistrement**
2. FFmpeg encode automatiquement les frames en MP4
3. Le fichier est sauvegarde dans `Recordings/`

**Parametres (RecordingSettings) :**

| Parametre | Defaut | Description |
|-----------|--------|-------------|
| Resolution | 1920x1080 | Resolution de sortie |
| Framerate | 30fps | Images par seconde |
| Qualite JPEG | 85 | Qualite de compression |
| Capture audio | true | Inclure l'audio |
| Dossier | Recordings | Emplacement de sauvegarde |

**Fichiers de sortie :**
```
Recordings/
├── recording_2026-02-27_14-30-00.mp4    # Video finale
├── recording_2026-02-27_14-30-00.json   # Metadonnees + marqueurs
└── frames/                               # (supprime apres encodage)
```

**Prerequis :**
- FFmpeg installe et dans le PATH systeme
- SpectatorCamera present dans la scene Meet
- Seul l'hote peut enregistrer

---

## 6. Controles

### Mode VR (Quest / PCVR)

| Action | Controller |
|--------|------------|
| Teleportation | Joystick + Trigger |
| Regarder | Rotation de la tete |
| Grab objet | Grip (bouton lateral) |
| Laser pointer | Bouton A |
| Menu | Bouton Menu / Start |
| Push-to-talk | Touche V (clavier) |

### Mode Desktop

| Action | Controle |
|--------|----------|
| Avancer/Reculer | W / S |
| Gauche/Droite | A / D |
| Courir | Shift |
| Regarder | Clic droit + Souris |
| Dessiner | Clic gauche |
| Laser pointer | L |
| Menu | Echap |
| Push-to-talk | V |

---

## 7. Configuration et parametres

### VRNetworkManager (Inspector)

| Parametre | Description | Valeur par defaut |
|-----------|-------------|-------------------|
| Server Url | Adresse du serveur WebSocket | ws://localhost:8080 |
| Reconnect Delay | Delai entre reconnexions | 2.0 secondes |
| Max Reconnect Attempts | Nombre max de tentatives | 5 |
| Offline Mode | Mode hors-ligne (debug) | false |

### Options in-game

**Audio :**
- Volume principal (0-100%)
- Volume voix (0-100%)
- Selection du microphone

**Graphiques :**
- Qualite (Low / Medium / High / Ultra)
- Resolution
- Mode plein ecran

**VR :**
- Mode de rotation : Snap (par paliers) ou Smooth (continu)
- Angle de snap : 15 / 30 / 45 / 90 degres
- Vitesse de rotation smooth

**Desktop :**
- Sensibilite souris
- Inverser l'axe Y

---

## 8. Depannage

### Le client ne se connecte pas au serveur

| Verifier | Solution |
|----------|----------|
| Serveur demarre ? | `npm run dev` dans Server/ |
| Bonne URL ? | Verifier VRNetworkManager > Server Url |
| Pare-feu ? | Ouvrir le port 8080 |
| Meme reseau ? (Quest) | PC et Quest sur le meme Wi-Fi |

### Pas de son (chat vocal)

| Verifier | Solution |
|----------|----------|
| Microphone autorise ? | Verifier permissions Windows/Quest |
| Push-to-talk actif ? | Maintenir V pour parler |
| Meme room ? | Les 2 joueurs doivent etre dans la meme salle |
| TURN configure ? | En production, configurer un serveur TURN |

### VR ne demarre pas

| Verifier | Solution |
|----------|----------|
| Casque connecte ? | Verifier la connexion USB/Wi-Fi |
| Runtime actif ? | Oculus App ou SteamVR lance |
| OpenXR configure ? | Project Settings > XR Plug-in Management |

### Le tableau blanc ne synchronise pas

| Verifier | Solution |
|----------|----------|
| Meme room ? | Les joueurs doivent etre dans la meme salle |
| Serveur demarre ? | Verifier les logs serveur |
| WhiteboardDrawingSurface ? | Composant present sur le tableau |

### Quest : Build echoue

| Erreur | Solution |
|--------|----------|
| "No Android SDK" | Installer Android Build Support dans Unity Hub |
| "Device not found" | Activer mode developpeur + debogage USB |
| "IL2CPP error" | Project Settings > Player > Scripting Backend = IL2CPP |

### Performance (FPS bas)

| Cause | Solution |
|-------|----------|
| Qualite trop haute | Reduire dans Options > Graphiques |
| Trop de joueurs | Limiter a 10 joueurs par room |
| Enregistrement actif | Desactiver ou reduire la qualite |

---

## Contacts

**Projet :** VRMeet
**Organisation :** Rndp
**Version Unity :** 6000.2.14f1

---

*Guide genere le 27 Fevrier 2026*
