# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity 6000.2.14f1 VR multiplayer meeting room application using WebSockets (NativeWebSocket) for real-time networking and WebRTC for voice chat. Targets OpenXR-compatible headsets (Quest, PCVR).

## Product Vision & Requirements

### Tech Stack
- **Engine:** Unity 6000.2.14f1
- **Multiplayer:** WebSocket (NativeWebSocket) + WebRTC
- **Database:** MariaDB (user data, sessions, meetings persistence)
- **Platforms:** Cross-platform hybrid (VR headsets, Desktop, potentially Web)

### Core Features

| Feature | Description | Status |
|---------|-------------|--------|
| **Spatial Audio** | 3D positional audio for natural conversations | Implemented |
| **Presentation Tools** | Screen sharing, slides, media playback | In Progress |
| **Interactive Whiteboard** | Real-time collaborative drawing, network-synced | Implemented |
| **3D Object Manipulation** | Grab, move, scale, rotate shared objects | Planned |
| **Expressive Professional Avatars** | Business-appropriate customizable avatars | Not Started |
| **Modular Environments** | Configurable meeting room layouts | Planned |
| **Note-taking & Export** | In-meeting notes with PDF/text export | Planned |
| **Screen Sharing** | Share desktop/window to Whiteboard | In Progress |

### Must Have (Non-Negotiable)

| Requirement | Implementation Notes |
|-------------|---------------------|
| **Cross-Platform Hybrid** | Quest, PCVR, Desktop mode (no VR required) |
| **Zero Friction Onboarding** | Intuitive UI - first-time VR users must succeed without tutorial |
| **Data Encryption** | End-to-end for voice, TLS for all network traffic |
| **GDPR Compliance** | Data residency, deletion rights, consent management |
| **SSO Integration** | OAuth2/OIDC for enterprise identity providers |
| **Low Latency Audio** | < 150ms round-trip, stable connection |
| **Stable Performance** | 72+ FPS on Quest, no frame drops |

### Must NOT Have (Design Constraints)

| Anti-Pattern | Why | Mitigation |
|--------------|-----|------------|
| "Video Game" aesthetic | Undermines professional credibility | Clean, minimal UI; corporate-friendly environments |
| Motion sickness triggers | Excludes users, bad UX | Teleport locomotion only; no forced camera movement; stable horizon |
| Isolated from work tools | Reduces adoption | Calendar integration, file sharing, meeting recordings |
| Hardware overheating | Quest thermal throttling ruins meetings | LOD optimization, texture compression, occlusion culling |
| Complex controls | Blocks non-VR users | One-button actions, familiar desktop metaphors |

### Feature Roadmap Priority

**Phase 1 - Foundation (Current)**
- [x] WebSocket networking
- [x] WebRTC voice chat (mesh topology)
- [x] Spatial audio (3D positioned on head)
- [x] Basic avatar sync
- [x] Whiteboard
- [ ] Desktop mode (non-VR)
- [ ] MariaDB integration

**Phase 2 - Collaboration**
- [~] Screen sharing (implémenté, à tester)
- [~] File sharing (implémenté, à tester)
- [ ] 3D object manipulation
- [ ] Presentation mode
- [ ] Note-taking system

**Phase 3 - Enterprise**
- [ ] SSO authentication
- [ ] End-to-end encryption
- [ ] GDPR compliance tools
- [ ] Admin dashboard
- [ ] Meeting recordings

**Phase 4 - Polish**
- [ ] Advanced avatars (expressions, gestures)
- [ ] Modular environments
- [ ] Calendar integration
- [ ] Mobile companion app

## Build & Development

**Server Requirement:** Requires a Node.js WebSocket server running at `ws://localhost:8080` (configurable in `VRNetworkManager.serverUrl`). Server handles message routing, room management, and WebRTC signaling.

**Testing Multiplayer Locally:** Use ParrelSync to clone the project and run multiple Unity instances.

**Quick Test Tools:** Press F1 in-game to toggle QuickRoomJoiner debug UI, F2 to create room, F3 to leave.

## Project Structure

