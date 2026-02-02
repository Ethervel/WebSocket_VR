# Server Architecture - VR Meeting Rooms

Technical document describing the current state of the WebSocket server for the VR Meeting Rooms application.

> **Last updated: 2026-02-02** - Synchronized with the current source code.

---

## Overview

### Current Tech Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Runtime | Node.js | >= 16 (22 LTS recommended) |
| WebSocket | ws | 8.14.2 |
| UUID | uuid | 9.0.1 |
| PDF (optional) | pdf-poppler | 0.2.3 |
| Tests | Jest | 30.2.0 (dev) |

---

## Server Architecture

### File Structure

```
Server/
├── server.js           # Main server (887 lines)
├── filePresentation.js # PDF conversion (257 lines, optional)
├── package.json        # npm dependencies
└── node_modules/       # Installed dependencies
```

### Active Features (server.js)

| Feature | Implementation | Status |
|---------|----------------|--------|
| WebSocket connection | `ws` library, port 8080 | Active |
| Room management | Create, join, leave, close, update, kick | Active |
| VR position sync | Broadcast 30Hz per room | Active |
| Interactive object sync | `obj-sync`, `obj-state` per room | Active |
| Whiteboard | Batch drawing, clear, state sync | Active |
| Screen sharing | WebSocket frames + WebRTC signaling | Active |
| File sharing | Chunks, announce, complete, list | Active |
| File presentation | Start, navigate, stop, state sync | Active |
| PDF conversion | Via filePresentation.js + cache | Optional |
| Voice chat | WebRTC offer/answer/ICE relay | Active |
| Kick player | Host only, authority verification | Active |
| Heartbeat | Ping 30s, timeout 60s | Active |
| Periodic status | Log every 60s | Active |
| PDF cache cleanup | Every 5 minutes, TTL 30 min | Active |
| Graceful shutdown | SIGINT handler | Active |

### Current Configuration

| Variable | Default Value | Source |
|----------|---------------|--------|
| `PORT` | 8080 | `process.env.PORT` |
| `HEARTBEAT_INTERVAL` | 30000 ms | Hardcoded |
| `PDF_CACHE_TTL` | 30 min | Hardcoded |
| Max players/room | 10 | `handleRoomAvailable` |

### Console Output on Startup

```
============================================
  VR MEETING ROOMS - WebSocket Server
============================================
  Port: 8080
  Heartbeat: 30s
============================================
[Server] filePresentation module loaded    (or "not available")
```

### Periodic Logs

Every 60 seconds:
```
[Status] 3 clients | 2 rooms
```

### Maintenance Intervals

| Interval | Frequency | Action |
|----------|-----------|--------|
| Heartbeat | 30s | Ping clients, timeout 60s |
| PDF cache | 5 min | Cleanup TTL 30 min |
| Status log | 60s | Display clients/rooms |

---

## Communication Protocol

### Message Format

```json
{
    "type": "message-type",
    "senderId": "uuid-client",
    "data": "{\"json\":\"serialized\"}"
}
```

### Handled Message Types (46 explicit types)

| Category | Types | Routing |
|----------|-------|---------|
| Connection | `welcome`, `peer-connected`, `peer-disconnected` | Global (server-generated) |
| Room Lifecycle | `room-available`, `room-closed`, `room-join`, `room-leave`, `room-update`, `room-list-request` | Dedicated functions |
| Room State | `room-welcome`, `room-teleport`, `player-name-update` | broadcastToRoom |
| VR Position | `vr-position`, `position` | broadcastToRoom |
| Interactive Objects | `obj-sync`, `obj-state` | broadcastToRoom |
| Whiteboard | `whiteboard-draw`, `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request` | broadcastToRoom |
| Whiteboard State | `whiteboard-state` | Point-to-point (targetId) |
| Voice WebRTC | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | Point-to-point |
| Screen Share | `screen-share-start`, `screen-share-stop`, `screen-share-frame`, `screen-share-request`, `screen-share-state` | broadcastToRoom |
| Screen WebRTC | `screen-video-offer`, `screen-video-answer`, `screen-video-ice` | Point-to-point |
| File Share | `file-announce`, `file-chunk`, `file-complete`, `file-request`, `file-list-request`, `file-list-response` | broadcastToRoom / Point-to-point |
| File Presentation | `file-present-start`, `file-present-page`, `file-present-navigate`, `file-present-stop`, `file-present-request`, `file-present-state` | broadcastToRoom / Point-to-point |
| PDF | `pdf-convert-request`, `pdf-page-request` | Dedicated functions (direct response) |
| Admin | `kick-player` | Point-to-point (host only) |
| Default | Any other type | broadcastToRoom if in room, otherwise global broadcast |

