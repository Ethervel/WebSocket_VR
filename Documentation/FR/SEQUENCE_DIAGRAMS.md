# Diagrammes de Sequence - VR Meeting Platform

## 1. Connexion Client

```
Client                          Serveur                         MariaDB
   │                               │                               │
   │──── WebSocket Connect ───────►│                               │
   │                               │                               │
   │◄─── welcome {senderId} ──────│                               │
   │                               │                               │
   │──── auth-login ──────────────►│                               │
   │     {username, password}      │──── SELECT password_hash ────►│
   │                               │◄─── user data ───────────────│
   │                               │     bcrypt.compare()          │
   │◄─── auth-login-response ─────│                               │
   │     {success, userId, ...}    │──── UPDATE last_login ───────►│
   │                               │                               │
```

## 2. Creation Room (Host)

```
Host                            Serveur                      Autres Clients
  │                                │                               │
  │──── room-available ───────────►│                               │
  │     {roomId, roomName, ...}    │                               │
  │                                │──── room-available ──────────►│
  │                                │     (broadcast global)        │
  │                                │                               │
  │     [Host entre dans la room]  │                               │
  │     rooms.set(roomId, info)    │                               │
  │     client.roomId = roomId     │                               │
```

## 3. Rejoindre une Room

```
Client A                        Serveur                           Host
   │                               │                               │
   │──── room-join ───────────────►│                               │
   │     {roomId, playerName}      │──── room-join ───────────────►│
   │                               │     (forward to room)         │
   │                               │                               │
   │                               │◄─── room-welcome ────────────│
   │◄─── room-welcome ────────────│     {players[], roomType}     │
   │     (via broadcast room)      │                               │
   │                               │                               │
   │     [Client A dans la room]   │                               │
   │     client.roomId = roomId    │                               │
```

## 4. Synchronisation Position VR (30 Hz)

```
Client A                        Serveur                        Client B
   │                               │                               │
   │  [Chaque 33ms si mouvement]   │                               │
   │                               │                               │
   │──── vr-position ─────────────►│                               │
   │     {posX, headPos, hands...} │──── vr-position ─────────────►│
   │                               │     (broadcast room)          │
   │                               │                               │
   │                               │◄─── vr-position ─────────────│
   │◄─── vr-position ─────────────│     {posX, headPos, hands...} │
   │     (de Client B)             │                               │
```

## 5. WebRTC Voice Chat (Mesh)

```
Client A (ID: "aaa")            Serveur                   Client B (ID: "bbb")
   │                               │                               │
   │  [A < B lexicographiquement]  │                               │
   │  [A initie la connexion]      │                               │
   │                               │                               │
   │──── webrtc-offer ────────────►│                               │
   │     {targetId: "bbb", sdp}    │──── webrtc-offer ────────────►│
   │                               │     (point-to-point)          │
   │                               │                               │
   │                               │◄─── webrtc-answer ───────────│
   │◄─── webrtc-answer ───────────│     {targetId: "aaa", sdp}    │
   │                               │                               │
   │◄──────────────────────────────┼───── ICE candidates ─────────►│
   │     (bidirectionnel)          │                               │
   │                               │                               │
   │◄═══════════════════════════════════════════════════════════►│
   │            Connexion P2P directe (voix WebRTC)                │
```

## 6. Whiteboard Drawing

```
Drawer                          Serveur                        Viewers
   │                               │                               │
   │  [Chaque 33ms pendant dessin] │                               │
   │                               │                               │
   │──── whiteboard-batch ────────►│                               │
   │     {color, penSize,          │──── whiteboard-batch ────────►│
   │      pointsFlat[u,v,...]}     │     (broadcast room)          │
   │                               │                               │
   │                               │                               │
   │  === Late Joiner ===          │                               │
   │                               │◄─── whiteboard-request ──────│
   │◄─── whiteboard-request ──────│     {requesterId}             │
   │                               │                               │
   │──── whiteboard-state ────────►│                               │
   │     {textureData: base64 PNG} │──── whiteboard-state ────────►│
   │                               │     (point-to-point)          │
```

## 7. Screen Share

```
Sharer                          Serveur                        Viewers
   │                               │                               │
   │──── screen-share-start ──────►│                               │
   │     {sharerId, sharerName}    │──── screen-share-start ──────►│
   │                               │     (broadcast room)          │
   │                               │                               │
   │  [Chaque 333ms (3fps)]        │                               │
   │                               │                               │
   │──── screen-share-frame ──────►│                               │
   │     {imageData: JPEG base64}  │──── screen-share-frame ──────►│
   │                               │     (broadcast room)          │
   │                               │                               │
   │  [Arret partage]              │                               │
   │                               │                               │
   │──── screen-share-stop ───────►│                               │
   │                               │──── screen-share-stop ───────►│
   │                               │     (broadcast room)          │
```

## 8. Deconnexion / Fermeture Room

```
Host                            Serveur                      Autres Clients
  │                                │                               │
  │  [Host ferme la room]          │                               │
  │                                │                               │
  │──── room-closed ──────────────►│                               │
  │     {roomId}                   │──── room-closed ─────────────►│
  │                                │     (broadcast global)        │
  │                                │                               │
  │  [Deconnexion WebSocket]       │                               │
  │        ────X                   │                               │
  │                                │──── peer-disconnected ───────►│
  │                                │     {senderId: hostId}        │
  │                                │                               │
  │                                │     rooms.delete(roomId)      │
  │                                │     clients.delete(hostId)    │
```

## 9. Flux Complet - Reunion Type

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        FLUX COMPLET REUNION                            │
└─────────────────────────────────────────────────────────────────────────┘

1. CONNEXION
   Host ──► WebSocket Connect ──► welcome ──► auth-login ──► auth-response

2. CREATION ROOM
   Host ──► room-available ──► Broadcast global

3. PARTICIPANTS REJOIGNENT
   Client ──► room-join ──► Forward to Host ──► room-welcome ──► Broadcast room

4. ETABLISSEMENT VOIX (pour chaque paire)
   Peer A ──► webrtc-offer ──► Peer B ──► webrtc-answer ──► ICE ──► P2P Voice

5. SYNCHRONISATION CONTINUE (parallele)
   ├── vr-position (30Hz) ──► Broadcast room
   ├── whiteboard-batch (si dessin) ──► Broadcast room
   └── screen-share-frame (si partage, 3fps) ──► Broadcast room

6. FIN REUNION
   Host ──► room-closed ──► Broadcast global
   Clients ──► Cleanup local state
```

---

## Legende

```
────►  Message WebSocket
═════► Connexion P2P directe (WebRTC)
──X    Deconnexion
[...] Action locale / condition
```
