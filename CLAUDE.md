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
| **Presentation Tools** | Screen sharing, slides, laser pointer, file presentation | Implemented |
| **Interactive Whiteboard** | Real-time collaborative drawing, network-synced | Implemented |
| **Laser Pointer** | VR/Desktop laser pointer visible to all, network-synced | Implemented |
| **File Presentation** | Present images/PDFs on whiteboard with navigation | Implemented |
| **3D Object Manipulation** | Grab, move, scale, rotate shared objects | Partial (basic interactable) |
| **Avatar Customization** | Name + color selection, synced across network | Implemented |
| **File Sharing** | Upload/download files with VR browser | Implemented (Testing) |
| **Desktop Mode** | Non-VR FPS-style controls (WASD + mouse) | Implemented |
| **Modular Environments** | Configurable meeting room layouts | Planned |
| **Note-taking & Export** | In-meeting notes with PDF/text export | Planned |
| **Screen Sharing** | Share desktop/window/VR view to Whiteboard | Implemented |

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

**Phase 1 - Foundation (Complete)**
- [x] WebSocket networking (auto-reconnect, exponential backoff)
- [x] WebRTC voice chat (mesh topology + TURN servers)
- [x] Spatial audio (3D positioned on head)
- [x] Avatar sync (30Hz, head + hands)
- [x] Avatar customization (name + color)
- [x] Whiteboard (collaborative drawing)
- [x] Desktop mode (FPS-style: WASD + mouse look)
- [ ] MariaDB integration

**Phase 2 - Collaboration (In Progress)**
- [x] Screen sharing (VR + Desktop, optimized 854x480 @ 3fps)
- [x] Laser pointer (VR: A button, Desktop: L key, network-synced @ 10Hz)
- [x] File presentation (images + PDF on whiteboard, navigation, zoom/pan)
- [~] File sharing (implemented, requires testing)
- [~] 3D object manipulation (basic networked interactable exists)
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

### Server Commands

```bash
cd Server/

# Install dependencies
npm install

# Run server (production)
npm start

# Run server with auto-reload (development)
npm run dev

# Run tests
npm test
```

**Server URL:** `ws://localhost:8080` (configurable via `VRNetworkManager.serverUrl` in Inspector)

#### Connection Configuration (`VRNetworkManager.cs`)

