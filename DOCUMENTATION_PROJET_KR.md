# 기술 문서 - VRMeet
## 멀티플레이어 가상 회의실 애플리케이션

---

## 1. 프로젝트 개요

### 1.1 설명
**VRMeet**은 Unity 6000.2.14f1로 개발된 멀티플레이어 가상 회의실 애플리케이션입니다. 여러 사용자가 VR(가상현실) 또는 데스크톱 모드로 몰입형 3D 환경에서 만날 수 있습니다.

### 1.2 목표
- 몰입형 가상 회의 지원
- 멀티 플랫폼 지원 (Quest, PCVR, Desktop)
- 실시간 음성 통신
- 협업 도구 (화이트보드, 화면 공유, 파일 프레젠테이션)
- 사용자 인증 시스템

### 1.3 프로젝트 정보
| 항목 | 값 |
|------|-----|
| 제품명 | VrMeet |
| 회사 | Rndp |
| Unity 버전 | 6000.2.14f1 |
| 렌더 파이프라인 | Universal Render Pipeline (URP) 17.2.0 |
| 대상 플랫폼 | Meta Quest, PCVR, Windows Desktop |

---

## 2. 기술 스택

### 2.1 게임 엔진 및 프레임워크

#### Unity 6000.2.14f1
- 메인 게임 엔진
- URP (Universal Render Pipeline)를 통한 3D 렌더링 관리
- 통합 물리 시스템
- 씬 및 프리팹 관리

#### XR Interaction Toolkit 3.2.2
- Unity 공식 VR 인터랙션 프레임워크
- 컨트롤러 및 핸드 트래킹 관리
- 텔레포트 시스템
- 오브젝트 인터랙션 (grab, poke 등)

#### OpenXR 1.16.1
- VR/AR 오픈 스탠다드
- 멀티 헤드셋 호환 (Quest, Index, Vive 등)
- 하드웨어 입력 추상화

### 2.2 네트워크 및 통신

#### NativeWebSocket
- **소스:** https://github.com/endel/NativeWebSocket
- **역할:** 클라이언트-서버 양방향 통신
- **용도:** 위치 동기화, 룸 관리, WebRTC 시그널링

#### Unity WebRTC 3.0.0
- **역할:** P2P 음성 통신
- **프로토콜:** STUN/TURN을 사용한 WebRTC
- **특징:** 3D 공간 오디오, 메시 토폴로지

### 2.3 백엔드 서버

#### Node.js (>= 16.0.0)
서버 사이드 JavaScript 런타임

#### NPM 의존성
| 패키지 | 버전 | 역할 |
|--------|------|------|
| ws | ^8.14.2 | WebSocket 서버 |
| uuid | ^9.0.1 | 고유 식별자 생성 |
| dotenv | ^16.3.1 | 환경 변수 |
| pdf-poppler | ^0.2.3 | PDF 변환 (선택) |
| jest | ^30.2.0 | 단위 테스트 (개발) |

#### 데이터베이스 (선택)
- **MariaDB** 인증용
- **bcrypt** 비밀번호 해싱 (12 라운드)
- **JWT** 세션 토큰 (24시간 유효)

### 2.4 사용된 Unity 패키지

| 패키지 | 버전 | 역할 |
|--------|------|------|
| com.unity.xr.interaction.toolkit | 3.2.2 | VR 인터랙션 |
| com.unity.xr.openxr | 1.16.1 | OpenXR 런타임 |
| com.unity.xr.hands | 1.7.2 | 핸드 트래킹 |
| com.unity.webrtc | 3.0.0 | 음성 통신 |
| com.unity.render-pipelines.universal | 17.2.0 | URP 렌더링 |
| com.unity.inputsystem | 1.16.0 | 새 입력 시스템 |
| com.endel.nativewebsocket | GitHub | 네이티브 WebSocket |
| com.veriorpies.parrelsync | GitHub | 멀티 인스턴스 테스트 |
| com.unity.nuget.newtonsoft-json | 3.2.1 | 고급 JSON 직렬화 |

---

## 3. 기술 아키텍처

### 3.1 클라이언트-서버 아키텍처

