# server.js 코드 설명

이 문서는 WebSocket 서버의 작동 방식을 라인별로 설명합니다.

---

## 개요

서버는 다음을 수행하는 **메시지 릴레이** WebSocket입니다:
1. Unity 클라이언트의 연결 수락
2. 클라이언트 간 메시지 라우팅
3. Room(회의실) 관리
4. 음성용 WebRTC 시그널링
5. MariaDB를 통한 사용자 인증

---

## 파일 구조

```
server.js (868 라인)
│
├── [1-25]     설정 및 Import
├── [27-78]    연결 관리
├── [80-231]   메시지 라우팅 (메인 switch)
├── [233-421]  Room 관리
├── [423-458]  화이트보드 핸들러
├── [460-590]  WebRTC 시그널링 (음성 + 비디오)
├── [592-624]  파일 공유
├── [626-738]  인증
├── [740-824]  브로드캐스트 유틸리티
└── [826-868]  서버 유지보수
```

---

## 1. 설정 및 Import (라인 1-25)

```javascript
const WebSocket = require('ws');           // WebSocket 라이브러리
const { v4: uuidv4 } = require('uuid');    // 고유 ID 생성
const { registerUser, loginUser, updateUserProfile } = require('./auth');  // MariaDB 인증

const PORT = process.env.PORT || 8080;     // 서버 포트 (설정 가능)
const HEARTBEAT_INTERVAL = 30000;          // 30초마다 ping

const clients = new Map();  // 연결된 모든 클라이언트 저장
const rooms = new Map();    // 활성 room 저장
```

### 데이터 구조

**clients Map** : `clientId → { ws, roomId, playerName, lastHeartbeat }`
| 필드 | 설명 |
|------|------|
| `ws` | WebSocket 연결 |
| `roomId` | 현재 room (또는 `null`) |
| `playerName` | 플레이어 이름 |
| `lastHeartbeat` | 마지막 ping 타임스탬프 |

**rooms Map** : `roomId → RoomInfo`
| 필드 | 설명 |
|------|------|
| `roomId` | Room 코드 (예: "ABCDEF") |
| `hostId` | 호스트의 ClientId |
| `playerCount` | 플레이어 수 |
| `maxPlayers` | 제한 (기본값: 10) |

---

## 2. 연결 관리 (라인 27-78)

```javascript
wss.on('connection', (ws) => {
    // 1. 클라이언트용 고유 ID 생성
    const clientId = uuidv4();

    // 2. Map에 클라이언트 등록
    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });

    // 3. 클라이언트에게 ID 전송
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // 4. 다른 클라이언트에게 알림
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);

    // 5. Room 목록 전송
    sendRoomList(ws);
```

### WebSocket 이벤트

| 이벤트 | 동작 |
|--------|------|
| `message` | JSON 파싱 후 `handleMessage()` 호출 |
| `close` | `handleDisconnect()` 호출 |
| `error` | 오류 로그 |
| `pong` | `lastHeartbeat` 업데이트 |

---

## 3. 메시지 라우팅 (라인 80-231)

**서버의 핵심**입니다. 각 메시지 타입을 라우팅하는 switch문입니다.

```javascript
function handleMessage(clientId, message) {
    const { type, senderId, data } = message;
    message.senderId = clientId;  // 보안을 위해 senderId 덮어쓰기

    switch (type) {
        // ... 모든 메시지 타입
    }
}
```

### 메시지 카테고리

| 카테고리 | 타입 | 범위 |
|----------|------|------|
| **Room** | `room-available`, `room-join`, `room-leave`, `room-closed` | 전역 또는 Room |
| **VR 동기화** | `vr-position`, `position` | Room만 |
| **오브젝트** | `obj-sync`, `obj-state` | Room만 |
| **화이트보드** | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request` | Room만 |
| **WebRTC** | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | 1:1 |
| **화면 공유** | `screen-share-start`, `screen-share-frame`, `screen-share-stop` | Room만 |
| **인증** | `auth-register`, `auth-login`, `auth-update-profile` | 1:1 |

### 세 가지 브로드캐스트 모드

```javascript
// 1. 전역 - 모든 클라이언트에게
broadcast(message, exceptClientId);

// 2. ROOM - 같은 room의 클라이언트에게만
broadcastToRoom(clientId, message);