```
Assets/Scrips/                    (Note: intentional typo "Scrips" - preserved for consistency)
├── Network/
│   ├── VRNetworkManager.cs       # WebSocket hub, message routing, auto-reconnect
│   ├── VRRoomManager.cs          # Room lifecycle, player roster, zone tracking
│   └── VRGameManager.cs          # Player spawning, VR pose sync (30Hz), interpolation
├── VR/
│   ├── BootstrapManager.cs       # Additive scene loading, persistent EventSystem setup
│   ├── VRPlayerController.cs     # Locomotion, snap/smooth turn, gravity
│   └── TeleportOnGrab.cs         # VR teleportation mechanics
├── WebRTC/
│   └── VoiceChatManager.cs       # WebRTC peers, spatial audio, push-to-talk
├── WhiteBoard/
│   ├── Whiteboard.cs             # Network-synced whiteboard + presentation mode
│   ├── WhiteboardMarker.cs       # Drawing input handling
│   ├── WhiteboardNetworkData.cs  # Serializable network classes
│   └── WhiteboardUIManager.cs    # Whiteboard UI controls
├── Sharing/
│   ├── ScreenShareManager.cs     # Screen capture + WebRTC video streaming
│   ├── ScreenShareUI.cs          # UI controls for screen share
│   ├── FileShareManager.cs       # File chunking and transfer
│   ├── FileShareData.cs          # Serializable file data classes
│   ├── FileViewer.cs             # Open/display shared files
│   ├── SharedFileUI.cs           # UI for file list
│   ├── VirtualScreen.cs          # Virtual display (unused, Whiteboard used instead)
│   └── SharingSystemSetup.cs     # Menu: VR Meeting > Setup Sharing System
├── UI/
│   ├── GlobalKeyboardAutoBind.cs
│   ├── VoiceChatUI.cs
│   └── VRMenuUi.cs
└── Testing/
    ├── QuickRoomJoiner.cs        # Debug room management (F1/F2/F3)
    └── VRNetworkedInteractable.cs

Assets/Scenes/
├── Bootstrap.unity               # Persistent managers (loads first)
├── Meet.unity                    # Main VR environment (loaded additively)
└── Test.unity

Assets/Prefabs/Unity/
├── LocalPlayer.prefab            # Local VR rig (XROrigin + CharacterController)
├── RemoteVRPlayer.prefab         # Remote avatar (head/hands detached for world-space sync)
├── Playername.prefab             # Floating name tag
├── Playeritem.prefab / RoomItem.prefab  # UI list items
└── XR Origin*.prefab             # XR rig templates
```

## Architecture

### Scene Flow
1. **Bootstrap** (persistent) - All singleton managers with `DontDestroyOnLoad`
2. **Meet** (additive) - Main VR environment, spawn points, whiteboard, teleport areas

### Core Singletons & Event Communication

Managers communicate via static events. **Subscribe in `OnEnable`, unsubscribe in `OnDisable`:**

```csharp
// VRNetworkManager (WebSocket) - Assets/Scrips/Network/VRNetworkManager.cs
VRNetworkManager.OnConnected           // Server assigned LocalId
VRNetworkManager.OnDisconnected
VRNetworkManager.OnPeerConnected       // Another client joined server
VRNetworkManager.OnPeerDisconnected
VRNetworkManager.OnMessageReceived     // NetworkMessage for game logic
VRNetworkManager.OnConnectionError

// VRRoomManager (Rooms) - Assets/Scrips/Network/VRRoomManager.cs
VRRoomManager.OnRoomCreated(roomId)
VRRoomManager.OnRoomJoined(roomId)
VRRoomManager.OnRoomLeft()
VRRoomManager.OnPlayerJoined(VRPlayerData)
VRRoomManager.OnPlayerLeft(playerId)
VRRoomManager.OnRoomTypeChanged(RoomType)
VRRoomManager.OnRoomListUpdated(Dictionary<string, RoomInfo>)
VRRoomManager.OnRoomError(message)

// VRGameManager (Spawning) - Assets/Scrips/Network/VRGameManager.cs
VRGameManager.OnLocalPlayerSpawned(GameObject)
VRGameManager.OnRemotePlayerSpawned(playerId, GameObject)
VRGameManager.OnRemotePlayerDespawned(playerId)

// VoiceChatManager (WebRTC) - Assets/Scrips/WebRTC/VoiceChatManager.cs
VoiceChatManager.OnVoiceChatReady
VoiceChatManager.OnPeerVoiceConnected(peerId)
VoiceChatManager.OnPeerVoiceDisconnected(peerId)
VoiceChatManager.OnMicrophoneStateChanged(bool)
```