### Server-Generated Messages

| Type | Trigger | Recipient |
|------|---------|-----------|
| `welcome` | Client connection | Connected client |
| `peer-connected` | Client connection | All except sender |
| `peer-disconnected` | Client disconnection | All except sender |
| `room-list` | Room changes | Specific client or all |
| `room-available` | Room creation | All |
| `room-closed` | Room closure | All |
| `error` | Invalid request | Concerned client |
| `pdf-convert-response` | Conversion complete | Requesting client |
| `pdf-page-response` | PDF page ready | Requesting client |

---

## Current Security

### Strengths

| Aspect | Implementation |
|--------|----------------|
| Room isolation | Messages filtered by `roomId` via `broadcastToRoom` |
| ID spoofing | `message.senderId` forced server-side (overwrites client value) |
| Kick authority | Verification `room.hostId === clientId` |
| Client timeout | Automatic disconnection after 60s without pong |
| Room capacity | Rejection if `room.playerCount >= room.maxPlayers` |
| JSON validation | Try/catch on all message handlers |
| WebSocket state | `readyState === OPEN` check before every send |
| Client rate limiting | Token bucket 60 msg/s in VRNetworkManager.cs (Unity-side) |

### Points to Consider

| Aspect | Current State | Risk |
|--------|---------------|------|
| Encryption | None (ws://) | Medium - data in cleartext |
| Authentication | None | High - anonymous access |
| Server rate limiting | None server-side | Medium - spam possible |
| Data validation | Basic (JSON parse) | Low |
| Message size limit | None | Medium - large payloads possible |
| Origin validation | None (CORS) | Medium - any origin accepted |
| Log persistence | Console only | Medium - lost on restart |
| Input sanitization | None (player names, etc.) | Low |

---

## Production Requirements

### Minimum Required

| Component | Specification |
|-----------|---------------|
| Node.js | >= 16 |
| RAM | 512 MB minimum |
| CPU | 1 vCPU |
| Network | Port 8080 accessible |

### Recommended for Production

| Component | Specification | Reason |
|-----------|---------------|--------|
| Reverse proxy | Nginx | SSL termination, WebSocket upgrade |
| SSL/TLS | Valid certificate | `wss://` encryption |
| Process manager | systemd or PM2 | Automatic restart |
| Monitoring | journalctl / logs | Debug and audit |
| TURN server | Private (Twilio/Xirsys) | Enterprise voice chat |

---

## WebRTC and TURN Server

### Why a TURN Server?

Voice chat and screen sharing use WebRTC for peer-to-peer (P2P) audio/video streams. The WebSocket server (`server.js`) only acts as a **signaling relay** (offer/answer/ICE candidates) - media streams never pass through it.

The WebRTC connection process follows this order:

```
Client A                    STUN Server                   Client B
   |                            |                             |
   |-- STUN request ---------->|                             |
   |<-- Public IP + port ------|                             |
   |                                                          |
   |=============== Direct P2P connection ===================|
   |                    (UDP, ideal case)                     |
```

When direct P2P fails (firewall, symmetric NAT, VPN), TURN takes over as relay:

```
Client A                    TURN Server                   Client B
   |                            |                             |
   |-- Media stream (relayed)->|--- Media stream (relayed) ->|
   |<-- Media stream (relayed)-|<-- Media stream (relayed) --|
   |                            |                             |
```

### When is TURN Necessary?

| Scenario | Direct P2P | TURN Required |
|----------|------------|---------------|
| Same local network (LAN) | Yes | No |
| Standard home network | Usually yes | Rarely |
| Enterprise network with firewall | Rarely | **Yes** |
| Symmetric NAT | No | **Yes** |
| Corporate VPN | Rarely | **Yes** |
| Mobile networks (4G/5G) | Variable | Often |

> **In enterprise environments, 20-30% of WebRTC connections require a TURN server.** Without one, those users will have no voice chat or WebRTC screen sharing.

### Current Configuration (Development)

ICE servers are configured in `VoiceChatManager.cs`:

```
STUN (public, always included):
  - stun:stun.l.google.com:19302
  - stun:stun1.l.google.com:19302
  - stun:stun.cloudflare.com:3478

TURN (dev only, NOT RELIABLE for production):
  - turn:openrelay.metered.ca:443
```

### coturn (Self-Hosted)

Open source TURN server. Cost = server cost only.

**Server prerequisites:**

| Component | Specification |
|-----------|---------------|
| OS | Ubuntu 22.04+ / Debian 12+ |
| RAM | 512 MB minimum (1 GB recommended) |
| CPU | 1 vCPU |
| Bandwidth | 1 Mbps per relayed connection |
| Ports | 3478 (UDP+TCP), 443 (TLS), 49152-65535 (relay UDP) |
| Public IP | Required |

**Installation:**

```bash
# Install
sudo apt update
sudo apt install coturn

# Enable the service
sudo sed -i 's/#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn
```

**Configuration (`/etc/turnserver.conf`):**

```ini
# Network
listening-port=3478
tls-listening-port=5349
listening-ip=0.0.0.0
external-ip=YOUR_PUBLIC_IP
relay-ip=YOUR_PUBLIC_IP
min-port=49152
max-port=65535

# Authentication
realm=your-domain.com
use-auth-secret
static-auth-secret=YOUR_STRONG_SECRET_HERE

# TLS (recommended)
cert=/etc/letsencrypt/live/turn.your-domain.com/fullchain.pem
pkey=/etc/letsencrypt/live/turn.your-domain.com/privkey.pem

# Security
no-multicast-peers
no-cli
denied-peer-ip=10.0.0.0-10.255.255.255
denied-peer-ip=172.16.0.0-172.31.255.255
denied-peer-ip=192.168.0.0-192.168.255.255

# Limits
total-quota=100
stale-nonce=600
max-bps=1048576

# Logs
log-file=/var/log/turnserver.log
simple-log
```

**Start the service:**

```bash
sudo systemctl enable coturn
sudo systemctl start coturn
sudo systemctl status coturn

# Verify ports are open
sudo ss -tulnp | grep turnserver
```

**Firewall rules:**

```bash
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 5349/udp
sudo ufw allow 49152:65535/udp
```

**Test with Trickle ICE:**

Open https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/ and add:
- `turn:YOUR_PUBLIC_IP:3478` with username/credential

### Unity Configuration (Production)

In the Unity Inspector on `VoiceChatManager`:

```csharp
useCustomTurnServer = true;
customTurnUrl = "turn:turn.your-domain.com:3478";
customTurnUsername = ""; // generated dynamically if use-auth-secret
customTurnCredential = ""; // generated dynamically if use-auth-secret
enableTurnTcp = true;  // TCP fallback for restrictive firewalls
```

> **Note:** With `use-auth-secret` in coturn, credentials are temporary (HMAC-SHA1). You will need to implement server-side generation in Node.js and transmit them to clients via WebSocket. For an initial deployment, using `lt-cred-mech` with a fixed username/password is simpler.

**Simple alternative (fixed credentials):**

Replace in `/etc/turnserver.conf`:
```ini
# Replace use-auth-secret with:
lt-cred-mech
user=vrmeeting:YOUR_PASSWORD
```

Then in Unity:
```csharp
customTurnUsername = "vrmeeting";
customTurnCredential = "YOUR_PASSWORD";
```

### Monitoring coturn

```bash
# Real-time logs
sudo tail -f /var/log/turnserver.log

# Active connections
sudo ss -tunp | grep turnserver | wc -l

# System statistics
sudo systemctl status coturn
```

### WebRTC Summary

| Component | Role | Required in Production |
|-----------|------|------------------------|
| STUN (public) | Public IP discovery | Yes (already configured) |
| TURN (private) | Media relay when P2P impossible | **Yes for enterprise** |
| WebSocket server | Signaling only (offer/answer/ICE) | Yes (already in place) |

---

## Unity Integration

### VRNetworkManager Parameters (460 lines)

| Parameter | Current Value | Description |
|-----------|---------------|-------------|
| `serverUrl` | `ws://localhost:8080` | Server URL |
| `enforceSecureConnection` | `false` | Force `wss://` if `true` |
| `autoReconnect` | `true` | Automatic reconnection |
| `welcomeTimeout` | 5s | Welcome message timeout |
| `maxMessagesPerSecond` | 60 | Client rate limiting (token bucket) |
| `burstAllowance` | 10 | Allowed burst |
| `initialReconnectDelay` | 1s | Initial reconnection delay |
| `maxReconnectDelay` | 30s | Max reconnection delay |
| `backoffMultiplier` | 2x | Exponential multiplier |

### VoiceChatManager Parameters (1139 lines)

| Parameter | Current Value | Description |
|-----------|---------------|-------------|
| `useCustomTurnServer` | `false` | Private TURN |
| `usePushToTalk` | `true` | Push-to-talk mode |
| `use3DAudio` | `true` | 3D spatial audio |
| `maxAudioDistance` | 20m | Max audio distance |
| `peerConnectionTimeout` | 15s | WebRTC connection timeout |
| STUN servers | Google (x2), CloudFlare | Public |
| TURN servers | openrelay.metered.ca | Public (dev only) |

### VRGameManager Parameters (1888 lines)

| Parameter | Current Value | Description |
|-----------|---------------|-------------|
| `syncRate` | 30Hz | Position sync frequency |
| `interpolationSpeed` | 15 | Network interpolation speed |
| `movementThreshold` | 0.01m | Movement threshold (optimization) |
| `rotationThreshold` | 1 degree | Rotation threshold (optimization) |

---

## Unity Project Statistics

### Main Scripts

| Script | Lines | Role |
|--------|-------|------|
| VRNetworkManager.cs | 460 | WebSocket client, connection, rate limiting |
| VRRoomManager.cs | 931 | Rooms, players, zones, avatar sync |
| VRGameManager.cs | 1888 | Spawn, 30Hz sync, interpolation, teleport |
| VoiceChatManager.cs | 1139 | WebRTC mesh, spatial audio, push-to-talk |
| BootstrapManager.cs | 292 | Scene flow, XR init, singletons |
| AvatarCustomization.cs | 315 | Colors, username, UI |
| DebugManager.cs | 169 | Per-category logging |
| LaserPointer.cs | 338 | Network laser pointer 10Hz |
| Whiteboard (12 files) | ~4158 | Drawing, UI, network, eraser |
| Sharing (8 files) | ~4185 | Screen, files, presentation |
| UI/Menu (~14 files) | ~2000+ | Main menu + VR in-game menu |
| VR Modules (~10 files) | ~2000+ | Controllers, tracking, input |
| **Total** | **~61 files** | **~28,765 lines** |

---

## Server Metrics

### Available Statistics

| Metric | Access | Frequency |
|--------|--------|-----------|
| Client count | Console log | 60s |
| Room count | Console log | 60s |
| Connections/disconnections | Console log | Real-time |
| Room events | Console log | Real-time |
| Kick events | Console log | Real-time |
| Errors | Console error | Real-time |

### Log Examples

```
[Connect] Client a1b2c3d4...
[Room] Created: XYZ789
[Room] Join: e5f6g7h8 -> XYZ789
[Status] 2 clients | 1 rooms
[Kick] Host a1b2c3d4 kicked e5f6g7h8 from XYZ789
[Timeout] Client a1b2c3d4...
[Disconnect] Client a1b2c3d4...
```

---

## Summary

### Currently Working

- Standalone WebSocket server
- Full room management (create, join, leave, close, update, kick)
- VR position sync 30Hz with interpolation
- Interactive object sync (`obj-sync`, `obj-state`)
- Collaborative whiteboard (drawing, eraser, batch, clear, state sync)
- Screen sharing (WebSocket frames + WebRTC signaling)
- File sharing (chunks, announce, complete, list)
- File presentation (navigation, state sync)
- PDF conversion (30 min cache, automatic cleanup)
- WebRTC voice chat (mesh topology, 3D spatial audio)
- Network laser pointer (10Hz)
- Avatar customization (color, name)
- Client rate limiting (token bucket 60 msg/s)
- Heartbeat and timeout management (60s)
- Automatic reconnection with exponential backoff
- Graceful shutdown (SIGINT)

### Missing for Production

- SSL/TLS configuration (wss://)
- Reverse proxy (Nginx)
- Service manager (systemd/PM2)
- Private TURN server (enterprise voice chat)
- Server-side rate limiting
- Data validation/sanitization
- Message size limit
- Monitoring/alerting
- Log persistence

---

## Changelog

| Date | Version | Description |
|------|---------|-------------|
| 2026-02-02 | 1.0 | English version created from GUIDE_DEPLOIEMENT_ENTREPRISE.md v2.1 |

---

## References

- [GUIDE_DEPLOIEMENT_ENTREPRISE.md](./GUIDE_DEPLOIEMENT_ENTREPRISE.md) - Version francaise
- [LOCAL_TESTING_GUIDE.md](./LOCAL_TESTING_GUIDE.md) - Local testing (WSL2, VM, LAN)
- [SERVER_ARCHITECTURE.md](./SERVER_ARCHITECTURE.md) - Server technical details
- [NETWORKING_CODE_EXPLAINED.md](./NETWORKING_CODE_EXPLAINED.md) - Annotated code
- [SERVER_ARCHITECTURE_KO.md](./SERVER_ARCHITECTURE_KO.md) - Korean version
- [CLAUDE.md](../CLAUDE.md) - Project instructions
