# 네트워킹 완전 가이드 - 쉬운 설명

이 문서는 VR 프로젝트의 네트워크가 어떻게 작동하는지 아무것도 모르는 사람에게 설명하듯이 설명합니다.

---

## 목차

1. [네트워킹이란?](#1-네트워킹이란)
2. [중요한 3개의 파일](#2-중요한-3개의-파일)
3. [VRNetworkManager.cs - Unity 클라이언트 (한 줄씩 설명)](#3-vrnetworkmanagercs---unity-클라이언트)
4. [server.js - Node.js 서버 (한 줄씩 설명)](#4-serverjs---nodejs-서버)
5. [db.js - 데이터베이스 (한 줄씩 설명)](#5-dbjs---데이터베이스)
6. [모든 것이 어떻게 함께 작동하는지](#6-모든-것이-어떻게-함께-작동하는지)

---

## 1. 네트워킹이란?

가상 방에서 친구들과 함께 플레이한다고 상상해보세요. 모든 사람이 같은 것을 보려면:

```
나 (Unity)  ←──────→  서버 (Node.js)  ←──────→  친구 (Unity)
     │                       │                           │
     │    "나 움직였어!"      │                           │
     │ ──────────────────→   │                           │
     │                       │   "그가 움직였어!"         │
     │                       │ ──────────────────────→   │
```

- **클라이언트** = 컴퓨터/VR 헤드셋에서 실행되는 Unity 게임
- **서버** = 모든 사람의 메시지를 받아서 재분배하는 중앙 컴퓨터
- **WebSocket** = 즉시 메시지를 보낼 수 있도록 열린 상태로 유지되는 "파이프"

---

## 2. 중요한 3개의 파일

| 파일 | 위치 | 역할 |
|------|------|------|
| `VRNetworkManager.cs` | Unity (클라이언트) | 메시지 송수신 |
| `server.js` | Node.js (서버) | 메시지를 받아서 재분배 |
| `db.js` | Node.js (서버) | 데이터베이스에 사용자 저장 |

---

## 3. VRNetworkManager.cs - Unity 클라이언트

**경로:** `Assets/Scrips/Network/VRNetworkManager.cs`

이 파일은 Unity 게임의 "네트워크 두뇌"입니다. 서버 연결을 관리합니다.

### import문 (1-4줄)

```csharp
using System;                    // 기본 함수 사용 (에러 처리 등)
using System.Threading.Tasks;    // "병렬" 작업 수행 (async/await)
using UnityEngine;               // Unity 함수 사용 (MonoBehaviour 등)
using NativeWebSocket;           // WebSocket 통신을 가능하게 하는 라이브러리
```

**쉬운 설명:**
- 작업을 시작하기 전에 "이 도구들이 필요해"라고 말하는 것과 같습니다
- `NativeWebSocket`은 서버와 통신할 수 있게 해주는 외부 라이브러리입니다

---

### 클래스와 싱글톤 (13-16줄)

```csharp
public class VRNetworkManager : MonoBehaviour
{
    public static VRNetworkManager Instance { get; private set; }
```

**쉬운 설명:**
- `MonoBehaviour` = 이 스크립트는 Unity 오브젝트에 붙일 수 있습니다
- `Instance` = 게임 전체에서 VRNetworkManager는 단 하나만 존재합니다 ("싱글톤")
- "서버에 전화할 전화기는 하나뿐"이라고 말하는 것과 같습니다

---

### 서버 설정 (17-32줄)

```csharp
[Header("Server Configuration")]
public string serverUrl = "ws://localhost:8080";    // 서버 주소
public bool autoReconnect = true;                   // 연결이 끊기면 자동 재연결?
public float reconnectDelay = 3f;                   // 재시도 전 대기 시간(초)

[Header("Connection Timeout (P0 Fix)")]
public float welcomeTimeout = 5f;                   // 서버 응답 최대 대기 시간 5초

[Header("Exponential Backoff (P0 Fix)")]
public float initialReconnectDelay = 1f;            // 첫 번째 시도: 1초 대기
public float maxReconnectDelay = 30f;               // 최대: 30초 대기
public float backoffMultiplier = 2f;                // 실패할 때마다 대기 시간 2배
```

**쉬운 설명:**
- `serverUrl` = 서버가 있는 주소. `ws://`는 WebSocket용, `localhost`는 "내 컴퓨터", `8080`은 문 번호입니다
- `autoReconnect` = 연결이 끊기면 자동으로 재시도
- `Exponential Backoff` = 실패하면 재시도 전 대기 시간이 점점 길어집니다 (1초, 2초, 4초, 8초...) 서버를 "스팸"하지 않기 위해

---

### 상태 변수 (34-47줄)

```csharp
public static string LocalId { get; private set; }   // 내 고유 식별자 (서버에서 받음)
public static bool IsConnected { get; private set; } // 연결됐나? true/false

private WebSocket _websocket;           // 연결을 관리하는 객체
private bool _isReconnecting;           // 재연결 중인가?
private float _reconnectTimer;          // 재연결 카운터

private float _currentReconnectDelay;   // 현재 재연결 대기 시간
private int _reconnectAttempts;         // 재연결 시도 횟수

private float _welcomeTimeoutTimer;     // "welcome" 메시지 타임아웃 카운터
private bool _waitingForWelcome;        // "welcome" 메시지를 기다리고 있나?
```

**쉬운 설명:**
- `LocalId` = 게임에서의 전화번호 같은 것. 연결하면 서버가 줍니다.
- `IsConnected` = 연결 여부를 나타내는 간단한 "예" 또는 "아니오"
- `_websocket` = 통신 "파이프"
- 나머지 변수들은 재연결을 관리하는 데 사용됩니다

---

### 이벤트 (52-60줄)

```csharp
public static event Action OnConnected;                    // 연결 시 발생
public static event Action OnDisconnected;                 // 연결 해제 시 발생
public static event Action<string> OnPeerConnected;        // 다른 사람이 연결할 때 발생
public static event Action<string> OnPeerDisconnected;     // 다른 사람이 연결 해제할 때 발생
public static event Action<NetworkMessage> OnMessageReceived;  // 메시지 수신 시 발생
public static event Action<string> OnConnectionError;      // 에러 발생 시
```

**쉬운 설명:**
- "이벤트"는 다른 스크립트가 듣는 "알림"과 같습니다
- 예를 들어, `VRRoomManager`는 `OnConnected`를 듣고 언제 방을 만들 수 있는지 알 수 있습니다
- "누군가 오면 알려줘!"라고 말하는 것과 같습니다

**다른 스크립트에서 사용하는 방법:**
```csharp
void OnEnable() {
    VRNetworkManager.OnConnected += MyFunctionWhenConnected;  // 구독
}

void OnDisable() {
    VRNetworkManager.OnConnected -= MyFunctionWhenConnected;  // 구독 해제
}

void MyFunctionWhenConnected() {
    Debug.Log("야호, 연결됐다!");
}
```

---

### Awake - 싱글톤 생성 (65-75줄)

```csharp
void Awake()
{
    // VRNetworkManager가 이미 존재하면...
    if (Instance != null)
    {
        Destroy(gameObject);  // ...이 새로운 것을 파괴 (하나만 원함)
        return;
    }

    Instance = this;                    // 내가 바로 그 VRNetworkManager
    DontDestroyOnLoad(gameObject);      // 씬이 바뀌어도 파괴하지 마
}
```

**쉬운 설명:**
- `Awake()`는 Unity에서 객체가 생성될 때 호출됩니다
- VRNetworkManager가 이미 존재하는지 확인합니다. 존재하면 새 것을 파괴합니다.
- `DontDestroyOnLoad` = "레벨이 바뀌어도 나를 살려줘"

---

### Start - 연결 시작 (78-83줄)

```csharp
void Start()
{
    _currentReconnectDelay = initialReconnectDelay;  // 초기 대기 = 1초
    _reconnectAttempts = 0;                          // 아직 시도 없음
    ConnectAsync();                                  // 연결 시작!
}
```

**쉬운 설명:**
- `Start()`는 게임 시작 시 한 번 호출됩니다
- 카운터를 초기화하고 연결을 시작합니다

---

### ConnectAsync - 연결 래퍼 (86-98줄)

```csharp
private async void ConnectAsync()
{
    try
    {
        await Connect();  // 연결 시도
    }
    catch (Exception e)   // 실패하면...
    {
        Debug.LogError($"[VRNet] Connection failed: {e.Message}");
        OnConnectionError?.Invoke(e.Message);  // 모두에게 에러 알림
        HandleDisconnection();                  // 연결 해제 처리
    }
}
```

**쉬운 설명:**
- `async` = 이 함수는 게임을 멈추지 않고 "병렬"로 작업할 수 있습니다
- `try/catch` = "이거 해보고, 안 되면 이거 해"
- 실제 `Connect()` 함수를 감싸는 "안전 포장"입니다

---

### Update - 메인 루프 (100-130줄)

```csharp
void Update()
{
    // PC에서 (웹 브라우저가 아닌), 수신된 메시지를 수동으로 처리해야 함
    #if !UNITY_WEBGL || UNITY_EDITOR
        _websocket?.DispatchMessageQueue();  // 대기 중인 메시지 처리
    #endif

    // 서버가 "welcome" 응답에 너무 오래 걸리는지 확인
    if (_waitingForWelcome)
    {
        _welcomeTimeoutTimer -= Time.deltaTime;  // 카운트다운
        if (_welcomeTimeoutTimer <= 0f)          // 시간 초과!
        {
            Debug.LogWarning("[VRNet] Welcome timeout - reconnecting");
            _waitingForWelcome = false;
            HandleDisconnection();  // 연결 해제로 간주
        }
    }

    // 자동 재연결 관리
    if (_isReconnecting && autoReconnect)
    {
        _reconnectTimer -= Time.deltaTime;  // 카운트다운
        if (_reconnectTimer <= 0f)          // 재시도 시간!
        {
            _isReconnecting = false;
            _reconnectAttempts++;
            Debug.Log($"[VRNet] Reconnect attempt #{_reconnectAttempts}");
            ConnectAsync();  // 다시 연결 시도
        }
    }
}
```

**쉬운 설명:**
- `Update()`는 게임의 각 프레임마다 호출됩니다 (초당 약 60번)
- 서버가 응답하는 데 너무 오래 걸리는지 확인합니다
- 다시 연결해야 할 때인지 확인합니다

---

### Connect - 실제 연결 (160-207줄)

```csharp
public async Task Connect()
{
    // 이미 연결됐거나 연결 중이면, 아무것도 안 함
    if (_websocket != null &&
        (_websocket.State == WebSocketState.Open ||
         _websocket.State == WebSocketState.Connecting))
        return;

    try
    {
        Debug.Log($"[VRNet] Connecting to {serverUrl}");

        // 서버로의 새 WebSocket 생성
        _websocket = new WebSocket(serverUrl);

        // 연결이 열리면...
        _websocket.OnOpen += () =>
        {
            Debug.Log("[VRNet] WebSocket opened");
            _waitingForWelcome = true;           // "welcome" 메시지 대기
            _welcomeTimeoutTimer = welcomeTimeout;  // 카운터 시작 (5초)
        };

        // 메시지를 받으면...
        _websocket.OnMessage += bytes =>
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);  // 바이트를 텍스트로 변환
            HandleMessage(json);  // 메시지 처리
        };

        // 연결이 닫히면...
        _websocket.OnClose += code =>
        {
            Debug.Log($"[VRNet] Closed ({code})");
            HandleDisconnection();
        };

        // 에러가 발생하면...
        _websocket.OnError += err =>
        {
            Debug.LogError($"[VRNet] Error: {err}");
            OnConnectionError?.Invoke(err);
            HandleDisconnection();
        };

        // 연결 시작!
        await _websocket.Connect();
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] Connection exception: {e.Message}");
        HandleDisconnection();
    }
}
```

**쉬운 설명:**
- 서버로의 WebSocket "파이프"를 생성합니다
- 다양한 상황에서 무슨 일이 일어나는지 정의합니다:
  - `OnOpen` = 파이프가 열렸다! 이제 "welcome" 메시지를 기다립니다
  - `OnMessage` = 메시지를 받았다! 처리합니다.
  - `OnClose` = 파이프가 닫혔다. 연결 해제를 처리합니다.
  - `OnError` = 문제가 발생했다. 에러를 처리합니다.
- `await _websocket.Connect()` = 연결이 완료될 때까지 기다립니다

---

### HandleDisconnection - 연결 해제 관리 (224-253줄)

```csharp
private void HandleDisconnection()
{
    bool wasConnected = IsConnected;  // 이전에 연결되어 있었나?

    _waitingForWelcome = false;  // 더 이상 "welcome"을 기다리지 않음
    IsConnected = false;         // 더 이상 연결 안 됨
    LocalId = null;              // 더 이상 식별자 없음

    // 연결되어 있었다면, 모두에게 알림
    if (wasConnected)
    {
        OnDisconnected?.Invoke();
        // 재연결 대기 시간 초기화 (1초부터 다시 시작)
        _currentReconnectDelay = initialReconnectDelay;
        _reconnectAttempts = 0;
    }

    // 자동 재연결이 활성화되어 있으면...
    if (autoReconnect && !_isReconnecting)
    {
        _isReconnecting = true;
        _reconnectTimer = _currentReconnectDelay;  // 재시도 전 이 시간만큼 대기

        Debug.Log($"[VRNet] Reconnecting in {_currentReconnectDelay}s");

        // EXPONENTIAL BACKOFF: 다음을 위해 대기 시간 두 배
        // 1초 → 2초 → 4초 → 8초 → 16초 → 30초 (최대)
        _currentReconnectDelay = Mathf.Min(
            _currentReconnectDelay * backoffMultiplier,
            maxReconnectDelay
        );
    }
}
```

**쉬운 설명:**
- 연결을 잃으면, 모든 것을 정리합니다
- 이전에 정말 연결되어 있었다면, 다른 스크립트에 알립니다
- 자동 재연결을 예약합니다
- **Exponential Backoff**: 서버를 "괴롭히지" 않기 위해 각 시도 사이에 더 오래 기다립니다

---

### HandleMessage - 수신 메시지 처리 (258-305줄)

```csharp
void HandleMessage(string json)
{
    try
    {
        // JSON 텍스트를 NetworkMessage 객체로 변환
        NetworkMessage msg = JsonUtility.FromJson<NetworkMessage>(json);

        // "welcome" 메시지 = 서버가 인사하고 ID를 줌
        if (msg.type == "welcome")
        {
            _waitingForWelcome = false;  // welcome 받았다!

            // backoff 초기화 (연결 성공)
            _currentReconnectDelay = initialReconnectDelay;
            _reconnectAttempts = 0;

            LocalId = msg.senderId;   // 고유 식별자 저장
            IsConnected = true;       // 공식적으로 연결됨!

            Debug.Log($"[VRNet] Assigned ID: {LocalId}");
            OnConnected?.Invoke();    // 연결됐다고 모두에게 알림
            return;
        }

        // "peer-connected" 메시지 = 다른 사람이 연결함
        if (msg.type == "peer-connected")
        {
            OnPeerConnected?.Invoke(msg.senderId);  // ID와 함께 알림
            return;
        }

        // "peer-disconnected" 메시지 = 누군가 연결 해제함
        if (msg.type == "peer-disconnected")
        {
            OnPeerDisconnected?.Invoke(msg.senderId);
            return;
        }

        // 내가 보낸 메시지는 무시 (에코)
        if (msg.senderId == LocalId && msg.type != "whiteboard-history")
            return;

        // 다른 모든 메시지: 다른 스크립트로 전달
        OnMessageReceived?.Invoke(msg);
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] JSON parse error: {e.Message}");
    }
}
```

**쉬운 설명:**
- 메시지를 받으면, 무엇을 해야 하는지 알기 위해 `type`을 확인합니다
- `welcome` = 서버가 우리를 환영하고 ID를 줍니다. "진짜" 연결된 순간입니다.
- `peer-connected` = 다른 플레이어가 방금 도착했습니다
- `peer-disconnected` = 다른 플레이어가 떠났습니다
- 다른 모든 메시지는 `OnMessageReceived`를 통해 다른 스크립트로 전달됩니다

---

### Send - 메시지 보내기 (310-342줄)

```csharp
// 간단한 버전: type만, 데이터 없음
public void Send(string type)
{
    SendInternal(type, "{}");  // 빈 객체와 함께 보냄
}

// 완전한 버전: type + 데이터
public void Send(string type, object payload)
{
    // 객체를 JSON 텍스트로 변환
    string dataJson = payload is string s ? s : JsonUtility.ToJson(payload);
    SendInternal(type, dataJson);
}

// 실제 전송 함수 (private)
private async void SendInternal(string type, string dataJson)
{
    // 연결 안 됐으면, 아무것도 안 함
    if (_websocket == null || _websocket.State != WebSocketState.Open)
        return;

    try
    {
        // 메시지 준비
        _cachedOutgoingMessage.type = type;
        _cachedOutgoingMessage.senderId = LocalId;
        _cachedOutgoingMessage.data = dataJson;

        // 서버에 메시지 보내기
        await _websocket.SendText(JsonUtility.ToJson(_cachedOutgoingMessage));
    }
    catch (Exception e)
    {
        Debug.LogError($"[VRNet] Send failed for '{type}': {e.Message}");
    }
}
```

**쉬운 설명:**
- `Send("room-join", myObject)` = 서버에 메시지 보내기
- 메시지는 보내기 전에 JSON 텍스트로 변환됩니다
- 불필요한 객체 생성을 피하기 위해 "캐시된" 객체(`_cachedOutgoingMessage`)를 사용합니다 (메모리 최적화)

**코드에서 사용하는 방법:**
```csharp
// 예: 방 참가
var data = new RoomJoinData { roomId = "ABC123", playerName = "철수" };
VRNetworkManager.Instance.Send("room-join", data);

// 예: 내 위치 보내기
var posData = new PositionData { x = 1.5f, y = 0, z = 3.2f };
VRNetworkManager.Instance.Send("vr-position", posData);
```

---

### 메시지 형식 (356-362줄)

```csharp
[Serializable]
public class NetworkMessage
{
    public string type;      // 메시지 유형 (예: "vr-position", "room-join")
    public string senderId;  // 보내는 사람의 ID
    public string data;      // 데이터 (항상 JSON 텍스트)
}
```

**쉬운 설명:**
- 모든 메시지는 같은 구조를 가집니다
- `type` = 어떤 메시지인가?
- `senderId` = 누가 보내나?
- `data` = 정보 (위치, 방 이름 등)

**JSON 메시지 예시:**
```json
{
    "type": "vr-position",
    "senderId": "abc123-def456-...",
    "data": "{\"x\":1.5,\"y\":0,\"z\":3.2,\"rotY\":45}"
}
```

---

## 4. server.js - Node.js 서버

**경로:** `LocalServ/Server/server.js`

서버는 "전화 교환원"과 같습니다: 전화를 받아서 올바른 사람에게 전달합니다.

### import와 설정 (13-21줄)

```javascript
const WebSocket = require('ws');        // Node.js용 WebSocket 라이브러리
const { v4: uuidv4 } = require('uuid'); // 고유 식별자 생성용
const { registerUser, loginUser, updateUserProfile } = require('./auth');  // 인증 함수

const PORT = process.env.PORT || 8080;  // 서버 포트 (기본 8080)
const HEARTBEAT_INTERVAL = 30000;       // 30초마다 연결 확인

const clients = new Map();  // 연결된 클라이언트 목록: clientId → {ws, roomId, playerName}
const rooms = new Map();    // 방 목록: roomId → RoomInfo
```

**쉬운 설명:**
- 필요한 도구들을 불러옵니다
- `PORT` = 서버의 "문" 번호. 기본값 8080이지만 환경 변수로 변경 가능
- `clients` = 모든 연결된 플레이어 목록
- `rooms` = 모든 미팅 방 목록

---

### 서버 시작 (23-25줄)

```javascript
const wss = new WebSocket.Server({ port: PORT });

console.log(`[SERVER] WebSocket server started on port ${PORT}`);
```

**쉬운 설명:**
- 설정된 포트에서 수신하는 WebSocket 서버를 생성합니다
- 서버가 준비됐다고 메시지를 표시합니다

---

### 클라이언트 연결 시 (31-78줄)

```javascript
wss.on('connection', (ws) => {
    // 이 클라이언트를 위한 고유 식별자 생성
    const clientId = uuidv4();  // 예: "550e8400-e29b-41d4-a716-446655440000"

    // 클라이언트를 목록에 추가
    clients.set(clientId, {
        ws: ws,              // WebSocket 연결
        roomId: null,        // 아직 방에 없음
        playerName: 'Player', // 기본 이름
        lastHeartbeat: Date.now()  // 마지막 활동
    });

    console.log(`[SERVER] Client connected: ${clientId}`);

    // ID와 함께 "welcome" 메시지 보내기
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // 다른 모든 클라이언트에게 새 플레이어 도착 알림
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);  // 새 클라이언트 자신은 제외

    // 사용 가능한 방 목록 보내기
    sendRoomList(ws);

    // 이 클라이언트로부터 메시지를 받으면...
    ws.on('message', (data) => {
        try {
            const message = JSON.parse(data.toString());  // 텍스트를 객체로 변환
            handleMessage(clientId, message);              // 메시지 처리
        } catch (e) {
            console.error(`[SERVER] Parse error: ${e.message}`);
        }
    });

    // 이 클라이언트가 연결 해제하면...
    ws.on('close', () => {
        handleDisconnect(clientId);
    });

    // 이 클라이언트에 에러가 발생하면...
    ws.on('error', (error) => {
        console.error(`[SERVER] Client error (${clientId}): ${error.message}`);
    });

    // ping에 대한 응답 (클라이언트가 아직 있는지 확인)
    ws.on('pong', () => {
        const client = clients.get(clientId);
        if (client) {
            client.lastHeartbeat = Date.now();
        }
    });
});
```

**쉬운 설명:**
1. 새 플레이어가 연결합니다
2. 고유 ID를 생성합니다 (배지 번호 같은 것)
3. 플레이어 목록에 추가합니다
4. "환영합니다! 당신의 ID는 XXX입니다" 라고 알려줍니다 (`welcome` 메시지)
5. 다른 사람들에게 "야, 누군가 왔어!" 라고 알립니다 (`peer-connected` 메시지)
6. 사용 가능한 방 목록을 보냅니다
7. 메시지를 보내거나 연결 해제할 때 무슨 일이 일어나는지 설정합니다

---

### 메시지 라우팅 (84-231줄)

```javascript
function handleMessage(clientId, message) {
    const { type, senderId, data } = message;
    message.senderId = clientId;  // ID가 올바른지 확인

    console.log(`[SERVER] Message from ${clientId}: ${type}`);

    switch (type) {
        // === 방 관리 ===
        case 'room-available':      // 새 방 생성
            handleRoomAvailable(clientId, data);
            break;

        case 'room-closed':         // 방 닫기
            handleRoomClosed(clientId, data);
            break;

        case 'room-join':           // 방 참가
            handleRoomJoin(clientId, data);
            break;

        case 'room-leave':          // 방 나가기
            handleRoomLeave(clientId, data);
            break;

        // === VR 동기화 (플레이어 위치) ===
        case 'vr-position':
        case 'position':
            broadcastToRoom(clientId, message);  // 같은 방의 모두에게 보내기
            break;

        // === 화이트보드 ===
        case 'whiteboard-draw':
        case 'whiteboard-batch':
        case 'whiteboard-clear':
            broadcastToRoom(clientId, message);  // 같은 방의 모두에게 보내기
            break;

        // === WEBRTC 시그널링 (음성 채팅용) ===
        case 'webrtc-offer':
            handleWebRTCOffer(clientId, data);   // 특정 클라이언트에게 보내기
            break;

        case 'webrtc-answer':
            handleWebRTCAnswer(clientId, data);
            break;

        case 'webrtc-ice-candidate':
            handleWebRTCIceCandidate(clientId, data);
            break;

        // ... 다른 메시지 유형 ...

        default:
            // 알 수 없는 메시지: 방이나 모두에게 보내기
            const client = clients.get(clientId);
            if (client && client.roomId) {
                broadcastToRoom(clientId, message);
            } else {
                broadcast(message, clientId);
            }
    }
}
```

**쉬운 설명:**
- 서버는 무엇을 해야 할지 알기 위해 메시지의 `type`을 확인합니다
- 어떤 메시지는 **같은 방의 모든 플레이어**에게 갑니다 (`broadcastToRoom`)
- 어떤 메시지는 **특정 플레이어**에게 갑니다 (WebRTC)
- 어떤 메시지는 **모든 사람**에게 갑니다 (`broadcast`)

---

### 방 관리 - 방 생성 (237-270줄)

```javascript
function handleRoomAvailable(clientId, dataStr) {
    try {
        // JSON 데이터를 객체로 변환
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // 방 정보 생성
        const roomInfo = {
            roomId: data.roomId,           // 방 고유 ID (예: "ABC123")
            hostId: clientId,               // 누가 방을 만들었는지
            roomName: data.roomName || `Room ${data.roomId}`,
            roomType: data.roomType || 0,
            playerCount: 1,                 // 생성자가 안에 있음
            maxPlayers: data.maxPlayers || 10,
            createdAt: Date.now()
        };

        // 목록에 방 추가
        rooms.set(data.roomId, roomInfo);

        // 생성자는 이제 이 방에 있음
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
        }

        console.log(`[SERVER] Room created: ${data.roomId} by ${clientId}`);

        // 모두에게 방 목록 업데이트
        broadcastRoomList();

        // 모두에게 새 방이 있다고 알림
        broadcast({
            type: 'room-available',
            senderId: clientId,
            data: JSON.stringify(roomInfo)
        });

    } catch (e) {
        console.error(`[SERVER] handleRoomAvailable error: ${e.message}`);
    }
}
```

**쉬운 설명:**
- 플레이어가 방을 만들려고 합니다
- 방의 모든 정보가 담긴 객체를 생성합니다
- 목록에 방을 추가합니다
- 모두에게 "새 방이 생겼어!" 라고 알립니다

---

### 방 관리 - 방 참가 (295-331줄)

```javascript
function handleRoomJoin(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);

        // 방이 존재하는지 확인
        if (!room) {
            sendError(clientId, `Room ${data.roomId} not found`);
            return;
        }

        // 방이 꽉 찼는지 확인
        if (room.playerCount >= room.maxPlayers) {
            sendError(clientId, 'Room is full');
            return;
        }

        // 클라이언트 정보 업데이트
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
            client.playerName = data.playerName || 'Player';
        }

        // 플레이어 수 증가
        room.playerCount++;

        console.log(`[SERVER] Player ${clientId} joined room ${data.roomId}`);

        // 방의 다른 플레이어들에게 알림
        broadcastToRoom(clientId, {
            type: 'room-join',
            senderId: clientId,
            data: JSON.stringify(data)
        });

        // 방 목록 업데이트
        broadcastRoomList();

    } catch (e) {
        console.error(`[SERVER] handleRoomJoin error: ${e.message}`);
    }
}
```

**쉬운 설명:**
- 플레이어가 방에 참가하려고 합니다
- 방이 존재하고 꽉 차지 않았는지 확인합니다
- 플레이어를 방에 추가합니다
- 방의 다른 플레이어들에게 "야, 누군가 온다!" 라고 알립니다

---

### 방 관리 - 방 나가기 (334-361줄)

```javascript
function handleRoomLeave(clientId, dataStr) {
    try {
        // JSON 데이터를 객체로 변환
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // 방 찾기
        const room = rooms.get(data.roomId);

        // 플레이어 수 감소 (최소 0)
        if (room) {
            room.playerCount = Math.max(0, room.playerCount - 1);
        }

        // 방에서 플레이어 제거
        const client = clients.get(clientId);
        if (client) {
            client.roomId = null;  // 플레이어가 더 이상 어떤 방에도 없음
        }

        console.log(`[SERVER] Player ${clientId} left room ${data.roomId}`);

        // 방의 다른 플레이어들에게 알림
        broadcastToRoom(clientId, {
            type: 'room-leave',
            senderId: clientId,
            data: JSON.stringify(data)
        });

        // 모두에게 방 목록 업데이트
        broadcastRoomList();

    } catch (e) {
        console.error(`[SERVER] handleRoomLeave error: ${e.message}`);
    }
}
```

**쉬운 설명:**
- 플레이어가 방을 나가려고 합니다
- 방의 플레이어 카운터를 감소시킵니다
- 더 이상 방에 없음을 나타내기 위해 `roomId = null`을 설정합니다
- 다른 사람들에게 "야, 누군가 나갔어!" 라고 알립니다

---

### 플레이어 연결 해제 관리 (379-421줄)

```javascript
function handleDisconnect(clientId) {
    const client = clients.get(clientId);

    if (client) {
        // 플레이어가 방에 있었다면...
        if (client.roomId) {
            const room = rooms.get(client.roomId);

            if (room) {
                // 방의 호스트였다면...
                if (room.hostId === clientId) {
                    // ...방 전체를 닫습니다!
                    rooms.delete(client.roomId);

                    // 모두에게 방이 닫혔다고 알림
                    broadcast({
                        type: 'room-closed',
                        senderId: clientId,
                        data: JSON.stringify({ roomId: client.roomId })
                    });
                } else {
                    // 일반 플레이어였다면, 카운터만 감소
                    room.playerCount = Math.max(0, room.playerCount - 1);
                }
            }

            // 같은 방의 플레이어들에게만 알림
            broadcastToRoom(clientId, {
                type: 'room-leave',
                senderId: clientId,
                data: JSON.stringify({
                    roomId: client.roomId,
                    playerId: clientId
                })
            });
        }
    }

    // 목록에서 클라이언트 삭제
    clients.delete(clientId);

    // 모두에게 플레이어가 연결 해제됐다고 알림
    broadcast({
        type: 'peer-disconnected',
        senderId: clientId
    });

    // 방 목록 업데이트
    broadcastRoomList();

    console.log(`[SERVER] Client disconnected: ${clientId}`);
}
```

**쉬운 설명:**
- 플레이어가 연결 해제될 때 (게임 종료, 인터넷 끊김 등)
- **호스트**였다면 → 방이 **모두에게 닫힙니다**
- 일반 플레이어였다면 → 방에서 제거됩니다
- 모두에게 떠났다고 알립니다
- Zoom 미팅에서 누군가 나갈 때와 같습니다

---

### 핵심 함수: broadcastToRoom (763-791줄)

```javascript
/**
 * 같은 방의 클라이언트에게만 메시지를 보냅니다
 * 성능을 위해 가장 중요한 함수입니다!
 */
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);

    // 보내는 사람이 방에 없으면, 아무것도 안 함
    if (!sender || !sender.roomId) {
        return;
    }

    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);

    let recipientCount = 0;

    // 모든 클라이언트를 순회
    clients.forEach((client, clientId) => {
        // 다음 조건을 모두 만족할 때만 보내기:
        // 1. 보내는 사람 자신이 아님
        // 2. 같은 방에 있음
        // 3. 연결이 열려 있음
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {

            client.ws.send(messageStr);
            recipientCount++;
        }
    });

    // 디버그용 로그
    if (message.type && (message.type.includes('whiteboard') || message.type.includes('obj-'))) {
        console.log(`[Room:${roomId}] ${message.type} from ${senderId} → ${recipientCount} clients`);
    }
}
```

**쉬운 설명:**
- 이 함수는 정말 중요합니다!
- 같은 방의 플레이어에게만 메시지를 보냅니다
- 모든 사람의 위치를 모든 사람에게 보내는 것을 피합니다
- 10개의 방에 100명의 플레이어가 있다고 상상해보세요: 이것 없이는 각 플레이어가 9명 대신 99명의 위치를 받게 됩니다!

---

### Heartbeat - 연결 확인 (830-847줄)

```javascript
// 30초마다 클라이언트가 아직 있는지 확인
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    // 모든 클라이언트에게 "ping" 보내기
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();  // "아직 있어?"
        }
    });

    // 오래 응답하지 않은 클라이언트 확인
    clients.forEach((client, clientId) => {
        // 60초 동안 응답 없으면 (2 x 30초)...
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            console.log(`[SERVER] Client timeout: ${clientId}`);
            client.ws.terminate();      // 연결 닫기
            handleDisconnect(clientId); // 정리
        }
    });

}, HEARTBEAT_INTERVAL);  // 30초마다
```

**쉬운 설명:**
- 서버가 30초마다 "Ping!" 합니다
- 클라이언트가 "Pong!"으로 응답합니다
- 클라이언트가 60초 동안 응답하지 않으면, 연결 해제된 것으로 간주합니다
- 정기적으로 "아직 있어?"라고 부르는 것과 같습니다

---

## 5. db.js - 데이터베이스

**경로:** `LocalServ/Server/db.js`

이 파일은 사용자를 저장하기 위한 MariaDB 데이터베이스 연결을 관리합니다.

```javascript
const mysql = require('mysql2/promise');  // MySQL/MariaDB용 라이브러리

// 연결 "풀" 생성 (재사용 가능한 여러 연결)
const pool = mysql.createPool({
    host: process.env.DB_HOST || 'localhost',        // 데이터베이스 서버 주소
    port: process.env.DB_PORT || 3306,               // 포트 (MySQL 기본 3306)
    user: process.env.DB_USER || 'root',             // 사용자 이름
    password: process.env.DB_PASSWORD || 'JJkk2812', // 비밀번호
    database: process.env.DB_NAME || 'vr_meeting',   // 데이터베이스 이름
    waitForConnections: true,   // 모든 연결이 사용 중이면 대기
    connectionLimit: 10,        // 최대 10개 동시 연결
    queueLimit: 0               // 대기열 제한 없음
});

// 시작 시 연결 테스트
pool.getConnection()
    .then(conn => {
        console.log('[DB] Connected to MariaDB');
        conn.release();  // 연결 해제
    })
    .catch(err => {
        console.error('[DB] Connection failed:', err.message);
    });

module.exports = pool;  // 다른 파일에서 사용할 수 있도록 pool 내보내기
```

**쉬운 설명:**
- 이 파일은 데이터베이스 연결을 생성합니다
- "풀"은 여러 전화선을 갖는 것과 같습니다: 동시에 여러 통화 가능
- 환경 변수(`process.env.XXX`)를 사용하면 코드를 수정하지 않고 설정을 변경할 수 있습니다

---

## 6. 모든 것이 어떻게 함께 작동하는지

### 전체 흐름 다이어그램

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              초기 연결                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Unity 시작                                                                 │
│       │                                                                     │
│       ▼                                                                     │
│  VRNetworkManager.Start()                                                   │
│       │                                                                     │
│       ▼                                                                     │
│  Connect() ──── WebSocket ────────────────────────► 서버                    │
│       │         ws://localhost:8080                    │                    │
│       │                                                │                    │
│       │                                                ▼                    │
│       │                                          고유 ID 생성               │
│       │                                          (예: "abc-123-...")        │
│       │                                                │                    │
│       │         ◄────── "welcome" 메시지 ──────────────┘                    │
│       │         {type:"welcome", senderId:"abc-123-..."}                    │
│       ▼                                                                     │
│  LocalId = "abc-123-..."                                                    │
│  IsConnected = true                                                         │
│  OnConnected?.Invoke()  ──────► 다른 스크립트에 알림                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                              방 참가                                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  플레이어가 "ABC123 방 참가" 클릭                                            │
│       │                                                                     │
│       ▼                                                                     │
│  VRRoomManager.JoinRoom("ABC123")                                           │
│       │                                                                     │
│       ▼                                                                     │
│  VRNetworkManager.Send("room-join", {roomId:"ABC123", playerName:"철수"})   │
│       │                                                                     │
│       │         ──── 메시지 ────────────────────────► 서버                   │
│       │                                                    │                │
│       │                                                    ▼                │
│       │                                          handleRoomJoin()           │
│       │                                          - 방 존재 확인             │
│       │                                          - 꽉 찼는지 확인           │
│       │                                          - 플레이어 추가            │
│       │                                                    │                │
│       │                                                    ▼                │
│       │                                          broadcastToRoom()          │
│       │                                          (방의 다른 사람들에게)      │
│       │                                                    │                │
│       │         ◄──── "room-join" 메시지 ─────────────────┘                 │
│       ▼                                                                     │
│  OnPlayerJoined?.Invoke(data)  ──────► VRGameManager가 아바타 생성          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                            실시간 동기화                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  VRGameManager.Update() (초당 30번)                                         │
│       │                                                                     │
│       ▼                                                                     │
│  위치/회전이 변경되면:                                                       │
│       │                                                                     │
│       ▼                                                                     │
│  VRNetworkManager.Send("vr-position", {                                     │
│      roomId: "ABC123",                                                      │
│      posX: 1.5, posY: 0, posZ: 3.2,                                        │
│      headPosX: 1.5, headPosY: 1.7, headPosZ: 3.2,                          │
│      headRotX: 0, headRotY: 0.7, headRotZ: 0, headRotW: 0.7,               │
│      ... (손도 포함)                                                        │
│  })                                                                         │
│       │                                                                     │
│       │         ──── 메시지 ────────────────────────► 서버                   │
│       │                                                    │                │
│       │                                                    ▼                │
│       │                                          broadcastToRoom()          │
│       │                                          (ABC123 방에만)             │
│       │                                                    │                │
│       │                                          ┌─────────┴─────────┐      │
│       │                                          ▼                   ▼      │
│       │         ◄──── 메시지 ──────────────── 플레이어2           플레이어3  │
│       │                                                                     │
│       ▼                                                                     │
│  OnMessageReceived  ──────► VRGameManager.HandlePositionMessage()           │
│                             - 플레이어의 아바타 찾기                         │
│                             - 위치 업데이트                                  │
│                             - 부드러움을 위해 보간                           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 요약

| 구성 요소 | 파일 | 역할 |
|-----------|------|------|
| Unity 클라이언트 | `VRNetworkManager.cs` | 서버에 연결, 메시지 송수신 |
| 서버 | `server.js` | 메시지 수신 및 재분배 |
| 데이터베이스 | `db.js` | 사용자 저장 |

**핵심 개념 3가지:**

1. **WebSocket** = 즉시 통신하기 위해 열린 상태로 유지되는 파이프
2. **broadcastToRoom** = 같은 방의 플레이어에게만 메시지 보내기 (성능에 매우 중요!)
3. **Exponential Backoff** = 연결 실패 시 재시도 전 점점 더 오래 대기

---

*WebSocket_VR 프로젝트를 위해 생성된 문서*
