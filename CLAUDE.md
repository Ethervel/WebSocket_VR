# CLAUDE.md

## Project Overview
Unity 6000.2.14f1 VR multiplayer meeting room. WebSocket (NativeWebSocket) + WebRTC voice. OpenXR (Quest, PCVR, Desktop).

## Tech Stack
- **Engine:** Unity 6000.2.14f1 | **Multiplayer:** WebSocket + WebRTC | **Database:** MariaDB via Node.js (never direct)
- **Platforms:** Quest, PCVR, Desktop | **Folder typo:** `Assets/Scrips/` (preserved)

## Build & Server
```bash
cd Server/ && npm install && npm run dev   # Dev server with auto-reload
```
**Server URL:** `ws://localhost:8080` | **Scenes:** Bootstrap (0), Meet (1) | **Testing:** ParrelSync

## Project Structure
```
Assets/Scrips/
├── Network/          VRNetworkManager.cs, VRRoomManager.cs, VRGameManager.cs
├── VR/               BootstrapManager.cs, VRPlayerController.cs, DesktopPlayerController.cs
├── WebRTC/           VoiceChatManager.cs, WebRTCPeerManager.cs, MicrophoneManager.cs
├── WhiteBoard/       Whiteboard.cs, WhiteboardDrawingSurface.cs, WhiteboardMarker.cs, DesktopWhiteboardDrawer.cs
├── Interaction/      LaserPointer.cs, LaserPointerData.cs
├── Sharing/          ScreenShareManager.cs, FileShareManager.cs, FilePresentationManager.cs
├── Avatar/           AvatarCustomization.cs, AvatarColorTarget.cs
├── Auth/             AuthManager.cs, AuthUI.cs (implemented, not yet used)
├── Recording/        RecordingManager.cs, SpectatorCameraController.cs, FFmpegEncoder.cs, AudioCapture.cs
├── Audio/            SoundManager.cs, AudioMuteZone.cs, AmbienceManager.cs
├── UI/MainMenu/      MainMenuManager.cs, MainMenuSettings.cs, MainMenuOptionsUI.cs
├── UI/Menu/          VRMenuUI.cs, VRMenuToggle.cs
└── Debug/            DebugManager.cs

Server/
├── server.js         Main WebSocket server
└── src/
    ├── database.js   MariaDB pool connection
    └── auth.js       bcrypt + JWT authentication

Assets/Prefabs/Unity/ LocalPlayer, RemoteVRPlayer, DesktopPlayer, WhiteboardComplete, XR Origin Hands
Assets/Scenes/        Bootstrap.unity (persistent), Meet.unity (additive)
```

## Architecture
**Scene Flow:** Bootstrap (singletons + DontDestroyOnLoad) → Meet (additive)

### Core Events (subscribe in OnEnable, unsubscribe in OnDisable)
```csharp
// VRNetworkManager
OnConnected, OnDisconnected, OnPeerConnected, OnPeerDisconnected, OnMessageReceived, OnConnectionError

// VRRoomManager
OnRoomCreated, OnRoomJoined, OnRoomLeft, OnPlayerJoined(VRPlayerData), OnPlayerLeft, OnRoomTypeChanged, OnAvatarUpdated

// VRGameManager
OnLocalPlayerSpawned, OnRemotePlayerSpawned, OnRemotePlayerDespawned
GetLocalPlayer(), GetRemotePlayer(id), GetRemotePlayerHead(id)

// VoiceChatManager
OnVoiceChatReady, OnPeerVoiceConnected, OnPeerVoiceDisconnected

// RecordingManager
OnRecordingStarted, OnRecordingStopped, OnStateChanged, OnMarkerAdded

// AuthManager (implemented, not yet integrated)
OnLoginSuccess, OnRegisterSuccess, OnAuthError, OnLogout
```

### Network Protocol
```csharp
[Serializable] public class NetworkMessage { string type, senderId, data; } // JsonUtility, no nested objects!
```

| Category | Message Types |
|----------|---------------|
| Connection | `welcome`, `peer-connected`, `peer-disconnected` |
| Rooms | `room-join`, `room-welcome`, `room-leave`, `room-list`, `room-teleport`, `player-name-update`, `avatar-update` |
| VR Sync | `vr-position` (30Hz) |
| Voice | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` |
| Whiteboard | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request`, `whiteboard-state` |
| Sharing | `screen-share-*`, `file-share-*`, `file-present-*`, `laser-pointer` |
| Recording | `recording-status`, `recording-marker` |
| Auth | `auth-login`, `auth-register`, `auth-verify`, `auth-logout`, `auth-response` |

### Key Systems

