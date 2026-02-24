# CLAUDE.md

## Project Overview
Unity 6000.2.14f1 VR multiplayer meeting room. WebSocket (NativeWebSocket) + WebRTC voice. OpenXR (Quest, PCVR, Desktop).

## Tech Stack
- **Engine:** Unity 6000.2.14f1 | **Multiplayer:** WebSocket + WebRTC | **Database:** MariaDB via Node.js (never direct)
- **Platforms:** Quest, PCVR, Desktop | **Folder typo:** `Assets/Scrips/` (preserved)

## Build & Server
```bash
cd Server/ && npm install && npm run dev   # Dev server with auto-reload
```
**Server URL:** `ws://localhost:8080` | **Scenes:** Bootstrap (0), Meet (1) | **Testing:** ParrelSync

## Project Structure
```
Assets/Scrips/
├── Network/          VRNetworkManager.cs, VRRoomManager.cs, VRGameManager.cs
├── VR/               BootstrapManager.cs, VRPlayerController.cs, DesktopPlayerController.cs
├── WebRTC/           VoiceChatManager.cs, WebRTCPeerManager.cs, MicrophoneManager.cs
├── WhiteBoard/       Whiteboard.cs, WhiteboardDrawingSurface.cs, WhiteboardMarker.cs, DesktopWhiteboardDrawer.cs
├── Interaction/      LaserPointer.cs, LaserPointerData.cs
├── Sharing/          ScreenShareManager.cs, FileShareManager.cs, FilePresentationManager.cs
├── Avatar/           AvatarCustomization.cs, AvatarColorTarget.cs
├── Auth/             AuthManager.cs, AuthUI.cs (implemented, not yet used)
├── Recording/        RecordingManager.cs, SpectatorCameraController.cs, FFmpegEncoder.cs, AudioCapture.cs
├── Audio/            SoundManager.cs, AudioMuteZone.cs, AmbienceManager.cs
├── UI/MainMenu/      MainMenuManager.cs, MainMenuSettings.cs, MainMenuOptionsUI.cs
├── UI/Menu/          VRMenuUI.cs, VRMenuToggle.cs
└── Debug/            DebugManager.cs

Server/
├── server.js         Main WebSocket server
└── src/
    ├── database.js   MariaDB pool connection
    └── auth.js       bcrypt + JWT authentication

Assets/Prefabs/Unity/ LocalPlayer, RemoteVRPlayer, DesktopPlayer, WhiteboardComplete, XR Origin Hands
Assets/Scenes/        Bootstrap.unity (persistent), Meet.unity (additive)
```

## Architecture
**Scene Flow:** Bootstrap (singletons + DontDestroyOnLoad) → Meet (additive)

### Core Events (subscribe in OnEnable, unsubscribe in OnDisable)
```csharp
// VRNetworkManager
OnConnected, OnDisconnected, OnPeerConnected, OnPeerDisconnected, OnMessageReceived, OnConnectionError

// VRRoomManager
OnRoomCreated, OnRoomJoined, OnRoomLeft, OnPlayerJoined(VRPlayerData), OnPlayerLeft, OnRoomTypeChanged, OnAvatarUpdated

// VRGameManager
OnLocalPlayerSpawned, OnRemotePlayerSpawned, OnRemotePlayerDespawned
GetLocalPlayer(), GetRemotePlayer(id), GetRemotePlayerHead(id)

// VoiceChatManager
OnVoiceChatReady, OnPeerVoiceConnected, OnPeerVoiceDisconnected

// RecordingManager
OnRecordingStarted, OnRecordingStopped, OnStateChanged, OnMarkerAdded

// AuthManager (implemented, not yet integrated)
OnLoginSuccess, OnRegisterSuccess, OnAuthError, OnLogout
```

### Network Protocol
```csharp
[Serializable] public class NetworkMessage { string type, senderId, data; } // JsonUtility, no nested objects!
```

| Category | Message Types |
|----------|---------------|
| Connection | `welcome`, `peer-connected`, `peer-disconnected` |
| Rooms | `room-join`, `room-welcome`, `room-leave`, `room-list`, `room-teleport`, `player-name-update`, `avatar-update` |
| VR Sync | `vr-position` (30Hz) |
| Voice | `webrtc-offer`, `webrtc-answer`, `webrtc-ice-candidate` |
| Whiteboard | `whiteboard-batch`, `whiteboard-clear`, `whiteboard-request`, `whiteboard-state` |
| Sharing | `screen-share-*`, `file-share-*`, `file-present-*`, `laser-pointer` |
| Recording | `recording-status`, `recording-marker` |
| Auth | `auth-login`, `auth-register`, `auth-verify`, `auth-logout`, `auth-response` |