| Parameter | Default | Description |
|-----------|---------|-------------|
| `serverUrl` | `ws://localhost:8080` | WebSocket server URL |
| `enforceSecureConnection` | `false` | Block insecure (ws://) connections in builds |
| `autoReconnect` | `true` | Automatically reconnect on disconnect |
| `welcomeTimeout` | `5s` | Timeout waiting for server welcome message |
| `initialReconnectDelay` | `1s` | First reconnect attempt delay |
| `maxReconnectDelay` | `30s` | Maximum backoff delay |
| `backoffMultiplier` | `2` | Exponential backoff multiplier |
| `maxMessagesPerSecond` | `60` | Rate limit (0 = unlimited) |
| `burstAllowance` | `10` | Messages allowed in quick succession |

#### Security Validation

- **Production:** Use `wss://` (TLS encrypted) - set `enforceSecureConnection = true`
- **Development:** `ws://localhost:8080` allowed without warnings
- **Remote ws://:** Logs warning in editor, blocks in build if `enforceSecureConnection` enabled

#### Reconnection (Exponential Backoff)

Connection failures trigger automatic reconnection with exponential backoff:
```
Attempt 1: 1s → Attempt 2: 2s → Attempt 3: 4s → ... → Max: 30s
```
Resets to initial delay on successful connection.

#### Connection Flow

1. `Start()` → `ValidateConnectionSecurity()` → `ConnectAsync()`
2. Server sends `welcome` message with assigned `LocalId`
3. If no welcome within `welcomeTimeout` → reconnect
4. On disconnect → exponential backoff reconnection (if `autoReconnect` enabled)

### Unity Build

```bash
# Unity Editor: File > Build Settings
# Platforms: Windows, Android (Quest)
# Scenes: Bootstrap (index 0), Meet (index 1)
```

### Local Multiplayer Testing

Use **ParrelSync** to clone the project and run multiple Unity instances simultaneously. Each clone shares the same project files but runs independently.

## Project Structure

```
Assets/Scrips/                    (Note: intentional typo "Scrips" - preserved for consistency)
├── Network/
│   ├── VRNetworkManager.cs       # WebSocket hub, message routing, auto-reconnect
│   ├── VRRoomManager.cs          # Room lifecycle, player roster, zone tracking
│   ├── VRGameManager.cs          # Player spawning, VR pose sync (30Hz), interpolation
│   └── AuthManager.cs            # Registration, login, profile updates
├── VR/
│   ├── BootstrapManager.cs       # Additive scene loading, persistent EventSystem setup
│   ├── VRPlayerController.cs     # Locomotion, snap/smooth turn, gravity
│   ├── DesktopPlayerController.cs # FPS-style controls (WASD + mouse look)
│   ├── TeleportOnGrab.cs         # VR teleportation mechanics
│   └── TeleportOnButtonClick.cs  # Button-triggered teleportation
├── WebRTC/
│   └── VoiceChatManager.cs       # WebRTC peers, spatial audio, push-to-talk, TURN servers
├── WhiteBoard/
│   ├── Whiteboard.cs             # Fond blanc + mode présentation (screen share)
│   ├── WhiteboardDrawingSurface.cs # Surface transparente, reçoit dessins réseau
│   ├── WhiteboardMarker.cs       # Dessin VR (stylo)
│   ├── DesktopWhiteboardDrawer.cs # Dessin Desktop (souris)
│   ├── WhiteboardNetworkData.cs  # Classes sérialisables réseau
│   ├── WhiteboardUIManager.cs    # UI (couleurs, clear)
│   └── WhiteboardUISetup.cs      # Editor tool for UI configuration
├── Interaction/
│   ├── LaserPointer.cs           # Local laser pointer (VR: A button, Desktop: L key)
│   └── LaserPointerData.cs       # Network serialization for laser sync
├── Sharing/
│   ├── ScreenShareManager.cs     # Capture écran + envoi JPEG Base64 via WebSocket
│   ├── ScreenShareData.cs        # Classes sérialisables pour messages réseau
│   ├── FileShareManager.cs       # Upload/download files, validation, network sync
│   ├── FileShareData.cs          # FileMetadata, upload/download serialization
│   ├── FileSharingUI.cs          # File list, download path, preview panel
│   ├── FilePresentationManager.cs # Present files (images/PDF) on whiteboard
│   ├── FilePresentationData.cs   # Network serialization for presentation sync
│   ├── VRFileBrowser.cs          # In-VR file navigation, folder browsing
│   └── WindowCapture.cs          # Desktop window enumeration (Windows native)
├── Avatar/
│   └── AvatarCustomization.cs    # Color selection, username input, PlayerPrefs
├── UI/
│   ├── GlobalKeyboardAutoBind.cs
│   ├── VoiceChatUI.cs
│   ├── VRMenuUi.cs
│   └── FilePresentationUI.cs     # Presentation controls (prev/next/stop, page info)
└── Testing/
    └── VRNetworkedInteractable.cs # Shared object sync, grab ownership

Assets/Scenes/
├── Bootstrap.unity               # Persistent managers (loads first)
├── Meet.unity                    # Main VR environment (loaded additively)
└── Test.unity

Assets/Prefabs/Unity/
├── LocalPlayer.prefab            # Local VR rig (XROrigin + CharacterController)
├── RemoteVRPlayer.prefab         # Remote avatar (head/hands detached for world-space sync)
├── DesktopPlayer.prefab          # Non-VR player (FPS controls)
├── Playername.prefab             # Floating name tag
├── Playeritem.prefab / RoomItem.prefab  # UI list items
├── WhiteboardComplete.prefab     # Full whiteboard setup
└── XR Origin Hands (XR Rig).prefab # VR control rig with gravity
```

## Code Metrics

| Metric | Value |
|--------|-------|
| **Total Scripts** | 51 |
| **Total Lines of Code** | ~17,000 |
| **Commits** | 103+ |
| **Scenes** | 2 (Bootstrap + Meet) |
| **Prefabs** | 8 |
| **Network Message Types** | 30+ |

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
| Laser Pointer | `laser-pointer` (10Hz when active) |
| Screen Share | `screen-share-start`, `screen-share-stop`, `screen-share-frame`, `screen-share-request`, `screen-share-state` |
| File Share | `file-share-upload`, `file-share-download`, `file-share-list`, `file-share-request`, `file-share-delete` |
| File Present | `file-present-start`, `file-present-stop`, `file-present-page`, `file-present-navigate`, `file-present-zoom-pan`, `file-present-request`, `file-present-state` |
| PDF Convert | `pdf-convert-request`, `pdf-convert-response`, `pdf-page-request`, `pdf-page-response` |
| Auth | `auth-register`, `auth-login`, `auth-profile-update`, `auth-response` |

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

- **STUN/TURN servers:** Google public STUN + TURN servers for NAT traversal
  - STUN: `stun:stun.l.google.com:19302`, CloudFlare
  - TURN: Configured for corporate firewall traversal (use private TURN in production)
- **Mesh topology:** All clients connected to each other (not just to host)
  - Deterministic rule: player with smaller ID (lexicographically) initiates connection
  - Ensures no duplicate connections and full mesh with 3+ clients
- **Spatial audio:**
  - AudioSource attached to remote player's **head** (via `GetRemotePlayerHead()`)
  - 3D spatialBlend = 1.0, maxDistance = 20m, Linear rolloff
  - AudioListener on local player's Main Camera
- **Push-to-talk:** V key (desktop), VR button (configurable)
- **Auto-start:** Microphone starts automatically on initialization
- **Connection timeout:** 15s default, with proper cleanup on failure

### Whiteboard System (`Assets/Scrips/WhiteBoard/`)

**Architecture à 3 couches:**
1. **Whiteboard.cs** - Fond blanc + mode présentation (screen share)
2. **WhiteboardDrawingSurface.cs** - Surface transparente devant le fond, reçoit les dessins du réseau
3. **Systèmes de dessin** - WhiteboardMarker (VR) et DesktopWhiteboardDrawer (Desktop)

**Systèmes de dessin (IMPORTANT - ne pas dupliquer):**
- **WhiteboardMarker** → VR uniquement (stylo à tenir)
- **DesktopWhiteboardDrawer** → Desktop uniquement (clic souris/molette)
- **WhiteboardDrawingSurface** → NE dessine PAS, reçoit seulement les données réseau
- **Couleur par défaut:** Bleu (`Color.blue`)
- **ATTENTION:** Les couleurs doivent être synchronisées entre les systèmes dans la scène Meet.unity pour éviter les mélanges de couleurs

**Configuration texture:**
- **Taille:** 2048x2048 (configurable)
- **Shader:** `Sprites/Default` (100% transparent où pas de dessin)
- **Network format:** `WhiteboardPacket` avec `pointsFlat` array (u,v pairs)
- **Room-scoped:** Tous les messages incluent `roomId` pour filtrage

**Network sync (WhiteboardMarker.cs):**
- **Send rate:** 33ms (~30fps) pour fluidité
- **Batch continuity:** Dernier point inclus au début de chaque batch
- **Interpolation:** 25% de la texture max entre points (permet dessins rapides)

**Réception réseau (WhiteboardDrawingSurface.cs):**
- Mémorise le dernier point reçu entre batches (`_lastReceivedPoint`)
- Interpole automatiquement pour éviter les coupures
- Reset continuité si nouveau sender ou points trop éloignés

**Late joiner sync:**
- Demande état via `whiteboard-request` à `OnRoomJoined` ou `Start()`
- Réponse avec texture PNG encodée en base64

**Room change behavior:**
- Clear texture à `OnRoomLeft`
- Demande état à `OnRoomJoined`
- **History buffer:** 100 packets max

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
├── VRNetworkManager ─── WebSocket ──→ Server (configurable, default: ws://localhost:8080)
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

## Screen Sharing (`Assets/Scrips/Sharing/`)

### Architecture
- **Méthode:** Capture écran → JPEG → Base64 → WebSocket → Broadcast room
- **Affichage:** Sur le Whiteboard (mode présentation)
- **Support:** Desktop (fenêtre/écran) ET VR (vue casque)
- **Performance:** ~3 FPS, ~30-50KB par frame (optimisé)

### ScreenShareManager.cs
Singleton avec DontDestroyOnLoad.

**Settings (optimisés pour performance):**
- `captureWidth = 854`, `captureHeight = 480`
- `jpegQuality = 50` (0-100)
- `captureFrameRate = 3f`

**API publique:**
- `CanShare()` → true (VR et Desktop supportés)
- `IsVRMode()` → true si en mode VR (partage vue casque)
- `StartSharing()` → démarre capture + broadcast
- `StopSharing()` → arrête capture + notifie room
- `IsSharing`, `IsReceiving`, `CurrentSharerName`

**Events:**
- `OnScreenShareStarted(sharerId, sharerName)`
- `OnScreenShareStopped(sharerId)`

### Whiteboard - Mode Présentation
```csharp
whiteboard.IsPresentationMode          // true si screen share actif
whiteboard.PresenterName               // nom du présentateur
whiteboard.EnterPresentationMode(name) // sauvegarde dessin, active mode
whiteboard.ExitPresentationMode()      // restaure dessin
whiteboard.UpdatePresentationTexture(texture) // affiche frame
```

### Flux réseau
1. Sharer envoie `screen-share-start` → room notifiée
2. Sharer envoie `screen-share-frame` (JPEG Base64) à 5 FPS
3. Receivers décodent et affichent sur whiteboard
4. Late joiner envoie `screen-share-request`, sharer répond avec `screen-share-state`
5. Sharer envoie `screen-share-stop` → room sort du mode présentation

### Setup UI (à faire par l'utilisateur)
Créer un Canvas World Space près du whiteboard avec:
- Button "Start Share" → `ScreenShareManager.Instance.StartSharing()`
- Button "Stop" → `ScreenShareManager.Instance.StopSharing()`
- Connecter aux events pour update UI

## File Sharing (`Assets/Scrips/Sharing/`)

### Architecture
- **FileShareManager.cs** - Singleton handling upload/download, validation, network sync
- **FileShareData.cs** - Serializable classes for metadata and transfer
- **FileSharingUI.cs** - Desktop UI with file list, download path selector
- **VRFileBrowser.cs** - In-VR file navigation and folder browsing

### Constraints
- **Max file size:** 10 MB
- **Allowed extensions:** pdf, doc, docx, xls, xlsx, png, jpg, jpeg, gif
- **Storage:** Files stored per-room with late-joiner sync

### API publique
```csharp
FileShareManager.Instance.UploadFile(filePath)    // Upload file to room
FileShareManager.Instance.DownloadFile(fileId)   // Download shared file
FileShareManager.Instance.GetFileList()          // Get room's file list
FileShareManager.Instance.DeleteFile(fileId)     // Remove shared file (host only)
```

### Network Flow
1. Uploader sends `file-share-upload` with Base64 content + metadata
2. Server broadcasts to room, receivers store locally
3. Late joiner sends `file-share-request`, receives `file-share-list`
4. Download via `file-share-download` request

### VR File Browser
- In-headset folder navigation
- Filter by allowed extensions
- Select file for upload directly in VR
- Recent commit fixes: `aa2d325` (fix vr file sharing)

## Laser Pointer (`Assets/Scrips/Interaction/`)

### Architecture
- **LaserPointer.cs** - Component on local player, creates and syncs laser
- **LaserPointerData.cs** - Serializable data for network transmission
- **VRGameManager.cs** - Receives `laser-pointer` messages, creates remote laser visuals

### Controls
| Mode | Toggle | Origin |
|------|--------|--------|
| VR | A button (right controller) | Right hand controller |
| Desktop | L key | Camera center |

### Visual Components
- **LineRenderer** - Red beam from origin to hit point
- **Sphere (dot)** - Small sphere at hit point, oriented to surface normal
- **Color:** Red by default, synced over network

### Network Sync
- **Sync rate:** 10 updates/second when active
- **Data synced:** `isActive`, origin position, hit point position, color
- **Room-scoped:** Only visible to players in same room
- **Deactivation:** Sends `isActive=false` on toggle off, disable, or destroy

### LaserPointerData
```csharp
[Serializable]
public class LaserPointerData {
    public string roomId;
    public bool isActive;
    public float originX, originY, originZ;
    public float hitX, hitY, hitZ;
    public float colorR, colorG, colorB;
}
```

### Remote Laser Display (VRGameManager.cs)
- Creates `LineRenderer` and dot sphere for each remote player with active laser
- Updates positions in real-time from network messages
- Cleanup on player disconnect or laser deactivation
- Stored in `VRRemotePlayer`: `laserLine`, `laserDot`, `laserActive`

## File Presentation (`Assets/Scrips/Sharing/`)

### Architecture
- **FilePresentationManager.cs** - Singleton, handles presentation logic and network sync
- **FilePresentationData.cs** - Serializable classes for all presentation messages
- **FilePresentationUI.cs** - UI controls (prev/next/stop buttons, page counter)

### Supported File Types
| Type | Handling |
|------|----------|
| PNG, JPG, JPEG, GIF | Direct display on whiteboard |
| PDF | Server-side conversion to images (requires server support) |

### Features
- **Multi-page navigation:** Previous/Next page buttons
- **Zoom:** 0.5x to 4x (step 0.25x)
- **Pan:** Move view when zoomed in
- **Late joiner sync:** Automatic state request and display
- **Room-scoped:** Presentation visible only in current room

### API publique
```csharp
FilePresentationManager.Instance.CanPresentFile(fileId)     // Check if file can be presented
FilePresentationManager.Instance.StartPresentation(fileId, whiteboard)
FilePresentationManager.Instance.StopPresentation()
FilePresentationManager.Instance.NextPage()
FilePresentationManager.Instance.PreviousPage()
FilePresentationManager.Instance.NavigateToPage(pageNumber)
FilePresentationManager.Instance.ZoomIn() / ZoomOut()
FilePresentationManager.Instance.SetZoom(level)
FilePresentationManager.Instance.Pan(delta)
FilePresentationManager.Instance.ResetZoomPan()
```

### Events
```csharp
FilePresentationManager.OnPresentationStarted(wbId, fileId, presenterId, presenterName)
FilePresentationManager.OnPresentationStopped(wbId, presenterId)
FilePresentationManager.OnPageChanged(fileId, currentPage, totalPages)
FilePresentationManager.OnPresentationError(context, error)
FilePresentationManager.OnZoomPanChanged(zoomLevel, panOffset)
```

### Network Flow
1. Presenter calls `StartPresentation(fileId, whiteboard)`
2. Whiteboard enters presentation mode via `EnterPresentationMode()`
3. `file-present-start` broadcast to room with metadata
4. Image/PDF page sent via `file-present-page` (JPEG Base64)
5. Navigation via `file-present-navigate`, zoom/pan via `file-present-zoom-pan`
6. Late joiners request state via `file-present-request`, receive `file-present-state`
7. Presenter calls `StopPresentation()` → `file-present-stop` broadcast

### PDF Conversion (Server Required)
For PDF files, the client sends `pdf-convert-request` with Base64 PDF content.
Server must respond with `pdf-convert-response` containing total pages.
Individual pages requested via `pdf-page-request`, returned as JPEG images.

### Whiteboard Integration
```csharp
whiteboard.EnterPresentationMode(presenterName)  // Save drawing, show presenter name
whiteboard.UpdatePresentationTexture(texture)    // Display presentation frame
whiteboard.SetPresentationZoomPan(zoom, pan)     // Apply zoom/pan from network
whiteboard.ExitPresentationMode()                // Restore drawing
```

## Avatar Customization (`Assets/Scrips/Avatar/`)

### AvatarCustomization.cs
- **Color options:** 8 colors (blue, red, green, yellow, purple, orange, cyan, pink)
- **Username:** Text input with validation
- **Persistence:** PlayerPrefs (`PlayerColor`, `PlayerName`)
- **Network sync:** Color and name synced to all players in room
- **Name tag:** Floating TextMeshPro above remote player heads

### API
```csharp
AvatarCustomization.Instance.SetColor(colorIndex) // 0-7
AvatarCustomization.Instance.SetPlayerName(name)
AvatarCustomization.GetLocalColor()
AvatarCustomization.GetLocalName()
```

## Desktop Mode (`Assets/Scrips/VR/DesktopPlayerController.cs`)

### Controls
- **Movement:** WASD keys
- **Look:** Right-click + mouse drag
- **Sprint:** Hold Shift (2x speed multiplier)
- **Whiteboard:** Left-click to draw (via DesktopWhiteboardDrawer)
- **Laser Pointer:** L key to toggle (via LaserPointer.cs)

### Implementation
- Uses Unity Input System
- Head Transform auto-detected from child "Head" or Main Camera
- Smooth movement with configurable speed
- FPS-style camera with mouse sensitivity settings

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

### Whiteboard Color Mix Fix (WhiteboardDrawingSurface.cs)
- **Problem:** Plusieurs systèmes de dessin actifs avec des couleurs différentes (rouge, vert, bleu) causaient un mélange de couleurs indésirable
- **Solution:**
  - Désactivé le dessin direct dans `WhiteboardDrawingSurface` (ne reçoit que les données réseau)
  - Conservé uniquement `WhiteboardMarker` (VR) et `DesktopWhiteboardDrawer` (Desktop)
  - Synchronisé toutes les couleurs en bleu par défaut dans la scène et le code
- **Result:** Un seul système de dessin actif selon le mode (VR/Desktop), couleur bleue uniforme

### VR Physics Fix (XR Origin Hands prefab)
- **Problem:** Le joueur VR flottait et traversait les murs (pas de gravité ni collisions)
- **Solution:** Changé `m_UseGravity` de `0` à `1` dans le prefab YAML (`XR Origin Hands (XR Rig).prefab:2508`)
- **Result:** Gravité et collisions fonctionnelles via XR Interaction Toolkit (GravityProvider + CharacterController)

### VR Screen Share Support (ScreenShareManager.cs)
- **Problem:** Screen share limité au mode Desktop uniquement
- **Solution:**
  - `CanShare()` retourne maintenant `true` pour VR et Desktop
  - Ajout de `IsVRMode()` pour détecter le mode
  - En VR, `_selectedWindow = null` → capture la vue du casque
- **Result:** Partage d'écran fonctionnel en VR (partage vue casque) et Desktop (fenêtre/écran)

### Screen Share Performance Optimization (ScreenShareManager.cs)
- **Problem:** Le jeu ralentissait pendant le partage d'écran
- **Solution:** Paramètres optimisés:
  - Résolution: 1280×720 → 854×480 (~55% moins de pixels)
  - Qualité JPEG: 70 → 50
  - Frame rate: 5fps → 3fps
- **Result:** ~3× moins de données/seconde, performance fluide

### Whiteboard Fast Drawing Sync (WhiteboardMarker.cs + WhiteboardDrawingSurface.cs)
- **Problem:** Dessin rapide apparaissait coupé chez l'adversaire
- **Solution:**
  - Send rate: 50ms → 33ms (~30fps)
  - Ajout continuité entre batches (`_lastSentPoint` côté émetteur)
  - Mémoire du dernier point reçu (`_lastReceivedPoint` côté récepteur)
  - Seuil d'interpolation: 5% → 25% de la texture
- **Result:** Dessins rapides fluides et continus pour tous les joueurs

### Drawing Surface Transparency Fix (WhiteboardDrawingSurface.cs)
- **Problem:** La surface de dessin ajoutait un effet de filtre sur le screen share
- **Solution:** Changé le shader de URP Lit vers `Sprites/Default`
- **Result:** Surface 100% transparente où il n'y a pas de dessin, screen share visible sans filtre

### P0 Critical Stability Fixes (Recent)

#### TURN Servers for NAT Traversal (VoiceChatManager.cs)
- **Problem:** Voice chat failed behind corporate firewalls/NAT
- **Solution:** Added TURN servers alongside STUN (use private TURN in production)
- **Result:** WebRTC works through NAT/firewalls

#### Async Void Exception Handling (VRNetworkManager.cs)
- **Problem:** `async void Start()` exceptions were swallowed silently
- **Solution:** Wrapper `ConnectAsync()` with try-catch, proper error propagation
- **Result:** Connection errors properly logged and handled

#### Exponential Backoff Reconnection (VRNetworkManager.cs)
- **Problem:** Aggressive reconnection could flood server
- **Solution:** Exponential backoff (1s → 2s → 4s → ... → 30s max)
- **Result:** Graceful reconnection without server overload

#### Race Condition in Player Spawning (VRGameManager.cs)
- **Problem:** Rapid join/leave could spawn duplicate players
- **Solution:** `_isSpawning` flag with lock to prevent concurrent spawns
- **Result:** Clean single spawn per player

#### JSON Validation Helper (VRRoomManager.cs)
- **Problem:** Malformed JSON from server could crash client
- **Solution:** `TryDeserialize<T>()` helper with null checks
- **Result:** Graceful handling of invalid messages

#### Rate Limiting (VRNetworkManager.cs)
- **Problem:** Uncontrolled message sending could flood server/network
- **Solution:** Token bucket rate limiter with configurable `maxMessagesPerSecond` (60) and `burstAllowance` (10)
- **Result:** Controlled message flow, prevents server overload

#### Connection Security Validation (VRNetworkManager.cs)
- **Problem:** Insecure ws:// connections in production expose data
- **Solution:** `ValidateConnectionSecurity()` validates URL, `enforceSecureConnection` option blocks ws:// in builds
- **Result:** Security warnings in editor, mandatory wss:// enforcement in production builds

### P1 Performance Fixes (Recent)

#### Whiteboard Texture Batching
- **Problem:** Individual `SetPixels()` calls caused 30ms frame spikes
- **Solution:** Batch all pixels, single `Apply()` per frame
- **Result:** ~1ms per frame, smooth drawing

#### Memory Leak in FindObjects
- **Problem:** Repeated `FindObjectsOfType()` in Update() causing GC spikes
- **Solution:** Cache XRInteractionManager, teleport areas, canvases at Start()
- **Result:** Zero GC allocations in hot path

#### Clear Texture Memory Allocation
- **Problem:** 16MB allocation on every whiteboard clear
- **Solution:** `_cachedClearPixels` array reused across clears
- **Result:** No GC during whiteboard operations

### File Sharing Feature (Recent - Commits f6d7757, 706c066, aa2d325)
- **Feature:** Upload/download files within VR meeting rooms
- **VR Browser:** In-headset file navigation and selection
- **Constraints:** 10MB max, allowed document/image extensions
- **Status:** Implemented, requires thorough testing
