using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.WebRTC;

/// <summary>
/// Gestionnaire de chat vocal WebRTC pour VR Meeting Rooms
/// ADAPTÉ pour fonctionner avec VRNetworkManager et VRRoomManager
/// </summary>
public class VoiceChatManager : MonoBehaviour
{
    public static VoiceChatManager Instance { get; private set; }
    
    [Header("Audio Settings")]
    [Tooltip("Activer le microphone au démarrage")]
    public bool autoStartMicrophone = true;
    
    [Tooltip("Volume du microphone (0-1)")]
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
    [Tooltip("Bouton VR pour Push-To-Talk (exemple: Primary Button)")]
    public bool useVRPushToTalk = false;
    
    [Header("Debug")]
    public bool showDebugInfo = true;

    [Header("Connection Timeout (P0 Fix)")]
    [Tooltip("Timeout in seconds for peer connections that don't complete")]
    public float peerConnectionTimeout = 15f;

    [Header("TURN Server Configuration (SECURITY)")]
    [Tooltip("SECURITY: Use your own private TURN server in production. Public servers are for development only!")]
    public bool useCustomTurnServer = false;

    [Tooltip("Your private TURN server URL (e.g., turn:your-server.com:3478)")]
    public string customTurnUrl = "";

    [Tooltip("TURN server username")]
    public string customTurnUsername = "";

    [Tooltip("TURN server credential/password")]
    public string customTurnCredential = "";

    [Tooltip("Enable TURN over TCP (helps with restrictive firewalls)")]
    public bool enableTurnTcp = true;

    // État
    private bool _isInitialized = false;
    private bool _isMicrophoneActive = false;
    private AudioSource _microphoneAudioSource;
    private string _selectedMicrophone;

    // Test tone (temporary - for testing without microphone)
    private bool _isTestToneActive = false;

    // WebRTC
    // MINOR FIX: Added readonly to collections that are only assigned once
    private readonly Dictionary<string, RTCPeerConnection> _peerConnections = new Dictionary<string, RTCPeerConnection>();
    private readonly Dictionary<string, AudioSource> _remoteAudioSources = new Dictionary<string, AudioSource>();
    private MediaStream _localStream;
    private AudioStreamTrack _localAudioTrack;

    // P0 FIX: Track pending connections with their creation time for timeout handling
    // MINOR FIX: Added readonly
    private readonly Dictionary<string, float> _pendingConnectionStartTimes = new Dictionary<string, float>();
    private Coroutine _timeoutCheckCoroutine;

    // Input System
    private Keyboard _keyboard;
    
    // Configuration STUN/TURN - built dynamically based on settings
    private RTCConfiguration _rtcConfig;

    // MINOR FIX: Constants for STUN/TURN server URLs
    private const string STUN_GOOGLE_1 = "stun:stun.l.google.com:19302";
    private const string STUN_GOOGLE_2 = "stun:stun1.l.google.com:19302";
    private const string STUN_CLOUDFLARE = "stun:stun.cloudflare.com:3478";
    private const string TURN_PUBLIC_URL = "turn:openrelay.metered.ca";
    private const string TURN_PUBLIC_USERNAME = "openrelayproject";
    private const string TURN_PUBLIC_CREDENTIAL = "openrelayproject";

