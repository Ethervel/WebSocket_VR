# 시퀀스 다이어그램 - VR 회의 플랫폼

## 1. 클라이언트 연결

```
클라이언트                        서버                           MariaDB
   │                               │                               │
   │──── WebSocket 연결 ──────────►│                               │
   │                               │                               │
   │◄─── welcome {senderId} ──────│                               │
   │                               │                               │
   │──── auth-login ──────────────►│                               │
   │     {username, password}      │──── SELECT password_hash ────►│
   │                               │◄─── 사용자 데이터 ────────────│
   │                               │     bcrypt.compare()          │
   │◄─── auth-login-response ─────│                               │
   │     {success, userId, ...}    │──── UPDATE last_login ───────►│
   │                               │                               │
```

## 2. 룸 생성 (호스트)

```
호스트                            서버                        다른 클라이언트
  │                                │                               │
  │──── room-available ───────────►│                               │
  │     {roomId, roomName, ...}    │                               │
  │                                │──── room-available ──────────►│
  │                                │     (글로벌 브로드캐스트)      │
  │                                │                               │
  │     [호스트가 룸에 입장]        │                               │
  │     rooms.set(roomId, info)    │                               │
  │     client.roomId = roomId     │                               │
```

## 3. 룸 참가

```
클라이언트 A                       서버                           호스트
   │                               │                               │
   │──── room-join ───────────────►│                               │
   │     {roomId, playerName}      │──── room-join ───────────────►│
   │                               │     (룸으로 전달)              │
   │                               │                               │
   │                               │◄─── room-welcome ────────────│
   │◄─── room-welcome ────────────│     {players[], roomType}     │
   │     (룸 브로드캐스트 경유)     │                               │
   │                               │                               │
   │     [클라이언트 A가 룸에 입장] │                               │
   │     client.roomId = roomId    │                               │
```

## 4. VR 위치 동기화 (30 Hz)

```
클라이언트 A                       서버                        클라이언트 B
   │                               │                               │
   │  [움직임이 있을 때마다 33ms]   │                               │
   │                               │                               │
   │──── vr-position ─────────────►│                               │
   │     {posX, headPos, hands...} │──── vr-position ─────────────►│
   │                               │     (룸 브로드캐스트)          │
   │                               │                               │
   │                               │◄─── vr-position ─────────────│
   │◄─── vr-position ─────────────│     {posX, headPos, hands...} │
   │     (클라이언트 B로부터)       │                               │
```

## 5. WebRTC 음성 채팅 (메시)

```
클라이언트 A (ID: "aaa")          서버                   클라이언트 B (ID: "bbb")
   │                               │                               │
   │  [A < B 사전순]               │                               │
   │  [A가 연결 시작]              │                               │
   │                               │                               │
   │──── webrtc-offer ────────────►│                               │
   │     {targetId: "bbb", sdp}    │──── webrtc-offer ────────────►│
   │                               │     (1:1)                     │
   │                               │                               │
   │                               │◄─── webrtc-answer ───────────│
   │◄─── webrtc-answer ───────────│     {targetId: "aaa", sdp}    │
   │                               │                               │
   │◄──────────────────────────────┼───── ICE 후보 ───────────────►│
   │     (양방향)                   │                               │
   │                               │                               │
   │◄═══════════════════════════════════════════════════════════►│
   │            P2P 직접 연결 (WebRTC 음성)                        │
```

## 6. 화이트보드 그리기

```
그리는 사람                        서버                         시청자들
   │                               │                               │
   │  [그리는 동안 33ms마다]        │                               │
   │                               │                               │
   │──── whiteboard-batch ────────►│                               │
   │     {color, penSize,          │──── whiteboard-batch ────────►│
   │      pointsFlat[u,v,...]}     │     (룸 브로드캐스트)          │
   │                               │                               │
   │                               │                               │
   │  === 늦은 참가자 ===           │                               │
   │                               │◄─── whiteboard-request ──────│
   │◄─── whiteboard-request ──────│     {requesterId}             │
   │                               │                               │
   │──── whiteboard-state ────────►│                               │
   │     {textureData: base64 PNG} │──── whiteboard-state ────────►│
   │                               │     (1:1)                     │
```

## 7. 화면 공유

```
공유자                            서버                         시청자들
   │                               │                               │
   │──── screen-share-start ──────►│                               │
   │     {sharerId, sharerName}    │──── screen-share-start ──────►│
   │                               │     (룸 브로드캐스트)          │
   │                               │                               │
   │  [333ms마다 (3fps)]           │                               │
   │                               │                               │
   │──── screen-share-frame ──────►│                               │
   │     {imageData: JPEG base64}  │──── screen-share-frame ──────►│
   │                               │     (룸 브로드캐스트)          │
   │                               │                               │
   │  [공유 중지]                   │                               │
   │                               │                               │
   │──── screen-share-stop ───────►│                               │
   │                               │──── screen-share-stop ───────►│
   │                               │     (룸 브로드캐스트)          │
```

## 8. 연결 해제 / 룸 종료

```
호스트                            서버                        다른 클라이언트
  │                                │                               │
  │  [호스트가 룸 종료]             │                               │
  │                                │                               │
  │──── room-closed ──────────────►│                               │
  │     {roomId}                   │──── room-closed ─────────────►│
  │                                │     (글로벌 브로드캐스트)      │
  │                                │                               │
  │  [WebSocket 연결 해제]          │                               │
  │        ────X                   │                               │
  │                                │──── peer-disconnected ───────►│
  │                                │     {senderId: hostId}        │
  │                                │                               │
  │                                │     rooms.delete(roomId)      │
  │                                │     clients.delete(hostId)    │
```

## 9. 전체 흐름 - 일반 회의

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          전체 회의 흐름                                  │
└─────────────────────────────────────────────────────────────────────────┘

1. 연결
   호스트 ──► WebSocket 연결 ──► welcome ──► auth-login ──► auth-response

2. 룸 생성
   호스트 ──► room-available ──► 글로벌 브로드캐스트

3. 참가자 입장
   클라이언트 ──► room-join ──► 호스트로 전달 ──► room-welcome ──► 룸 브로드캐스트

4. 음성 설정 (각 쌍마다)
   피어 A ──► webrtc-offer ──► 피어 B ──► webrtc-answer ──► ICE ──► P2P 음성

5. 지속적 동기화 (병렬)
   ├── vr-position (30Hz) ──► 룸 브로드캐스트
   ├── whiteboard-batch (그리기 시) ──► 룸 브로드캐스트
   └── screen-share-frame (공유 시, 3fps) ──► 룸 브로드캐스트

6. 회의 종료
   호스트 ──► room-closed ──► 글로벌 브로드캐스트
   클라이언트 ──► 로컬 상태 정리
```

---

## 범례

```
────►  WebSocket 메시지
═════► P2P 직접 연결 (WebRTC)
──X    연결 해제
[...] 로컬 동작 / 조건
```