```
┌─────────────────────────────────────────────────────────────────────┐
│                         UNITY 클라이언트                              │
├─────────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │
│  │   Quest     │  │    PCVR     │  │   Desktop   │                 │
│  │   Client    │  │   Client    │  │   Client    │                 │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘                 │
│         │                │                │                         │
│         └────────────────┼────────────────┘                         │
│                          │                                          │
│                    WebSocket + WebRTC                               │
└──────────────────────────┼──────────────────────────────────────────┘
                           │
┌──────────────────────────┼──────────────────────────────────────────┐
│                     NODE.JS 서버                                     │
├──────────────────────────┼──────────────────────────────────────────┤
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    WebSocket 서버                            │   │
│  │  - 클라이언트 연결 관리                                       │   │
│  │  - 메시지 라우팅                                              │   │
│  │  - 룸 관리                                                    │   │
│  │  - WebRTC 시그널링                                            │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                          │                                          │
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                 MariaDB (선택)                               │   │
│  │  - 사용자 인증                                                │   │
│  │  - 프로필 저장                                                │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 Unity 씬 아키텍처

```
Bootstrap (씬 0) - 영구적
├── NetworkManager (VRNetworkManager)
├── RoomManager (VRRoomManager)
├── GameManager (VRGameManager)
├── VoiceChatManager
├── AuthManager
├── SoundManager
├── DebugManager
└── MainMenuUI