**Room System:** RoomType enum (`Lobby`, `MeetingRoomA`, `MeetingRoomB`), 6-char codes, host authority

**VR Sync (30Hz):** Movement threshold 0.01m/1°, interpolation 15, head+hands detached for world-space

**Voice Chat:** Mesh topology (smaller ID initiates), STUN+TURN, spatial audio on head, push-to-talk V key

**Whiteboard (3 layers):**
1. `Whiteboard.cs` - fond blanc + mode presentation
2. `WhiteboardDrawingSurface.cs` - transparent, network only (ne dessine pas!)
3. `WhiteboardMarker` (VR) / `DesktopWhiteboardDrawer` (Desktop) - local drawing

Config: 2048x2048, Sprites/Default shader, 33ms send rate, blue default color

**Screen Share:** 854x480 @ 3fps, JPEG 50%, VR+Desktop, displays on whiteboard presentation mode

**File Share:** 10MB max, extensions: pdf/doc/docx/xls/xlsx/png/jpg/jpeg/gif

**Laser Pointer:** VR=A button, Desktop=L key, 10Hz sync, red LineRenderer+dot

**Offline Mode:** Test without server - set `offlineMode=true` in VRNetworkManager Inspector

## Offline Mode (Debug)
In `VRNetworkManager` Inspector:
```
[Header("Debug / Offline Mode")]
offlineMode = true              // Skip server connection
offlineAutoCreateRoom = true    // Auto-create room on start
offlineRoomType = MeetingRoomA  // Room type to create
```
Simulates connection + room creation. All network sends are silently ignored.

## Recording System (VR-Optimized)

**Architecture:** 3-stage async pipeline to avoid VR motion sickness

```
Main Thread          Encode Thread       Write Thread
RequestFrame() ──▶  RGB → TGA ──────▶  File.Write()
  (~0.1ms)          (background)        (background)
     ↑
AsyncGPUReadback (non-blocking GPU read)
```

**Key files:**
- `SpectatorCameraController.cs` - AsyncGPUReadback, buffer pooling, camera in Meet scene
- `RecordingManager.cs` - Pipeline orchestration, ConcurrentQueues, host-only recording
- `FFmpegEncoder.cs` - TGA→MP4 encoding via FFmpeg
- `RecordingData.cs` - Settings, metadata, markers

**Settings (RecordingSettings):**
```csharp
width = 1920, height = 1080, frameRate = 30
jpegQuality = 85, captureAudio = true
outputFolder = "Recordings"
```

**Output:** TGA frames + audio.wav → FFmpeg → recording.mp4

**Markers:** Important, Question, Todo, Idea (synced across clients)

**Note:** SpectatorCamera must be in Meet scene (auto-detected, prioritizes Meet over Bootstrap)

## Controls

| Mode | Movement | Look | Actions |
|------|----------|------|---------|
| VR | Teleport | Head | A=Laser, Grab, V=Push-to-talk |
| Desktop | WASD+Shift | Right-click drag | L=Laser, Left-click=Draw |

## Code Conventions
- Events: subscribe `OnEnable`, unsubscribe `OnDisable`
- Serialization: `JsonUtility` + `[Serializable]`, **no nested objects**
- Logging: `DebugManager.Log(msg, DebugCategory.Network)` or `[SystemName]` prefix
- GC: cache message objects (`_cachedPositionData`)

## Settings (MainMenuSettings.cs)
Audio: MasterVolume, VoiceVolume, Microphone | Graphics: Quality, Resolution, Fullscreen
VR: TurnMode (0=Snap/1=Smooth), SnapAngle, SmoothTurnSpeed | Desktop: MouseSensitivity, InvertY

## Database & Auth (Implemented, Not Integrated)

**Architecture:** Unity ←WebSocket→ Node.js ←mariadb→ MariaDB (never direct connection!)

**Status:** Code exists but auth doesn't gate any features yet. Login/guest both have same access.

**Server files (exist):**
- `Server/src/database.js` - MariaDB pool (requires .env config)
- `Server/src/auth.js` - bcrypt 12 rounds, JWT 24h, rate limiting 5/min

**Unity files (exist):**
- `Assets/Scrips/Auth/AuthManager.cs` - Login, Register, Logout, Token verify
- `Assets/Scrips/Auth/AuthUI.cs` - Login/Register panels, guest mode, skipAuthInEditor

**Future integration ideas:**
- Private rooms (auth required to join)
- Meeting history linked to account
- Persistent avatar config in DB
- File upload quotas per user
- Admin/moderator roles