### Network Message Protocol

All messages use `NetworkMessage` format (defined in `VRNetworkManager.cs`):
```csharp
[Serializable]
public class NetworkMessage {
    public string type;      // Message type identifier
    public string senderId;  // Peer ID from server
    public string data;      // JSON payload (JsonUtility - no nested objects!)
}
```

**Message Types:**
| Category | Types |
|----------|-------|
| Connection | `welcome`, `peer-connected`, `peer-disconnected` |
| Rooms | `room-available`, `room-join`, `room-welcome`, `room-leave`, `room-list`, `room-teleport`, `room-closed`, `player-name-update` |
| VR Sync | `vr-position` (30Hz, optimized with movement threshold) |
| Voice | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` |
| Whiteboard | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request`, `whiteboard-state` |
| Screen Share | `screen-share-start`, `screen-share-stop`, `screen-video-offer`, `screen-video-answer`, `screen-video-ice` |
| File Share | `file-announce`, `file-chunk`, `file-complete`, `file-request`, `file-list-request`, `file-list-response` |

### Room System

- **RoomType enum:** `Lobby`, `MeetingRoomA`, `MeetingRoomB`
- **Room codes:** 6-character alphanumeric (charset excludes O/0, I/1 for clarity)
- **Host authority:** Host manages roster, broadcasts `room-welcome` with authoritative player list
- **Zone changes:** `TeleportToRoomType()` changes zone without reconnection
- **Player name:** Stored in `PlayerPrefs.GetString("PlayerName")`

### VR Sync Details (`VRGameManager.cs`)

- **Sync rate:** 30Hz (configurable via `syncRate`)
- **Movement threshold:** Only syncs when delta > 0.01m position or 1° rotation
- **Interpolation speed:** 15 (configurable)
- **Remote player design:** Head and hands are **detached from hierarchy** to follow world-space targets
- **Data synced:** Body position/Y-rotation, head position/quaternion, both hands position/quaternion
- **Public utilities:**
  - `GetLocalPlayer()` → local player GameObject
  - `GetRemotePlayer(playerId)` → remote player body GameObject
  - `GetRemotePlayerHead(playerId)` → remote player head Transform (for spatial audio)

### Voice Chat (`VoiceChatManager.cs`)

- **STUN servers:** Google public (`stun:stun.l.google.com:19302`)
- **Mesh topology:** All clients connected to each other (not just to host)
  - Deterministic rule: player with smaller ID (lexicographically) initiates connection
  - Ensures no duplicate connections and full mesh with 3+ clients
- **Spatial audio:**
  - AudioSource attached to remote player's **head** (via `GetRemotePlayerHead()`)
  - 3D spatialBlend = 1.0, maxDistance = 20m, Linear rolloff
  - AudioListener on local player's Main Camera
- **Push-to-talk:** V key (desktop), VR button (configurable)
- **Auto-start:** Microphone starts automatically on initialization

### Whiteboard (`WhiteBoard/Whiteboard.cs`)

- **Texture:** 2048x2048 (configurable)
- **Network format:** `WhiteboardPacket` with `pointsFlat` array (u,v pairs)
- **Room-scoped:** All messages include `roomId` for filtering
- **Late joiner sync:**
  - Requests state on `OnRoomJoined` or at `Start()` if already in room
  - Responds with PNG base64 encoded texture (not history-dependent)
- **Room change behavior:**
  - Clears texture on `OnRoomLeft`
  - Requests state on `OnRoomJoined`
- **History buffer:** 100 packets max (for received network packets only)

### Networked Interactables (`VRNetworkedInteractable.cs`)

- **Room-scoped sync:** All sync messages include `roomId`
- **Room change behavior:**
  - Resets to initial spawn position on room change/leave
  - Requests current state from other players on room join
- **Ownership:** Grab to take ownership, deterministic sync
- **State request:** Late joiners request object positions via `obj-state-request`

### XR Interaction Toolkit Configuration

