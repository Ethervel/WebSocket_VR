using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    public KeyCode pushToTalkKey = KeyCode.V;
    
    [Header("VR Controls")]
    [Tooltip("Bouton VR pour Push-To-Talk (exemple: Primary Button)")]
    public bool useVRPushToTalk = false;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // État
    private bool _isInitialized = false;
    private bool _isMicrophoneActive = false;
    private AudioSource _microphoneAudioSource;
    private string _selectedMicrophone;
    
    // WebRTC
    private Dictionary<string, RTCPeerConnection> _peerConnections = new Dictionary<string, RTCPeerConnection>();
    private Dictionary<string, AudioSource> _remoteAudioSources = new Dictionary<string, AudioSource>();
    private MediaStream _localStream;
    private AudioStreamTrack _localAudioTrack;
    
    // Configuration STUN/TURN
    private RTCConfiguration _rtcConfig = new RTCConfiguration
    {
        iceServers = new[]
        {
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
            new RTCIceServer { urls = new[] { "stun:stun1.l.google.com:19302" } }
        }
    };
    
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
    }
    
    void Start()
    {
        StartCoroutine(InitializeWebRTC());
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
    }
    
    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomCreated -= OnRoomCreated;
    }
    
    void Update()
    {
        // Push-To-Talk Desktop
        if (usePushToTalk && _isInitialized && !useVRPushToTalk)
        {
            if (Input.GetKeyDown(pushToTalkKey))
            {
                StartMicrophone();
            }
            else if (Input.GetKeyUp(pushToTalkKey))
            {
                StopMicrophone();
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
        
        OnVoiceChatReady?.Invoke();
        
        // Auto-start si configuré
        if (autoStartMicrophone)
        {
            StartMicrophone();
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
    
    #region Room Events (ADAPTÉ)
    
    void OnRoomCreated(string roomId)
    {
        LogDebug($"[VoiceChat] Room created: {roomId}");
        // Pas besoin de créer de connexions, on attend que d'autres jouent rejoignent
    }
    
    void OnRoomJoined(string roomId)
    {
        LogDebug($"[VoiceChat] Joined room: {roomId}");
        
        // Connecter aux joueurs déjà présents
        if (VRRoomManager.Instance != null)
        {
            var players = VRRoomManager.Instance.GetPlayers();
            foreach (var player in players)
            {
                // Ne pas se connecter à soi-même
                if (player.playerId != VRNetworkManager.LocalId)
                {
                    // L'hôte initie les connexions
                    if (VRRoomManager.Instance.IsHost)
                    {
                        StartCoroutine(CreatePeerConnection(player.playerId, true));
                    }
                }
            }
        }
    }
    
    void OnPlayerJoined(VRPlayerData player)
    {
        if (player.playerId == VRNetworkManager.LocalId) return;
        
        LogDebug($"[VoiceChat] Player joined: {player.playerId} ({player.playerName})");
        
        // ADAPTÉ: Seul l'hôte initie la connexion pour éviter les doublons
        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsHost)
        {
            StartCoroutine(CreatePeerConnection(player.playerId, true));
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
                OnPeerVoiceConnected?.Invoke(peerId);
            }
            else if (state == RTCIceConnectionState.Disconnected || 
                     state == RTCIceConnectionState.Failed)
            {
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
    
    // ADAPTÉ: Créer l'AudioSource sur le GameObject du remote player
    AudioSource CreateAudioSourceForPlayer(string playerId)
    {
        // Essayer de trouver le GameObject du remote player via VRGameManager
        GameObject playerGO = VRGameManager.Instance?.GetRemotePlayer(playerId);
        
        if (playerGO == null)
        {
            Debug.LogWarning($"[VoiceChat] Remote player GameObject not found for {playerId}, creating fallback");
            // Fallback: créer un GameObject enfant de VoiceChatManager
            // Safe substring pour éviter IndexOutOfRangeException si playerId < 8 caractères
            string shortId = playerId.Length >= 8 ? playerId.Substring(0, 8) : playerId;
            GameObject audioGO = new GameObject($"VoiceAudio_{shortId}");
            audioGO.transform.SetParent(transform);
            playerGO = audioGO;
        }
        else
        {
            // Créer un enfant pour l'audio
            GameObject audioGO = new GameObject("VoiceAudio");
            audioGO.transform.SetParent(playerGO.transform);
            audioGO.transform.localPosition = Vector3.zero;
            playerGO = audioGO;
        }
        
        // Configurer l'AudioSource
        AudioSource audioSource = playerGO.AddComponent<AudioSource>();
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
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
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