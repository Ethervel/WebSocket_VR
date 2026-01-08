# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Unity 6+ VR multiplayer meeting room application using WebSockets (NativeWebSocket) for real-time networking and WebRTC for voice chat. Targets OpenXR-compatible headsets (Quest, PCVR).

## Technology Stack
- **Game Engine:** Unity 6+ VR with URP
- **Multiplayer:** WebSocket (NativeWebSocket) for real-time messaging, WebRTC for voice chat
- **Database:** MariaDB
- **Target Platforms:** OpenXR-compatible headsets (Quest, PCVR)

## Product Requirements

### Core Features
- **Audio Spécialisé** - High-quality spatial voice chat
- **Outils de présentation et de Collaboration** - Presentation and collaboration tools
- **Partage d'écran** - Screen sharing capability
- **Tableau blanc interactif** - Interactive whiteboard (implemented)
- **Manipulation d'objet 3D** - 3D object manipulation
- **Avatar expressif mais Professionnels** - Expressive yet professional avatars
- **Environnement modulable** - Modular/customizable environments
- **Prise de note et Exportation** - Note-taking with export functionality

### Must Have (Critical Requirements)
- **Accessibilité Hybride (Cross-Platform)** - Support for multiple platforms
- **Onboarding "Zero Friction"** - Intuitive interface; users who have never used a VR headset must be able to use it easily
- **Sécurité et Confidentialité** - Security and privacy as core principles
- **Chiffrement des données** - Data encryption
- **Conformité RGPD** - GDPR compliance
- **Gestion des accès via SSO** - Single Sign-On access management
- **Stabilité Audio et Latence Faible** - Audio stability with low latency

## Build & Development

**Open in Unity:** Unity 6+ with URP (Universal Render Pipeline 17.2.0)

**Key Package Dependencies:**
- `com.endel.nativewebsocket` - WebSocket communication
- `com.unity.webrtc` (3.0.0) - Voice chat
- `com.unity.xr.interaction.toolkit` (3.2.2) - VR interactions
- `com.unity.xr.hands` (1.7.2) - Hand tracking
- `com.unity.xr.openxr` (1.16.1) - OpenXR runtime
- `com.veriorpies.parrelsync` - Multi-instance testing (clone project for local multiplayer testing)

**Server Requirement:** Requires a Node.js WebSocket server running (default: `ws://localhost:8080`). Server URL is configured in `VRNetworkManager.serverUrl`.

## Architecture

### Scene Flow
- **Bootstrap** (persistent) - Contains all managers, loads other scenes additively
- **Meet** - Main VR meeting environment with lobby and meeting rooms

### Core Singletons (DontDestroyOnLoad)
Located in `Assets/Scrips/`:

| Manager | Purpose |
|---------|---------|
| `VRNetworkManager` | WebSocket connection, message routing, auto-reconnect |
| `VRRoomManager` | Room lifecycle (create/join/leave), player roster, room discovery |
| `VRGameManager` | Player spawning, VR pose sync (30Hz), remote player interpolation |
| `VoiceChatManager` | WebRTC peer connections, spatial audio, push-to-talk |
| `BootstrapManager` | Additive scene loading, EventSystem cleanup |

### Network Message Protocol
Messages use `NetworkMessage` format:
```csharp
public class NetworkMessage {
    public string type;      // e.g., "vr-position", "room-join", "webrtc-offer"
    public string senderId;
    public string data;      // JSON-serialized payload
}
```

Key message types:
- **Connection:** `welcome`, `peer-connected`, `peer-disconnected`
- **Rooms:** `room-available`, `room-join`, `room-welcome`, `room-leave`, `room-list`
- **VR Sync:** `vr-position` (body, head, hands at 30Hz with movement threshold)
- **Voice:** `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate`
- **Whiteboard:** `whiteboard-batch`, `whiteboard-clear`, `whiteboard-state`

### Room System
- `RoomType` enum: `Lobby`, `MeetingRoomA`, `MeetingRoomB`
- Room codes are 6-character alphanumeric (unambiguous charset)
- Host manages player roster and broadcasts room state via `room-welcome`
- Zone changes within room use `TeleportToRoomType()` (no reconnect needed)

### VR Player Sync
- Local player: XR Origin with CharacterController, continuous locomotion + snap/smooth turn
- Remote players: Interpolated at 15 speed, head/hands detached from hierarchy for world-space sync
- Movement threshold optimization: only syncs when position/rotation delta exceeds threshold

### Whiteboard System
- Network-synced drawing with batched packets (`WhiteboardPacket` with UV coordinates)
- State transfer via PNG base64 encoding for late joiners
- History maintained for sync (100 packet max)

## Code Conventions

- French comments in some files (project has French-speaking developer)
- Managers subscribe to events in `OnEnable`, unsubscribe in `OnDisable`
- Event-driven communication between managers (e.g., `VRRoomManager.OnPlayerJoined`)
- JsonUtility for serialization (requires `[Serializable]` classes, no nested objects)

## Testing Multiplayer Locally

Use ParrelSync to clone the project and run multiple Unity instances for local multiplayer testing.