- **Prefab:** `Assets/Prefabs/Unity/XR Origin Hands (XR Rig).prefab`
- **Interaction Layers:**
  - Poke Interactors (hands): Layer "Default" only (`m_Bits: 1`)
  - Teleport Interactor: Layer "Teleport" only (bit 31)
  - TeleportationAreas: Layer "Teleport" (bit 31)
- **Important:** Grab interactors must NOT include Teleport layer to avoid grabbing floor

## Key Data Classes

```csharp
// Player data (VRRoomManager.cs)
[Serializable] public class VRPlayerData {
    public string playerId, playerName;
    public bool isHost;
    public RoomType roomType;
    // Pose data: posX/Y/Z, rotX/Y/Z/W, head*, leftHand*, rightHand*
}

// Room info (VRRoomManager.cs)
[Serializable] public class RoomInfo {
    public string roomId, hostId, roomName;
    public RoomType roomType;
    public int playerCount, maxPlayers;
}

// Position sync (VRGameManager.cs)
[Serializable] public class VRPositionData {
    public string roomId;
    public RoomType roomType;
    // Body: posX/Y/Z, rotY
    // Head: headPosX/Y/Z, headRotX/Y/Z/W
    // Hands: leftHand*, rightHand*
}

// Whiteboard stroke (WhiteboardNetworkData.cs)
[Serializable] public class WhiteboardPacket {
    public string whiteboardId, roomId;
    public float r, g, b, a;
    public int penSize;
    public float[] pointsFlat;  // [u1,v1, u2,v2, ...] UV coords
}
```

## Code Conventions

- **French comments** in some files (French-speaking developer)
- **Event subscription:** Always in `OnEnable`, unsubscribe in `OnDisable`
- **Serialization:** Use `JsonUtility` - requires `[Serializable]`, **no nested objects**
- **GC optimization:** Cached message objects (`_cachedPositionData`, `_cachedOutgoingMessage`)
- **Logging prefix:** `[SystemName]` (e.g., `[VRNet]`, `[VRRoom]`, `[VRGame]`)
- **Folder typo:** `Assets/Scrips/` (not "Scripts") - preserved for consistency

## Package Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `com.endel.nativewebsocket` | Git UPM | WebSocket communication |
| `com.unity.webrtc` | 3.0.0 | Voice chat |
| `com.unity.xr.interaction.toolkit` | 3.2.2 | VR interactions |
| `com.unity.xr.hands` | 1.7.2 | Hand tracking |
| `com.unity.xr.openxr` | 1.16.1 | OpenXR runtime |
| `com.unity.render-pipelines.universal` | 17.2.0 | URP graphics |
| `com.veriorpies.parrelsync` | Git | Local multiplayer testing |

## Integration Flow

```
Bootstrap Scene (Persistent via DontDestroyOnLoad)
├── VRNetworkManager ─── WebSocket ──→ Server (ws://localhost:8080)
├── VRRoomManager ←──────── OnConnected, OnMessageReceived
├── VRGameManager ←──────── OnRoomCreated/Joined, OnPlayerJoined/Left, OnRoomTypeChanged
├── VoiceChatManager ←───── OnPlayerJoined (mesh topology: smaller ID initiates WebRTC)
├── EventSystem ←────────── Persistent, with XRUIInputModule (configured by BootstrapManager)
└── BootstrapManager ──→ Loads Meet.unity additively

Meet Scene (Additive)
├── Spawn Points (Lobby, RoomA, RoomB)
├── Teleportation Areas/Anchors
├── Whiteboard components
└── UI (VoiceChat, Menu, QuickRoomJoiner)
```

## Screen Sharing & File Sharing (Work In Progress)

### Status: Partially Implemented - Needs Testing

**Files créés dans `Assets/Scrips/Sharing/`:**

| Fichier | Description | Status |
|---------|-------------|--------|
| `ScreenShareManager.cs` | Capture écran + WebRTC VideoStreamTrack | Implémenté |
| `ScreenShareUI.cs` | UI simple pour contrôler le partage | Implémenté |
| `FileShareManager.cs` | Envoi/réception fichiers en chunks Base64 | Implémenté |
| `FileShareData.cs` | Classes sérialisables pour fichiers | Implémenté |
| `FileViewer.cs` | Ouverture fichiers + affichage sur Whiteboard | Implémenté |
| `SharedFileUI.cs` | UI liste des fichiers partagés | Implémenté |
| `VirtualScreen.cs` | Écran virtuel (non utilisé, remplacé par Whiteboard) | Implémenté |
| `SharingSystemSetup.cs` | Menu setup automatique des managers | Implémenté |

