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
├── WebRTC/           VoiceChatManager.cs
├── WhiteBoard/       Whiteboard.cs, WhiteboardDrawingSurface.cs, WhiteboardMarker.cs, DesktopWhiteboardDrawer.cs
├── Interaction/      LaserPointer.cs, LaserPointerData.cs
├── Sharing/          ScreenShareManager.cs, FileShareManager.cs, FilePresentationManager.cs, VRFileBrowser.cs
├── Avatar/           AvatarCustomization.cs
├── UI/MainMenu/      MainMenuManager.cs, MainMenuSettings.cs, MainMenuOptionsUI.cs
└── Debug/            DebugManager.cs

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
OnRoomCreated, OnRoomJoined, OnRoomLeft, OnPlayerJoined(VRPlayerData), OnPlayerLeft, OnRoomTypeChanged

// VRGameManager
OnLocalPlayerSpawned, OnRemotePlayerSpawned, OnRemotePlayerDespawned
GetLocalPlayer(), GetRemotePlayer(id), GetRemotePlayerHead(id)

// VoiceChatManager
OnVoiceChatReady, OnPeerVoiceConnected, OnPeerVoiceDisconnected
```

### Network Protocol
```csharp
[Serializable] public class NetworkMessage { string type, senderId, data; } // JsonUtility, no nested objects!
```

| Category | Message Types |
|----------|---------------|
| Connection | `welcome`, `peer-connected`, `peer-disconnected` |
| Rooms | `room-join`, `room-welcome`, `room-leave`, `room-list`, `room-teleport`, `player-name-update` |
| VR Sync | `vr-position` (30Hz) |
| Voice | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` |
| Whiteboard | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request`, `whiteboard-state` |
| Sharing | `screen-share-*`, `file-share-*`, `file-present-*`, `laser-pointer` |

### Key Systems

**Room System:** RoomType enum (`Lobby`, `MeetingRoomA`, `MeetingRoomB`), 6-char codes, host authority

**VR Sync (30Hz):** Movement threshold 0.01m/1°, interpolation 15, head+hands detached for world-space

**Voice Chat:** Mesh topology (smaller ID initiates), STUN+TURN, spatial audio on head, push-to-talk V key

**Whiteboard (3 layers):**
1. `Whiteboard.cs` - fond blanc + mode présentation
2. `WhiteboardDrawingSurface.cs` - transparent, network only (ne dessine pas!)
3. `WhiteboardMarker` (VR) / `DesktopWhiteboardDrawer` (Desktop) - local drawing

Config: 2048x2048, Sprites/Default shader, 33ms send rate, blue default color

**Screen Share:** 854x480 @ 3fps, JPEG 50%, VR+Desktop, displays on whiteboard presentation mode

**File Share:** 10MB max, extensions: pdf/doc/docx/xls/xlsx/png/jpg/jpeg/gif

**Laser Pointer:** VR=A button, Desktop=L key, 10Hz sync, red LineRenderer+dot

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

## Database Integration (Phase 3 - Not Implemented)

**Architecture:** Unity ←WebSocket→ Node.js ←mariadb→ MariaDB (never direct connection!)

**Tables to create:** `users`, `rooms`, `shared_files`, `meeting_logs`

**Auth messages:** `auth-login`, `auth-register`, `auth-response`, `auth-logout`

**Files to create:**
1. `Server/src/database.js` - MariaDB pool
2. `Server/src/auth.js` - bcrypt auth
3. `Assets/Scrips/Auth/AuthManager.cs` - Unity singleton

**Security:** bcrypt 12 rounds, TLS, env vars, rate limiting 5/min, JWT 24h

## Package Dependencies
| Package | Purpose |
|---------|---------|
| `com.endel.nativewebsocket` | WebSocket |
| `com.unity.webrtc` 3.0.0 | Voice |
| `com.unity.xr.interaction.toolkit` 3.2.2 | VR |
| `com.unity.xr.openxr` 1.16.1 | OpenXR |
| `com.unity.render-pipelines.universal` 17.2.0 | URP |

## Feature Status
| Done | In Progress | Planned |
|------|-------------|---------|
| WebSocket, WebRTC, Spatial Audio, Avatar sync/customization, Whiteboard, Desktop mode, Main menu, Screen share, Laser pointer, File sharing/presentation | 3D object manipulation | Database, Auth, SSO, E2E encryption, GDPR, Admin, Recordings, Advanced avatars, Calendar |

## Important Notes
- **XR Layers:** Teleport on bit 31 only, Grab must NOT include Teleport layer
- **Remote players:** Head/hands detached from hierarchy (world-space targets)
- **Late joiners:** Request state via `*-request` messages, receive `*-state`
- **Room-scoped:** All sync messages include `roomId`