### Key Systems

**Room System:** RoomType enum (`Lobby`, `MeetingRoomA`, `MeetingRoomB`), 6-char codes, host authority

**VR Sync (30Hz):** Movement threshold 0.01m/1°, interpolation 15, head+hands detached for world-space

**Voice Chat:** Mesh topology (smaller ID initiates), STUN+TURN, spatial audio on head, push-to-talk V key

**Whiteboard (3 layers):**
1. `Whiteboard.cs` - fond blanc + mode presentation
2. `WhiteboardDrawingSurface.cs` - transparent, network only (ne dessine pas!)
3. `WhiteboardMarker` (VR) / `DesktopWhiteboardDrawer` (Desktop) - local drawing

Config: 2048x2048, Sprites/Default shader, 33ms send rate, blue default color

**Screen Share:** 854x480 @ 3fps, JPEG 50%, VR+Desktop, displays on whiteboard presentation mode

**File Share:** 10MB max, extensions: pdf/doc/docx/xls/xlsx/png/jpg/jpeg/gif

**Laser Pointer:** VR=A button, Desktop=L key, 10Hz sync, red LineRenderer+dot

**Offline Mode:** Test without server - set `offlineMode=true` in VRNetworkManager Inspector

## Offline Mode (Debug)
In `VRNetworkManager` Inspector:
```
[Header("Debug / Offline Mode")]
offlineMode = true              // Skip server connection
offlineAutoCreateRoom = true    // Auto-create room on start
offlineRoomType = MeetingRoomA  // Room type to create
```
Simulates connection + room creation. All network sends are silently ignored.

## Recording System (VR-Optimized)

**Architecture:** 3-stage async pipeline to avoid VR motion sickness

```
Main Thread          Encode Thread       Write Thread
RequestFrame() ──▶  RGB → TGA ──────▶  File.Write()
  (~0.1ms)          (background)        (background)
     ↑
AsyncGPUReadback (non-blocking GPU read)
```

**Key files:**
- `SpectatorCameraController.cs` - AsyncGPUReadback, buffer pooling, camera in Meet scene
- `RecordingManager.cs` - Pipeline orchestration, ConcurrentQueues, host-only recording
- `FFmpegEncoder.cs` - TGA→MP4 encoding via FFmpeg
- `RecordingData.cs` - Settings, metadata, markers

**Settings (RecordingSettings):**
```csharp
width = 1920, height = 1080, frameRate = 30
jpegQuality = 85, captureAudio = true
outputFolder = "Recordings"
```

**Output:** TGA frames + audio.wav → FFmpeg → recording.mp4

**Markers:** Important, Question, Todo, Idea (synced across clients)

**Note:** SpectatorCamera must be in Meet scene (auto-detected, prioritizes Meet over Bootstrap)

## Controls

| Mode | Movement | Look | Actions |
|------|----------|------|---------|
| VR | Teleport | Head | A=Laser, Grab, V=Push-to-talk |
| Desktop | WASD+Shift | Right-click drag | L=Laser, Left-click=Draw |

## Code Conventions
- Events: subscribe `OnEnable`, unsubscribe `OnDisable`
- Serialization: `JsonUtility` + `[Serializable]`, **no nested objects**
- Logging: `DebugManager.Log(msg, DebugCategory.Network)` or `[SystemName]` prefix
- GC: cache message objects (`_cachedPositionData`)

## Settings (MainMenuSettings.cs)
Audio: MasterVolume, VoiceVolume, Microphone | Graphics: Quality, Resolution, Fullscreen
VR: TurnMode (0=Snap/1=Smooth), SnapAngle, SmoothTurnSpeed | Desktop: MouseSensitivity, InvertY

## Database & Auth (Implemented, Not Integrated)

**Architecture:** Unity ←WebSocket→ Node.js ←mariadb→ MariaDB (never direct connection!)

**Status:** Code exists but auth doesn't gate any features yet. Login/guest both have same access.

**Server files (exist):**
- `Server/src/database.js` - MariaDB pool (requires .env config)
- `Server/src/auth.js` - bcrypt 12 rounds, JWT 24h, rate limiting 5/min

**Unity files (exist):**
- `Assets/Scrips/Auth/AuthManager.cs` - Login, Register, Logout, Token verify
- `Assets/Scrips/Auth/AuthUI.cs` - Login/Register panels, guest mode, skipAuthInEditor