// 3. 1:1 - 단일 클라이언트에게만
sendToClient(targetClient.ws, message);
```

---

## 4. Room 관리 (라인 233-421)

### Room 생성 (handleRoomAvailable)

```javascript
function handleRoomAvailable(clientId, dataStr) {
    const data = JSON.parse(dataStr);

    // Room 객체 생성
    const roomInfo = {
        roomId: data.roomId,
        hostId: clientId,           // 생성자가 호스트가 됨
        roomName: data.roomName,
        playerCount: 1,
        maxPlayers: 10,
        createdAt: Date.now()
    };

    // Room 저장
    rooms.set(data.roomId, roomInfo);

    // 클라이언트를 이 room에 연결
    clients.get(clientId).roomId = data.roomId;

    // 모두에게 알림
    broadcastRoomList();
}
```

### Room 참가 (handleRoomJoin)

```javascript
function handleRoomJoin(clientId, dataStr) {
    const data = JSON.parse(dataStr);
    const room = rooms.get(data.roomId);

    // 검증
    if (!room) return sendError(clientId, 'Room not found');
    if (room.playerCount >= room.maxPlayers) return sendError(clientId, 'Room full');

    // 클라이언트 업데이트
    clients.get(clientId).roomId = data.roomId;

    // 카운터 증가
    room.playerCount++;

    // Room에만 알림
    broadcastToRoom(clientId, { type: 'room-join', ... });
}
```

### 연결 해제 (handleDisconnect)

```javascript
function handleDisconnect(clientId) {
    const client = clients.get(clientId);

    if (client.roomId) {
        const room = rooms.get(client.roomId);

        if (room.hostId === clientId) {
            // 호스트가 나감 → room 닫기
            rooms.delete(client.roomId);
            broadcast({ type: 'room-closed', ... });
        } else {
            // 일반 플레이어 → 감소
            room.playerCount--;
        }

        // Room에 알림
        broadcastToRoom(clientId, { type: 'room-leave', ... });
    }

    // 클라이언트 삭제
    clients.delete(clientId);

    // 전역 알림
    broadcast({ type: 'peer-disconnected', senderId: clientId });
}
```

---

## 5. 화이트보드 (라인 423-458)

### handleWhiteboardState

늦은 참가자에게 화이트보드 상태(PNG 텍스처) 전송을 처리합니다.

```javascript
function handleWhiteboardState(clientId, dataStr) {
    const stateData = JSON.parse(dataStr);

    if (stateData.targetId) {
        // 단일 클라이언트에게 타겟팅 전송 (늦은 참가자)
        const targetClient = clients.get(stateData.targetId);
        sendToClient(targetClient.ws, {
            type: 'whiteboard-state',
            senderId: clientId,
            data: dataStr
        });
    } else {
        // 전체 room에 브로드캐스트
        broadcastToRoom(clientId, { type: 'whiteboard-state', ... });
    }
}
```

---

## 6. WebRTC 시그널링 (라인 460-590)

서버는 클라이언트 간 SDP 및 ICE 메시지만 **릴레이**합니다.

### WebRTC 흐름 (음성)

```
클라이언트 A                서버                    클라이언트 B
    │                          │                          │
    │── webrtc-offer ─────────►│                          │
    │   {targetId: B, sdp}     │── webrtc-offer ─────────►│
    │                          │                          │
    │                          │◄── webrtc-answer ───────│
    │◄── webrtc-answer ───────│   {targetId: A, sdp}     │
    │                          │                          │
    │◄─────────────────────────┼── ICE candidates ───────►│
