# Documentation Technique - VRMeet
## Application de Salles de Reunion Virtuelles Multiplayer

---

## 1. Vue d'Ensemble du Projet

### 1.1 Description
**VRMeet** est une application de salles de reunion virtuelles multiplayer developpee avec Unity 6000.2.14f1. Elle permet a plusieurs utilisateurs de se reunir dans un environnement 3D immersif, que ce soit en realite virtuelle (VR) ou en mode desktop classique.

### 1.2 Objectifs
- Permettre des reunions virtuelles immersives
- Support multi-plateforme (Quest, PCVR, Desktop)
- Communication vocale en temps reel
- Outils collaboratifs (tableau blanc, partage d'ecran, presentation de fichiers)
- Systeme d'authentification utilisateur

### 1.3 Informations du Projet
| Element | Valeur |
|---------|--------|
| Nom du produit | VrMeet |
| Entreprise | Rndp |
| Version Unity | 6000.2.14f1 |
| Pipeline de rendu | Universal Render Pipeline (URP) 17.2.0 |
| Plateformes cibles | Meta Quest, PCVR, Windows Desktop |

---

## 2. Stack Technologique

### 2.1 Moteur de Jeu et Frameworks

#### Unity 6000.2.14f1
- Moteur de jeu principal
- Gestion du rendu 3D avec URP (Universal Render Pipeline)
- Systeme de physique integre
- Gestion des scenes et prefabs

#### XR Interaction Toolkit 3.2.2
- Framework officiel Unity pour les interactions VR
- Gestion des controllers et hand tracking
- Systeme de teleportation
- Interactions avec les objets (grab, poke, etc.)

#### OpenXR 1.16.1
- Standard ouvert pour la VR/AR
- Compatibilite multi-casques (Quest, Index, Vive, etc.)
- Abstraction des entrees materielle

### 2.2 Reseau et Communication

#### NativeWebSocket
- **Source:** https://github.com/endel/NativeWebSocket
- **Role:** Communication bidirectionnelle client-serveur
- **Utilisation:** Synchronisation des positions, gestion des rooms, signaling WebRTC

#### Unity WebRTC 3.0.0
- **Role:** Communication vocale peer-to-peer
- **Protocole:** WebRTC avec STUN/TURN
- **Caracteristiques:** Audio spatial 3D, mesh topology

### 2.3 Backend Server

#### Node.js (>= 16.0.0)
Runtime JavaScript cote serveur

#### Dependances NPM
| Package | Version | Role |
|---------|---------|------|
| ws | ^8.14.2 | Serveur WebSocket |
| uuid | ^9.0.1 | Generation d'identifiants uniques |
| dotenv | ^16.3.1 | Variables d'environnement |
| pdf-poppler | ^0.2.3 | Conversion PDF (optionnel) |
| jest | ^30.2.0 | Tests unitaires (dev) |

#### Base de Donnees (Optionnel)
- **MariaDB** pour l'authentification
- **bcrypt** pour le hachage des mots de passe (12 rounds)
- **JWT** pour les tokens de session (24h de validite)

### 2.4 Packages Unity Utilises

| Package | Version | Role |
|---------|---------|------|
| com.unity.xr.interaction.toolkit | 3.2.2 | Interactions VR |
| com.unity.xr.openxr | 1.16.1 | Runtime OpenXR |
| com.unity.xr.hands | 1.7.2 | Hand tracking |
| com.unity.webrtc | 3.0.0 | Communication vocale |
| com.unity.render-pipelines.universal | 17.2.0 | Rendu URP |
| com.unity.inputsystem | 1.16.0 | Nouveau systeme d'input |
| com.endel.nativewebsocket | GitHub | WebSocket natif |
| com.veriorpies.parrelsync | GitHub | Test multi-instances |
| com.unity.nuget.newtonsoft-json | 3.2.1 | Serialisation JSON avancee |

---

## 3. Architecture Technique

### 3.1 Architecture Client-Serveur

```
┌─────────────────────────────────────────────────────────────────────┐
│                         CLIENTS UNITY                                │
├─────────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │
│  │   Quest     │  │    PCVR     │  │   Desktop   │                 │
│  │   Client    │  │   Client    │  │   Client    │                 │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘                 │
│         │                │                │                         │
│         └────────────────┼────────────────┘                         │
│                          │                                          │
│                    WebSocket + WebRTC                               │
└──────────────────────────┼──────────────────────────────────────────┘
                           │
┌──────────────────────────┼──────────────────────────────────────────┐
│                     SERVER NODE.JS                                   │
├──────────────────────────┼──────────────────────────────────────────┤
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    WebSocket Server                          │   │
│  │  - Gestion des connexions clients                            │   │
│  │  - Routage des messages                                      │   │
│  │  - Gestion des rooms                                         │   │
│  │  - Signaling WebRTC                                          │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                          │                                          │
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                 MariaDB (Optionnel)                          │   │
│  │  - Authentification utilisateurs                             │   │
│  │  - Stockage des profils                                      │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 Architecture des Scenes Unity

```
Bootstrap (Scene 0) - Persistante
├── NetworkManager (VRNetworkManager)
├── RoomManager (VRRoomManager)
├── GameManager (VRGameManager)
├── VoiceChatManager
├── AuthManager
├── SoundManager
├── DebugManager
└── MainMenuUI

Meet (Scene 1) - Additive
├── Environment (Lobby, Meeting Rooms)
├── Whiteboards
├── SpectatorCamera (Recording)
└── Room-specific objects
```

### 3.3 Pattern Singleton et Persistence

Les managers principaux suivent le pattern Singleton avec `DontDestroyOnLoad`:

```csharp
public class VRNetworkManager : MonoBehaviour
{
    public static VRNetworkManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
```

### 3.4 Systeme d'Evenements

Architecture basee sur les evenements C# pour le decouplage:

```csharp
// VRNetworkManager Events
public static event Action OnConnected;
public static event Action OnDisconnected;
public static event Action<string> OnPeerConnected;
public static event Action<string> OnPeerDisconnected;
public static event Action<NetworkMessage> OnMessageReceived;
public static event Action<string> OnConnectionError;

// VRRoomManager Events
public static event Action OnRoomCreated;
public static event Action OnRoomJoined;
public static event Action OnRoomLeft;
public static event Action<VRPlayerData> OnPlayerJoined;
public static event Action<string> OnPlayerLeft;
```

---

## 4. Structure du Code Source

### 4.1 Organisation des Dossiers

```
Assets/Scrips/
├── Network/           # Coeur reseau
│   ├── VRNetworkManager.cs      # Connexion WebSocket, singleton principal
│   ├── VRRoomManager.cs         # Gestion des rooms et joueurs
│   └── VRGameManager.cs         # Spawn/despawn des players
│
├── VR/                # Controleurs VR
│   ├── BootstrapManager.cs      # Initialisation de l'application
│   ├── VRPlayerController.cs    # Controleur joueur VR local
│   ├── VRTrackingFix.cs         # Corrections tracking
│   ├── ControllerTrackingFix.cs # Fix tracking controllers
│   ├── ControllerModelLoader.cs # Chargement modeles controllers
│   ├── ControllerInputFix.cs    # Fix inputs controllers
│   ├── TeleportOnButtonClick.cs # Teleportation UI
│   ├── TeleportOnGrab.cs        # Teleportation par grab
│   ├── XRUIInteractionBridge.cs # Bridge UI/XR
│   └── XRInteractorInputBridge.cs # Bridge Input/XR
│
├── Desktop/           # Mode desktop
│   └── DesktopPlayerController.cs # Controleur clavier/souris
│
├── WebRTC/            # Communication vocale
│   ├── VoiceChatManager.cs      # Orchestration voice chat
│   ├── WebRTCPeerManager.cs     # Gestion connexions peer
│   ├── MicrophoneManager.cs     # Capture microphone
│   ├── WebRTCSignaling.cs       # Signaling offer/answer/ICE
│   ├── WebRTCConfiguration.cs   # Config STUN/TURN
│   ├── RemoteAudioManager.cs    # Audio distant spatial
│   └── VoiceChatData.cs         # Structures de donnees
│
├── WhiteBoard/        # Tableau blanc collaboratif
│   ├── Whiteboard.cs            # Fond blanc + mode presentation
│   ├── WhiteboardDrawingSurface.cs # Surface de dessin reseau
│   ├── WhiteboardMarker.cs      # Marqueur VR
│   ├── DesktopWhiteboardDrawer.cs # Dessin desktop
│   ├── WhiteboardEraser.cs      # Gomme
│   ├── WhiteboardNetworkData.cs # Donnees reseau
│   └── WhiteboardUI*.cs         # Scripts UI
│
├── Interaction/       # Interactions
│   ├── LaserPointer.cs          # Pointeur laser
│   ├── LaserPointerData.cs      # Donnees laser
│   ├── VRNetworkedInteractable.cs # Objets interactifs reseau
│   └── RoomBlocker.cs           # Blocage acces rooms
│
├── Sharing/           # Partage de contenu
│   ├── ScreenShareManager.cs    # Partage d'ecran
│   ├── FileShareManager.cs      # Partage de fichiers
│   ├── FilePresentationManager.cs # Presentation de fichiers
│   ├── WindowCapture.cs         # Capture fenetre
│   └── *Data.cs                 # Structures de donnees
│
├── Avatar/            # Personnalisation avatar
│   ├── AvatarCustomization.cs   # Systeme de customisation
│   └── AvatarColorTarget.cs     # Cibles de couleur
│
├── Auth/              # Authentification
│   ├── AuthManager.cs           # Gestion auth
│   └── AuthUI.cs                # Interface auth
│
├── Recording/         # Enregistrement video
│   ├── RecordingManager.cs      # Pipeline d'enregistrement
│   ├── SpectatorCameraController.cs # Camera spectateur
│   ├── FFmpegEncoder.cs         # Encodage FFmpeg
│   ├── AudioCapture.cs          # Capture audio
│   └── RecordingData.cs         # Donnees/settings
│
├── Audio/             # Systeme audio
│   ├── SoundManager.cs          # Manager sons
│   ├── AmbienceManager.cs       # Sons d'ambiance
│   ├── AudioMuteZone.cs         # Zones de mute
│   └── UIButtonSounds.cs        # Sons boutons
│
├── UI/                # Interface utilisateur
│   ├── MainMenu/                # Menu principal
│   ├── Menu/                    # Menu VR in-game
│   ├── VRMenuUi.cs              # UI menu VR
│   ├── VRCanvasAdapter.cs       # Adaptation canvas VR
│   ├── LaunchLoadingScreen.cs   # Ecran de chargement
│   └── ...
│
├── Utils/             # Utilitaires
│   ├── TransformUtility.cs
│   ├── JsonHelper.cs
│   ├── ScreenFader.cs
│   ├── LoadingIndicator.cs
│   └── SceneLoader.cs
│
├── Effects/           # Effets visuels
│   └── GlowingLight.cs
│
└── Debug/             # Debug
    ├── DebugManager.cs
    └── XRDebugOverlay.cs
```

### 4.2 Structure du Serveur

```
Server/
├── server.js          # Point d'entree, serveur WebSocket principal
├── package.json       # Dependances NPM
├── .env               # Variables d'environnement (non versionne)
└── src/
    ├── database.js    # Pool de connexion MariaDB
    └── auth.js        # Authentification bcrypt + JWT
```

---

## 5. Protocole Reseau

### 5.1 Format des Messages

```csharp
[Serializable]
public class NetworkMessage
{
    public string type;     // Type du message
    public string senderId; // ID de l'emetteur
    public string data;     // Payload JSON (string)
}
```

### 5.2 Types de Messages

| Categorie | Types | Description |
|-----------|-------|-------------|
| Connexion | `welcome`, `peer-connected`, `peer-disconnected` | Handshake et gestion peers |
| Rooms | `room-join`, `room-leave`, `room-available`, `room-closed`, `room-list` | Gestion des salles |
| Sync VR | `vr-position` | Synchronisation position (30Hz) |
| Voice | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Signaling WebRTC |
| Whiteboard | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request`, `whiteboard-state` | Tableau blanc |
| Sharing | `screen-share-*`, `file-share-*`, `file-present-*` | Partage de contenu |
| Recording | `recording-status`, `recording-marker` | Enregistrement |
| Auth | `auth-login`, `auth-register`, `auth-verify`, `auth-logout`, `auth-response` | Authentification |

### 5.3 Synchronisation VR (30Hz)

```csharp
// Donnees de position envoyees a 30Hz
public class VRPositionData
{
    public float[] headPos;     // [x, y, z]
    public float[] headRot;     // [x, y, z, w] (quaternion)
    public float[] leftHandPos;
    public float[] leftHandRot;
    public float[] rightHandPos;
    public float[] rightHandRot;
}
```

---

## 6. Workflow de Developpement

### 6.1 Configuration de l'Environnement

#### Prerequisites
1. **Unity Hub** avec Unity 6000.2.14f1
2. **Node.js** >= 16.0.0
3. **Git** pour le versioning
4. **Visual Studio** ou **Rider** pour le developpement C#

#### Installation
```bash
# 1. Cloner le repository
git clone <repository-url>
cd WebSocket_VR

# 2. Ouvrir le projet Unity
# Via Unity Hub, ouvrir le dossier WebSocket_VR

# 3. Installer les dependances serveur
cd Server
npm install

# 4. Configurer l'environnement (optionnel pour auth)
cp .env.example .env
# Editer .env avec les credentials DB
```

### 6.2 Lancement en Developpement

```bash
# Terminal 1: Lancer le serveur
cd Server
npm run dev   # Avec auto-reload

# Terminal 2: Lancer Unity Editor
# Play mode dans l'editeur
```

### 6.3 Test Multi-Instances avec ParrelSync

ParrelSync permet de tester le multiplayer localement:

1. **Window > ParrelSync > Clones Manager**
2. Creer un clone du projet
3. Ouvrir le clone dans une nouvelle instance Unity
4. Lancer les deux instances en Play mode

### 6.4 Mode Offline (Debug)

Pour tester sans serveur:

1. Selectionner `VRNetworkManager` dans la hierarchie
2. Dans l'Inspector, cocher `Offline Mode`
3. Configurer `Offline Room Type` si desire
4. Lancer le Play mode

### 6.5 Conventions de Code

#### Evenements
```csharp
// S'abonner dans OnEnable
void OnEnable()
{
    VRRoomManager.OnPlayerJoined += HandlePlayerJoined;
}

// Se desabonner dans OnDisable
void OnDisable()
{
    VRRoomManager.OnPlayerJoined -= HandlePlayerJoined;
}
```

#### Serialisation JSON
```csharp
// Utiliser JsonUtility avec [Serializable]
// PAS d'objets imbriques complexes
[Serializable]
public class MyData
{
    public string id;
    public float value;
}

string json = JsonUtility.ToJson(data);
MyData parsed = JsonUtility.FromJson<MyData>(json);
```

#### Logging
```csharp
// Format: [SystemName] Message
Debug.Log("[VRNet] Connected to server");
Debug.LogWarning("[Voice] Microphone not found");
Debug.LogError("[Room] Failed to join room");
```

---

## 7. Fonctionnalites Implementees

### 7.1 Systeme de Rooms

- **Types de rooms:** Lobby, MeetingRoomA, MeetingRoomB
- **Codes de room:** 6 caracteres alphanumeriques
- **Autorite host:** Le createur de la room est l'host
- **Kick players:** L'host peut expulser des joueurs

### 7.2 Synchronisation VR

- **Frequence:** 30Hz
- **Seuil de mouvement:** 0.01m / 1 degre
- **Interpolation:** Factor 15 pour fluidite
- **Detachement tete/mains:** World-space pour precision

### 7.3 Communication Vocale (WebRTC)

- **Topologie:** Mesh (chaque client connecte a tous les autres)
- **Initiation:** Le client avec l'ID le plus petit initie
- **Serveurs:** STUN + TURN pour NAT traversal
- **Audio spatial:** Source attachee a la tete des avatars
- **Push-to-talk:** Touche V (optionnel)

### 7.4 Tableau Blanc

- **Architecture 3 couches:**
  1. `Whiteboard.cs` - Fond blanc + mode presentation
  2. `WhiteboardDrawingSurface.cs` - Surface transparente, reseau
  3. `WhiteboardMarker/DesktopWhiteboardDrawer` - Dessin local
- **Resolution:** 2048x2048
- **Frequence envoi:** 33ms
- **Synchronisation late-joiners:** Request/State pattern

### 7.5 Partage d'Ecran

- **Resolution:** 854x480
- **Framerate:** 3 FPS
- **Compression:** JPEG 50%
- **Affichage:** Mode presentation du whiteboard

### 7.6 Partage de Fichiers

- **Taille max:** 10MB
- **Extensions:** pdf, doc, docx, xls, xlsx, png, jpg, jpeg, gif
- **Transfer:** Par chunks via WebSocket

### 7.7 Pointeur Laser

- **VR:** Bouton A
- **Desktop:** Touche L
- **Frequence sync:** 10Hz
- **Visuel:** LineRenderer rouge + point

### 7.8 Enregistrement Video

- **Architecture:** Pipeline 3 etapes async (evite motion sickness VR)
  1. Main Thread: AsyncGPUReadback
  2. Encode Thread: RGB -> TGA
  3. Write Thread: File.Write()
- **Resolution:** 1920x1080 @ 30fps
- **Host-only:** Seul l'host peut enregistrer
- **Sortie:** TGA frames + WAV -> FFmpeg -> MP4

### 7.9 Authentification

- **Flow:** Main Menu -> Auth Screen -> Meet
- **Options:** Login, Register, Guest
- **Securite:** bcrypt 12 rounds, JWT 24h, rate limiting 5/min
- **Optionnel:** Fonctionne sans database en mode guest

---

## 8. Controles

### 8.1 Mode VR

| Action | Input |
|--------|-------|
| Deplacement | Teleportation (raycast + trigger) |
| Regard | Rotation de la tete |
| Pointeur laser | Bouton A |
| Grab objets | Grip |
| Push-to-talk | Touche V (clavier) |
| Menu | Bouton Menu |

### 8.2 Mode Desktop

| Action | Input |
|--------|-------|
| Deplacement | WASD + Shift (courir) |
| Regard | Clic droit + souris |
| Pointeur laser | Touche L |
| Dessiner | Clic gauche |
| Menu | Echap |

---

## 9. Deploiement

### 9.1 Build Unity

1. **File > Build Settings**
2. Selectionner la plateforme (Windows, Android)
3. Pour Quest: Configurer les XR settings
4. Build & Run

### 9.2 Deploiement Serveur

```bash
# Production
cd Server
npm start

# Avec PM2 (recommande)
pm2 start server.js --name vrmeet-server
```

### 9.3 Configuration Production

```env
# .env
PORT=8080
DB_HOST=localhost
DB_USER=vrmeet
DB_PASSWORD=secure_password
DB_NAME=vrmeet_db
JWT_SECRET=your_jwt_secret_key
```

### 9.4 Securite

- Utiliser `wss://` (WebSocket Secure) en production
- Configurer `enforceSecureConnection = true` dans VRNetworkManager
- Mettre en place un reverse proxy (nginx) avec SSL

---

## 10. Historique des Commits (Extraits)

```
984c969 commit for creating a branch (little changement in the fade)
448be19 upscaling
865a179 wss://vrmeeting-test.duckdns.org/, server connection test
2dfe067 menu and transition update
efd8d86 add door that disappear at room creation
3885a3a update project memo, make recording mode more fluid
f55b8f3 add auth UI
3b8db9b auth code
525baae update for recording, add server documentation
1207571 move audio manager to bootstrap, create ambiant mute zones
79e59b2 add audio effects
845f798 UI fix for desktop mode
0c48b57 whiteboard drawing fix
679e645 change resolution, whiteboard drawing fix
```

---

## 11. Ressources et References

### 11.1 Documentation Officielle
- [Unity XR Interaction Toolkit](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.2/manual/index.html)
- [Unity WebRTC](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/index.html)
- [OpenXR](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.16/manual/index.html)
- [NativeWebSocket](https://github.com/endel/NativeWebSocket)

### 11.2 Packages Tiers
- [ParrelSync](https://github.com/VeriorPies/ParrelSync) - Test multi-instances
- [ws (Node.js)](https://github.com/websockets/ws) - WebSocket server

---

## 12. Auteurs et Licence

**Projet:** VRMeet
**Organisation:** Rndp
**Annee:** 2024-2025

---

*Documentation generee le 27 Fevrier 2026*