    /// <summary>
    /// SECURITY FIX: Builds RTCConfiguration dynamically based on settings.
    /// Uses custom TURN server if configured, otherwise falls back to public servers with warnings.
    /// </summary>
    private RTCConfiguration BuildRTCConfiguration()
    {
        var iceServers = new System.Collections.Generic.List<RTCIceServer>();

        // STUN servers (always included - these are public and safe)
        // MINOR FIX: Use constants instead of hardcoded URLs
        iceServers.Add(new RTCIceServer { urls = new[] { STUN_GOOGLE_1 } });
        iceServers.Add(new RTCIceServer { urls = new[] { STUN_GOOGLE_2 } });
        iceServers.Add(new RTCIceServer { urls = new[] { STUN_CLOUDFLARE } });

        // TURN servers - use custom if configured, otherwise public fallback
        if (useCustomTurnServer && !string.IsNullOrEmpty(customTurnUrl))
        {
            // Use custom private TURN server
            Debug.Log("[VoiceChat] SECURITY: Using custom TURN server");

            iceServers.Add(new RTCIceServer
            {
                urls = new[] { customTurnUrl },
                username = customTurnUsername,
                credential = customTurnCredential
            });

            // Add TCP variant if enabled
            if (enableTurnTcp && !customTurnUrl.Contains("transport="))
            {
                string tcpUrl = customTurnUrl.Contains("?")
                    ? customTurnUrl + "&transport=tcp"
                    : customTurnUrl + "?transport=tcp";
                iceServers.Add(new RTCIceServer
                {
                    urls = new[] { tcpUrl },
                    username = customTurnUsername,
                    credential = customTurnCredential
                });
            }
        }
        else
        {
            // SECURITY WARNING: Using public TURN servers
#if !UNITY_EDITOR
            Debug.LogWarning("[VoiceChat] SECURITY WARNING: Using public TURN servers with shared credentials. " +
                           "This is acceptable for development but NOT recommended for production. " +
                           "Configure useCustomTurnServer with your own TURN server (Twilio, Xirsys, or self-hosted).");
#else
            Debug.Log("[VoiceChat] Note: Using public TURN servers. Configure custom TURN server for production.");
#endif

            // Public TURN servers as fallback (development only)
            // MINOR FIX: Use constants instead of hardcoded URLs
            iceServers.Add(new RTCIceServer
            {
                urls = new[] { $"{TURN_PUBLIC_URL}:80" },
                username = TURN_PUBLIC_USERNAME,
                credential = TURN_PUBLIC_CREDENTIAL
            });
            iceServers.Add(new RTCIceServer
            {
                urls = new[] { $"{TURN_PUBLIC_URL}:443" },
                username = TURN_PUBLIC_USERNAME,
                credential = TURN_PUBLIC_CREDENTIAL
            });
            iceServers.Add(new RTCIceServer
            {
                urls = new[] { $"{TURN_PUBLIC_URL}:443?transport=tcp" },
                username = TURN_PUBLIC_USERNAME,
                credential = TURN_PUBLIC_CREDENTIAL
            });
        }

        return new RTCConfiguration
        {
            iceServers = iceServers.ToArray()
        };
    }
    
    // Events
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