```

```javascript
function handleWebRTCOffer(senderId, dataStr) {
    const { targetId, sdp } = JSON.parse(dataStr);

    // 대상 클라이언트 찾기
    const targetClient = clients.get(targetId);

    // 오퍼 릴레이
    sendToClient(targetClient.ws, {
        type: 'webrtc-offer',
        senderId: senderId,
        data: JSON.stringify({ sdp })
    });
}
```

---

## 7. 인증 (라인 626-738)

MariaDB와 통신하는 `auth.js` 모듈을 사용합니다.

### 회원가입

```javascript
async function handleAuthRegister(clientId, dataStr) {
    const { username, email, password, displayName } = JSON.parse(dataStr);

    // 인증 함수 호출 (bcrypt 해시 + SQL INSERT)
    const result = await registerUser(username, email, password, displayName);

    // 클라이언트에게 결과 전송
    sendAuthResponse(clientId, 'auth-register-response', result);

    // 성공 시 클라이언트 업데이트
    if (result.success) {
        clients.get(clientId).userId = result.userId;
        clients.get(clientId).playerName = result.displayName;
    }
}
```

### 로그인

```javascript
async function handleAuthLogin(clientId, dataStr) {
    const { username, password } = JSON.parse(dataStr);

    // 자격 증명 확인 (SELECT + bcrypt.compare)
    const result = await loginUser(username, password);

    sendAuthResponse(clientId, 'auth-login-response', result);
}
```

---

## 8. 브로드캐스트 유틸리티 (라인 740-824)

### sendToClient - 단일 클라이언트에 전송

```javascript
function sendToClient(ws, message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
    }
}
```

### broadcast - 전역 전송

```javascript
function broadcast(message, exceptClientId = null) {
    clients.forEach((client, clientId) => {
        if (clientId !== exceptClientId && client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(JSON.stringify(message));
        }
    });
}
```

### broadcastToRoom - Room에 전송 (핵심)

```javascript
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    if (!sender || !sender.roomId) return;

    const roomId = sender.roomId;

    clients.forEach((client, clientId) => {
        // 조건:
        // 1. 발신자가 아님
        // 2. 같은 room
        // 3. 연결 열림
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(JSON.stringify(message));
        }
    });
}
```

> **중요** : 이 함수는 room 간 격리를 보장합니다. VR, 화이트보드, 화면 공유 메시지는 같은 room의 멤버에게만 전달됩니다.

---

## 9. 서버 유지보수 (라인 826-868)

### Heartbeat (ping/pong)

```javascript
const heartbeatInterval = setInterval(() => {
    // 모든 클라이언트에 ping 전송
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });

    // 비활성 클라이언트 연결 해제 (2배 간격 = 60초)
    clients.forEach((client, clientId) => {
        if (Date.now() - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
}, HEARTBEAT_INTERVAL);  // 30초마다
```

### 정상 종료 (SIGINT)

```javascript
process.on('SIGINT', () => {
    clearInterval(heartbeatInterval);

    // 모든 연결 닫기
    wss.clients.forEach((ws) => ws.close());

    // 서버 종료
    wss.close(() => process.exit(0));
});
```

### 주기적 로그

```javascript
setInterval(() => {
    console.log(`[SERVER] ${clients.size} clients | Rooms: ...`);
}, 60000);  // 1분마다
```

---

## 흐름 요약

```
┌─────────────────────────────────────────────────────────────────┐
│                        NODE.JS 서버                              │
│                                                                 │
│  1. 클라이언트 연결                                              │
│     └─► UUID 생성, 'welcome' 전송, 'peer-connected' 브로드캐스트 │
│                                                                 │
│  2. 클라이언트가 room 생성/참가                                  │
│     └─► clients Map과 rooms Map 업데이트                        │
│                                                                 │
│  3. 클라이언트가 메시지 전송                                     │
│     └─► handleMessage()가 타입별 라우팅                         │
│         ├─► broadcastToRoom() - VR/화이트보드/화면 공유용       │
│         ├─► sendToClient() - WebRTC 시그널링용                  │
│         └─► broadcast() - 전역 이벤트용                         │
│                                                                 │
│  4. 클라이언트 연결 해제                                         │
│     └─► handleDisconnect()가 정리, room에 알림                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 의존성

| 패키지 | 버전 | 역할 |
|--------|------|------|
| `ws` | ^8.x | WebSocket 서버 |
| `uuid` | ^9.x | ID 생성 |
| `bcrypt` | ^5.x | 비밀번호 해시 (auth.js 경유) |
| `mysql2` | ^3.x | MariaDB 연결 (db.js 경유) |

---

## 보안 핵심 사항

1. **senderId 덮어쓰기** (라인 86) : 서버는 항상 senderId를 실제 클라이언트 ID로 교체하여 위장을 방지합니다.

2. **호스트 확인** (라인 277, 368) : 호스트만 room을 닫거나 수정할 수 있습니다.

3. **Room 격리** : `broadcastToRoom()`이 room 간 메시지 누출을 방지합니다.

4. **Heartbeat** : 비활성 클라이언트는 60초 후 연결 해제됩니다.

---

*내부 프레젠테이션용 문서*