Meet (씬 1) - 추가 로드
├── Environment (로비, 회의실)
├── Whiteboards
├── SpectatorCamera (녹화)
└── 룸별 오브젝트
```

### 3.3 싱글톤 패턴과 영구성

주요 매니저들은 `DontDestroyOnLoad`와 함께 싱글톤 패턴을 따릅니다:

```csharp
public class VRNetworkManager : MonoBehaviour
{
    public static VRNetworkManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
```

### 3.4 이벤트 시스템

디커플링을 위한 C# 이벤트 기반 아키텍처:

```csharp
// VRNetworkManager 이벤트
public static event Action OnConnected;
public static event Action OnDisconnected;
public static event Action<string> OnPeerConnected;
public static event Action<string> OnPeerDisconnected;
public static event Action<NetworkMessage> OnMessageReceived;
public static event Action<string> OnConnectionError;

// VRRoomManager 이벤트
public static event Action OnRoomCreated;
public static event Action OnRoomJoined;
public static event Action OnRoomLeft;
public static event Action<VRPlayerData> OnPlayerJoined;
public static event Action<string> OnPlayerLeft;
```

---

## 4. 소스 코드 구조

### 4.1 폴더 구성

```
Assets/Scrips/
├── Network/           # 네트워크 코어
│   ├── VRNetworkManager.cs      # WebSocket 연결, 메인 싱글톤
│   ├── VRRoomManager.cs         # 룸 및 플레이어 관리
│   └── VRGameManager.cs         # 플레이어 스폰/디스폰
│
├── VR/                # VR 컨트롤러
│   ├── BootstrapManager.cs      # 앱 초기화
│   ├── VRPlayerController.cs    # 로컬 VR 플레이어 컨트롤러
│   ├── VRTrackingFix.cs         # 트래킹 수정
│   ├── ControllerTrackingFix.cs # 컨트롤러 트래킹 수정
│   ├── ControllerModelLoader.cs # 컨트롤러 모델 로드
│   ├── ControllerInputFix.cs    # 컨트롤러 입력 수정
│   ├── TeleportOnButtonClick.cs # UI 텔레포트
│   ├── TeleportOnGrab.cs        # 그랩 텔레포트
│   ├── XRUIInteractionBridge.cs # UI/XR 브릿지
│   └── XRInteractorInputBridge.cs # Input/XR 브릿지
│
├── Desktop/           # 데스크톱 모드
│   └── DesktopPlayerController.cs # 키보드/마우스 컨트롤러
│
├── WebRTC/            # 음성 통신
│   ├── VoiceChatManager.cs      # 음성 채팅 오케스트레이션
│   ├── WebRTCPeerManager.cs     # 피어 연결 관리
│   ├── MicrophoneManager.cs     # 마이크 캡처
│   ├── WebRTCSignaling.cs       # offer/answer/ICE 시그널링
│   ├── WebRTCConfiguration.cs   # STUN/TURN 설정
│   ├── RemoteAudioManager.cs    # 원격 공간 오디오
│   └── VoiceChatData.cs         # 데이터 구조
│
├── WhiteBoard/        # 협업 화이트보드
│   ├── Whiteboard.cs            # 흰색 배경 + 프레젠테이션 모드
│   ├── WhiteboardDrawingSurface.cs # 네트워크 그리기 표면
│   ├── WhiteboardMarker.cs      # VR 마커
│   ├── DesktopWhiteboardDrawer.cs # 데스크톱 그리기
│   ├── WhiteboardEraser.cs      # 지우개
│   ├── WhiteboardNetworkData.cs # 네트워크 데이터
│   └── WhiteboardUI*.cs         # UI 스크립트
│
├── Interaction/       # 인터랙션
│   ├── LaserPointer.cs          # 레이저 포인터
│   ├── LaserPointerData.cs      # 레이저 데이터
│   ├── VRNetworkedInteractable.cs # 네트워크 인터랙티브 오브젝트
│   └── RoomBlocker.cs           # 룸 접근 차단
│
├── Sharing/           # 콘텐츠 공유
│   ├── ScreenShareManager.cs    # 화면 공유
│   ├── FileShareManager.cs      # 파일 공유
│   ├── FilePresentationManager.cs # 파일 프레젠테이션
│   ├── WindowCapture.cs         # 윈도우 캡처
│   └── *Data.cs                 # 데이터 구조
│
├── Avatar/            # 아바타 커스터마이징
│   ├── AvatarCustomization.cs   # 커스터마이징 시스템
│   └── AvatarColorTarget.cs     # 색상 타겟
│
├── Auth/              # 인증
│   ├── AuthManager.cs           # 인증 관리
│   └── AuthUI.cs                # 인증 인터페이스
│
├── Recording/         # 비디오 녹화
│   ├── RecordingManager.cs      # 녹화 파이프라인
│   ├── SpectatorCameraController.cs # 관전 카메라
│   ├── FFmpegEncoder.cs         # FFmpeg 인코딩
│   ├── AudioCapture.cs          # 오디오 캡처
│   └── RecordingData.cs         # 데이터/설정
│
├── Audio/             # 오디오 시스템
│   ├── SoundManager.cs          # 사운드 매니저
│   ├── AmbienceManager.cs       # 앰비언스 사운드
│   ├── AudioMuteZone.cs         # 뮤트 존
│   └── UIButtonSounds.cs        # 버튼 사운드
│
├── UI/                # 사용자 인터페이스
│   ├── MainMenu/                # 메인 메뉴
│   ├── Menu/                    # VR 인게임 메뉴
│   ├── VRMenuUi.cs              # VR 메뉴 UI
│   ├── VRCanvasAdapter.cs       # VR 캔버스 어댑터
│   ├── LaunchLoadingScreen.cs   # 로딩 화면
│   └── ...
│
├── Utils/             # 유틸리티
│   ├── TransformUtility.cs
│   ├── JsonHelper.cs
│   ├── ScreenFader.cs
│   ├── LoadingIndicator.cs
│   └── SceneLoader.cs
│
├── Effects/           # 시각 효과
│   └── GlowingLight.cs
│
└── Debug/             # 디버그
    ├── DebugManager.cs
    └── XRDebugOverlay.cs
```

### 4.2 서버 구조

```
Server/
├── server.js          # 진입점, 메인 WebSocket 서버
├── package.json       # NPM 의존성
├── .env               # 환경 변수 (버전 관리 제외)
└── src/
    ├── database.js    # MariaDB 연결 풀
    └── auth.js        # bcrypt + JWT 인증
```

---

## 5. 네트워크 프로토콜

### 5.1 메시지 형식

```csharp
[Serializable]
public class NetworkMessage
{
    public string type;     // 메시지 타입
    public string senderId; // 발신자 ID
    public string data;     // JSON 페이로드 (string)
}
```

### 5.2 메시지 타입

| 카테고리 | 타입 | 설명 |
|----------|------|------|
| 연결 | `welcome`, `peer-connected`, `peer-disconnected` | 핸드셰이크 및 피어 관리 |
| 룸 | `room-join`, `room-leave`, `room-available`, `room-closed`, `room-list` | 룸 관리 |
| VR 동기화 | `vr-position` | 위치 동기화 (30Hz) |
| 음성 | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` | WebRTC 시그널링 |
| 화이트보드 | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request`, `whiteboard-state` | 화이트보드 |
| 공유 | `screen-share-*`, `file-share-*`, `file-present-*` | 콘텐츠 공유 |
| 녹화 | `recording-status`, `recording-marker` | 녹화 |
| 인증 | `auth-login`, `auth-register`, `auth-verify`, `auth-logout`, `auth-response` | 인증 |

### 5.3 VR 동기화 (30Hz)

```csharp
// 30Hz로 전송되는 위치 데이터
public class VRPositionData
{
    public float[] headPos;     // [x, y, z]
    public float[] headRot;     // [x, y, z, w] (쿼터니언)
    public float[] leftHandPos;
    public float[] leftHandRot;
    public float[] rightHandPos;
    public float[] rightHandRot;
}
```

---

## 6. 개발 워크플로우

### 6.1 환경 설정

#### 사전 요구 사항
1. **Unity Hub**와 Unity 6000.2.14f1
2. **Node.js** >= 16.0.0
3. **Git** 버전 관리용
4. **Visual Studio** 또는 **Rider** C# 개발용

#### 설치
```bash
# 1. 저장소 클론
git clone <repository-url>
cd WebSocket_VR

# 2. Unity 프로젝트 열기
# Unity Hub에서 WebSocket_VR 폴더 열기

# 3. 서버 의존성 설치
cd Server
npm install

# 4. 환경 설정 (인증용, 선택)
cp .env.example .env
# DB 자격 증명으로 .env 편집
```

### 6.2 개발 실행

```bash
# 터미널 1: 서버 실행
cd Server
npm run dev   # 자동 리로드 포함

# 터미널 2: Unity 에디터 실행
# 에디터에서 플레이 모드
```

### 6.3 ParrelSync로 멀티 인스턴스 테스트

ParrelSync로 로컬 멀티플레이어 테스트가 가능합니다:

1. **Window > ParrelSync > Clones Manager**
2. 프로젝트 클론 생성
3. 새 Unity 인스턴스에서 클론 열기
4. 두 인스턴스 모두 플레이 모드 실행

### 6.4 오프라인 모드 (디버그)

서버 없이 테스트하려면:

1. Hierarchy에서 `VRNetworkManager` 선택
2. Inspector에서 `Offline Mode` 체크
3. 원하면 `Offline Room Type` 설정
4. 플레이 모드 실행

### 6.5 코드 컨벤션

#### 이벤트
```csharp
// OnEnable에서 구독
void OnEnable()
{
    VRRoomManager.OnPlayerJoined += HandlePlayerJoined;
}

// OnDisable에서 구독 해제
void OnDisable()
{
    VRRoomManager.OnPlayerJoined -= HandlePlayerJoined;
}
```

#### JSON 직렬화
```csharp
// [Serializable]과 함께 JsonUtility 사용
// 복잡한 중첩 객체 금지
[Serializable]
public class MyData
{
    public string id;
    public float value;
}

string json = JsonUtility.ToJson(data);
MyData parsed = JsonUtility.FromJson<MyData>(json);
```

#### 로깅
```csharp
// 형식: [SystemName] Message
Debug.Log("[VRNet] Connected to server");
Debug.LogWarning("[Voice] Microphone not found");
Debug.LogError("[Room] Failed to join room");
```

---

## 7. 구현된 기능

### 7.1 룸 시스템

- **룸 타입:** Lobby, MeetingRoomA, MeetingRoomB
- **룸 코드:** 6자리 영숫자
- **호스트 권한:** 룸 생성자가 호스트
- **강퇴:** 호스트가 플레이어 강퇴 가능

### 7.2 VR 동기화

- **주파수:** 30Hz
- **이동 임계값:** 0.01m / 1도
- **보간:** 부드러움을 위한 Factor 15
- **헤드/손 분리:** 정확도를 위한 월드 스페이스

### 7.3 음성 통신 (WebRTC)

- **토폴로지:** 메시 (각 클라이언트가 모두에게 연결)
- **시작:** 가장 작은 ID를 가진 클라이언트가 시작
- **서버:** NAT 통과를 위한 STUN + TURN
- **공간 오디오:** 아바타 머리에 부착된 소스
- **Push-to-talk:** V 키 (선택)

### 7.4 화이트보드

- **3계층 아키텍처:**
  1. `Whiteboard.cs` - 흰색 배경 + 프레젠테이션 모드
  2. `WhiteboardDrawingSurface.cs` - 투명 표면, 네트워크
  3. `WhiteboardMarker/DesktopWhiteboardDrawer` - 로컬 그리기
- **해상도:** 2048x2048
- **전송 주기:** 33ms
- **후발 참가자 동기화:** Request/State 패턴

### 7.5 화면 공유

- **해상도:** 854x480
- **프레임레이트:** 3 FPS
- **압축:** JPEG 50%
- **표시:** 화이트보드 프레젠테이션 모드

### 7.6 파일 공유

- **최대 크기:** 10MB
- **확장자:** pdf, doc, docx, xls, xlsx, png, jpg, jpeg, gif
- **전송:** WebSocket을 통한 청크

### 7.7 레이저 포인터

- **VR:** A 버튼
- **Desktop:** L 키
- **동기화 주파수:** 10Hz
- **시각화:** 빨간색 LineRenderer + 점

### 7.8 비디오 녹화

- **아키텍처:** 3단계 비동기 파이프라인 (VR 멀미 방지)
  1. Main Thread: AsyncGPUReadback
  2. Encode Thread: RGB -> TGA
  3. Write Thread: File.Write()
- **해상도:** 1920x1080 @ 30fps
- **호스트 전용:** 호스트만 녹화 가능
- **출력:** TGA 프레임 + WAV -> FFmpeg -> MP4

### 7.9 인증

- **플로우:** 메인 메뉴 -> 인증 화면 -> Meet
- **옵션:** 로그인, 회원가입, 게스트
- **보안:** bcrypt 12 라운드, JWT 24시간, 분당 5회 제한
- **선택:** 게스트 모드에서 데이터베이스 없이 작동

---

## 8. 조작법

### 8.1 VR 모드

| 동작 | 입력 |
|------|------|
| 이동 | 텔레포트 (레이캐스트 + 트리거) |
| 시선 | 머리 회전 |
| 레이저 포인터 | A 버튼 |
| 오브젝트 잡기 | 그립 |
| Push-to-talk | V 키 (키보드) |
| 메뉴 | 메뉴 버튼 |

### 8.2 데스크톱 모드

| 동작 | 입력 |
|------|------|
| 이동 | WASD + Shift (달리기) |
| 시선 | 우클릭 + 마우스 |
| 레이저 포인터 | L 키 |
| 그리기 | 좌클릭 |
| 메뉴 | ESC |

---

## 9. 배포

### 9.1 Unity 빌드

1. **File > Build Settings**
2. 플랫폼 선택 (Windows, Android)
3. Quest용: XR 설정 구성
4. Build & Run

### 9.2 서버 배포

```bash
# 프로덕션
cd Server
npm start

# PM2 사용 (권장)
pm2 start server.js --name vrmeet-server
```

### 9.3 프로덕션 설정

```env
# .env
PORT=8080
DB_HOST=localhost
DB_USER=vrmeet
DB_PASSWORD=secure_password
DB_NAME=vrmeet_db
JWT_SECRET=your_jwt_secret_key
```

### 9.4 보안

- 프로덕션에서 `wss://` (WebSocket Secure) 사용
- VRNetworkManager에서 `enforceSecureConnection = true` 설정
- SSL이 있는 리버스 프록시 (nginx) 구성

---

## 10. 커밋 히스토리 (발췌)

```
984c969 commit for creating a branch (little changement in the fade)
448be19 upscaling
865a179 wss://vrmeeting-test.duckdns.org/, server connection test
2dfe067 menu and transition update
efd8d86 add door that disappear at room creation
3885a3a update project memo, make recording mode more fluid
f55b8f3 add auth UI
3b8db9b auth code
525baae update for recording, add server documentation
1207571 move audio manager to bootstrap, create ambiant mute zones
79e59b2 add audio effects
845f798 UI fix for desktop mode
0c48b57 whiteboard drawing fix
679e645 change resolution, whiteboard drawing fix
```

---

## 11. 리소스 및 참고 자료

### 11.1 공식 문서
- [Unity XR Interaction Toolkit](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.2/manual/index.html)
- [Unity WebRTC](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/index.html)
- [OpenXR](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.16/manual/index.html)
- [NativeWebSocket](https://github.com/endel/NativeWebSocket)

### 11.2 서드파티 패키지
- [ParrelSync](https://github.com/VeriorPies/ParrelSync) - 멀티 인스턴스 테스트
- [ws (Node.js)](https://github.com/websockets/ws) - WebSocket 서버

---

## 12. 저자 및 라이선스

**프로젝트:** VRMeet
**조직:** Rndp
**연도:** 2024-2025

---

*문서 생성일: 2026년 2월 27일*