**Future integration ideas:**
- Private rooms (auth required to join)
- Meeting history linked to account
- Persistent avatar config in DB
- File upload quotas per user
- Admin/moderator roles

## Package Dependencies
| Package | Purpose |
|---------|---------|
| `com.endel.nativewebsocket` | WebSocket |
| `com.unity.webrtc` 3.0.0 | Voice |
| `com.unity.xr.interaction.toolkit` 3.2.2 | VR |
| `com.unity.xr.openxr` 1.16.1 | OpenXR |
| `com.unity.xr.hands` 1.7.2 | Hand tracking |
| `com.unity.render-pipelines.universal` 17.2.0 | URP |
| `com.veriorpies.parrelsync` | Multi-instance testing |

## Feature Status

| Done | Implemented (not used) | Planned |
|------|------------------------|---------|
| WebSocket + reconnect, WebRTC voice 3D, Avatar sync/customization, Whiteboard, Desktop mode, Main menu + settings, Screen share, File share/presentation, Laser pointer, VR Menu, Sound system, Offline mode, Recording (VR-optimized) | Auth (login/register/guest) | SSO, E2E encryption, GDPR, Admin panel, Advanced avatars, Calendar, Meeting history |

## Important Notes
- **XR Layers:** Teleport on bit 31 only, Grab must NOT include Teleport layer
- **Remote players:** Head/hands detached from hierarchy (world-space targets)
- **Late joiners:** Request state via `*-request` messages, receive `*-state`
- **Room-scoped:** All sync messages include `roomId`
- **Recording:** Requires FFmpeg in PATH, SpectatorCamera in Meet scene, host-only
- **Recording VR:** Uses AsyncGPUReadback + background threads to avoid motion sickness

## Recent Changes (Session)

### Auth Flow Integration (DONE)
Le bouton Start affiche maintenant l'écran d'authentification avant de charger le jeu:
```
Main Menu → [Start] → Auth Screen → [Login/Register/Guest] → Loading → Meet
```
**Fichiers modifiés:**
- `AuthUI.cs` - Ajout singleton, event `OnAuthComplete`, méthode `Show()`
- `MainMenuManager.cs` - `OnStartClicked()` affiche AuthUI, écoute `OnAuthComplete`

**Note:** AuthUI est dans `MainMenuUI/Background/AuthPanel`

### VR Canvas Adapter (DONE)
`VRCanvasAdapter.cs` - Adapte les Canvas pour VR (Screen Space → World Space)
- À ajouter sur le Canvas "Loading screen" dans Bootstrap

---

## TODO: Launch Loading Screen (À IMPLÉMENTER)

### Objectif
Afficher un écran de chargement avec logo et barre de progression au lancement de l'application, avant d'afficher le Main Menu.

### Flow cible
```
App Launch
    ↓
┌─────────────────────────────────────┐
│         LOADING SCREEN              │
│            [LOGO]                   │
│     ════════════════════  45%       │
│      Connexion au serveur...        │
└─────────────────────────────────────┘
    ↓ (Initialisation terminée)
┌─────────────────────────────────────┐
│          MAIN MENU                  │
│          [START] [OPTIONS] [QUIT]   │
└─────────────────────────────────────┘
```

### Étapes d'initialisation avec progression

| Étape | Description | Progression |
|-------|-------------|-------------|
| 1 | Initialisation XR | 0% → 20% |
| 2 | Connexion serveur WebSocket | 20% → 50% |
| 3 | Vérification token auth (si existant) | 50% → 70% |
| 4 | Chargement des paramètres utilisateur | 70% → 90% |
| 5 | Finalisation | 90% → 100% |

### Fichier à créer: `Assets/Scrips/UI/LaunchLoadingScreen.cs`

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gère l'écran de chargement au lancement de l'application.
/// Affiche la progression de l'initialisation des systèmes.
/// </summary>
public class LaunchLoadingScreen : MonoBehaviour
{
    public static LaunchLoadingScreen Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Image du logo de l'application")]
    public Image logoImage;

    [Tooltip("Image de remplissage de la barre de progression")]
    public Image progressBarFill;

    [Tooltip("Texte affichant le pourcentage")]
    public TextMeshProUGUI progressText;

    [Tooltip("Texte affichant l'étape actuelle")]
    public TextMeshProUGUI statusText;

    [Tooltip("Texte de version (optionnel)")]
    public TextMeshProUGUI versionText;

    [Header("Settings")]
    [Tooltip("Temps minimum d'affichage du loading (secondes)")]
    public float minimumDisplayTime = 2f;

