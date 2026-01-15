# 서버 문서 - VR 회의 플랫폼

## 개요

이 플랫폼은 다음을 사용하는 VR 멀티플레이어 회의 애플리케이션입니다:
- **클라이언트**: Unity 6000.2.14f1 (Quest, PCVR, 데스크톱)
- **서버**: Node.js + WebSocket (포트 8080)
- **데이터베이스**: MariaDB (인증, 사용자 데이터 저장)
- **음성**: WebRTC (메시 P2P)

---

## 1. 네트워크 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                        NODE.JS 서버                              │
│                         (포트 8080)                              │
├─────────────────────────────────────────────────────────────────┤
│  WebSocket 서버                                                  │
│  ├── Clients Map: clientId -> {ws, roomId, playerName}         │
│  ├── Rooms Map: roomId -> RoomInfo                             │
│  └── 메시지 라우터 (type별 switch)                              │
├─────────────────────────────────────────────────────────────────┤
│  MariaDB 연결 (auth.js)                                         │
│  └── 테이블: users (인증)                                       │
└─────────────────────────────────────────────────────────────────┘
          │                    │                    │
          ▼                    ▼                    ▼
    ┌─────────┐          ┌─────────┐          ┌─────────┐
    │ 클라이언트│          │ 클라이언트│          │ 클라이언트│
    │  Unity  │◄────────►│  Unity  │◄────────►│  Unity  │
    │  (VR)   │  WebRTC  │(데스크톱)│  WebRTC  │  (VR)   │
    └─────────┘  (음성)  └─────────┘  (음성)  └─────────┘
