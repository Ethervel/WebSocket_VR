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
| **Spatial Audio** | 3D positional audio for natural conversations | In Progress |
| **Presentation Tools** | Screen sharing, slides, media playback | Planned |
| **Interactive Whiteboard** | Real-time collaborative drawing, network-synced | Implemented |
| **3D Object Manipulation** | Grab, move, scale, rotate shared objects | Planned |
| **Expressive Professional Avatars** | Business-appropriate customizable avatars | Not Started |
| **Modular Environments** | Configurable meeting room layouts | Planned |
| **Note-taking & Export** | In-meeting notes with PDF/text export | Planned |
| **Screen Sharing** | Share desktop/window to virtual display | Planned |

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
- [x] WebRTC voice chat
- [x] Basic avatar sync
- [x] Whiteboard
- [ ] Desktop mode (non-VR)
- [ ] MariaDB integration

**Phase 2 - Collaboration**
- [ ] Screen sharing
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
│   ├── Whiteboard.cs             # Network-synced whiteboard (2048x2048 texture)
│   ├── WhiteboardMarker.cs       # Drawing input handling
│   ├── WhiteboardNetworkData.cs  # Serializable network classes
│   └── WhiteboardUIManager.cs    # Whiteboard UI controls
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

### Voice Chat (`VoiceChatManager.cs`)

- **STUN servers:** Google public (`stun:stun.l.google.com:19302`)
- **Host initiates:** Prevents duplicate WebRTC connections
- **Spatial audio:** 3D blend with 20m max distance (configurable)
- **Push-to-talk:** V key (desktop), VR button (configurable)

### Whiteboard (`WhiteBoard/Whiteboard.cs`)

- **Texture:** 2048x2048 (configurable)
- **Network format:** `WhiteboardPacket` with `pointsFlat` array (u,v pairs)
- **State sync:** PNG base64 encoding for late joiners
- **History buffer:** 100 packets max

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
├── VoiceChatManager ←───── OnPlayerJoined/Left (host initiates WebRTC)
├── EventSystem ←────────── Persistent, with XRUIInputModule (configured by BootstrapManager)
└── BootstrapManager ──→ Loads Meet.unity additively

Meet Scene (Additive)
├── Spawn Points (Lobby, RoomA, RoomB)
├── Teleportation Areas/Anchors
├── Whiteboard components
└── UI (VoiceChat, Menu, QuickRoomJoiner)
```
