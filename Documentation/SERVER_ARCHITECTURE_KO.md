# WebSocket 서버 아키텍처 - VR 미팅룸

이 문서는 WebSocket 서버의 작동 방식, Unity 프로젝트와의 연결 방법, 교환되는 메시지, 원격 서버 배포 방법을 설명합니다.

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
| VRRoomManager    |                                     | MariaDB          |
| VRGameManager    |                                     | (auth.js/db.js)  |
| VoiceChatManager |                                     |                  |
+------------------+                                     +------------------+
```

### 기술 스택

| 구성 요소 | 기술 | 파일 |
|-----------|------|------|
| WebSocket 서버 | Node.js + ws@8.14.2 | `server.js` |
| WebSocket 클라이언트 | NativeWebSocket (Unity) | `VRNetworkManager.cs` |
| 인증 | bcrypt + MariaDB | `auth.js`, `db.js` |
| 음성 (WebRTC) | Unity.WebRTC | `VoiceChatManager.cs` |

---

## 서버 아키텍처

### 서버 파일 구조

```
Server/
├── server.js           # 메인 WebSocket 서버 (1047줄)
├── auth.js             # 인증 관리 (bcrypt)
├── db.js               # MariaDB 연결 풀
├── filePresentation.js # PDF 변환 (선택사항)
├── package.json        # npm 의존성
└── server.test.js      # 단위 테스트
```

### server.js - 주요 구성 요소

#### 1. 초기화 (1-35줄)

```javascript
const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');

const PORT = process.env.PORT || 8080;
const HEARTBEAT_INTERVAL = 30000;  // 30초

// 메모리 저장소
const clients = new Map();  // clientId -> { ws, roomId, playerName, lastHeartbeat }
const rooms = new Map();    // roomId -> RoomInfo
```

#### 2. 연결 관리 (40-87줄)

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

#### 3. 메시지 라우팅 (93-263줄)

메인 `switch`문이 각 메시지 타입을 해당 핸들러로 라우팅:

| 카테고리 | 메시지 타입 | 동작 |
|----------|-------------|------|
| **방 생명주기** | `room-available`, `room-closed`, `room-join`, `room-leave`, `room-update` | 방 CRUD |
| **VR 위치** | `vr-position`, `position` | 방으로 브로드캐스트 |
| **오브젝트** | `obj-sync`, `obj-state` | 방으로 브로드캐스트 |
| **화이트보드** | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request/state` | 브로드캐스트 또는 P2P |
| **WebRTC 음성** | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | P2P |
| **화면 공유** | `screen-share-start/stop/frame` | 방으로 브로드캐스트 |
| **파일 공유** | `file-announce`, `file-chunk`, `file-complete` | 방으로 브로드캐스트 |
| **인증** | `auth-register`, `auth-login`, `auth-update-profile` | 전용 핸들러 |

#### 4. 핵심 함수: `broadcastToRoom` (948-976줄)

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

#### 5. 하트비트와 타임아웃 (1015-1032줄)

```javascript
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
            console.log(`[SERVER] Client timeout: ${clientId}`);
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
}, HEARTBEAT_INTERVAL);  // 30초마다
```

---

## Unity <-> 서버 연결

### Unity 측: VRNetworkManager.cs

#### 설정 (Unity Inspector)

| 매개변수 | 기본값 | 설명 |
|----------|--------|------|
| `serverUrl` | `ws://localhost:8080` | WebSocket 서버 URL |
| `enforceSecureConnection` | `false` | 프로덕션에서 wss:// 강제 |
| `autoReconnect` | `true` | 자동 재연결 |
| `welcomeTimeout` | `5초` | welcome 메시지 타임아웃 |
| `maxMessagesPerSecond` | `60` | 속도 제한 |

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
// VRNetworkManager.cs - 176-188줄
if (_isReconnecting && autoReconnect)
{
    _reconnectTimer -= Time.deltaTime;
    if (_reconnectTimer <= 0f)
    {
        _isReconnecting = false;
        _reconnectAttempts++;
        ConnectAsync();
    }
}

// 지연 계산: 1초 -> 2초 -> 4초 -> 8초 -> ... -> 최대 30초
_currentReconnectDelay = Mathf.Min(
    _currentReconnectDelay * backoffMultiplier,
    maxReconnectDelay
);
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
| `room-closed` | 호스트 -> 전체 | `{roomId}` |

#### 3. VR 동기화 (30Hz)

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `vr-position` | 클라이언트 -> 방 | VRPositionData 구조 참조 |

```csharp
// VRPositionData (VRGameManager.cs)
{
    "roomId": "ABC123",
    "roomType": 1,  // 0=로비, 1=RoomA, 2=RoomB
    // 몸
    "posX": 1.234, "posY": 0.0, "posZ": -5.678,
    "rotY": 45.0,
    // 머리 (월드 스페이스)
    "headPosX": 1.234, "headPosY": 1.7, "headPosZ": -5.678,
    "headRotX": 0.0, "headRotY": 0.707, "headRotZ": 0.0, "headRotW": 0.707,
    // 손 (월드 스페이스) - 0 = 데스크톱 모드
    "leftHandPosX": ..., "leftHandRotX": ...,
    "rightHandPosX": ..., "rightHandRotX": ...
}
```