```

### 데이터 흐름
1. **WebSocket**: 모든 메시지가 서버를 통해 전달됨 (라우팅, 브로드캐스트)
2. **WebRTC**: 음성을 위한 P2P 직접 연결 (메시 토폴로지)
3. **MariaDB**: 인증만 담당 (로그인/회원가입)

---

## 2. 메시지 형식

모든 WebSocket 메시지는 다음 JSON 형식을 따릅니다:

```json
{
  "type": "message-type",
  "senderId": "client-uuid",
  "data": "{\"key\":\"value\"}"  // JSON 문자열화
}
```

> **중요**: `data` 필드는 Unity JsonUtility 호환성을 위해 항상 JSON 문자열입니다 (중첩 객체 아님).

---

## 3. 클라이언트 → 서버 메시지

### 3.1 연결 및 인증

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `auth-register` | `{username, email, password, displayName}` | 신규 사용자 등록 |
| `auth-login` | `{username, password}` | 사용자 로그인 |
| `auth-update-profile` | `{displayName, avatarColor}` | 프로필 업데이트 |

### 3.2 룸 관리

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `room-available` | `{roomId, hostId, roomName, roomType, maxPlayers}` | 호스트가 룸 생성 알림 |
| `room-join` | `{roomId, playerId, playerName, colorR, colorG, colorB}` | 플레이어 룸 참가 |
| `room-leave` | `{roomId, playerId}` | 플레이어 룸 퇴장 |
| `room-list-request` | `{}` | 룸 목록 요청 |
| `room-teleport` | `{roomId, playerId, targetRoomType}` | 구역 변경 |
| `room-closed` | `{roomId}` | 호스트가 룸 종료 |

### 3.3 VR 동기화 (30 Hz)

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `vr-position` | 아래 구조 참조 | 신체/머리/손 위치 |

```json
{
  "roomId": "ABCDEF",
  "roomType": 1,
  "posX": 0.0, "posY": 1.0, "posZ": 0.0,
  "rotY": 45.0,
  "headPosX": 0.0, "headPosY": 1.7, "headPosZ": 0.0,
  "headRotX": 0.0, "headRotY": 0.0, "headRotZ": 0.0, "headRotW": 1.0,
  "leftHandPosX": -0.3, "leftHandPosY": 1.0, "leftHandPosZ": 0.2,
  "leftHandRotX": 0.0, "leftHandRotY": 0.0, "leftHandRotZ": 0.0, "leftHandRotW": 1.0,
  "rightHandPosX": 0.3, "rightHandPosY": 1.0, "rightHandPosZ": 0.2,
  "rightHandRotX": 0.0, "rightHandRotY": 0.0, "rightHandRotZ": 0.0, "rightHandRotW": 1.0
}
```

### 3.4 WebRTC 시그널링 (음성)

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `webrtc-offer` | `{targetId, sdp}` | SDP 오퍼 (개시자) |
| `webrtc-answer` | `{targetId, sdp}` | SDP 응답 |
| `webrtc-ice-candidate` | `{targetId, candidate, sdpMid, sdpMLineIndex}` | ICE 후보 |

### 3.5 화이트보드

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `whiteboard-batch` | `{whiteboardId, roomId, r, g, b, a, penSize, pointsFlat[]}` | 그리기 획 |
| `whiteboard-clear` | `{whiteboardId, roomId, senderId}` | 보드 지우기 |
| `whiteboard-request` | `{whiteboardId, roomId, requesterId}` | 상태 요청 (늦은 참가) |

### 3.6 화면 공유

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `screen-share-start` | `{roomId, whiteboardId, sharerId, sharerName, width, height}` | 공유 시작 |
| `screen-share-frame` | `{roomId, whiteboardId, sharerId, imageData, frameIndex}` | JPEG base64 프레임 |
| `screen-share-stop` | `{roomId, whiteboardId, sharerId}` | 공유 종료 |
| `screen-share-request` | `{roomId, whiteboardId, requesterId}` | 상태 요청 |

---

## 4. 서버 → 클라이언트 메시지

### 4.1 연결

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `welcome` | `{senderId: "uuid"}` | 클라이언트 ID 할당 |
| `peer-connected` | `{senderId: "peer-uuid"}` | 새 피어 연결됨 |
| `peer-disconnected` | `{senderId: "peer-uuid"}` | 피어 연결 해제됨 |

### 4.2 인증

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `auth-register-response` | `{success, error, userId, username}` | 등록 결과 |
| `auth-login-response` | `{success, error, userId, username, email, displayName, avatarColor}` | 로그인 결과 |
| `auth-update-response` | `{success, error}` | 업데이트 결과 |

### 4.3 룸

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `room-list` | `{rooms: [{roomId, hostId, roomName, playerCount, maxPlayers}]}` | 사용 가능한 룸 목록 |
| `room-welcome` | `{roomId, roomType, players: [{playerId, playerName, isHost}]}` | 참가 확인 + 플레이어 목록 |

### 4.4 화이트보드

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `whiteboard-state` | `{whiteboardId, roomId, textureData, width, height}` | 전체 상태 (PNG base64) |

### 4.5 화면 공유

| 타입 | 페이로드 | 설명 |
|------|---------|------|
| `screen-share-state` | `{roomId, whiteboardId, isSharing, sharerId, sharerName}` | 공유 상태 |

---

## 5. 메시지 라우팅

서버는 범위에 따라 메시지를 라우팅합니다:

| 범위 | 동작 | 해당 메시지 |
|------|------|------------|
| **글로벌** | 모든 클라이언트에 브로드캐스트 | `welcome`, `peer-*`, `room-available` |
| **룸** | 같은 룸의 클라이언트에만 브로드캐스트 | `vr-position`, `whiteboard-*`, `screen-share-*` |
| **1:1** | 특정 클라이언트에만 전송 | `webrtc-*`, `auth-*-response` |

### 룸 브로드캐스트 함수 (의사 코드)

```javascript
function broadcastToRoom(senderId, message) {
  const senderRoom = clients.get(senderId).roomId;

  for (const [clientId, client] of clients) {
    if (clientId !== senderId &&
        client.roomId === senderRoom &&
        client.ws.readyState === OPEN) {
      client.ws.send(JSON.stringify(message));
    }
  }
}
```

---

## 6. 데이터베이스 구조 (MariaDB)

### 테이블 `users`

```sql
CREATE TABLE users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  username VARCHAR(50) UNIQUE NOT NULL,
  email VARCHAR(100) UNIQUE NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  display_name VARCHAR(100),
  avatar_color VARCHAR(20) DEFAULT '#3498db',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  last_login TIMESTAMP NULL,

  INDEX idx_username (username),
  INDEX idx_email (email)
);
```

### 테이블 `rooms` (제안 - 미구현)

```sql
CREATE TABLE rooms (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_code VARCHAR(6) UNIQUE NOT NULL,
  room_name VARCHAR(100),
  host_id INT NOT NULL,
  room_type ENUM('Lobby', 'MeetingRoomA', 'MeetingRoomB') DEFAULT 'Lobby',
  max_players INT DEFAULT 10,
  is_active BOOLEAN DEFAULT TRUE,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  closed_at TIMESTAMP NULL,

  FOREIGN KEY (host_id) REFERENCES users(id),
  INDEX idx_room_code (room_code),
  INDEX idx_active (is_active)
);
```

### 테이블 `room_participants` (제안 - 미구현)

```sql
CREATE TABLE room_participants (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  user_id INT NOT NULL,
  joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  left_at TIMESTAMP NULL,

  FOREIGN KEY (room_id) REFERENCES rooms(id),
  FOREIGN KEY (user_id) REFERENCES users(id),
  INDEX idx_room_user (room_id, user_id)
);
```

### 테이블 `meetings` (제안 - 히스토리용)

```sql
CREATE TABLE meetings (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  title VARCHAR(200),
  started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  ended_at TIMESTAMP NULL,
  recording_url VARCHAR(500) NULL,

  FOREIGN KEY (room_id) REFERENCES rooms(id)
);
```

---

## 7. 서버 설정

### 환경 변수

```env
# 서버
PORT=8080
NODE_ENV=production