### Architecture Screen Share

- **Affichage:** Sur le Whiteboard (mode présentation), pas d'écran virtuel séparé
- **Capture:** `ScreenCapture.CaptureScreenshotIntoRenderTexture()` → RenderTexture BGRA32
- **Streaming:** WebRTC VideoStreamTrack
- **Restriction:** Desktop uniquement (VR ne peut pas partager)

### Whiteboard - Mode Présentation

Le Whiteboard a été étendu avec un mode présentation:

```csharp
// Propriétés
whiteboard.IsPresentationMode      // true si affiche screen share ou image
whiteboard.CurrentPresentationTitle
whiteboard.CurrentPresenterId

// Méthodes
whiteboard.StartPresentationMode(presenterId, title)  // Sauvegarde le dessin
whiteboard.StopPresentationMode()                      // Restaure le dessin
whiteboard.UpdatePresentationTexture(texture, flipY)  // Met à jour l'affichage
whiteboard.DisplayImage(texture, presenterId, fileName)
whiteboard.DisplayScreenShare(texture, presenterId, name)

// Events
Whiteboard.OnPresentationModeChanged(whiteboard, isPresenting)
Whiteboard.OnPresentationTextureUpdated(whiteboard, texture)
```

### Messages Réseau Screen Share

| Type | Direction | Description |
|------|-----------|-------------|
| `screen-share-start` | Sharer → Room | Annonce début partage |
| `screen-share-stop` | Sharer → Room | Annonce fin partage |
| `screen-video-offer` | Receiver → Sharer | Demande flux vidéo |
| `screen-video-answer` | Sharer → Receiver | Réponse WebRTC |
| `screen-video-ice` | Bidirectionnel | ICE candidates |

### Messages Réseau File Share

| Type | Direction | Description |
|------|-----------|-------------|
| `file-announce` | Sender → Room | Annonce nouveau fichier |
| `file-chunk` | Sender → Room | Chunk Base64 (64KB) |
| `file-complete` | Sender → Room | Fichier complet |
| `file-request` | Receiver → Sender | Demande re-envoi |
| `file-list-request` | Late joiner → Room | Demande liste fichiers |
| `file-list-response` | Host → Requester | Liste des fichiers |

### Server (D:\Test_project\LocalServ\Server\server.js)

Handlers ajoutés pour Screen Share et File Share:
- `handleScreenVideoOffer`, `handleScreenVideoAnswer`, `handleScreenVideoIce`
- `handleFileListResponse`
- Broadcast par room pour tous les messages `screen-share-*` et `file-*`

### Setup Required

1. **Menu Unity:** `VR Meeting → Setup Sharing System` crée les managers
2. **UI Screen Share:** Créer Canvas World Space près du Whiteboard avec:
   - Button "Share Screen" → `shareButton`
   - Button "Stop" → `stopButton`
   - TextMeshPro status → `statusText`
   - Ajouter script `ScreenShareUI` et assigner les références

### Known Issues / TODO

- [ ] Tester Screen Share entre Unity Editor et Build
- [ ] Tester File Share complet
- [ ] Le flip Y de la texture peut nécessiter ajustement selon la plateforme
- [ ] Late joiner ne reçoit pas automatiquement le screen share en cours

## Recent Fixes & Changes

### WebRTC Mesh Topology (VoiceChatManager.cs:367-381)
- **Problem:** With 3+ clients, only host was connected to everyone (star topology)
- **Solution:** Deterministic rule - player with lexicographically smaller ID initiates
- **Result:** Full mesh where all clients hear each other

### Spatial Audio Positioning (VoiceChatManager.cs:485-520)
- **Problem:** AudioSource was attached to remote player body, not head
- **Solution:** AudioSource now attached to detached head Transform via `GetRemotePlayerHead()`
- **Result:** Correct 3D audio positioning based on head position

### EventSystem Fix (BootstrapManager.cs)
- **Problem:** Duplicate EventSystems caused random VR UI interaction failures
- **Solution:** Single persistent EventSystem in Bootstrap with XRUIInputModule
- **Result:** Reliable VR controller UI interaction