    [Tooltip("Vitesse d'animation de la barre (lerp)")]
    public float progressAnimSpeed = 3f;

    [Tooltip("Timeout pour la connexion serveur (secondes)")]
    public float networkTimeout = 10f;

    [Header("Fade")]
    public CanvasGroup canvasGroup;
    public float fadeDuration = 0.5f;

    // État
    private float _targetProgress = 0f;
    private float _currentProgress = 0f;
    private float _startTime;
    private bool _isComplete = false;

    // Event déclenché quand le loading est terminé
    public static event Action OnLoadingComplete;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Afficher immédiatement
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        gameObject.SetActive(true);

        // Version
        if (versionText != null)
            versionText.text = $"v{Application.version}";
    }

    void Start()
    {
        _startTime = Time.time;
        StartCoroutine(RunInitializationSequence());
    }

    void Update()
    {
        // Animation fluide de la barre de progression
        if (_currentProgress < _targetProgress)
        {
            _currentProgress = Mathf.Lerp(_currentProgress, _targetProgress, Time.deltaTime * progressAnimSpeed);
            UpdateProgressUI(_currentProgress);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Séquence principale d'initialisation.
    /// </summary>
    IEnumerator RunInitializationSequence()
    {
        // Étape 1: XR (0-20%)
        yield return StartCoroutine(InitializeXR());

        // Étape 2: Network (20-50%)
        yield return StartCoroutine(InitializeNetwork());

        // Étape 3: Auth check (50-70%)
        yield return StartCoroutine(CheckAuthentication());

        // Étape 4: Settings (70-90%)
        yield return StartCoroutine(LoadSettings());

        // Étape 5: Finalize (90-100%)
        yield return StartCoroutine(Finalize());

        // Attendre le temps minimum d'affichage
        float elapsed = Time.time - _startTime;
        if (elapsed < minimumDisplayTime)
        {
            yield return new WaitForSeconds(minimumDisplayTime - elapsed);
        }

        // Terminé
        _isComplete = true;
        yield return StartCoroutine(FadeOut());

        OnLoadingComplete?.Invoke();
        gameObject.SetActive(false);
    }

    #region Initialization Steps

    IEnumerator InitializeXR()
    {
        SetStatus("Initialisation VR...");
        SetProgress(0f);

        // Attendre que XR soit prêt (BootstrapManager gère ça)
        yield return new WaitForSeconds(0.3f);

        // Vérifier si XR est actif
        bool xrReady = UnityEngine.XR.XRSettings.isDeviceActive;
        Debug.Log($"[LaunchLoading] XR Ready: {xrReady}");

        SetProgress(0.2f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator InitializeNetwork()
    {
        SetStatus("Connexion au serveur...");
        SetProgress(0.2f);

        // Attendre la connexion WebSocket
        float timeout = networkTimeout;
        float elapsed = 0f;

        while (!VRNetworkManager.IsConnected && elapsed < timeout)
        {
            elapsed += Time.deltaTime;

            // Progression graduelle pendant l'attente
            float networkProgress = 0.2f + (elapsed / timeout) * 0.25f;
            SetProgress(Mathf.Min(networkProgress, 0.45f));

            yield return null;
        }

        if (VRNetworkManager.IsConnected)
        {
            Debug.Log("[LaunchLoading] Network connected");
            SetProgress(0.5f);
        }
        else
        {
            Debug.LogWarning("[LaunchLoading] Network timeout - continuing anyway");
            SetStatus("Mode hors-ligne...");
            SetProgress(0.5f);
            yield return new WaitForSeconds(0.5f);
        }
    }

    IEnumerator CheckAuthentication()
    {
        SetStatus("Vérification...");
        SetProgress(0.5f);

        // Vérifier si un token existe
        if (AuthManager.Instance != null && !string.IsNullOrEmpty(AuthManager.Instance.Token))
        {
            SetStatus("Vérification du compte...");
            // Le token sera vérifié automatiquement par AuthManager.OnNetworkConnected
            yield return new WaitForSeconds(0.5f);
        }

        SetProgress(0.7f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator LoadSettings()
    {
        SetStatus("Chargement paramètres...");
        SetProgress(0.7f);

        // Charger les paramètres utilisateur
        if (MainMenuSettings.Instance != null)
        {
            // Les settings sont chargés automatiquement dans Awake
            yield return new WaitForSeconds(0.2f);
        }

        SetProgress(0.9f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator Finalize()
    {
        SetStatus("Prêt !");
        SetProgress(1f);
        yield return new WaitForSeconds(0.3f);
    }

    #endregion

    #region UI Updates

    void SetProgress(float progress)
    {
        _targetProgress = Mathf.Clamp01(progress);
    }

    void UpdateProgressUI(float progress)
    {
        if (progressBarFill != null)
            progressBarFill.fillAmount = progress;

        if (progressText != null)
            progressText.text = $"{Mathf.RoundToInt(progress * 100)}%";
    }

    void SetStatus(string status)
    {
        if (statusText != null)
            statusText.text = status;

        Debug.Log($"[LaunchLoading] {status}");
    }

    IEnumerator FadeOut()
    {
        if (canvasGroup == null)
            yield break;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = 1f - (elapsed / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = 0f;
    }

    #endregion

    /// <summary>
    /// Permet de forcer la complétion (pour tests).
    /// </summary>
    public void ForceComplete()
    {
        StopAllCoroutines();
        _isComplete = true;
        OnLoadingComplete?.Invoke();
        gameObject.SetActive(false);
    }
}
```

### Modifications dans `BootstrapManager.cs`

Ajouter dans les variables:
```csharp
[Header("Launch Loading")]
[Tooltip("Écran de chargement au lancement (avec LaunchLoadingScreen)")]
public GameObject launchLoadingScreen;

private bool _launchComplete = false;
```

Modifier `Start()`:
```csharp
void Start()
{
    // S'abonner à l'event de fin de loading
    LaunchLoadingScreen.OnLoadingComplete += OnLaunchLoadingComplete;

    // Cacher le main menu pendant le loading initial
    if (MainMenuManager.Instance != null && MainMenuManager.Instance.mainPanel != null)
    {
        MainMenuManager.Instance.mainPanel.SetActive(false);
    }

    // Le LaunchLoadingScreen se lance automatiquement
    // Une fois terminé, OnLaunchLoadingComplete sera appelé
}

void OnLaunchLoadingComplete()
{
    _launchComplete = true;
    LaunchLoadingScreen.OnLoadingComplete -= OnLaunchLoadingComplete;

    // Afficher le main menu
    if (MainMenuManager.Instance != null)
    {
        MainMenuManager.Instance.ShowMainPanel();
    }

    Debug.Log("[Bootstrap] Launch loading complete - showing main menu");
}
```

### Structure UI dans Unity (modifier "Loading screen" existant)

```
Loading screen (Canvas)
├── CanvasGroup (pour fade)
├── Background (Image - noir ou dégradé)
├── LogoContainer (RectTransform - centré en haut)
│   └── Logo (Image - votre logo, 300x300 environ)
├── ProgressContainer (RectTransform - centré)
│   ├── ProgressBarBG (Image - gris foncé, 400x20)
│   │   └── ProgressBarFill (Image - couleur accent, Fill method: Horizontal)
│   └── ProgressText (TMP - "0%" - sous la barre)
├── StatusText (TMP - "Initialisation..." - sous progress)
└── VersionText (TMP - "v1.0.0" - coin inférieur droit)

+ Ajouter composant: LaunchLoadingScreen
+ Ajouter composant: VRCanvasAdapter (pour VR)
+ Ajouter composant: CanvasGroup (pour fade)
```

### Références à assigner dans l'Inspector (LaunchLoadingScreen)

| Champ | GameObject |
|-------|------------|
| logoImage | Logo |
| progressBarFill | ProgressBarFill |
| progressText | ProgressText |
| statusText | StatusText |
| versionText | VersionText |
| canvasGroup | Loading screen (root) |

### Ordre d'exécution

1. **Bootstrap.Awake()** - Initialise les singletons, XR
2. **LaunchLoadingScreen.Awake()** - S'affiche immédiatement
3. **LaunchLoadingScreen.Start()** - Lance la séquence d'init
4. **MainMenuManager.Start()** - Main panel caché pendant loading
5. **LaunchLoadingScreen termine** - Déclenche OnLoadingComplete
6. **BootstrapManager.OnLaunchLoadingComplete()** - Affiche Main Menu

### Checklist d'implémentation

- [ ] Créer `Assets/Scrips/UI/LaunchLoadingScreen.cs` (code ci-dessus)
- [ ] Modifier la hiérarchie de "Loading screen" dans Bootstrap
- [ ] Ajouter les composants (LaunchLoadingScreen, VRCanvasAdapter, CanvasGroup)
- [ ] Créer/importer le logo de l'app
- [ ] Assigner toutes les références dans l'Inspector
- [ ] Modifier `BootstrapManager.cs` (code ci-dessus)
- [ ] Tester en Desktop et VR
- [ ] Ajuster les timings si nécessaire