# 데이터베이스
DB_HOST=localhost
DB_PORT=3306
DB_USER=vr_meeting_user
DB_PASSWORD=secure_password_here
DB_NAME=vr_meeting

# 보안
BCRYPT_SALT_ROUNDS=10
```

### Node.js 의존성

```json
{
  "dependencies": {
    "ws": "^8.x",
    "mysql2": "^3.x",
    "bcrypt": "^5.x",
    "uuid": "^9.x",
    "dotenv": "^16.x"
  }
}
```

---

## 8. 배포

### 요구 사항
- Node.js 18+ LTS
- MariaDB 10.6+
- 개방 포트: 8080 (WebSocket), 3306 (MariaDB)

### 단계

1. **저장소 복제**
```bash
git clone <repo-url>
cd Server
```

2. **의존성 설치**
```bash
npm install
```

3. **데이터베이스 설정**
```bash
mysql -u root -p < schema.sql
```

4. **환경 변수 설정**
```bash
cp .env.example .env
# .env를 프로덕션 값으로 편집
```

5. **서버 시작**
```bash
# 개발
npm run dev

# 프로덕션 (PM2 사용)
pm2 start server.js --name vr-meeting-server
```

### Nginx 설정 (리버스 프록시)

```nginx
server {
    listen 443 ssl;
    server_name meeting.company.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    location /ws {
        proxy_pass http://localhost:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400;
    }
}
```

---

## 9. 보안

### 구현됨
- bcrypt로 비밀번호 해시 (10 라운드)
- 프로덕션에서 WSS를 통한 WebSocket (리버스 프록시 경유)

### 구현 예정 (3단계)
- [ ] 인증된 세션을 위한 JWT
- [ ] 메시지 속도 제한
- [ ] 서버 측 페이로드 검증
- [ ] 음성용 E2E 암호화 (SRTP)
- [ ] CORS 설정
- [ ] 감사 로그

---

## 10. 모니터링

### 권장 로그
- 클라이언트 연결/연결 해제
- 룸 생성/종료
- 인증 오류
- 지연 시간 메트릭

### 모니터링할 메트릭
- 연결된 클라이언트 수
- 활성 룸 수
- WebSocket 대역폭
- 평균 메시지 지연 시간

---

## 부록: 룸 코드

룸 코드는 다음으로 생성됩니다:
- 6자리 영숫자
- 문자셋: `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (O/0, I/1 제외)
- 예시: `ABCDEF`, `X7K9M2`

---

*문서 생성일 2026/01/15 - 버전 1.0*
