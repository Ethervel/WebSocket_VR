# VR 회의 플랫폼 문서

## 목차

| 파일 | 설명 |
|------|------|
| `SERVER_DOCUMENTATION_KO.md` | 서버 전체 문서 (아키텍처, 메시지, 배포) |
| `SEQUENCE_DIAGRAMS_KO.md` | 주요 흐름 시퀀스 다이어그램 |
| `schema.sql` | MariaDB 데이터베이스 초기화 SQL 스크립트 |

## 빠른 시작

### 1. 데이터베이스
```bash
mysql -u root -p < schema.sql
```

### 2. Node.js 서버
```bash
cd Server
npm install
cp .env.example .env  # 변수 설정
npm start
```

### 3. Unity 클라이언트
- Unity 6000.2.14f1에서 프로젝트 열기
- `VRNetworkManager.serverUrl`을 서버 주소로 설정
- Quest/PCVR/데스크톱용 빌드

## 아키텍처 요약

```
Unity 클라이언트 ◄──── WebSocket ────► Node.js 서버 ◄────► MariaDB
     │                                    │
     └──────── WebRTC P2P (음성) ─────────┘
```

## 포트

| 서비스 | 포트 | 프로토콜 |
|--------|------|----------|
| WebSocket 서버 | 8080 | WS/WSS |
| MariaDB | 3306 | TCP |
| STUN (Google) | 19302 | UDP |