#### 4. WebRTC 시그널링 (P2P)

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `webrtc-offer` | 클라이언트 -> 클라이언트 | `{targetId, sdp}` |
| `webrtc-answer` | 클라이언트 -> 클라이언트 | `{targetId, sdp}` |
| `webrtc-ice-candidate` | 클라이언트 -> 클라이언트 | `{targetId, candidate, sdpMid, sdpMLineIndex}` |

#### 5. 화이트보드

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `whiteboard-batch` | 클라이언트 -> 방 | `{whiteboardId, roomId, r/g/b/a, penSize, pointsFlat: [u,v,...]}` |
| `whiteboard-clear` | 클라이언트 -> 방 | `{whiteboardId, roomId}` |
| `whiteboard-request` | 클라이언트 -> 방 | `{whiteboardId, roomId}` |
| `whiteboard-state` | 클라이언트 -> 클라이언트 | `{targetId, textureData (base64 PNG)}` |

#### 6. 화면 공유

| 타입 | 방향 | `data` 내용 |
|------|------|-------------|
| `screen-share-start` | 클라이언트 -> 방 | `{sharerId, sharerName}` |
| `screen-share-frame` | 클라이언트 -> 방 | `{imageData (base64 JPEG)}` |
| `screen-share-stop` | 클라이언트 -> 방 | `{sharerId}` |

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

### `room-join` 핸들러 (server.js:327-364)

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
- MariaDB 10.5+ (인증용)
- SSL 인증서 (wss://용)
- 공인 IP가 있는 서버

### 1단계: 서버 준비

```bash
# 서버에서 (Linux)
sudo apt update
sudo apt install nodejs npm mariadb-server nginx

# 프로젝트 복제
git clone <your-repo>
cd WebSocket_VR/Server

# 의존성 설치
npm install
```

### 2단계: 데이터베이스 설정

```sql
-- 데이터베이스 생성
CREATE DATABASE vr_meeting;
USE vr_meeting;

-- 사용자 테이블
CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(50),
    avatar_color VARCHAR(20),
    last_login DATETIME,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 전용 사용자 생성
CREATE USER 'vr_app'@'localhost' IDENTIFIED BY '강력한_비밀번호';
GRANT ALL PRIVILEGES ON vr_meeting.* TO 'vr_app'@'localhost';
FLUSH PRIVILEGES;
```

### 3단계: 환경 변수

`.env` 파일 생성:

```bash
# 서버
PORT=8080

# 데이터베이스
DB_HOST=localhost
DB_PORT=3306
DB_USER=vr_app
DB_PASSWORD=강력한_비밀번호
DB_NAME=vr_meeting
```

`.env` 로드를 위해 스크립트 수정:

```javascript
// server.js 맨 위에 추가
require('dotenv').config();
```

### 4단계: Nginx를 사용한 SSL 설정 (리버스 프록시)

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

### 5단계: Systemd 서비스

```ini
# /etc/systemd/system/vr-meeting.service
[Unit]
Description=VR Meeting WebSocket Server
After=network.target mariadb.service

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

### 6단계: Unity 설정

Unity에서 `VRNetworkManager` 수정:

```csharp
// Inspector에서 serverUrl 변경
serverUrl = "wss://your-domain.com";

// 보안 활성화
enforceSecureConnection = true;
```

---

## 보안

### 프로덕션 체크리스트

| 항목 | 상태 | 조치 |
|------|------|------|
| TLS/SSL | 필수 | 유효한 인증서로 `wss://` 사용 |
| DB 비밀번호 | 필수 | 기본값 변경 |
| TURN 서버 | 권장 | 개인 TURN 서버 사용 (Twilio/Xirsys) |
| 속도 제한 | 포함됨 | 클라이언트당 60 msg/s (설정 가능) |
| JSON 검증 | 포함됨 | 오류 처리가 있는 `TryDeserialize` |

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

### 서버 로그

```bash
# 실시간 로그 보기
sudo journalctl -u vr-meeting -f

# 일반적인 로그
[SERVER] WebSocket server started on port 8080
[SERVER] Client connected: abc-123-def-456
[SERVER] Room created: XYZ789 by abc-123-def-456
[Room:XYZ789] whiteboard-batch from abc-123 -> 2 clients
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
[SERVER] 3 clients | Rooms: ABC123(2), XYZ789(1)
```

고급 모니터링을 위해 다음을 고려하세요:
- PM2 (`pm2 monit`)
- Prometheus + Grafana
- 업타임 모니터링 (UptimeRobot, Pingdom)

---

## 요약

1. **서버**는 클라이언트 간 메시지를 라우팅하는 WebSocket 허브입니다
2. **방**은 메시지를 격리합니다 (화이트보드, 위치, 화면 공유)
3. **WebRTC**는 P2P지만 시그널링은 서버를 통과합니다
4. **프로덕션에서**: SSL 필수, 개인 TURN 권장, 활성 모니터링

질문이 있으시면 소스 코드를 참조하세요:
- 서버: `Server/server.js`
- 클라이언트: `Assets/Scrips/Network/VRNetworkManager.cs`
