# VRMeet 완벽 가이드
## 가상 회의실 애플리케이션

---

## 목차

1. [프로젝트 소개](#1-프로젝트-소개)
2. [구현된 기능](#2-구현된-기능)
3. [기술 아키텍처](#3-기술-아키텍처)
4. [설치 및 실행](#4-설치-및-실행)
   - [필수 요구사항](#41-필수-요구사항)
   - [PC (Desktop) 실행](#42-pc-desktop-실행)
   - [PC VR (PCVR) 실행](#43-pc-vr-pcvr-실행)
   - [Meta Quest 실행](#44-meta-quest-실행)
5. [애플리케이션 사용법](#5-애플리케이션-사용법)
6. [조작법](#6-조작법)
7. [설정 및 옵션](#7-설정-및-옵션)
8. [문제 해결](#8-문제-해결)

---

## 1. 프로젝트 소개

### 사용 기술

| 구성요소 | 기술 |
|----------|------|
| 게임 엔진 | Unity 6000.2.14f1 |
| 실시간 통신 | WebSocket (NativeWebSocket) |
| 음성 채팅 | WebRTC (P2P) |
| VR 지원 | OpenXR (멀티 헤드셋) |
| 백엔드 서버 | Node.js |
| 데이터베이스 | MariaDB (선택사항) |

### 지원 플랫폼

- **Windows Desktop** - 키보드/마우스, VR 헤드셋 없이 사용
- **PCVR** - Meta Quest Link, SteamVR (Valve Index, HTC Vive 등)
- **Meta Quest** - 독립 실행형 (Quest 2, Quest 3, Quest Pro)

---

## 2. 구현된 기능

### 통신 및 멀티플레이어

| 기능 | 설명 |
|------|------|
| **WebSocket 연결** | 자동 재연결 기능이 있는 실시간 연결 |
| **룸 시스템** | 6자리 코드로 방 생성 및 참가 |
| **VR 동기화** | 30Hz로 위치와 회전 동기화 |
| **음성 채팅** | 3D 공간 오디오가 적용된 WebRTC 음성 통신 |
| **Push-to-talk** | V키로 마이크 활성화 옵션 |

<!-- [SCREENSHOT 추천: 여러 플레이어가 로비에 있는 모습] -->

### 협업 도구

| 기능 | 설명 |
|------|------|
| **화이트보드** | 실시간 동기화되는 다중 사용자 그리기 |
| **화면 공유** | 화면 스트리밍 (854x480 @ 3fps) |
| **파일 공유** | PDF, 이미지, 문서 전송 (최대 10MB) |
| **프레젠테이션** | 화이트보드에 파일 표시 |
| **레이저 포인터** | 요소를 가리키는 포인터 (VR: A버튼, Desktop: L키) |

<!-- [SCREENSHOT 추천: 화이트보드에 그림을 그리는 모습] -->
<!-- [SCREENSHOT 추천: 화면 공유가 화이트보드에 표시되는 모습] -->

### VR 기능

| 기능 | 설명 |
|------|------|
| **텔레포트** | 텔레포트로 이동 |
| **핸드 트래킹** | 손과 컨트롤러 추적 |
| **VR 메뉴** | 손목에 부착된 인터페이스 |
| **물체 잡기** | 상호작용 가능한 물체 잡기 |

<!-- [SCREENSHOT 추천: VR 손목 메뉴 UI] -->
<!-- [SCREENSHOT 추천: 텔레포트 레이캐스트] -->

### 개인화 및 인터페이스

| 기능 | 설명 |
|------|------|
| **아바타 커스터마이즈** | 아바타 색상 선택 |
| **이름표** | 머리 위에 표시되는 이름 |
| **메인 메뉴** | 옵션이 있는 시작 인터페이스 |
| **설정** | 오디오, 그래픽, VR, 컨트롤 |
| **인증** | 로그인/회원가입/게스트 모드 |

<!-- [SCREENSHOT 추천: 아바타 커스터마이즈 화면] -->
<!-- [SCREENSHOT 추천: 설정 메뉴] -->

### 녹화

| 기능 | 설명 |
|------|------|
| **비디오 캡처** | 회의 녹화 (1920x1080 @ 30fps) |
| **마커** | 시간 기준점 |
| **MP4 내보내기** | FFmpeg를 통한 변환 |

---

## 3. 기술 아키텍처

### 씬 구조

```
Bootstrap (Scene 0) - 영구 씬
    ├── VRNetworkManager     → WebSocket 관리
    ├── VRRoomManager        → 룸 관리
    ├── VRGameManager        → 플레이어 스폰
    ├── VoiceChatManager     → WebRTC 음성 채팅
    ├── SoundManager         → 사운드 및 앰비언스
    ├── AuthManager          → 인증
    └── MainMenuUI           → 메뉴 인터페이스

Meet (Scene 1) - 추가 로드 씬
    ├── Lobby                → 대기 공간
    ├── MeetingRoomA         → 회의실 A
    ├── MeetingRoomB         → 회의실 B
    ├── Whiteboards          → 화이트보드
    └── SpectatorCamera      → 녹화 카메라
```

<!-- [SCREENSHOT 추천: Unity Hierarchy에서 Bootstrap 씬 구조] -->

### 연결 흐름

```
실행 → 메인 메뉴 → 인증 → 서버 연결 → 로비 → 회의실
```

### 네트워크 아키텍처

```
┌─────────────────┐                      ┌─────────────────┐
│  Unity 클라이언트 │◄────WebSocket───────►│  Node.js 서버    │
│  (VR/Desktop)   │                      │   (port 8080)    │
└────────┬────────┘                      └─────────────────┘
         │
         │ WebRTC (P2P)
         │
┌────────▼────────┐
│  다른 클라이언트  │  ← 클라이언트 간 직접 음성 채팅
└─────────────────┘
```

---

## 4. 설치 및 실행

### 4.1 필수 요구사항

#### 필요 소프트웨어

| 소프트웨어 | 버전 | 필수 | 용도 |
|------------|------|------|------|
| Unity Hub + Unity | 6000.2.14f1 | 예 | 에디터/빌드 |
| Node.js | >= 16.0.0 | 예 | 서버 |
| Visual Studio | 2022 | 권장 | C# IDE |
| FFmpeg | 최신 | 녹화용 | 비디오 인코딩 |
| SteamVR | 최신 | PCVR용 | VR 런타임 |
| Oculus App | 최신 | Quest Link용 | VR 런타임 |

<!-- [SCREENSHOT 추천: Unity Hub에서 올바른 Unity 버전 선택] -->

#### 열어야 할 네트워크 포트

| 포트 | 프로토콜 | 용도 |
|------|----------|------|
| 8080 | TCP | WebSocket |
| 3478 | TCP/UDP | STUN/TURN (음성) |
| 49152-65535 | UDP | WebRTC 미디어 |

---

### 4.2 PC (Desktop) 실행

#### 1단계: 서버 시작 (LAN 서버 + PM2)

같은 네트워크(LAN)에 있는 서버에서 PM2로 Node.js 서버를 실행합니다.
클라이언트는 서버 IP로 접속합니다.

```
┌─────────────────┐         ┌─────────────────┐
│  서버 (Ubuntu)   │         │  클라이언트      │
│  192.168.1.100  │◄────────│  (PC/Quest)     │
│  PM2 + Node.js  │  LAN    │                 │
│  포트 8080      │         │                 │
└─────────────────┘         └─────────────────┘
```

##### 서버 설정 (Ubuntu/Linux)

**1. Node.js 설치:**
```bash
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs poppler-utils
```

**2. 프로젝트 복사:**
```bash
# Windows에서 서버로 복사 (PowerShell)
scp -r "D:\Test_project\WebSocket_VR\Server" user@192.168.1.100:~/vr-meeting/

# 또는 서버에서 직접 git clone
git clone <repo-url> ~/vr-meeting
```

**3. 의존성 설치:**
```bash
cd ~/vr-meeting/Server
npm install
```

**4. PM2 설치 및 설정:**
```bash
# PM2 전역 설치
sudo npm install -g pm2

# ecosystem.config.js 생성
nano ecosystem.config.js
```

```javascript
module.exports = {
  apps: [{
    name: 'vr-meeting',
    script: 'server.js',
    cwd: '/home/user/vr-meeting/Server',  // 경로 수정
    instances: 1,
    max_memory_restart: '500M',
    env: { NODE_ENV: 'production', PORT: 8080 },
    autorestart: true,
    watch: false
  }]
};
```

**5. PM2로 서버 시작:**
```bash
# 서버 시작
pm2 start ecosystem.config.js

# 상태 확인
pm2 status

# 부팅 시 자동 시작 설정
pm2 startup
# 출력된 sudo 명령어 복사하여 실행

pm2 save
```

<!-- [SCREENSHOT 추천: pm2 status 출력 화면] -->

**6. 방화벽 설정:**
```bash
sudo ufw allow 8080/tcp
sudo ufw enable
```

**7. 서버 IP 확인:**
```bash
ip addr show | grep "inet "
# 예: 192.168.1.100
```

##### PM2 명령어 요약

```bash
pm2 status              # 상태 확인
pm2 logs vr-meeting     # 로그 보기
pm2 restart vr-meeting  # 재시작
pm2 stop vr-meeting     # 중지
pm2 start vr-meeting    # 시작
```

##### Unity 설정

`VRNetworkManager` (Inspector)에서:
- `Server Url`: `ws://192.168.1.100:8080` (서버 IP로 변경)

<!-- [SCREENSHOT 추천: VRNetworkManager Inspector 설정] -->

#### 2단계: Unity Editor에서 실행

1. **Unity 6000.2.14f1**에서 프로젝트 열기
2. `Assets/Scenes/Bootstrap.unity` 씬 열기
3. `VRNetworkManager`의 `Server Url = ws://localhost:8080` 확인
4. **Play** 버튼 누르기
5. 데스크톱 모드로 자동 시작 (헤드셋 미감지 시)

<!-- [SCREENSHOT 추천: Unity Inspector에서 VRNetworkManager 설정] -->
<!-- [SCREENSHOT 추천: Bootstrap 씬이 열린 Unity Editor] -->

#### 3단계: Windows 실행 파일 빌드

1. **File → Build Settings**
2. 플랫폼: **Windows, Mac, Linux**
3. 아키텍처: **x86_64**
4. **Build** 클릭
5. 대상 폴더 선택
6. 생성된 실행 파일 실행

<!-- [SCREENSHOT 추천: Build Settings 창] -->

#### 데스크톱 모드 설정

VRNetworkManager (Inspector)에서:
- `Server Url`: `ws://localhost:8080` (로컬) 또는 `wss://your-server.com` (프로덕션)
- `Offline Mode`: 서버 없이 테스트하려면 체크

---

### 4.3 PC VR (PCVR) 실행

#### 호환 헤드셋

- Meta Quest (Link/Air Link 사용)
- Valve Index
- HTC Vive / Vive Pro
- Windows Mixed Reality
- 모든 OpenXR 호환 헤드셋

#### 1단계: VR 런타임 설정

**Meta Quest Link 사용 시:**
1. PC에 **Oculus** 앱 설치
2. USB-C 케이블(Link) 또는 Wi-Fi(Air Link)로 Quest 연결
3. Oculus 앱에서: Settings → General → OpenXR Runtime → **Set Oculus as active**

<!-- [SCREENSHOT 추천: Oculus 앱에서 OpenXR 런타임 설정] -->

**SteamVR 사용 시:**
1. Steam에서 **SteamVR** 설치
2. 헤드셋 연결
3. SteamVR에서: Settings → Developer → **Set SteamVR as OpenXR Runtime**

<!-- [SCREENSHOT 추천: SteamVR 설정에서 OpenXR 런타임 설정] -->

#### 2단계: Unity PCVR 설정

1. **Edit → Project Settings → XR Plug-in Management**
2. **Windows** 탭에서:
   - **OpenXR** 체크
   - OpenXR 클릭하여 Interaction Profiles 설정

<!-- [SCREENSHOT 추천: XR Plug-in Management 설정 창] -->

#### 3단계: 서버 시작

```bash
cd Server
npm run dev
```

#### 4단계: 애플리케이션 실행

**Unity Editor에서:**
1. VR 헤드셋 착용
2. Unity에서 **Play** 버튼 누르기
3. 헤드셋에서 애플리케이션 실행

**독립 실행형 빌드:**
1. **File → Build Settings**
2. 플랫폼: **Windows**
3. **Build and Run** 클릭
4. 헤드셋에서 애플리케이션 실행

---

### 4.4 Meta Quest 실행

#### Quest 필수 요구사항

- Meta Quest 2, Quest 3 또는 Quest Pro
- Meta Developer 계정 (무료)
- 배포용 USB-C 케이블 또는 Wi-Fi

#### 1단계: Unity Android/Quest 설정

1. **File → Build Settings**
2. **Android** 선택
3. **Switch Platform** 클릭

<!-- [SCREENSHOT 추천: Build Settings에서 Android 플랫폼 선택] -->

4. **Edit → Project Settings → XR Plug-in Management**
5. **Android** 탭에서:
   - **OpenXR** 체크
   - features에서 **Meta Quest Support** 추가

<!-- [SCREENSHOT 추천: Android용 XR Plug-in Management 설정] -->

6. **Edit → Project Settings → Player**
7. **Android** 탭에서:
   - Company Name: 회사명
   - Minimum API Level: **Android 10.0 (API level 29)**
   - Target API Level: **Automatic**
   - Scripting Backend: **IL2CPP**
   - Target Architectures: **ARM64**

<!-- [SCREENSHOT 추천: Player Settings Android 설정] -->

#### 2단계: Quest 개발자 모드 활성화

1. 스마트폰에서 **Meta Quest** 앱 열기
2. **Devices → [Quest 선택] → Settings → Developer Mode**로 이동
3. **Developer Mode** 활성화
4. Quest 재시작

<!-- [SCREENSHOT 추천: Meta Quest 앱에서 개발자 모드 설정 (스마트폰)] -->

#### 3단계: Quest를 PC에 연결

1. USB-C 케이블로 Quest를 PC에 연결
2. Quest를 착용하고 **USB 디버깅** 요청 수락
3. Unity에서: **File → Build Settings → Refresh**로 Quest가 목록에 표시되는지 확인

<!-- [SCREENSHOT 추천: Quest에서 USB 디버깅 허용 대화상자] -->

#### 4단계: Quest용 서버 설정

Quest가 서버에 접근할 수 있어야 합니다. 옵션:

**옵션 A - 로컬 네트워크 (테스트 권장):**

1. PC의 IP 주소 찾기:
   ```bash
   # Windows
   ipconfig
   # "IPv4 Address" 찾기 (예: 192.168.1.100)
   ```

2. Unity에서 `VRNetworkManager` 수정:
   - `Server Url`: `ws://192.168.1.100:8080`

3. PC와 Quest가 같은 Wi-Fi 네트워크에 있는지 확인

4. Windows 방화벽에서 포트 8080 열기:
   ```powershell
   # PowerShell (관리자 권한)
   netsh advfirewall firewall add rule name="VRMeet" dir=in action=allow protocol=TCP localport=8080
   ```

**옵션 B - 공용 서버 (프로덕션):**

1. VPS에 서버 배포 (DOCUMENTATION_STAGE.md 참조)
2. `wss://your-domain.com` 설정

#### 5단계: 빌드 및 배포

1. **File → Build Settings**
2. **Run Device**에 Quest가 있는지 확인
3. **Build and Run** 클릭
4. Unity가 컴파일하고 Quest에 설치
5. 애플리케이션 자동 시작

<!-- [SCREENSHOT 추천: Build Settings에서 Quest가 Run Device로 선택됨] -->

#### 6단계: Quest에서 애플리케이션 실행

설치 후:
1. Quest에서: **App Library → Unknown Sources**
2. **VRMeet** 찾아서 실행

<!-- [SCREENSHOT 추천: Quest의 Unknown Sources에서 VRMeet 앱] -->

---

## 5. 애플리케이션 사용법

### 시작하기

1. **로딩 화면** - 시스템 초기화
2. **메인 메뉴** - 옵션: Start, Options, Quit
3. **인증** - 로그인, 회원가입 또는 게스트 모드
4. **서버 연결** - 자동
5. **로비** - 대기 공간, 방 선택

<!-- [SCREENSHOT 추천: 로딩 화면] -->
<!-- [SCREENSHOT 추천: 메인 메뉴] -->
<!-- [SCREENSHOT 추천: 인증 화면 (로그인/회원가입)] -->
<!-- [SCREENSHOT 추천: 로비 전경] -->

### 방 참가/생성

**방 생성:**
1. 방 문으로 텔레포트 (Room A 또는 B)
2. 방이 자동으로 생성됨
3. 6자리 코드 생성
4. 다른 참가자에게 코드 공유

**방 참가:**
1. 인터페이스에 방 코드 입력
2. 해당 방으로 텔레포트

<!-- [SCREENSHOT 추천: 로비에서 Room A/B 문] -->
<!-- [SCREENSHOT 추천: 방 코드 입력 UI] -->

### 화이트보드 사용

**VR에서:**
1. 마커 잡기 (컨트롤러로 Grab)
2. 보드에 마커 끝 가까이 대기
3. 마커를 보드에 대고 그리기

**Desktop에서:**
1. 마우스로 보드 조준
2. 왼쪽 클릭으로 그리기
3. 인터페이스로 색상 변경

<!-- [SCREENSHOT 추천: VR에서 화이트보드에 그리는 모습] -->
<!-- [SCREENSHOT 추천: Desktop에서 화이트보드 UI] -->

### 음성 채팅

- **자동**: 마이크가 기본적으로 활성화
- **Push-to-talk**: V키(Desktop)를 눌러 말하기
- 오디오는 공간(3D) - 플레이어 위치에서 소리 발생

<!-- [SCREENSHOT 추천: 음성 채팅 활성화 표시 (말하고 있는 아이콘)] -->

### 화면 공유

1. VR 메뉴 열기 또는 메뉴 키 누르기
2. "화면 공유" 선택
3. 공유할 창 선택
4. 화이트보드에 화면 표시

<!-- [SCREENSHOT 추천: 화면 공유 선택 UI] -->
<!-- [SCREENSHOT 추천: 화이트보드에 표시된 공유 화면] -->

### 회의 녹화

> **참고:** 녹화 기능은 **호스트 전용**입니다. FFmpeg가 시스템에 설치되어 있어야 합니다.

**녹화 시작:**
1. VR 메뉴 열기 (또는 Desktop에서 메뉴 키)
2. **Recording** 탭 선택
3. **녹화 시작** 버튼 클릭
4. 녹화 중 표시가 나타남

**녹화 중 마커 추가:**
녹화 중 중요한 순간에 마커를 추가할 수 있습니다:
- **Important** - 중요한 내용
- **Question** - 질문
- **Todo** - 할 일
- **Idea** - 아이디어

마커는 타임라인에 저장되어 나중에 해당 시점으로 쉽게 이동할 수 있습니다.

**녹화 중지:**
1. **녹화 중지** 버튼 클릭
2. FFmpeg가 자동으로 프레임을 MP4로 인코딩
3. 파일이 `Recordings/` 폴더에 저장됨

**녹화 설정 (RecordingSettings):**

| 설정 | 기본값 | 설명 |
|------|--------|------|
| 해상도 | 1920x1080 | 출력 비디오 해상도 |
| 프레임레이트 | 30fps | 초당 프레임 수 |
| JPEG 품질 | 85 | 프레임 압축 품질 |
| 오디오 캡처 | true | 오디오 포함 여부 |
| 출력 폴더 | Recordings | 저장 위치 |

**출력 파일:**
```
Recordings/
├── recording_2026-02-27_14-30-00.mp4    # 최종 비디오
├── recording_2026-02-27_14-30-00.json   # 메타데이터 + 마커
└── frames/                               # (인코딩 후 삭제됨)
```

<!-- [SCREENSHOT 추천: 녹화 UI 패널] -->
<!-- [SCREENSHOT 추천: 마커 추가 버튼들] -->

**요구사항:**
- FFmpeg가 시스템 PATH에 설치되어 있어야 함
- SpectatorCamera가 Meet 씬에 있어야 함
- 호스트만 녹화 가능

---

## 6. 조작법

### VR 모드 (Quest / PCVR)

| 동작 | 컨트롤러 |
|------|----------|
| 텔레포트 | 조이스틱 + 트리거 |
| 보기 | 머리 회전 |
| 물체 잡기 | 그립 (측면 버튼) |
| 레이저 포인터 | A 버튼 |
| 메뉴 | 메뉴 / 스타트 버튼 |
| Push-to-talk | V키 (키보드) |

<!-- [SCREENSHOT 추천: VR 컨트롤러 버튼 매핑 다이어그램] -->

### Desktop 모드

| 동작 | 조작 |
|------|------|
| 앞/뒤 이동 | W / S |
| 좌/우 이동 | A / D |
| 달리기 | Shift |
| 보기 | 우클릭 + 마우스 |
| 그리기 | 좌클릭 |
| 레이저 포인터 | L |
| 메뉴 | Esc |
| Push-to-talk | V |

<!-- [SCREENSHOT 추천: 키보드 조작 다이어그램] -->

---

## 7. 설정 및 옵션

### VRNetworkManager (Inspector)

| 파라미터 | 설명 | 기본값 |
|----------|------|--------|
| Server Url | WebSocket 서버 주소 | ws://localhost:8080 |
| Reconnect Delay | 재연결 간격 | 2.0초 |
| Max Reconnect Attempts | 최대 재시도 횟수 | 5 |
| Offline Mode | 오프라인 모드 (디버그) | false |

<!-- [SCREENSHOT 추천: VRNetworkManager Inspector 설정] -->

### 인게임 옵션

**오디오:**
- 마스터 볼륨 (0-100%)
- 음성 볼륨 (0-100%)
- 마이크 선택

**그래픽:**
- 품질 (Low / Medium / High / Ultra)
- 해상도
- 전체 화면 모드

**VR:**
- 회전 모드: Snap (단계별) 또는 Smooth (연속)
- 스냅 각도: 15 / 30 / 45 / 90도
- 부드러운 회전 속도

**Desktop:**
- 마우스 감도
- Y축 반전

<!-- [SCREENSHOT 추천: 인게임 옵션 메뉴 각 탭] -->

---

## 8. 문제 해결

### 클라이언트가 서버에 연결되지 않음

| 확인 사항 | 해결책 |
|-----------|--------|
| 서버 시작됨? | Server/에서 `npm run dev` |
| 올바른 URL? | VRNetworkManager > Server Url 확인 |
| 방화벽? | 포트 8080 열기 |
| 같은 네트워크? (Quest) | PC와 Quest가 같은 Wi-Fi에 |

### 소리가 안 들림 (음성 채팅)

| 확인 사항 | 해결책 |
|-----------|--------|
| 마이크 허용됨? | Windows/Quest 권한 확인 |
| Push-to-talk 활성화? | V키를 눌러 말하기 |
| 같은 방? | 두 플레이어가 같은 방에 있어야 함 |
| TURN 설정됨? | 프로덕션에서 TURN 서버 설정 |

### VR이 시작되지 않음

| 확인 사항 | 해결책 |
|-----------|--------|
| 헤드셋 연결됨? | USB/Wi-Fi 연결 확인 |
| 런타임 활성화? | Oculus App 또는 SteamVR 실행 |
| OpenXR 설정됨? | Project Settings > XR Plug-in Management |

### 화이트보드가 동기화되지 않음

| 확인 사항 | 해결책 |
|-----------|--------|
| 같은 방? | 플레이어들이 같은 방에 있어야 함 |
| 서버 시작됨? | 서버 로그 확인 |
| WhiteboardDrawingSurface? | 보드에 컴포넌트 있는지 확인 |

### Quest: 빌드 실패

| 오류 | 해결책 |
|------|--------|
| "No Android SDK" | Unity Hub에서 Android Build Support 설치 |
| "Device not found" | 개발자 모드 + USB 디버깅 활성화 |
| "IL2CPP error" | Project Settings > Player > Scripting Backend = IL2CPP |

### 성능 (낮은 FPS)

| 원인 | 해결책 |
|------|--------|
| 높은 품질 | Options > Graphics에서 낮추기 |
| 너무 많은 플레이어 | 방당 10명으로 제한 |
| 녹화 활성화 | 비활성화하거나 품질 낮추기 |

---

## 연락처

**프로젝트:** VRMeet
**조직:** Rndp
**Unity 버전:** 6000.2.14f1

---

*가이드 생성일: 2026년 2월 27일*

---

## 스크린샷 권장 위치 요약

아래는 스크린샷을 추가하면 좋을 위치입니다:

### 필수 스크린샷 (High Priority)

| 위치 | 설명 | 파일명 제안 |
|------|------|-------------|
| 1. 프로젝트 소개 | 메인 메뉴 화면 | `main_menu.png` |
| 4.2 Desktop 실행 | Unity Editor에서 VRNetworkManager Inspector | `unity_network_manager.png` |
| 4.2 Desktop 실행 | 터미널에서 서버 실행 중 | `server_running.png` |
| 4.4 Quest 실행 | Build Settings에서 Android 선택 | `build_settings_android.png` |
| 4.4 Quest 실행 | XR Plug-in Management (Android) | `xr_plugin_android.png` |
| 5. 사용법 | 로비 전경 | `lobby_view.png` |
| 5. 사용법 | 화이트보드 사용 모습 | `whiteboard_drawing.png` |

### 권장 스크린샷 (Medium Priority)

| 위치 | 설명 | 파일명 제안 |
|------|------|-------------|
| 1. 프로젝트 소개 | 3가지 플랫폼 비교 | `platforms_comparison.png` |
| 2. 기능 | 여러 플레이어가 있는 모습 | `multiplayer_lobby.png` |
| 2. 기능 | VR 손목 메뉴 | `vr_wrist_menu.png` |
| 2. 기능 | 아바타 커스터마이즈 | `avatar_customization.png` |
| 4.3 PCVR | Oculus 앱 OpenXR 설정 | `oculus_openxr.png` |
| 4.4 Quest | Quest USB 디버깅 대화상자 | `quest_usb_debug.png` |
| 5. 사용법 | 화면 공유 화면 | `screen_share.png` |
| 6. 조작법 | VR 컨트롤러 다이어그램 | `vr_controls.png` |

### 선택 스크린샷 (Low Priority)

| 위치 | 설명 | 파일명 제안 |
|------|------|-------------|
| 3. 아키텍처 | Unity Hierarchy 구조 | `unity_hierarchy.png` |
| 5. 사용법 | 로딩 화면 | `loading_screen.png` |
| 5. 사용법 | 인증 화면 | `auth_screen.png` |
| 7. 설정 | 인게임 옵션 메뉴 | `options_menu.png` |

### 스크린샷 저장 위치 추천

```
DOCUMENTS/
└── images/
    ├── main_menu.png
    ├── lobby_view.png
    ├── whiteboard_drawing.png
    ├── unity_network_manager.png
    ├── build_settings_android.png
    └── ...
```

마크다운에서 이미지 삽입 방법:
```markdown
![메인 메뉴](images/main_menu.png)
```
