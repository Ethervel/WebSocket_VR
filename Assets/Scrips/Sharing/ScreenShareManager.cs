using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;

/// <summary>
/// Gestionnaire de partage d'écran via WebRTC.
/// - Capture l'écran (Desktop uniquement)
/// - Envoie le flux vidéo aux autres participants
/// - Reçoit et affiche les flux vidéo des autres
/// </summary>
public class ScreenShareManager : MonoBehaviour
{
    public static ScreenShareManager Instance { get; private set; }

    [Header("Capture Settings")]
    [Tooltip("Résolution de capture (largeur)")]
    public int captureWidth = 1920;

    [Tooltip("Résolution de capture (hauteur)")]
    public int captureHeight = 1080;

    [Tooltip("Images par seconde")]
    [Range(5, 30)]
    public int frameRate = 15;

    [Tooltip("Qualité de compression (0-100)")]
    [Range(0, 100)]
    public int quality = 75;

    [Header("Display")]
    [Tooltip("Whiteboard cible pour afficher le screen share (auto-détecté si vide)")]
    public Whiteboard targetWhiteboard;

    [Tooltip("Utiliser le whiteboard au lieu d'un écran virtuel séparé")]
    public bool useWhiteboard = true;

    [Header("Debug")]
    public bool showDebugInfo = true;

    // State
    private bool _isSharing = false;
    private bool _isReceiving = false;
    private string _currentSharerId = null;
    private Camera _captureCamera;
    private RenderTexture _captureRenderTexture;
    private Texture2D _captureTexture;

    // WebRTC
    private Dictionary<string, RTCPeerConnection> _peerConnections = new Dictionary<string, RTCPeerConnection>();
    private MediaStream _localVideoStream;
    private VideoStreamTrack _localVideoTrack;

