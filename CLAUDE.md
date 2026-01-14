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

**Phase 1 - Foundation (Current)**
- [x] WebSocket networking
- [x] WebRTC voice chat (mesh topology)
- [x] Spatial audio (3D positioned on head)
- [x] Basic avatar sync
- [x] Whiteboard
- [ ] Desktop mode (non-VR)
- [ ] MariaDB integration

**Phase 2 - Collaboration**
- [x] Screen sharing (VR + Desktop, optimisé)
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
│   ├── Whiteboard.cs             # Fond blanc + mode présentation (screen share)
│   ├── WhiteboardDrawingSurface.cs # Surface transparente, reçoit dessins réseau
│   ├── WhiteboardMarker.cs       # Dessin VR (stylo)
│   ├── DesktopWhiteboardDrawer.cs # Dessin Desktop (souris)
│   ├── WhiteboardNetworkData.cs  # Classes sérialisables réseau
│   └── WhiteboardUIManager.cs    # UI (couleurs, clear)
├── Sharing/
│   ├── ScreenShareManager.cs     # Capture écran + envoi JPEG Base64 via WebSocket
│   └── ScreenShareData.cs        # Classes sérialisables pour messages réseau
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
| Screen Share | `screen-share-start`, `screen-share-stop`, `screen-share-frame`, `screen-share-request`, `screen-share-state` |

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