## Package Dependencies
| Package | Purpose |
|---------|---------|
| `com.endel.nativewebsocket` | WebSocket |
| `com.unity.webrtc` 3.0.0 | Voice |
| `com.unity.xr.interaction.toolkit` 3.2.2 | VR |
| `com.unity.xr.openxr` 1.16.1 | OpenXR |
| `com.unity.xr.hands` 1.7.2 | Hand tracking |
| `com.unity.render-pipelines.universal` 17.2.0 | URP |
| `com.veriorpies.parrelsync` | Multi-instance testing |

## Feature Status

| Done | Implemented (not used) | Planned |
|------|------------------------|---------|
| WebSocket + reconnect, WebRTC voice 3D, Avatar sync/customization, Whiteboard, Desktop mode, Main menu + settings, Screen share, File share/presentation, Laser pointer, VR Menu, Sound system, Offline mode, Recording (VR-optimized) | Auth (login/register/guest) | SSO, E2E encryption, GDPR, Admin panel, Advanced avatars, Calendar, Meeting history |

## Important Notes
- **XR Layers:** Teleport on bit 31 only, Grab must NOT include Teleport layer
- **Remote players:** Head/hands detached from hierarchy (world-space targets)
- **Late joiners:** Request state via `*-request` messages, receive `*-state`
- **Room-scoped:** All sync messages include `roomId`
- **Recording:** Requires FFmpeg in PATH, SpectatorCamera in Meet scene, host-only
- **Recording VR:** Uses AsyncGPUReadback + background threads to avoid motion sickness

## Recent Changes (Session)

### Auth Flow Integration (DONE)
Le bouton Start affiche maintenant l'écran d'authentification avant de charger le jeu:
```
Main Menu → [Start] → Auth Screen → [Login/Register/Guest] → Loading → Meet
```
**Fichiers modifiés:**
- `AuthUI.cs` - Ajout singleton, event `OnAuthComplete`, méthode `Show()`
- `MainMenuManager.cs` - `OnStartClicked()` affiche AuthUI, écoute `OnAuthComplete`

**Note:** AuthUI est dans `MainMenuUI/Background/AuthPanel`

### VR Canvas Adapter (DONE)
`VRCanvasAdapter.cs` - Adapte les Canvas pour VR (Screen Space → World Space)
- À ajouter sur le Canvas "Loading screen" dans Bootstrap

### Launch Loading Screen (DONE)
`LaunchLoadingScreen.cs` - Ecran de chargement au lancement avec barre de progression
- Initialisation XR (0-20%), Network (20-50%), Auth (50-70%), Settings (70-90%), Finalize (90-100%)
- Auto-detection des references UI, fade out, event OnLoadingComplete

## TODO / Features à implémenter

### XR Socket Interactor (Snap Zone)
Emplacement où les objets grabbables se "snappent" automatiquement.

**Sur la Snap Zone :**
```
GameObject: SnapZone_XXX
├── XR Socket Interactor
├── Collider (Is Trigger = ✓)
└── Mesh Renderer (optionnel, visuel de l'emplacement)
```

**Paramètres XR Socket Interactor :**
- `Interaction Layer Mask` - Quels layers peuvent être socketés
- `Show Interactable Hover Meshes` - Aperçu de l'objet au hover
- `Hover Mesh Material` - Material transparent pour l'aperçu
- `Recycle Delay Time` - Délai avant re-socket

**Sur l'objet grabbable :**
- `XR Grab Interactable` + `Rigidbody` + `Collider` (déjà en place)

**Flow :** Grab objet → Approche socket → Release → Snap à position/rotation du socket

**Filtrage par tag (optionnel) :** Script `FilteredSocket.cs` avec `acceptedTag`

**Cas d'usage :** Inventaire, puzzles, rangement d'objets, clés/serrures

### Room Preview UI (Lobby)
Afficher un aperçu des rooms au hover sur les boutons de téléportation.

**Structure :**
```
Lobby/
├── RoomPreviewUI (Canvas World Space)
│   ├── TrackedDeviceGraphicRaycaster
│   └── PreviewPanel (désactivé par défaut)
│       ├── RoomImage (Image)
│       └── RoomName (TextMeshPro)
├── LobbyToA/Canvas/Button + RoomPreviewTrigger.cs
└── LobbyToB/Canvas/Button + RoomPreviewTrigger.cs
```

**Scripts à créer :**
- `RoomPreviewUI.cs` - Singleton, Show(name, sprite) / Hide()
- `RoomPreviewTrigger.cs` - IPointerEnterHandler/ExitHandler, appelle RoomPreviewUI

**Assets :** Screenshots des rooms dans `Assets/images/` (confRoom.png, Room2.png)