    // RTC Config (same as VoiceChatManager)
    private RTCConfiguration _rtcConfig = new RTCConfiguration
    {
        iceServers = new[]
        {
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } },
            new RTCIceServer { urls = new[] { "stun:stun1.l.google.com:19302" } }
        }
    };

    // Events
    public static event Action<string, string> OnScreenShareStarted; // sharerId, sharerName
    public static event Action<string> OnScreenShareStopped;         // sharerId
    public static event Action<Texture> OnScreenFrameReceived;       // texture

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[ScreenShare] Manager initialized");
    }

    void Start()
    {
        bool isDesktop = VRGameManager.Instance == null || VRGameManager.Instance.IsDesktopMode;
        Debug.Log($"[ScreenShare] Ready - Desktop mode: {isDesktop}");
    }

    void OnEnable()
    {
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
        Debug.Log("[ScreenShare] Subscribed to network events");
    }

    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
    }

    void OnDestroy()
    {
        StopSharing();
        CleanupAll();
    }

    #region Public API

    /// <summary>
    /// Démarre le partage d'écran (Desktop uniquement)
    /// </summary>
    public void StartSharing()
    {
        if (_isSharing)
        {
            LogDebug("[ScreenShare] Already sharing");
            return;
        }

        // Vérifier qu'on est en mode Desktop
        if (VRGameManager.Instance != null && !VRGameManager.Instance.IsDesktopMode)
        {
            Debug.LogWarning("[ScreenShare] Screen sharing is only available in Desktop mode");
            return;
        }

        // Vérifier qu'on est dans une room
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            Debug.LogWarning("[ScreenShare] Must be in a room to share screen");
            return;
        }

        // Vérifier qu'il n'y a pas déjà un partage en cours
        if (!string.IsNullOrEmpty(_currentSharerId))
        {
            Debug.LogWarning("[ScreenShare] Someone else is already sharing");
            return;
        }

        StartCoroutine(StartSharingCoroutine());
    }

    /// <summary>
    /// Arrête le partage d'écran
    /// </summary>
    public void StopSharing()
    {
        if (!_isSharing) return;

        _isSharing = false;

        // Arrêter la capture
        StopCapture();

        // Fermer les connexions WebRTC vidéo
        CloseAllVideoConnections();

        // Notifier les autres
        BroadcastScreenShareStop();

        LogDebug("[ScreenShare] Stopped sharing");
    }

    /// <summary>
    /// Bascule le partage d'écran
    /// </summary>
    public void ToggleSharing()
    {
        if (_isSharing)
            StopSharing();
        else
            StartSharing();
    }

    public bool IsSharing => _isSharing;
    public bool IsReceiving => _isReceiving;
    public string CurrentSharerId => _currentSharerId;

    #endregion

    #region Screen Capture

    IEnumerator StartSharingCoroutine()
    {
        LogDebug("[ScreenShare] Starting screen share...");

        // Créer la RenderTexture pour la capture avec format compatible WebRTC
        // WebRTC requiert B8G8R8A8_SRGB (BGRA32)
        _captureRenderTexture = new RenderTexture(captureWidth, captureHeight, 0, RenderTextureFormat.BGRA32);
        _captureRenderTexture.Create();

        if (!_captureRenderTexture.IsCreated())
        {
            Debug.LogError("[ScreenShare] Failed to create RenderTexture");
            yield break;
        }

        LogDebug($"[ScreenShare] RenderTexture created: {_captureRenderTexture.graphicsFormat}");

        // Créer la texture de capture
        _captureTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.BGRA32, false);

        // Créer le VideoStreamTrack
        _localVideoTrack = new VideoStreamTrack(_captureRenderTexture);
        _localVideoStream = new MediaStream();
        _localVideoStream.AddTrack(_localVideoTrack);

        _isSharing = true;
        _currentSharerId = VRNetworkManager.LocalId;

        // Afficher sur notre propre whiteboard aussi
        ShowOnWhiteboardLocal();

        // Notifier les autres
        BroadcastScreenShareStart();

        // Démarrer la capture
        StartCoroutine(CaptureLoop());

        LogDebug("[ScreenShare] Started sharing");

        yield return null;
    }

    void ShowOnWhiteboardLocal()
    {
        // Auto-détecter le whiteboard si pas assigné
        if (targetWhiteboard == null)
        {
            targetWhiteboard = FindFirstObjectByType<Whiteboard>();
        }

        if (targetWhiteboard == null)
        {
            Debug.LogWarning("[ScreenShare] No whiteboard found for local display");
            return;
        }

        string playerName = PlayerPrefs.GetString("PlayerName", "Me");
        targetWhiteboard.StartPresentationMode(VRNetworkManager.LocalId, $"Screen: {playerName}");
        LogDebug("[ScreenShare] Local whiteboard presentation mode started");
    }

    IEnumerator CaptureLoop()
    {
        WaitForSeconds waitInterval = new WaitForSeconds(1f / frameRate);

        while (_isSharing)
        {
            yield return new WaitForEndOfFrame();

            // Capture de l'écran vers la RenderTexture
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_captureRenderTexture);

            // Afficher sur notre whiteboard local
            if (targetWhiteboard != null && targetWhiteboard.IsPresentationMode)
            {
                targetWhiteboard.UpdatePresentationTexture(_captureRenderTexture);
            }

            yield return waitInterval;
        }
    }

    void StopCapture()
    {
        if (_localVideoTrack != null)
        {
            _localVideoTrack.Dispose();
            _localVideoTrack = null;
        }

        if (_localVideoStream != null)
        {
            _localVideoStream.Dispose();
            _localVideoStream = null;
        }

        if (_captureRenderTexture != null)
        {
            _captureRenderTexture.Release();
            Destroy(_captureRenderTexture);
            _captureRenderTexture = null;
        }

        if (_captureTexture != null)
        {
            Destroy(_captureTexture);
            _captureTexture = null;
        }
    }

    #endregion

    #region Network Messages

    void HandleNetworkMessage(NetworkMessage msg)
    {
        switch (msg.type)
        {
            case "screen-share-start":
                HandleScreenShareStart(msg);
                break;
            case "screen-share-stop":
                HandleScreenShareStop(msg);
                break;
            case "screen-video-offer":
                HandleVideoOffer(msg);
                break;
            case "screen-video-answer":
                HandleVideoAnswer(msg);
                break;
            case "screen-video-ice":
                HandleVideoIceCandidate(msg);
                break;
        }
    }

    void HandleScreenShareStart(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<ScreenShareData>(msg.data);
            string sharerId = msg.senderId;

            // Important: toujours logger pour debug
            Debug.Log($"[ScreenShare] Received screen-share-start from {sharerId} ({data.sharerName})");

            if (sharerId == VRNetworkManager.LocalId)
            {
                Debug.Log("[ScreenShare] Ignoring own screen-share-start");
                return;
            }

            Debug.Log($"[ScreenShare] {data.sharerName} started sharing ({data.width}x{data.height})");

            _currentSharerId = sharerId;
            _isReceiving = true;

            // Afficher sur le whiteboard
            ShowOnWhiteboard(data);

            // Initier la connexion WebRTC pour recevoir le flux vidéo
            StartCoroutine(CreateVideoReceiveConnection(sharerId));

            OnScreenShareStarted?.Invoke(sharerId, data.sharerName);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenShare] Error handling screen-share-start: {e.Message}");
        }
    }

    void HandleScreenShareStop(NetworkMessage msg)
    {
        string sharerId = msg.senderId;

        if (sharerId != _currentSharerId) return;

        LogDebug($"[ScreenShare] Sharer stopped sharing");

        _currentSharerId = null;
        _isReceiving = false;

        // Fermer la connexion vidéo
        CloseVideoConnection(sharerId);

        // Masquer du whiteboard
        HideFromWhiteboard();

        OnScreenShareStopped?.Invoke(sharerId);
    }

    void BroadcastScreenShareStart()
    {
        var data = new ScreenShareData
        {
            roomId = VRRoomManager.Instance?.CurrentRoomId ?? "",
            sharerId = VRNetworkManager.LocalId,
            sharerName = PlayerPrefs.GetString("PlayerName", "Unknown"),
            isSharing = true,
            width = captureWidth,
            height = captureHeight
        };

        VRNetworkManager.Instance?.Send("screen-share-start", data);
    }

    void BroadcastScreenShareStop()
    {
        var data = new ScreenShareData
        {
            roomId = VRRoomManager.Instance?.CurrentRoomId ?? "",
            sharerId = VRNetworkManager.LocalId,
            sharerName = PlayerPrefs.GetString("PlayerName", "Unknown"),
            isSharing = false,
            width = 0,
            height = 0
        };

        VRNetworkManager.Instance?.Send("screen-share-stop", data);
        _currentSharerId = null;
    }

    #endregion

    #region WebRTC Video

    IEnumerator CreateVideoReceiveConnection(string sharerId)
    {
        if (_peerConnections.ContainsKey(sharerId))
        {
            LogDebug($"[ScreenShare] Video connection already exists for: {sharerId}");
            yield break;
        }

        LogDebug($"[ScreenShare] Creating video receive connection for: {sharerId}");

        var pc = new RTCPeerConnection(ref _rtcConfig);
        _peerConnections[sharerId] = pc;

        // Event handlers
        pc.OnIceCandidate = candidate =>
        {
            if (candidate != null)
            {
                SendVideoIceCandidate(sharerId, candidate);
            }
        };

        pc.OnIceConnectionChange = state =>
        {
            LogDebug($"[ScreenShare] ICE state for {sharerId}: {state}");
        };

        pc.OnTrack = e =>
        {
            LogDebug($"[ScreenShare] Received track from {sharerId}");

            if (e.Track is VideoStreamTrack videoTrack)
            {
                // Attacher la texture au whiteboard
                videoTrack.OnVideoReceived += tex =>
                {
                    UpdateWhiteboardTexture(tex);
                    OnScreenFrameReceived?.Invoke(tex);
                };
            }
        };

        // Envoyer une offre pour recevoir le stream
        yield return StartCoroutine(CreateAndSendVideoOffer(sharerId, pc));
    }

    IEnumerator CreateVideoSendConnection(string receiverId)
    {
        if (_peerConnections.ContainsKey(receiverId))
        {
            LogDebug($"[ScreenShare] Video connection already exists for: {receiverId}");
            yield break;
        }

        LogDebug($"[ScreenShare] Creating video send connection for: {receiverId}");

        var pc = new RTCPeerConnection(ref _rtcConfig);
        _peerConnections[receiverId] = pc;

        // Ajouter notre track vidéo
        if (_localVideoTrack != null)
        {
            pc.AddTrack(_localVideoTrack, _localVideoStream);
        }

        // Event handlers
        pc.OnIceCandidate = candidate =>
        {
            if (candidate != null)
            {
                SendVideoIceCandidate(receiverId, candidate);
            }
        };

        pc.OnIceConnectionChange = state =>
        {
            LogDebug($"[ScreenShare] ICE state for {receiverId}: {state}");
        };

        yield return null;
    }

    IEnumerator CreateAndSendVideoOffer(string peerId, RTCPeerConnection pc)
    {
        var op = pc.CreateOffer();
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"[ScreenShare] Error creating offer: {op.Error.message}");
            yield break;
        }

        var desc = op.Desc;
        var op2 = pc.SetLocalDescription(ref desc);
        yield return op2;

        if (op2.IsError)
        {
            Debug.LogError($"[ScreenShare] Error setting local description: {op2.Error.message}");
            yield break;
        }

        // Envoyer l'offre
        var signaling = new VideoSignalingData
        {
            targetId = peerId,
            sdp = desc.sdp,
            type = desc.type.ToString()
        };

        VRNetworkManager.Instance?.Send("screen-video-offer", signaling);
        LogDebug($"[ScreenShare] Sent video offer to: {peerId}");
    }

    void HandleVideoOffer(NetworkMessage msg)
    {
        if (!_isSharing) return; // On n'accepte les offres que si on partage

        try
        {
            var data = JsonUtility.FromJson<VideoSignalingData>(msg.data);
            string peerId = msg.senderId;

            LogDebug($"[ScreenShare] Received video offer from: {peerId}");
            StartCoroutine(ProcessVideoOffer(peerId, data));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenShare] Error parsing video offer: {e.Message}");
        }
    }

    IEnumerator ProcessVideoOffer(string peerId, VideoSignalingData data)
    {
        // Créer la connexion si elle n'existe pas
        if (!_peerConnections.ContainsKey(peerId))
        {
            yield return StartCoroutine(CreateVideoSendConnection(peerId));
        }

        var pc = _peerConnections[peerId];

        // Appliquer l'offre
        var desc = new RTCSessionDescription
        {
            type = RTCSdpType.Offer,
            sdp = data.sdp
        };

        var op = pc.SetRemoteDescription(ref desc);
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"[ScreenShare] Error setting remote description: {op.Error.message}");
            yield break;
        }

        // Créer la réponse
        var op2 = pc.CreateAnswer();
        yield return op2;

        if (op2.IsError)
        {
            Debug.LogError($"[ScreenShare] Error creating answer: {op2.Error.message}");
            yield break;
        }

        var answerDesc = op2.Desc;
        var op3 = pc.SetLocalDescription(ref answerDesc);
        yield return op3;

        if (op3.IsError)
        {
            Debug.LogError($"[ScreenShare] Error setting local description: {op3.Error.message}");
            yield break;
        }

        // Envoyer la réponse
        var signaling = new VideoSignalingData
        {
            targetId = peerId,
            sdp = answerDesc.sdp,
            type = answerDesc.type.ToString()
        };

        VRNetworkManager.Instance?.Send("screen-video-answer", signaling);
        LogDebug($"[ScreenShare] Sent video answer to: {peerId}");
    }

    void HandleVideoAnswer(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<VideoSignalingData>(msg.data);
            string peerId = msg.senderId;

            LogDebug($"[ScreenShare] Received video answer from: {peerId}");
            StartCoroutine(ProcessVideoAnswer(peerId, data));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenShare] Error parsing video answer: {e.Message}");
        }
    }

    IEnumerator ProcessVideoAnswer(string peerId, VideoSignalingData data)
    {
        if (!_peerConnections.TryGetValue(peerId, out var pc))
        {
            Debug.LogError($"[ScreenShare] No peer connection for: {peerId}");
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
            Debug.LogError($"[ScreenShare] Error setting remote description: {op.Error.message}");
        }
        else
        {
            LogDebug($"[ScreenShare] Processed video answer from: {peerId}");
        }
    }

    void HandleVideoIceCandidate(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<VideoIceCandidateData>(msg.data);
            string peerId = msg.senderId;

            if (string.IsNullOrEmpty(data.candidate)) return;

            if (!_peerConnections.TryGetValue(peerId, out var pc))
            {
                Debug.LogWarning($"[ScreenShare] No peer connection for ICE candidate: {peerId}");
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

            LogDebug($"[ScreenShare] Added video ICE candidate from: {peerId}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenShare] Error parsing video ICE candidate: {e.Message}");
        }
    }

    void SendVideoIceCandidate(string targetId, RTCIceCandidate candidate)
    {
        var data = new VideoIceCandidateData
        {
            targetId = targetId,
            candidate = candidate.Candidate,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = candidate.SdpMLineIndex ?? 0
        };

        VRNetworkManager.Instance?.Send("screen-video-ice", data);
    }

    void CloseVideoConnection(string peerId)
    {
        if (_peerConnections.TryGetValue(peerId, out var pc))
        {
            pc.Close();
            pc.Dispose();
            _peerConnections.Remove(peerId);
        }

        LogDebug($"[ScreenShare] Closed video connection: {peerId}");
    }

    void CloseAllVideoConnections()
    {
        var peerIds = new List<string>(_peerConnections.Keys);
        foreach (var peerId in peerIds)
        {
            CloseVideoConnection(peerId);
        }
    }

    #endregion

    #region Whiteboard Display

    void ShowOnWhiteboard(ScreenShareData data)
    {
        // Auto-détecter le whiteboard si pas assigné
        if (targetWhiteboard == null)
        {
            targetWhiteboard = FindFirstObjectByType<Whiteboard>();
        }

        if (targetWhiteboard == null)
        {
            Debug.LogWarning("[ScreenShare] No whiteboard found for display");
            return;
        }

        // Démarrer le mode présentation sur le whiteboard
        targetWhiteboard.StartPresentationMode(data.sharerId, $"Screen: {data.sharerName}");
        LogDebug($"[ScreenShare] Whiteboard presentation mode started: {data.sharerName}");
    }

    void UpdateWhiteboardTexture(Texture texture)
    {
        if (targetWhiteboard != null && targetWhiteboard.IsPresentationMode)
        {
            targetWhiteboard.UpdatePresentationTexture(texture);
        }
    }

    void HideFromWhiteboard()
    {
        if (targetWhiteboard != null && targetWhiteboard.IsPresentationMode)
        {
            targetWhiteboard.StopPresentationMode();
            LogDebug("[ScreenShare] Whiteboard presentation mode stopped");
        }
    }

    #endregion

    #region Room Events

    void OnRoomLeft()
    {
        if (_isSharing)
        {
            StopSharing();
        }

        HideFromWhiteboard();
        _currentSharerId = null;
        _isReceiving = false;
    }

    void OnPlayerLeft(string playerId)
    {
        // Si le sharer quitte, arrêter la réception
        if (playerId == _currentSharerId)
        {
            LogDebug("[ScreenShare] Sharer left the room");
            _currentSharerId = null;
            _isReceiving = false;
            HideFromWhiteboard();
            CloseVideoConnection(playerId);
        }
    }

    void CleanupAll()
    {
        StopCapture();
        CloseAllVideoConnections();
        HideFromWhiteboard();
    }

    #endregion

    #region Debug

    void LogDebug(string message)
    {
        if (showDebugInfo)
            Debug.Log(message);
    }

    #endregion
}

#region Data Classes

[Serializable]
public class VideoSignalingData
{
    public string targetId;
    public string sdp;
    public string type;
}

[Serializable]
public class VideoIceCandidateData
{
    public string targetId;
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
}

#endregion
