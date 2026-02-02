# WebSocket 서버 아키텍처 - VR 미팅룸

이 문서는 WebSocket 서버의 작동 방식, Unity 프로젝트와의 연결 방법, 교환되는 메시지, 원격 서버 배포 방법을 설명합니다.

> **마지막 업데이트: 2026-02-02** - 현재 소스 코드와 동기화됨.

> **빠른 배포가 필요하신가요?** 단계별 배포를 위해 [기업 배포 가이드](./GUIDE_DEPLOIEMENT_ENTREPRISE.md)를 참조하세요. (프랑스어)

## 관련 문서

| 문서 | 설명 |
|------|------|
| [GUIDE_DEPLOIEMENT_ENTREPRISE.md](./GUIDE_DEPLOIEMENT_ENTREPRISE.md) | 기업 서버 배포를 위한 단계별 가이드 |
| [SERVER_ARCHITECTURE.md](./SERVER_ARCHITECTURE.md) | 서버 아키텍처 (프랑스어) |
| [NETWORKING_CODE_EXPLAINED.md](./NETWORKING_CODE_EXPLAINED.md) | 네트워크 코드 상세 설명 |

## 목차

1. [개요](#개요)
2. [서버 아키텍처](#서버-아키텍처)
3. [Unity <-> 서버 연결](#unity---서버-연결)
4. [메시지 프로토콜](#메시지-프로토콜)
5. [메시지 처리](#메시지-처리)
6. [프로덕션 배포](#프로덕션-배포)
7. [보안](#보안)
8. [문제 해결](#문제-해결)

---

## 개요

### 흐름도

```
+------------------+          WebSocket (ws/wss)         +------------------+
|                  | <----------------------------------> |                  |
|   Unity 클라이언트  |     양방향 JSON 메시지              |   Node.js 서버    |
|  (VRNetworkMgr)  |                                     |   (server.js)    |
|                  | <----------------------------------> |                  |
+------------------+                                     +------------------+
        |                                                        |
        v                                                        v
+------------------+                                     +------------------+
| VRRoomManager    |                                     | filePresentation |
| VRGameManager    |                                     |   .js (PDF)      |
| VoiceChatManager |                                     | (선택사항)         |
+------------------+                                     +------------------+
```

### 기술 스택

| 구성 요소 | 기술 | 버전 | 파일 |
|-----------|------|------|------|
| WebSocket 서버 | Node.js + ws | 8.14.2 | `server.js` (887줄) |
| WebSocket 클라이언트 | NativeWebSocket (Unity) | Unity 패키지 | `VRNetworkManager.cs` (460줄) |
| 음성 (WebRTC) | Unity.WebRTC | 3.0.0 | `VoiceChatManager.cs` (1139줄) |
| PDF (선택사항) | pdf-poppler | 0.2.3 | `filePresentation.js` (257줄) |

---

## 서버 아키텍처

### 서버 파일 구조

```
Server/
├── server.js           # 메인 WebSocket 서버 (887줄)
├── filePresentation.js # PDF 변환 (257줄, 선택사항)
├── package.json        # npm 의존성: ws, uuid, pdf-poppler
└── node_modules/       # 설치된 의존성
```

### server.js - 주요 구성 요소

#### 1. 초기화 (1-27줄)

```javascript
const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');

const PORT = process.env.PORT || 8080;
const HEARTBEAT_INTERVAL = 30000;  // 30초
const PDF_CACHE_TTL = 30 * 60 * 1000;  // 30분

// 메모리 저장소
const clients = new Map();  // clientId -> { ws, roomId, playerName, lastHeartbeat }
const rooms = new Map();    // roomId -> RoomInfo
const pdfCache = new Map(); // fileId -> { pages, totalPages, timestamp }
```

#### 2. 연결 관리 (41-96줄)

클라이언트 연결 시:
1. 고유 UUID 생성 (`clientId`)
2. `clients` Map에 클라이언트 저장
3. 할당된 ID와 함께 `welcome` 메시지 전송
4. 다른 클라이언트에게 `peer-connected` 브로드캐스트
5. 사용 가능한 방 목록 전송

```javascript
wss.on('connection', (ws) => {
    const clientId = uuidv4();

    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });

    // 할당된 ID와 함께 환영 메시지
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // 다른 클라이언트에게 알림
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);

    // 방 목록 전송
    sendRoomList(ws);
});
```

#### 3. 메시지 라우팅 (100-228줄)

메인 `switch`문이 각 메시지 타입을 해당 핸들러로 라우팅합니다 (총 46개 명시적 타입):

| 카테고리 | 메시지 타입 | 동작 |
|----------|-------------|------|
| **방 생명주기** | `room-available`, `room-closed`, `room-join`, `room-leave`, `room-update`, `room-list-request` | 전용 핸들러 |
| **VR 위치** | `vr-position`, `position` | 방으로 브로드캐스트 |
| **인터랙티브 오브젝트** | `obj-sync`, `obj-state` | 방으로 브로드캐스트 |
| **화이트보드** | `whiteboard-draw`, `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request` | 방으로 브로드캐스트 |
| **화이트보드 상태** | `whiteboard-state` | P2P (targetId) |
| **방 상태** | `room-welcome`, `room-teleport`, `player-name-update` | 방으로 브로드캐스트 |
| **관리** | `kick-player` | P2P (호스트만 가능) |
| **WebRTC 음성** | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | P2P |
| **화면 공유** | `screen-share-start/stop/frame/request/state` | 방으로 브로드캐스트 |
| **화면 WebRTC** | `screen-video-offer/answer/ice` | P2P |
| **파일 공유** | `file-announce`, `file-chunk`, `file-complete`, `file-request`, `file-list-request` | 방으로 브로드캐스트 |
| **파일 공유 P2P** | `file-list-response` | P2P 또는 브로드캐스트 |
| **파일 프레젠테이션** | `file-present-start/page/navigate/stop/request` | 방으로 브로드캐스트 |
| **파일 프레젠테이션 P2P** | `file-present-state` | P2P 또는 브로드캐스트 |
| **PDF** | `pdf-convert-request`, `pdf-page-request` | 전용 핸들러 (직접 응답) |
| **기본** | 기타 | 방에 있으면 방으로, 아니면 전체 브로드캐스트 |

#### 4. 핵심 함수: `broadcastToRoom`

이 함수는 매우 중요합니다 - 방별로 메시지를 필터링합니다:

```javascript
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    if (!sender || !sender.roomId) return;

    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);

    clients.forEach((client, clientId) => {
        // 다음 조건을 모두 만족할 때만 전송:
        // 1. 발신자가 아님
        // 2. 같은 방에 있음
        // 3. 연결이 열려 있음
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}
```

#### 5. 통신 함수

| 함수 | 설명 |
|------|------|
| `sendToClient(ws, message)` | 1개 클라이언트에 전송 |
| `broadcast(message, exceptId)` | 모든 클라이언트에 전송 |
| `broadcastToRoom(senderId, message)` | 방 멤버에게만 전송 |
| `sendRoomList(ws)` | 1개 클라이언트에 방 목록 전송 |
| `broadcastRoomList()` | 모든 클라이언트에 방 목록 전송 |
| `sendError(clientId, errorMessage)` | 1개 클라이언트에 오류 전송 |

#### 6. 하트비트와 유지보수 (838-887줄)

```javascript
// 하트비트 (30초마다)
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    // 모든 클라이언트에게 ping 전송
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });

    // 타임아웃된 클라이언트 연결 해제 (60초)
    clients.forEach((client, clientId) => {
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
}, HEARTBEAT_INTERVAL);

// PDF 캐시 정리 (5분마다)
setInterval(() => {
    for (const [fileId, entry] of pdfCache) {
        if (Date.now() - entry.timestamp > PDF_CACHE_TTL) {
            pdfCache.delete(fileId);
        }
    }
}, 5 * 60 * 1000);

// 상태 로그 (60초마다)
setInterval(() => {
    console.log(`[Status] ${clients.size} clients | ${rooms.size} rooms`);
}, 60000);
```

---

## Unity <-> 서버 연결

### Unity 측: VRNetworkManager.cs (460줄)

#### 설정 (Unity Inspector)

| 매개변수 | 기본값 | 설명 |
|----------|--------|------|
| `serverUrl` | `ws://localhost:8080` | WebSocket 서버 URL |
| `enforceSecureConnection` | `false` | 프로덕션에서 wss:// 강제 |
| `autoReconnect` | `true` | 자동 재연결 |
| `welcomeTimeout` | `5초` | welcome 메시지 타임아웃 |
| `maxMessagesPerSecond` | `60` | 속도 제한 (토큰 버킷) |
| `burstAllowance` | `10` | 버스트 허용량 |
| `initialReconnectDelay` | `1초` | 초기 재연결 지연 |
| `maxReconnectDelay` | `30초` | 최대 재연결 지연 |
| `backoffMultiplier` | `2배` | 지수 백오프 배율 |

#### 연결 흐름

```
Unity                                       서버
  |                                           |
  |-------- WebSocket 연결 ------------------->|
  |                                           |
  |<------- welcome {senderId: "uuid"} -------|
  |                                           |
  |-------- room-available {roomId, name} --->|
  |                                           |
  |<------- room-list {rooms: [...]} ---------|
  |                                           |
  |-------- vr-position (30Hz) --------------->|
  |                                           |
```

#### 오류 처리 (지수 백오프)

```csharp
// VRNetworkManager.cs
// 지연 계산: 1초 -> 2초 -> 4초 -> 8초 -> ... -> 최대 30초
_currentReconnectDelay = Mathf.Min(
    _currentReconnectDelay * backoffMultiplier,
    maxReconnectDelay
);
```

#### 속도 제한 (토큰 버킷)

```csharp
// 토큰 버킷 알고리즘으로 속도 제한
private bool CheckRateLimit(string messageType)
{
    float elapsed = Time.unscaledTime - _lastRateLimitRefill;
    _rateLimitTokens = Mathf.Min(burstAllowance, _rateLimitTokens + (elapsed * maxMessagesPerSecond));

    if (_rateLimitTokens >= 1f) {
        _rateLimitTokens -= 1f;
        return true;
    }
    return false;  // 속도 제한됨
}
```

---

## 메시지 프로토콜

### 표준 형식

모든 메시지는 다음 JSON 형식을 따릅니다:

```json
{
    "type": "message-type",
    "senderId": "client-uuid",
    "data": "{\"json\": \"serialized\"}"
}
```

> **중요:** `senderId`는 **서버 측에서 강제 설정**됩니다 (`message.senderId = clientId`). 클라이언트가 보낸 값은 덮어쓰여집니다.

### 카테고리별 메시지

#### 1. 연결

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `welcome` | 서버 -> 클라이언트 | `null` (senderId = 할당된 ID) |
| `peer-connected` | 서버 -> 전체 | `null` (senderId = 새 피어) |
| `peer-disconnected` | 서버 -> 전체 | `null` (senderId = 떠난 피어) |

#### 2. 방 관리

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `room-available` | 클라이언트 -> 서버 | `{roomId, roomName, roomType, maxPlayers}` |
| `room-join` | 클라이언트 -> 서버 | `{roomId, playerId, playerName, colorR/G/B}` |
| `room-welcome` | 호스트 -> 방 | `{roomId, roomType, players: [...]}` |
| `room-leave` | 클라이언트 -> 방 | `{roomId, playerId}` |
| `room-list` | 서버 -> 클라이언트 | `{rooms: [RoomInfo...]}` |
| `room-list-request` | 클라이언트 -> 서버 | (비어 있음) |
| `room-closed` | 호스트 -> 전체 | `{roomId}` |
| `room-update` | 호스트 -> 서버 | `{roomId, ...}` (호스트만 가능) |
| `room-teleport` | 클라이언트 -> 방 | `{roomId, roomType}` |
| `player-name-update` | 클라이언트 -> 방 | `{playerName}` |
| `kick-player` | 호스트 -> 대상 | `{roomId, playerId, reason}` |

#### 3. VR 동기화 (30Hz)

```csharp
// VRPositionData (VRGameManager.cs - 1888줄)
{
    "roomId": "ABC123",
    "roomType": 1,  // 0=로비, 1=RoomA, 2=RoomB
    // 몸
    "posX": 1.234, "posY": 0.0, "posZ": -5.678,
    "rotY": 45.0,
    // 머리 (월드 스페이스)
    "headPosX": 1.234, "headPosY": 1.7, "headPosZ": -5.678,
    "headRotX": 0.0, "headRotY": 0.707, "headRotZ": 0.0, "headRotW": 0.707,
    // 손 (월드 스페이스) - 0 = 데스크톱 모드 (손 숨김)
    "leftHandPosX": ..., "leftHandRotX": ...,
    "rightHandPosX": ..., "rightHandRotX": ...
}
```

#### 4. 인터랙티브 오브젝트

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `obj-sync` | 클라이언트 -> 방 | 오브젝트 위치/회전/상태 |
| `obj-state` | 클라이언트 -> 방 | 전체 상태 (후발 참가자용) |

#### 5. WebRTC 시그널링 (P2P)

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `webrtc-offer` | 클라이언트 -> 클라이언트 | `{targetId, sdp}` |
| `webrtc-answer` | 클라이언트 -> 클라이언트 | `{targetId, sdp}` |
| `webrtc-ice-candidate` | 클라이언트 -> 클라이언트 | `{targetId, candidate, sdpMid, sdpMLineIndex}` |

#### 6. 화이트보드

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `whiteboard-draw` | 클라이언트 -> 방 | `{whiteboardId, roomId, ...strokeData}` |
| `whiteboard-batch` | 클라이언트 -> 방 | `{whiteboardId, roomId, r/g/b/a, penSize, pointsFlat: [u,v,...]}` |
| `whiteboard-clear` | 클라이언트 -> 방 | `{whiteboardId, roomId}` |
| `whiteboard-request` | 클라이언트 -> 방 | `{whiteboardId, roomId}` |
| `whiteboard-state` | 클라이언트 -> 클라이언트 | `{targetId, textureData (base64 PNG)}` |

#### 7. 화면 공유

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `screen-share-start` | 클라이언트 -> 방 | `{sharerId, sharerName}` |
| `screen-share-frame` | 클라이언트 -> 방 | `{imageData (base64 JPEG)}` |
| `screen-share-stop` | 클라이언트 -> 방 | `{sharerId}` |
| `screen-share-request` | 클라이언트 -> 방 | `{sharerId}` |
| `screen-share-state` | 클라이언트 -> 방 | `{targetId, ...}` |
| `screen-video-offer` | 클라이언트 -> 클라이언트 | `{targetId, sdp}` (P2P) |
| `screen-video-answer` | 클라이언트 -> 클라이언트 | `{targetId, sdp}` (P2P) |
| `screen-video-ice` | 클라이언트 -> 클라이언트 | `{targetId, candidate}` (P2P) |

#### 8. 파일 공유

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `file-announce` | 클라이언트 -> 방 | `{fileId, fileName, fileSize, fileType}` |
| `file-chunk` | 클라이언트 -> 방 | `{fileId, chunkIndex, data}` |
| `file-complete` | 클라이언트 -> 방 | `{fileId}` |
| `file-request` | 클라이언트 -> 방 | `{fileId}` |
| `file-list-request` | 클라이언트 -> 방 | `{roomId}` |
| `file-list-response` | 클라이언트 -> 클라이언트 | `{targetId, files: [...]}` |

#### 9. 파일 프레젠테이션

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `file-present-start` | 클라이언트 -> 방 | `{fileId, fileName, totalPages}` |
| `file-present-page` | 클라이언트 -> 방 | `{fileId, pageIndex, imageData}` |
| `file-present-navigate` | 클라이언트 -> 방 | `{fileId, pageIndex}` |
| `file-present-stop` | 클라이언트 -> 방 | `{fileId}` |
| `file-present-request` | 클라이언트 -> 방 | `{roomId}` |
| `file-present-state` | 클라이언트 -> 클라이언트 | `{targetId, fileId, currentPage, ...}` |

#### 10. PDF 처리

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `pdf-convert-request` | 클라이언트 -> 서버 | `{fileId, data (base64)}` |
| `pdf-convert-response` | 서버 -> 클라이언트 | `{fileId, totalPages, success}` |
| `pdf-page-request` | 클라이언트 -> 서버 | `{fileId, pageIndex}` |
| `pdf-page-response` | 서버 -> 클라이언트 | `{fileId, pageIndex, imageData}` |

---

## 메시지 처리

### 전체 예시: 방 참가

```
플레이어 A (호스트)             서버                    플레이어 B
     |                             |                           |
     |-- room-available ---------->|                           |
     |                             |--- room-list (브로드캐스트)->|
     |                             |                           |
     |                             |<----- room-join ----------|
     |                             |                           |
     |<--- room-join --------------|--- room-join ------------>|
     |                             |                           |
     |--- room-welcome (players)-->|                           |
     |                             |--- room-welcome --------->|
     |                             |                           |
     |<======= vr-position (30Hz 양방향) =====================>|
     |                             |                           |
```

### 파일 프레젠테이션 흐름

```
발표자                          서버                    참가자
  |                                |                          |
  |-- pdf-convert-request -------->|                          |
  |<-- pdf-convert-response -------|                          |
  |                                |                          |
  |-- file-present-start --------->|                          |
  |                                |--- file-present-start -->|
  |                                |                          |
  |-- file-present-page ---------->|                          |
  |                                |--- file-present-page --->|
  |                                |                          |
  |-- file-present-navigate ------>| (후발 참가자)              |
  |                                |<-- file-present-request -|
  |                                |                          |
  |<-- file-present-request -------|                          |
  |-- file-present-state --------->|                          |
  |                                |--- file-present-state -->|
```

### `room-join` 핸들러

```javascript
function handleRoomJoin(clientId, dataStr) {
    const data = JSON.parse(dataStr);
    const room = rooms.get(data.roomId);

    // 검증
    if (!room) {
        sendError(clientId, `Room ${data.roomId} not found`);
        return;
    }

    if (room.playerCount >= room.maxPlayers) {
        sendError(clientId, 'Room is full');
        return;
    }

    // 클라이언트 상태 업데이트
    const client = clients.get(clientId);
    client.roomId = data.roomId;
    client.playerName = data.playerName;

    room.playerCount++;

    // 해당 방에만 브로드캐스트
    broadcastToRoom(clientId, {
        type: 'room-join',
        senderId: clientId,
        data: JSON.stringify(data)
    });

    // 전체 방 목록 업데이트
    broadcastRoomList();
}
```

---

## 프로덕션 배포

### 사전 요구 사항

- Node.js 18+ LTS
- SSL 인증서 (wss://용)
- 공인 IP가 있는 서버
- TURN 서버 (음성 채팅용, 권장)

### 1단계: 서버 준비

```bash
# 서버에서 (Linux)
sudo apt update
sudo apt install nodejs npm nginx

# 프로젝트 복제
git clone <your-repo>
cd WebSocket_VR/Server

# 의존성 설치
npm install
```

### 2단계: 환경 변수

`.env` 파일 생성 (현재는 포트만 필요):

```bash
# 서버
PORT=8080
```

### 3단계: Nginx를 사용한 SSL 설정 (리버스 프록시)

```nginx
# /etc/nginx/sites-available/vr-meeting
server {
    listen 443 ssl;
    server_name your-domain.com;

    ssl_certificate /etc/letsencrypt/live/your-domain.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/your-domain.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}

# HTTP를 HTTPS로 리다이렉트
server {
    listen 80;
    server_name your-domain.com;
    return 301 https://$server_name$request_uri;
}
```

```bash
# 사이트 활성화
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl restart nginx

# SSL 인증서 발급 (Let's Encrypt)
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

### 4단계: Systemd 서비스

```ini
# /etc/systemd/system/vr-meeting.service
[Unit]
Description=VR Meeting WebSocket Server
After=network.target

[Service]
Type=simple
User=www-data
WorkingDirectory=/path/to/WebSocket_VR/Server
ExecStart=/usr/bin/node server.js
Restart=always
RestartSec=10
Environment=NODE_ENV=production
EnvironmentFile=/path/to/WebSocket_VR/Server/.env

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable vr-meeting
sudo systemctl start vr-meeting
sudo systemctl status vr-meeting
```

### 5단계: Unity 설정

Unity에서 `VRNetworkManager` 수정:

```csharp
// Inspector에서 serverUrl 변경
serverUrl = "wss://your-domain.com";

// 보안 활성화
enforceSecureConnection = true;
```

---

## 보안

### 현재 구현된 보안

| 항목 | 구현 |
|------|------|
| ID 강제 | `message.senderId = clientId` (서버 측 강제) |
| 방 격리 | `broadcastToRoom`이 roomId로 필터링 |
| 킥 권한 | `room.hostId === clientId` 확인 |
| 방 업데이트 권한 | `room.hostId === clientId` 확인 |
| 방 용량 | `playerCount >= maxPlayers` 시 거부 |
| 타임아웃 | 60초 후 자동 연결 해제 |
| JSON 유효성 | 모든 핸들러에서 try/catch |
| WebSocket 상태 | 전송 전 `readyState === OPEN` 확인 |
| 속도 제한 (클라이언트) | Unity 측 토큰 버킷 60 msg/s |

### 프로덕션 체크리스트

| 항목 | 상태 | 조치 |
|------|------|------|
| TLS/SSL | 필수 | 유효한 인증서로 `wss://` 사용 |
| TURN 서버 | 권장 | 개인 TURN 서버 사용 (Twilio/Xirsys) |
| 속도 제한 (서버) | 미구현 | 서버 측 속도 제한 추가 필요 |
| 메시지 크기 제한 | 미구현 | 대용량 페이로드 제한 필요 |
| 입력 검증 | 기본적 | 플레이어 이름 등 살균 필요 |

### 개인 TURN 설정 (VoiceChatManager.cs)

```csharp
// Unity Inspector에서
useCustomTurnServer = true;
customTurnUrl = "turn:your-turn.com:3478";
customTurnUsername = "your_user";
customTurnCredential = "your_secret";
enableTurnTcp = true;  // 제한적인 방화벽용
```

---

## 문제 해결

### 일반적인 문제

| 증상 | 가능한 원인 | 해결책 |
|------|-------------|--------|
| "Welcome timeout" | 서버에 접근할 수 없음 | URL, 포트, 방화벽 확인 |
| 잦은 연결 끊김 | 하트비트 타임아웃 | 네트워크 안정성 확인 |
| 플레이어 간 오디오 없음 | TURN 서버 누락 | TURN 서버 추가 |
| 메시지 수신 안됨 | 잘못된 roomId | `broadcastToRoom` 필터링 확인 |
| 속도 제한 경고 | 60 msg/s 초과 | 메시지 빈도 최적화 |

### 서버 로그

```bash
# 실시간 로그 보기
sudo journalctl -u vr-meeting -f

# 일반적인 로그
[Connect] Client a1b2c3d4...
[Room] Created: XYZ789
[Room] Join: e5f6g7h8 -> XYZ789
[Kick] Host a1b2c3d4 kicked e5f6g7h8 from XYZ789
[Status] 2 clients | 1 rooms
[Timeout] Client a1b2c3d4...
[Disconnect] Client a1b2c3d4...
```

### 연결 테스트

```javascript
// wscat으로 빠른 테스트
npm install -g wscat
wscat -c ws://localhost:8080

// 테스트 메시지 전송
> {"type":"room-list-request","data":""}
< {"type":"room-list","senderId":"server","data":"{\"rooms\":[]}"}
```

---

## 서버 모니터링

서버는 60초마다 통계를 표시합니다:

```
[Status] 3 clients | 2 rooms
```

고급 모니터링을 위해 다음을 고려하세요:
- PM2 (`pm2 monit`)
- Prometheus + Grafana
- 업타임 모니터링 (UptimeRobot, Pingdom)

---

## Unity 스크립트 통계

| 스크립트 | 줄 수 | 역할 |
|----------|-------|------|
| VRNetworkManager.cs | 460 | WebSocket 클라이언트, 연결, 속도 제한 |
| VRRoomManager.cs | 931 | 방, 플레이어, 존, 아바타 동기화 |
| VRGameManager.cs | 1888 | 스폰, 30Hz 동기화, 보간, 텔레포트 |
| VoiceChatManager.cs | 1139 | WebRTC 메시, 공간 오디오, 푸시투톡 |
| BootstrapManager.cs | 292 | 씬 흐름, XR 초기화, 싱글톤 |
| AvatarCustomization.cs | 315 | 색상, 사용자 이름, UI |
| DebugManager.cs | 169 | 카테고리별 로깅 |
| LaserPointer.cs | 338 | 네트워크 레이저 포인터 10Hz |
| 화이트보드 (12개 파일) | ~4158 | 그리기, UI, 네트워크, 지우개 |
| 공유 (8개 파일) | ~4185 | 화면, 파일, 프레젠테이션 |
| UI/메뉴 (~14개 파일) | ~2000+ | 메인 메뉴 + VR 인게임 UI |
| VR 모듈 (~10개 파일) | ~2000+ | 컨트롤러, 트래킹, 입력 |
| **합계** | **~61개 파일** | **~28,765줄** |

---

## 요약

1. **서버**는 클라이언트 간 메시지를 라우팅하는 WebSocket 허브입니다 (46개 명시적 메시지 타입)
2. **방**은 메시지를 격리합니다 (화이트보드, 위치, 화면 공유, 파일 공유)
3. **WebRTC**는 P2P지만 시그널링은 서버를 통과합니다 (음성 + 화면 공유)
4. **PDF 변환**은 서버 측에서 처리됩니다 (30분 캐시)
5. **프로덕션에서**: SSL 필수, 개인 TURN 권장, 활성 모니터링

질문이 있으시면 소스 코드를 참조하세요:
- 서버: `Server/server.js`
- 클라이언트: `Assets/Scrips/Network/VRNetworkManager.cs`

---

## 변경 이력

| 날짜 | 버전 | 설명 |
|------|------|------|
| 2025-01-26 | 1.0 | 초기 문서 |
| 2026-02-02 | 2.0 | 전체 업데이트: 줄 수 수정 (887줄), 46개 메시지 타입 추가, 파일 프레젠테이션/PDF/인터랙티브 오브젝트/화면 WebRTC 추가, Unity 통계 업데이트 |
| 2026-02-02 | 2.1 | 데이터베이스 섹션 제거 (Phase 3 미구현) |
