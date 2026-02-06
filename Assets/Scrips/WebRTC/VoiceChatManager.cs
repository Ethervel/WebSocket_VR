using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VoiceChat;

/// <summary>
/// Orchestrator for WebRTC voice chat in VR Meeting Rooms.
/// Delegates to specialized managers: MicrophoneManager, RemoteAudioManager,
/// WebRTCPeerManager, WebRTCSignaling, WebRTCConfiguration.
/// Preserves the original public API for backward compatibility.
/// </summary>
public class VoiceChatManager : MonoBehaviour
{
    public static VoiceChatManager Instance { get; private set; }

    [Header("Audio Settings")]
    [Tooltip("Activer le microphone au démarrage")]
    public bool autoStartMicrophone = true;

    [Tooltip("Volume du microphone (0-3)")]
    [Range(0f, 3f)]
    public float microphoneVolume = 1f;

    [Tooltip("Volume des autres joueurs (0-1)")]
    [Range(0f, 1f)]
    public float playbackVolume = 0.8f;

    [Tooltip("Utiliser l'audio 3D spatialisé")]
    public bool use3DAudio = true;

    [Tooltip("Distance maximale d'audibilité (mètres)")]
    public float maxAudioDistance = 20f;

    [Header("Push To Talk")]
    [Tooltip("Utiliser Push-To-Talk au lieu de voix continue")]
    public bool usePushToTalk = false;

    [Tooltip("Touche pour Push-To-Talk (Desktop)")]
    public Key pushToTalkKey = Key.V;

    [Header("VR Controls")]
    [Tooltip("Bouton VR pour Push-To-Talk")]
    public bool useVRPushToTalk = false;

    [Header("Debug")]
    public bool showDebugInfo = true;

    [Header("Connection Timeout")]
    [Tooltip("Timeout in seconds for peer connections that don't complete")]
    public float peerConnectionTimeout = 15f;

    // Sub-managers (created as child components)
    private MicrophoneManager _micManager;
    private RemoteAudioManager _audioManager;
    private WebRTCPeerManager _peerManager;
    private WebRTCSignaling _signaling;
    private WebRTCConfiguration _config;

    // State
    private bool _isInitialized = false;
    private Keyboard _keyboard;