        // SECURITY FIX: Build RTCConfiguration with appropriate TURN servers
        _rtcConfig = BuildRTCConfiguration();
    }
    
    void Start()
    {
        // Load initial settings
        LoadSettings();
        StartCoroutine(InitializeWebRTC());
    }

    void LoadSettings()
    {
        // Load voice volume from settings
        playbackVolume = MainMenuSettings.GetVoiceVolume();

        // Load microphone device from settings
        string savedMic = MainMenuSettings.GetMicrophoneDevice();
        if (!string.IsNullOrEmpty(savedMic))
        {
            _selectedMicrophone = savedMic;
        }
    }
    
    void OnEnable()
    {
        // ADAPTÉ: Utiliser les events du nouveau système
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        VRRoomManager.OnPlayerJoined += OnPlayerJoined;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomCreated += OnRoomCreated;

        // Settings integration
        MainMenuSettings.OnVoiceVolumeChanged += OnVoiceVolumeChanged;
        MainMenuSettings.OnMicrophoneChanged += OnMicrophoneDeviceChanged;
    }

    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomCreated -= OnRoomCreated;

        // Settings integration
        MainMenuSettings.OnVoiceVolumeChanged -= OnVoiceVolumeChanged;
        MainMenuSettings.OnMicrophoneChanged -= OnMicrophoneDeviceChanged;
    }

    void OnVoiceVolumeChanged(float volume)
    {
        SetPlaybackVolume(volume);
    }

    void OnMicrophoneDeviceChanged(string device)
    {
        // Change microphone if currently recording
        if (_isMicrophoneActive)
        {
            StopMicrophone();
            _selectedMicrophone = string.IsNullOrEmpty(device) ? null : device;
            StartMicrophone();
        }
        else
        {
            _selectedMicrophone = string.IsNullOrEmpty(device) ? null : device;
        }
    }
    
    void Update()
    {
        // Push-To-Talk Desktop (using new Input System)
        if (usePushToTalk && _isInitialized && !useVRPushToTalk)
        {
            if (_keyboard == null)
            {
                _keyboard = Keyboard.current;
            }

            if (_keyboard != null)
            {
                if (_keyboard[pushToTalkKey].wasPressedThisFrame)
                {
                    StartMicrophone();
                }
                else if (_keyboard[pushToTalkKey].wasReleasedThisFrame)
                {
                    StopMicrophone();
                }
            }
        }

        // TODO: Ajouter support VR Push-To-Talk avec UnityEngine.XR.InputDevice
    }
    
    void OnDestroy()
    {
        CleanupAll();
    }
    
    #region Initialization
    
    IEnumerator InitializeWebRTC()
    {
        LogDebug("[VoiceChat] Initializing WebRTC...");
        
        yield return new WaitForSeconds(0.5f);
        
        // Créer l'AudioSource pour le microphone
        _microphoneAudioSource = gameObject.AddComponent<AudioSource>();
        _microphoneAudioSource.loop = true;
        _microphoneAudioSource.playOnAwake = false;
        _microphoneAudioSource.volume = 0; // On n'écoute pas notre propre micro
        
        // Sélectionner le microphone par défaut
        if (Microphone.devices.Length > 0)
        {
            _selectedMicrophone = Microphone.devices[0];
            LogDebug($"[VoiceChat] Microphone found: {_selectedMicrophone}");
        }
        else
        {
            Debug.LogWarning("[VoiceChat] No microphone found!");
        }
        
        _isInitialized = true;
        LogDebug("[VoiceChat] WebRTC initialized successfully");

        // P0 FIX: Start the connection timeout checker coroutine
        if (_timeoutCheckCoroutine != null)
            StopCoroutine(_timeoutCheckCoroutine);
        _timeoutCheckCoroutine = StartCoroutine(ConnectionTimeoutChecker());

        OnVoiceChatReady?.Invoke();

        // Auto-start si configuré
        if (autoStartMicrophone)
        {
            StartMicrophone();
        }
    }

    /// <summary>
    /// P0 FIX: Coroutine that periodically checks for timed out peer connections
    /// and cleans them up to prevent resource leaks
    /// </summary>
    IEnumerator ConnectionTimeoutChecker()
    {
        WaitForSeconds checkInterval = new WaitForSeconds(2f); // Check every 2 seconds

        while (true)
        {
            yield return checkInterval;

            if (_pendingConnectionStartTimes.Count == 0)
                continue;

            float currentTime = Time.time;
            List<string> timedOutPeers = new List<string>();

            foreach (var kvp in _pendingConnectionStartTimes)
            {
                string peerId = kvp.Key;
                float startTime = kvp.Value;

                // Check if connection has timed out
                if (currentTime - startTime > peerConnectionTimeout)
                {
                    // Verify the connection is actually still pending (not connected)
                    if (_peerConnections.TryGetValue(peerId, out var pc))
                    {
                        var state = pc.IceConnectionState;
                        if (state != RTCIceConnectionState.Connected &&
                            state != RTCIceConnectionState.Completed)
                        {
                            timedOutPeers.Add(peerId);
                            Debug.LogWarning($"[VoiceChat] P0 FIX: Connection to {peerId} timed out after {peerConnectionTimeout}s (state: {state})");
                        }
                    }
                }
            }

            // Clean up timed out connections
            foreach (string peerId in timedOutPeers)
            {
                _pendingConnectionStartTimes.Remove(peerId);
                ClosePeerConnection(peerId);
                Debug.Log($"[VoiceChat] P0 FIX: Cleaned up timed out connection: {peerId}");
            }
        }
    }
    
    #endregion
    
    #region Microphone Control
    
    public void StartMicrophone()
    {
        if (!_isInitialized || string.IsNullOrEmpty(_selectedMicrophone))
        {
            Debug.LogWarning("[VoiceChat] Cannot start microphone - not initialized or no device");
            return;
        }
        
        if (_isMicrophoneActive)
        {
            LogDebug("[VoiceChat] Microphone already active");
            return;
        }
        
        StartCoroutine(StartMicrophoneCoroutine());
    }
    
    IEnumerator StartMicrophoneCoroutine()
    {
        LogDebug("[VoiceChat] Starting microphone...");
        
        // Démarrer l'enregistrement du microphone
        _microphoneAudioSource.clip = Microphone.Start(_selectedMicrophone, true, 1, 48000);
        
        // Attendre que le microphone soit prêt
        int timeout = 100;
        while (Microphone.GetPosition(_selectedMicrophone) <= 0 && timeout > 0)
        {
            timeout--;
            yield return null;
        }
        
        if (timeout == 0)
        {
            Debug.LogError("[VoiceChat] Microphone timeout!");
            yield break;
        }
        
        _microphoneAudioSource.Play();
        
        // Créer le stream audio local
        _localAudioTrack = new AudioStreamTrack(_microphoneAudioSource);
        _localStream = new MediaStream();
        _localStream.AddTrack(_localAudioTrack);
        
        _isMicrophoneActive = true;
        
        LogDebug("[VoiceChat] Microphone started ✅");
        OnMicrophoneStateChanged?.Invoke(true);
        
        // Ajouter la piste audio à toutes les connexions existantes
        foreach (var kvp in _peerConnections)
        {
            AddTrackToPeer(kvp.Key);
        }
    }
    
    public void StopMicrophone()
    {
        if (!_isMicrophoneActive) return;
        
        Microphone.End(_selectedMicrophone);
        _microphoneAudioSource.Stop();
        
        if (_localAudioTrack != null)
        {
            _localAudioTrack.Dispose();
            _localAudioTrack = null;
        }
        
        if (_localStream != null)
        {
            _localStream.Dispose();
            _localStream = null;
        }
        
        _isMicrophoneActive = false;
        
        LogDebug("[VoiceChat] Microphone stopped");
        OnMicrophoneStateChanged?.Invoke(false);
    }
    
    public void ToggleMicrophone()
    {
        if (_isMicrophoneActive)
            StopMicrophone();
        else
            StartMicrophone();
    }
    
    public void SetMicrophone(string deviceName)
    {
        // MINOR FIX: Validate device name parameter
        if (string.IsNullOrEmpty(deviceName))
        {
            Debug.LogWarning("[VoiceChat] SetMicrophone called with null or empty device name");
            return;
        }

        // MINOR FIX: Validate that the device exists
        bool deviceExists = false;
        foreach (string device in Microphone.devices)
        {
            if (device == deviceName)
            {
                deviceExists = true;
                break;
            }
        }

        if (!deviceExists)
        {
            Debug.LogWarning($"[VoiceChat] Microphone device not found: {deviceName}");
            return;
        }

        if (_isMicrophoneActive)
        {
            StopMicrophone();
        }

        _selectedMicrophone = deviceName;
        LogDebug($"[VoiceChat] Microphone changed to: {deviceName}");
    }
    
    public string[] GetAvailableMicrophones()
    {
        return Microphone.devices;
    }
    
    #endregion

    #region Test Tone (temporary - remove when done testing)

    public void StartTestTone()
    {
        if (!_isInitialized) return;
        if (_isTestToneActive) return;

        // Stop real microphone if active
        if (_isMicrophoneActive)
        {
            StopMicrophone();
        }

        StartCoroutine(StartTestToneCoroutine());
    }

    IEnumerator StartTestToneCoroutine()
    {
        // Generate a 440Hz sine wave clip (1 second, looping)
        int sampleRate = 48000;
        int samples = sampleRate; // 1 second
        float frequency = 440f;
        AudioClip toneClip = AudioClip.Create("TestTone", samples, 1, sampleRate, false);
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            // Add slight variation to make it more audible
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * 0.8f;
        }
        toneClip.SetData(data, 0);

        _microphoneAudioSource.clip = toneClip;
        _microphoneAudioSource.loop = true;
        _microphoneAudioSource.volume = 0.01f; // Very low but not zero (WebRTC needs non-zero)
        _microphoneAudioSource.Play();

        yield return null; // Wait a frame for audio to start

        // Create WebRTC audio track from the tone
        _localAudioTrack = new AudioStreamTrack(_microphoneAudioSource);
        _localStream = new MediaStream();
        _localStream.AddTrack(_localAudioTrack);

        _isMicrophoneActive = true;
        _isTestToneActive = true;

        Debug.Log($"[VoiceChat] TEST TONE started (440Hz) - {_peerConnections.Count} peer connections");
        OnMicrophoneStateChanged?.Invoke(true);

        // Add track to existing peer connections AND renegotiate
        foreach (var kvp in _peerConnections)
        {
            string peerId = kvp.Key;
            RTCPeerConnection pc = kvp.Value;

            // Add the track
            pc.AddTrack(_localAudioTrack, _localStream);
            Debug.Log($"[VoiceChat] TEST TONE: Added track to {peerId}, renegotiating...");

            // Renegotiate by creating a new offer
            yield return StartCoroutine(CreateAndSendOffer(peerId, pc));
        }

        Debug.Log("[VoiceChat] TEST TONE: Renegotiation complete - other player should hear a 440Hz beep now");
    }

    public void StopTestTone()
    {
        if (!_isTestToneActive) return;

        _microphoneAudioSource.Stop();
        _microphoneAudioSource.clip = null;

        if (_localAudioTrack != null)
        {
            _localAudioTrack.Dispose();
            _localAudioTrack = null;
        }

        if (_localStream != null)
        {
            _localStream.Dispose();
            _localStream = null;
        }

        _isMicrophoneActive = false;
        _isTestToneActive = false;

        Debug.Log("[VoiceChat] TEST TONE stopped");
        OnMicrophoneStateChanged?.Invoke(false);
    }

    #endregion

    #region Room Events (ADAPTÉ)
    
    void OnRoomCreated(string roomId)
    {
        LogDebug($"[VoiceChat] Room created: {roomId}");
        // Pas besoin de créer de connexions, on attend que d'autres joueurs rejoignent
    }

    void OnRoomJoined(string roomId)
    {
        LogDebug($"[VoiceChat] Joined room: {roomId}");
        // Les connexions seront initiées via OnPlayerJoined quand room-welcome arrive
    }

    void OnPlayerJoined(VRPlayerData player)
    {
        if (player.playerId == VRNetworkManager.LocalId) return;

        LogDebug($"[VoiceChat] Player joined: {player.playerId} ({player.playerName})");

        // MESH TOPOLOGY FIX: Utiliser une règle déterministe pour éviter les doublons
        // Le joueur avec l'ID le plus petit (lexicographiquement) initie la connexion
        // Cela garantit qu'un seul côté initie et crée une topologie mesh complète
        string localId = VRNetworkManager.LocalId;
        if (string.Compare(localId, player.playerId, StringComparison.Ordinal) < 0)
        {
            // Notre ID est plus petit → on initie la connexion
            LogDebug($"[VoiceChat] MESH: {localId} < {player.playerId} → Initiating connection");
            StartCoroutine(CreatePeerConnection(player.playerId, true));
        }
        else
        {
            // Leur ID est plus petit → ils vont initier la connexion vers nous
            LogDebug($"[VoiceChat] MESH: {localId} > {player.playerId} → Waiting for them to initiate");
        }
    }
    
    void OnPlayerLeft(string playerId)
    {
        LogDebug($"[VoiceChat] Player left: {playerId}");
        ClosePeerConnection(playerId);
    }
    
    void OnRoomLeft()
    {
        LogDebug("[VoiceChat] Left room, cleaning up all connections");
        CloseAllPeerConnections();
    }
    
    #endregion
    
    #region Peer Connection Management
    
    IEnumerator CreatePeerConnection(string peerId, bool createOffer)
    {
        if (_peerConnections.ContainsKey(peerId))
        {
            LogDebug($"[VoiceChat] Peer connection already exists for: {peerId}");
            yield break;
        }

        LogDebug($"[VoiceChat] Creating peer connection for: {peerId} (initiator: {createOffer})");

        var pc = new RTCPeerConnection(ref _rtcConfig);
        _peerConnections[peerId] = pc;

        // P0 FIX: Track connection start time for timeout handling
        _pendingConnectionStartTimes[peerId] = Time.time;
        
        // ADAPTÉ: Créer l'AudioSource et l'attacher au GameObject du remote player
        AudioSource audioSource = CreateAudioSourceForPlayer(peerId);
        if (audioSource != null)
        {
            _remoteAudioSources[peerId] = audioSource;
        }
        
        // Event handlers
        pc.OnIceCandidate = candidate =>
        {
            if (candidate != null)
            {
                SendIceCandidate(peerId, candidate);
            }
        };
        
        pc.OnIceConnectionChange = state =>
        {
            LogDebug($"[VoiceChat] ICE state for {peerId}: {state}");

            if (state == RTCIceConnectionState.Connected)
            {
                // P0 FIX: Connection succeeded, remove from pending tracking
                _pendingConnectionStartTimes.Remove(peerId);
                OnPeerVoiceConnected?.Invoke(peerId);
            }
            else if (state == RTCIceConnectionState.Disconnected ||
                     state == RTCIceConnectionState.Failed)
            {
                // P0 FIX: Connection failed, remove from pending tracking
                _pendingConnectionStartTimes.Remove(peerId);
                OnPeerVoiceDisconnected?.Invoke(peerId);
            }
        };
        
        pc.OnTrack = e =>
        {
            LogDebug($"[VoiceChat] Received track from {peerId}");
            
            if (e.Track is AudioStreamTrack audioTrack && audioSource != null)
            {
                audioSource.SetTrack(audioTrack);
                audioSource.loop = true;
                audioSource.Play();
                LogDebug($"[VoiceChat] ✅ Playing audio from {peerId}");
            }
        };
        
        // Ajouter notre piste audio si le micro est actif
        if (_isMicrophoneActive && _localAudioTrack != null)
        {
            pc.AddTrack(_localAudioTrack, _localStream);
            LogDebug($"[VoiceChat] Added local audio track to {peerId}");
        }
        
        // Créer une offre si on est l'initiateur
        if (createOffer)
        {
            yield return StartCoroutine(CreateAndSendOffer(peerId, pc));
        }
    }
    
    // ADAPTÉ: Créer l'AudioSource sur la TÊTE du remote player pour audio spatialisée correcte
    AudioSource CreateAudioSourceForPlayer(string playerId)
    {
        // Essayer de trouver la TÊTE du remote player (détachée du body)
        Transform headTransform = VRGameManager.Instance?.GetRemotePlayerHead(playerId);
        GameObject audioGO;

        if (headTransform != null)
        {
            // Attacher l'audio à la tête pour une spatialisation correcte
            audioGO = new GameObject("VoiceAudio");
            audioGO.transform.SetParent(headTransform);
            audioGO.transform.localPosition = Vector3.zero;
            LogDebug($"[VoiceChat] AudioSource attached to HEAD of {playerId}");
        }
        else
        {
            // Fallback: essayer le body du remote player
            GameObject playerGO = VRGameManager.Instance?.GetRemotePlayer(playerId);

            if (playerGO != null)
            {
                audioGO = new GameObject("VoiceAudio");
                audioGO.transform.SetParent(playerGO.transform);
                audioGO.transform.localPosition = new Vector3(0, 1.6f, 0); // Approximation hauteur tête
                Debug.LogWarning($"[VoiceChat] Head not found for {playerId}, using body with offset");
            }
            else
            {
                // Fallback final: créer un GameObject enfant de VoiceChatManager
                string shortId = playerId.Length >= 8 ? playerId.Substring(0, 8) : playerId;
                audioGO = new GameObject($"VoiceAudio_{shortId}");
                audioGO.transform.SetParent(transform);
                Debug.LogWarning($"[VoiceChat] Remote player not found for {playerId}, creating fallback");
            }
        }

        // Configurer l'AudioSource
        AudioSource audioSource = audioGO.AddComponent<AudioSource>();
        audioSource.volume = playbackVolume;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        
        // Configuration 3D/2D
        if (use3DAudio)
        {
            audioSource.spatialBlend = 1f; // Full 3D
            audioSource.minDistance = 1f;
            audioSource.maxDistance = maxAudioDistance;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.dopplerLevel = 0f; // Pas de Doppler pour la voix
        }
        else
        {
            audioSource.spatialBlend = 0f; // 2D
        }
        
        LogDebug($"[VoiceChat] Created AudioSource for {playerId} (3D: {use3DAudio})");
        return audioSource;
    }
    
    void AddTrackToPeer(string peerId)
    {
        if (!_peerConnections.TryGetValue(peerId, out var pc)) return;
        if (_localAudioTrack == null) return;
        
        pc.AddTrack(_localAudioTrack, _localStream);
        LogDebug($"[VoiceChat] Added audio track to peer: {peerId}");
    }
    
    IEnumerator CreateAndSendOffer(string peerId, RTCPeerConnection pc)
    {
        var op = pc.CreateOffer();
        yield return op;
        
        if (op.IsError)
        {
            Debug.LogError($"[VoiceChat] Error creating offer: {op.Error.message}");
            yield break;
        }
        
        var desc = op.Desc;
        var op2 = pc.SetLocalDescription(ref desc);
        yield return op2;
        
        if (op2.IsError)
        {
            Debug.LogError($"[VoiceChat] Error setting local description: {op2.Error.message}");
            yield break;
        }
        
        // ADAPTÉ: Envoyer via le nouveau format de signaling
        SendSignalingMessage(peerId, "webrtc-offer", new SignalingData
        {
            sdp = desc.sdp,
            type = desc.type.ToString()
        });
        
        LogDebug($"[VoiceChat] ✅ Sent offer to: {peerId}");
    }
    
    void ClosePeerConnection(string peerId)
    {
        // P0 FIX: Remove from pending tracking when closing
        _pendingConnectionStartTimes.Remove(peerId);

        if (_peerConnections.TryGetValue(peerId, out var pc))
        {
            pc.Close();
            pc.Dispose();
            _peerConnections.Remove(peerId);
        }

        if (_remoteAudioSources.TryGetValue(peerId, out var audioSource))
        {
            if (audioSource != null)
                Destroy(audioSource.gameObject);
            _remoteAudioSources.Remove(peerId);
        }

        LogDebug($"[VoiceChat] Closed peer connection: {peerId}");
        OnPeerVoiceDisconnected?.Invoke(peerId);
    }
    
    void CloseAllPeerConnections()
    {
        var peerIds = new List<string>(_peerConnections.Keys);
        foreach (var peerId in peerIds)
        {
            ClosePeerConnection(peerId);
        }
    }
    
    void CleanupAll()
    {
        // P0 FIX: Stop the timeout checker coroutine
        if (_timeoutCheckCoroutine != null)
        {
            StopCoroutine(_timeoutCheckCoroutine);
            _timeoutCheckCoroutine = null;
        }

        // P0 FIX: Clear pending connection tracking
        _pendingConnectionStartTimes.Clear();

        StopMicrophone();
        CloseAllPeerConnections();

        LogDebug("[VoiceChat] Cleanup complete");
    }
    
    #endregion
    
    #region Signaling (ADAPTÉ pour nouveau protocole)
    
    void HandleNetworkMessage(NetworkMessage msg)
    {
        switch (msg.type)
        {
            case "webrtc-offer":
                HandleOffer(msg);
                break;
            case "webrtc-answer":
                HandleAnswer(msg);
                break;
            case "webrtc-ice-candidate":
                HandleIceCandidate(msg);
                break;
        }
    }
    
    void HandleOffer(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<SignalingData>(msg.data);
            string peerId = msg.senderId;
            
            if (string.IsNullOrEmpty(data.sdp))
            {
                Debug.LogError($"[VoiceChat] Received offer with empty SDP from: {peerId}");
                return;
            }
            
            LogDebug($"[VoiceChat] Received offer from: {peerId}");
            StartCoroutine(ProcessOffer(peerId, data));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VoiceChat] Error parsing offer: {e.Message}");
            Debug.LogError($"[VoiceChat] Raw data: {msg.data}");
        }
    }
    
    IEnumerator ProcessOffer(string peerId, SignalingData data)
    {
        // Créer la connexion si elle n'existe pas
        if (!_peerConnections.ContainsKey(peerId))
        {
            yield return StartCoroutine(CreatePeerConnection(peerId, false));
        }
        
        var pc = _peerConnections[peerId];
        
        // Appliquer l'offre distante
        var desc = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = data.sdp
        };
        
        var op = pc.SetRemoteDescription(ref desc);
        yield return op;
        
        if (op.IsError)
        {
            Debug.LogError($"[VoiceChat] Error setting remote description: {op.Error.message}");
            yield break;
        }
        
        // Créer et envoyer la réponse
        var op2 = pc.CreateAnswer();
        yield return op2;
        
        if (op2.IsError)
        {
            Debug.LogError($"[VoiceChat] Error creating answer: {op2.Error.message}");
            yield break;
        }
        
        var answerDesc = op2.Desc;
        var op3 = pc.SetLocalDescription(ref answerDesc);
        yield return op3;
        
        if (op3.IsError)
        {
            Debug.LogError($"[VoiceChat] Error setting local description: {op3.Error.message}");
            yield break;
        }
        
        SendSignalingMessage(peerId, "webrtc-answer", new SignalingData
        {
            sdp = answerDesc.sdp,
            type = answerDesc.type.ToString()
        });
        
        LogDebug($"[VoiceChat] ✅ Sent answer to: {peerId}");
    }
    
    void HandleAnswer(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<SignalingData>(msg.data);
            string peerId = msg.senderId;
            
            if (string.IsNullOrEmpty(data.sdp))
            {
                Debug.LogError($"[VoiceChat] Received answer with empty SDP from: {peerId}");
                return;
            }
            
            LogDebug($"[VoiceChat] Received answer from: {peerId}");
            StartCoroutine(ProcessAnswer(peerId, data));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VoiceChat] Error parsing answer: {e.Message}");
        }
    }
    
    IEnumerator ProcessAnswer(string peerId, SignalingData data)
    {
        if (!_peerConnections.TryGetValue(peerId, out var pc))
        {
            Debug.LogError($"[VoiceChat] No peer connection for: {peerId}");
            yield break;
        }
        
        var desc = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = data.sdp
        };
        
        var op = pc.SetRemoteDescription(ref desc);
        yield return op;
        
        if (op.IsError)
        {
            Debug.LogError($"[VoiceChat] Error setting remote description: {op.Error.message}");
        }
        else
        {
            LogDebug($"[VoiceChat] ✅ Processed answer from: {peerId}");
        }
    }
    
    void HandleIceCandidate(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<IceCandidateData>(msg.data);
            string peerId = msg.senderId;
            
            if (string.IsNullOrEmpty(data.candidate))
            {
                return; // ICE complete, c'est normal
            }
            
            if (!_peerConnections.TryGetValue(peerId, out var pc))
            {
                Debug.LogWarning($"[VoiceChat] No peer connection for ICE candidate: {peerId}");
                return;
            }
            
            var candidateInit = new RTCIceCandidateInit
            {
                candidate = data.candidate,
                sdpMid = data.sdpMid,
                sdpMLineIndex = data.sdpMLineIndex
            };
            
            var candidate = new RTCIceCandidate(candidateInit);
            pc.AddIceCandidate(candidate);
            
            LogDebug($"[VoiceChat] Added ICE candidate from: {peerId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VoiceChat] Error parsing ICE candidate: {e.Message}");
        }
    }
    
    // ADAPTÉ: Nouveau format d'envoi pour correspondre au serveur
    void SendSignalingMessage(string targetPeerId, string messageType, object data)
    {
        var wrapper = new WebRTCSignalingWrapper
        {
            targetId = targetPeerId,
            sdp = (data as SignalingData)?.sdp,
            candidate = (data as IceCandidateData)?.candidate,
            sdpMid = (data as IceCandidateData)?.sdpMid,
            sdpMLineIndex = (data as IceCandidateData)?.sdpMLineIndex ?? 0
        };
        
        VRNetworkManager.Instance?.Send(messageType, wrapper);
    }
    
    void SendIceCandidate(string targetPeerId, RTCIceCandidate candidate)
    {
        var data = new IceCandidateData
        {
            candidate = candidate.Candidate,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = candidate.SdpMLineIndex ?? 0
        };
        
        SendSignalingMessage(targetPeerId, "webrtc-ice-candidate", data);
    }
    
    #endregion
    
    #region Public API
    
    public bool IsInitialized => _isInitialized;
    public bool IsMicrophoneActive => _isMicrophoneActive;
    
    public void SetMicrophoneVolume(float volume)
    {
        microphoneVolume = Mathf.Clamp(volume, 0f, 3f);
    }
    
    public void SetPlaybackVolume(float volume)
    {
        playbackVolume = Mathf.Clamp01(volume);
        
        foreach (var audioSource in _remoteAudioSources.Values)
        {
            if (audioSource != null)
                audioSource.volume = playbackVolume;
        }
    }
    
    public void SetPlayerMuted(string playerId, bool muted)
    {
        if (_remoteAudioSources.TryGetValue(playerId, out var audioSource))
        {
            if (audioSource != null)
                audioSource.mute = muted;
        }
    }
    
    public bool IsPlayerConnected(string playerId)
    {
        return _peerConnections.ContainsKey(playerId);
    }
    
    public int GetActiveConnectionCount()
    {
        return _peerConnections.Count;
    }
    
    #endregion
    
    #region Debug
    
    void LogDebug(string message)
    {
        if (showDebugInfo)
            Debug.Log(message);
    }
    
    // MINOR FIX: Wrap debug GUI in preprocessor directives to avoid runtime overhead in production builds
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnGUI()
    {
        if (!showDebugInfo) return;

        GUILayout.BeginArea(new Rect(Screen.width - 310, 10, 300, 400));
        GUILayout.BeginVertical("box");

        GUILayout.Label("=== Voice Chat Debug ===");
        GUILayout.Label($"Initialized: {_isInitialized}");
        GUILayout.Label($"Microphone: {(_isMicrophoneActive ? "ON 🎤" : "OFF 🔇")}");
        GUILayout.Label($"Selected Mic: {_selectedMicrophone}");
        GUILayout.Label($"Connections: {_peerConnections.Count}");

        GUILayout.Space(10);

        foreach (var kvp in _peerConnections)
        {
            var state = kvp.Value.IceConnectionState;
            string stateIcon = state == RTCIceConnectionState.Connected ? "✅" : "⏳";
            string shortId = kvp.Key.Length >= 8 ? kvp.Key.Substring(0, 8) : kvp.Key;
            GUILayout.Label($"{stateIcon} {shortId}: {state}");
        }

        GUILayout.Space(10);

        if (GUILayout.Button(_isMicrophoneActive ? "Stop Mic 🔇" : "Start Mic 🎤"))
        {
            ToggleMicrophone();
        }

        // Test tone button (temporary)
        GUILayout.Space(5);
        if (GUILayout.Button(_isTestToneActive ? "⬛ Stop Test Tone" : "🔊 Send Test Tone (440Hz)"))
        {
            if (_isTestToneActive)
                StopTestTone();
            else
                StartTestTone();
        }
        if (_isTestToneActive)
        {
            GUILayout.Label("SENDING 440Hz TONE...");
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
#endif

    #endregion
}

#region Data Classes

[Serializable]
public class SignalingData
{
    public string sdp;
    public string type;
}

[Serializable]
public class IceCandidateData
{
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
}

// ADAPTÉ: Format pour correspondre au serveur Node.js
[Serializable]
public class WebRTCSignalingWrapper
{
    public string targetId;
    public string sdp;
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
}

#endregion