    // Events (preserved public API)
    public static event Action OnVoiceChatReady;
    public static event Action<string> OnPeerVoiceConnected;
    public static event Action<string> OnPeerVoiceDisconnected;
    public static event Action<bool> OnMicrophoneStateChanged;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        CreateSubManagers();
    }

    void CreateSubManagers()
    {
        // Create sub-managers as components on this GameObject
        _config = gameObject.AddComponent<WebRTCConfiguration>();
        _micManager = gameObject.AddComponent<MicrophoneManager>();
        _audioManager = gameObject.AddComponent<RemoteAudioManager>();
        _signaling = gameObject.AddComponent<WebRTCSignaling>();
        _peerManager = gameObject.AddComponent<WebRTCPeerManager>();

        // Apply initial settings to sub-managers
        _audioManager.playbackVolume = playbackVolume;
        _audioManager.use3DAudio = use3DAudio;
        _audioManager.maxAudioDistance = maxAudioDistance;
        _micManager.microphoneVolume = microphoneVolume;
        _peerManager.peerConnectionTimeout = peerConnectionTimeout;
        _peerManager.showDebugInfo = showDebugInfo;
        _signaling.showDebugInfo = showDebugInfo;
    }

    void Start()
    {
        LoadSettings();
        Initialize();
    }

    void LoadSettings()
    {
        playbackVolume = MainMenuSettings.GetVoiceVolume();
        _audioManager.playbackVolume = playbackVolume;
    }

    void Initialize()
    {
        // Initialize microphone manager
        _micManager.Initialize();

        // Initialize peer manager with dependencies
        _peerManager.Initialize(_config, _signaling, _micManager, _audioManager);

        _isInitialized = true;
        LogDebug("[VoiceChat] Initialized");

        OnVoiceChatReady?.Invoke();

        if (autoStartMicrophone)
        {
            _micManager.StartMicrophone();
        }
    }

    void OnEnable()
    {
        // Room events -> route to peer manager
        VRRoomManager.OnPlayerJoined += OnPlayerJoined;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomCreated += OnRoomCreated;

        // Settings integration
        MainMenuSettings.OnVoiceVolumeChanged += OnVoiceVolumeChanged;
        MainMenuSettings.OnMicrophoneChanged += OnMicrophoneDeviceChanged;

        // Subscribe to sub-manager events to relay them
        MicrophoneManager.OnMicrophoneStateChanged += RelayMicrophoneStateChanged;
        WebRTCPeerManager.OnPeerConnected += RelayPeerConnected;
        WebRTCPeerManager.OnPeerDisconnected += RelayPeerDisconnected;
    }

    void OnDisable()
    {
        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomCreated -= OnRoomCreated;

        MainMenuSettings.OnVoiceVolumeChanged -= OnVoiceVolumeChanged;
        MainMenuSettings.OnMicrophoneChanged -= OnMicrophoneDeviceChanged;

        MicrophoneManager.OnMicrophoneStateChanged -= RelayMicrophoneStateChanged;
        WebRTCPeerManager.OnPeerConnected -= RelayPeerConnected;
        WebRTCPeerManager.OnPeerDisconnected -= RelayPeerDisconnected;
    }

    void Update()
    {
        // Push-To-Talk Desktop (using new Input System)
        if (usePushToTalk && _isInitialized && !useVRPushToTalk)
        {
            if (_keyboard == null)
                _keyboard = Keyboard.current;

            if (_keyboard != null)
            {
                if (_keyboard[pushToTalkKey].wasPressedThisFrame)
                    _micManager?.StartMicrophone();
                else if (_keyboard[pushToTalkKey].wasReleasedThisFrame)
                    _micManager?.StopMicrophone();
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    #region Event Relays

    void RelayMicrophoneStateChanged(bool active) => OnMicrophoneStateChanged?.Invoke(active);
    void RelayPeerConnected(string peerId) => OnPeerVoiceConnected?.Invoke(peerId);
    void RelayPeerDisconnected(string peerId) => OnPeerVoiceDisconnected?.Invoke(peerId);

    #endregion

    #region Room Events

    void OnRoomCreated(string roomId) => LogDebug($"[VoiceChat] Room created: {roomId}");

    void OnRoomJoined(string roomId) => LogDebug($"[VoiceChat] Joined room: {roomId}");

    void OnPlayerJoined(VRPlayerData player) => _peerManager?.OnPlayerJoined(player);

    void OnPlayerLeft(string playerId) => _peerManager?.OnPlayerLeft(playerId);

    void OnRoomLeft() => _peerManager?.OnRoomLeft();

    #endregion

    #region Settings Integration

    void OnVoiceVolumeChanged(float volume) => SetPlaybackVolume(volume);

    void OnMicrophoneDeviceChanged(string device)
    {
        if (_micManager == null) return;

        bool wasActive = _micManager.IsActive;
        if (wasActive)
            _micManager.StopMicrophone();

        _micManager.SetMicrophone(device);

        if (wasActive)
            _micManager.StartMicrophone();
    }

    #endregion

    #region Public API (Preserved)

    public bool IsInitialized => _isInitialized;
    public bool IsMicrophoneActive => _micManager?.IsActive ?? false;

    public void StartMicrophone() => _micManager?.StartMicrophone();
    public void StopMicrophone() => _micManager?.StopMicrophone();
    public void ToggleMicrophone() => _micManager?.ToggleMicrophone();
    public void SetMicrophone(string deviceName) => _micManager?.SetMicrophone(deviceName);
    public string[] GetAvailableMicrophones() => _micManager?.GetAvailableMicrophones() ?? Array.Empty<string>();

    public void SetMicrophoneVolume(float volume)
    {
        microphoneVolume = Mathf.Clamp(volume, 0f, 3f);
        if (_micManager != null)
            _micManager.microphoneVolume = microphoneVolume;
    }

    public void SetPlaybackVolume(float volume)
    {
        playbackVolume = Mathf.Clamp01(volume);
        _audioManager?.SetPlaybackVolume(playbackVolume);
    }

    public void SetPlayerMuted(string playerId, bool muted) => _audioManager?.SetPlayerMuted(playerId, muted);

    public bool IsPlayerConnected(string playerId) => _peerManager?.IsPlayerConnected(playerId) ?? false;

    public int GetActiveConnectionCount() => _peerManager?.GetActiveConnectionCount() ?? 0;

    #endregion

    #region Debug

    void LogDebug(string message)
    {
        if (showDebugInfo)
            Debug.Log(message);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnGUI()
    {
        if (!showDebugInfo) return;

        GUILayout.BeginArea(new Rect(Screen.width - 310, 10, 300, 400));
        GUILayout.BeginVertical("box");

        GUILayout.Label("=== Voice Chat Debug ===");
        GUILayout.Label($"Initialized: {_isInitialized}");
        GUILayout.Label($"Microphone: {(IsMicrophoneActive ? "ON" : "OFF")}");
        GUILayout.Label($"Selected Mic: {_micManager?.SelectedDevice}");
        GUILayout.Label($"Connections: {GetActiveConnectionCount()}");

        GUILayout.Space(10);

        if (GUILayout.Button(IsMicrophoneActive ? "Stop Mic" : "Start Mic"))
        {
            ToggleMicrophone();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
#endif

    #endregion
}
