using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;

/// <summary>
/// Gestionnaire principal de l'enregistrement des reunions.
/// Singleton - Orchestration de la capture video/audio.
/// Seul l'hote peut demarrer/arreter l'enregistrement.
/// </summary>
public class RecordingManager : MonoBehaviour
{
    public static RecordingManager Instance { get; private set; }

    [Header("=== Settings ===")]
    public RecordingSettings settings = new RecordingSettings();

    [Header("=== References ===")]
    [Tooltip("Camera spectateur (auto-detectee si null)")]
    public SpectatorCameraController spectatorCamera;

    [Header("=== Status ===")]
    [SerializeField] private RecordingState _state = RecordingState.Idle;
    public RecordingState State => _state;

    [SerializeField] private float _elapsedTime = 0f;
    public float ElapsedTime => _elapsedTime;

    [SerializeField] private bool _isHost = false;
    public bool IsHost => _isHost;

    // Remote recording state (when another host is recording)
    [SerializeField] private bool _isRemoteRecording = false;
    public bool IsRemoteRecording => _isRemoteRecording;

    [SerializeField] private string _remoteRecordingHostName = "";
    public string RemoteRecordingHostName => _remoteRecordingHostName;

    // Metadonnees de l'enregistrement en cours
    private RecordingMetadata _currentMetadata;
    private List<RecordingMarker> _markers = new List<RecordingMarker>();

    // Capture
    private Coroutine _recordingCoroutine;
    private List<byte[]> _frameBuffer = new List<byte[]>();
    private AudioCapture _audioCapture;
    private string _currentRecordingPath;
    private DateTime _recordingStartTime;

    // Async frame writing queue
    private ConcurrentQueue<FrameData> _frameQueue = new ConcurrentQueue<FrameData>();
    private bool _isWritingFrames = false;
    private struct FrameData
    {
        public byte[] data;
        public string path;
    }

    // Events
    public static event Action OnRecordingStarted;
    public static event Action OnRecordingStopped;
    public static event Action<RecordingState> OnStateChanged;
    public static event Action<float> OnTimeUpdated;
    public static event Action<RecordingMarker> OnMarkerAdded;
    public static event Action<RecordingStatusMessage> OnRemoteRecordingStatusChanged;
    public static event Action<bool, string> OnRemoteRecordingChanged; // (isRecording, hostName)

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RecordingManager] Instance deja existante, destruction du doublon.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Creer le dossier de sortie s'il n'existe pas
        string outputPath = Path.Combine(Application.persistentDataPath, settings.outputFolder);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
            Debug.Log($"[RecordingManager] Dossier cree: {outputPath}");
        }
    }

    void Start()
    {
        // Auto-detect spectator camera (incluant les objets inactifs)
        if (spectatorCamera == null)
        {
            var cameras = FindObjectsByType<SpectatorCameraController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (cameras.Length > 0)
            {
                spectatorCamera = cameras[0];
                Debug.Log($"[RecordingManager] SpectatorCamera trouvee: {spectatorCamera.gameObject.name}");
            }
            else
            {
                Debug.LogWarning("[RecordingManager] SpectatorCamera non trouvee!");
            }
        }

        // Setup audio capture
        _audioCapture = gameObject.AddComponent<AudioCapture>();
        _audioCapture.Initialize(settings);

        // S'abonner aux evenements reseau
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;

        Debug.Log("[RecordingManager] Initialise.");
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;

        // Arreter l'enregistrement si en cours
        if (_state == RecordingState.Recording)
        {
            StopRecordingInternal();
        }
    }

    void Update()
    {
        if (_state == RecordingState.Recording)
        {
            _elapsedTime = (float)(DateTime.UtcNow - _recordingStartTime).TotalSeconds;
            OnTimeUpdated?.Invoke(_elapsedTime);
        }
    }

    #region Public API

    /// <summary>
    /// Demarre l'enregistrement (hote uniquement).
    /// </summary>
    public void StartRecording()
    {
        if (_state != RecordingState.Idle)
        {
            Debug.LogWarning($"[RecordingManager] Impossible de demarrer: etat actuel = {_state}");
            return;
        }

        // Verifier si on est l'hote
        if (VRRoomManager.Instance != null && !VRRoomManager.Instance.IsHost)
        {
            Debug.LogWarning("[RecordingManager] Seul l'hote peut demarrer l'enregistrement.");
            return;
        }

        _isHost = true;
        StartRecordingInternal();

        // Notifier les autres joueurs
        SendRecordingStatus(true);
    }

    /// <summary>
    /// Arrete l'enregistrement (hote uniquement).
    /// </summary>
    public void StopRecording()
    {
        if (_state != RecordingState.Recording)
        {
            Debug.LogWarning($"[RecordingManager] Impossible d'arreter: etat actuel = {_state}");
            return;
        }

        if (!_isHost)
        {
            Debug.LogWarning("[RecordingManager] Seul l'hote peut arreter l'enregistrement.");
            return;
        }

        StopRecordingInternal();

        // Notifier les autres joueurs
        SendRecordingStatus(false);
    }

    /// <summary>
    /// Ajoute un marqueur a l'enregistrement en cours.
    /// </summary>
    public void AddMarker(MarkerType type, string note = "")
    {
        if (_state != RecordingState.Recording)
        {
            Debug.LogWarning("[RecordingManager] Pas d'enregistrement en cours.");
            return;
        }

        string oderId = VRNetworkManager.LocalId ?? "local";
        string userName = GetLocalPlayerName();

        var marker = new RecordingMarker
        {
            timestamp = _elapsedTime,
            markerType = type.ToString(),
            userId = oderId,
            userName = userName,
            note = note
        };

        _markers.Add(marker);
        OnMarkerAdded?.Invoke(marker);

        Debug.Log($"[RecordingManager] Marqueur ajoute: {type} a {_elapsedTime:F1}s");

        // Envoyer le marqueur aux autres (pour synchronisation)
        SendMarkerToNetwork(marker);
    }

    /// <summary>
    /// Obtient le chemin du dernier enregistrement.
    /// </summary>
    public string GetLastRecordingPath()
    {
        return _currentRecordingPath;
    }

    /// <summary>
    /// Verifie si l'enregistrement est disponible.
    /// </summary>
    public bool CanRecord()
    {
        return spectatorCamera != null && spectatorCamera.IsReady();
    }

    #endregion

    #region Internal Recording Logic

    private void StartRecordingInternal()
    {
        SetState(RecordingState.Starting);

        _recordingStartTime = DateTime.UtcNow;
        _elapsedTime = 0f;
        _markers.Clear();
        _frameBuffer.Clear();

        // Creer le dossier pour cet enregistrement
        string timestamp = _recordingStartTime.ToString("yyyy-MM-dd_HH-mm-ss");
        string roomId = VRRoomManager.Instance?.CurrentRoomId ?? "local";
        string folderName = $"Meeting_{roomId}_{timestamp}";
        _currentRecordingPath = Path.Combine(Application.persistentDataPath, settings.outputFolder, folderName);
        Directory.CreateDirectory(_currentRecordingPath);

        // Initialiser les metadonnees
        _currentMetadata = new RecordingMetadata
        {
            recordingId = Guid.NewGuid().ToString(),
            roomId = roomId,
            roomType = VRRoomManager.Instance?.CurrentRoomType.ToString() ?? "Unknown",
            hostId = VRNetworkManager.LocalId ?? "local",
            hostName = GetLocalPlayerName(),
            startTimeUtc = _recordingStartTime.ToString("o"),
            width = settings.width,
            height = settings.height,
            frameRate = settings.frameRate,
            participants = GetCurrentParticipants(),
            markers = _markers
        };

        // Demarrer la capture de la camera spectateur
        if (spectatorCamera != null)
        {
            spectatorCamera.renderWidth = settings.width;
            spectatorCamera.renderHeight = settings.height;
            spectatorCamera.StartCapture();
        }

        // Demarrer la capture audio
        if (settings.captureAudio && _audioCapture != null)
        {
            _audioCapture.StartCapture();
        }

        // IMPORTANT: Mettre l'etat a Recording AVANT de lancer la coroutine
        SetState(RecordingState.Recording);
        OnRecordingStarted?.Invoke();

        Debug.Log($"[RecordingManager] Enregistrement demarre: {_currentRecordingPath}");

        // Demarrer la coroutine de capture des frames (apres que l'etat soit Recording)
        _recordingCoroutine = StartCoroutine(CaptureFramesCoroutine());
    }

    private void StopRecordingInternal()
    {
        SetState(RecordingState.Stopping);

        // Arreter la coroutine de capture
        if (_recordingCoroutine != null)
        {
            StopCoroutine(_recordingCoroutine);
            _recordingCoroutine = null;
        }

        // Arreter la capture camera
        if (spectatorCamera != null)
        {
            spectatorCamera.StopCapture();
        }

        // Arreter la capture audio
        if (_audioCapture != null)
        {
            _audioCapture.StopCapture();
        }

        // Finaliser les metadonnees
        _currentMetadata.endTimeUtc = DateTime.UtcNow.ToString("o");
        _currentMetadata.durationSeconds = _elapsedTime;
        _currentMetadata.markers = new List<RecordingMarker>(_markers);

        // Sauvegarder
        SetState(RecordingState.Saving);
        StartCoroutine(SaveRecordingCoroutine());
    }

    private IEnumerator CaptureFramesCoroutine()
    {
        float frameInterval = 1f / settings.frameRate;
        float nextCaptureTime = 0f;
        int frameIndex = 0;
        string framesPath = Path.Combine(_currentRecordingPath, "frames");
        Directory.CreateDirectory(framesPath);

        // Demarrer le thread d'ecriture en arriere-plan
        _isWritingFrames = true;
        Task.Run(() => WriteFramesAsync());

        while (_state == RecordingState.Recording)
        {
            if (Time.time >= nextCaptureTime)
            {
                // Capturer une frame
                Texture2D frame = spectatorCamera?.CaptureFrame();
                if (frame != null)
                {
                    // Encoder en JPEG
                    byte[] jpegData = frame.EncodeToJPG(85);
                    string framePath = Path.Combine(framesPath, $"frame_{frameIndex:D6}.jpg");

                    // Ajouter a la queue pour ecriture async
                    _frameQueue.Enqueue(new FrameData { data = jpegData, path = framePath });

                    // Liberer la memoire
                    Destroy(frame);
                    frameIndex++;
                }

                nextCaptureTime = Time.time + frameInterval;
            }

            yield return null;
        }

        // Attendre que toutes les frames soient ecrites
        _isWritingFrames = false;
        while (!_frameQueue.IsEmpty)
        {
            yield return new WaitForSeconds(0.1f);
        }

        Debug.Log($"[RecordingManager] {frameIndex} frames capturees.");
    }

    /// <summary>
    /// Thread d'ecriture des frames en arriere-plan.
    /// </summary>
    private async Task WriteFramesAsync()
    {
        while (_isWritingFrames || !_frameQueue.IsEmpty)
        {
            if (_frameQueue.TryDequeue(out FrameData frame))
            {
                try
                {
                    await Task.Run(() => File.WriteAllBytes(frame.path, frame.data));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RecordingManager] Erreur ecriture frame: {e.Message}");
                }
            }
            else
            {
                await Task.Delay(10); // Petite pause si queue vide
            }
        }
    }

    private IEnumerator SaveRecordingCoroutine()
    {
        Debug.Log("[RecordingManager] Sauvegarde en cours...");

        // Sauvegarder les metadonnees
        string metadataPath = Path.Combine(_currentRecordingPath, "metadata.json");
        string metadataJson = JsonUtility.ToJson(_currentMetadata, true);
        File.WriteAllText(metadataPath, metadataJson);

        // Sauvegarder les marqueurs separement
        string markersPath = Path.Combine(_currentRecordingPath, "markers.json");
        string markersJson = JsonHelper.ToJson(_markers.ToArray());
        File.WriteAllText(markersPath, markersJson);

        // Sauvegarder l'audio
        if (_audioCapture != null)
        {
            string audioPath = Path.Combine(_currentRecordingPath, "audio.wav");
            yield return _audioCapture.SaveToFile(audioPath);
        }

        // Generer le script d'encodage
        FFmpegEncoder.GenerateEncodeScript(_currentRecordingPath, settings.frameRate);

        // Tenter l'encodage automatique si FFmpeg est disponible
        if (FFmpegEncoder.IsAvailable())
        {
            Debug.Log("[RecordingManager] FFmpeg detecte, encodage automatique...");
            bool encodingComplete = false;
            string encodingResult = "";

            // Lancer l'encodage en tache de fond
            _ = FFmpegEncoder.EncodeToMp4Async(
                _currentRecordingPath,
                settings.frameRate,
                progress => Debug.Log($"[RecordingManager] Encodage: {progress:F0}%"),
                (success, result) =>
                {
                    encodingComplete = true;
                    encodingResult = result;
                    if (success)
                    {
                        Debug.Log($"[RecordingManager] MP4 cree: {result}");
                        // Optionnel: nettoyer les frames
                        // FFmpegEncoder.CleanupFrames(_currentRecordingPath);
                    }
                    else
                    {
                        Debug.LogWarning($"[RecordingManager] Echec encodage: {result}");
                    }
                }
            );

            // Attendre la fin de l'encodage (avec timeout)
            float timeout = 300f; // 5 minutes max
            float elapsed = 0f;
            while (!encodingComplete && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
        }
        else
        {
            Debug.Log("[RecordingManager] FFmpeg non disponible. Utilisez le script encode.bat/.sh pour creer le MP4.");
        }

        yield return null;

        SetState(RecordingState.Idle);
        _isHost = false;
        OnRecordingStopped?.Invoke();

        Debug.Log($"[RecordingManager] Enregistrement sauvegarde: {_currentRecordingPath}");
    }

    private List<string> GetCurrentParticipants()
    {
        var participants = new List<string>();

        // Ajouter tous les joueurs de la room
        if (VRRoomManager.Instance != null)
        {
            var roomPlayers = VRRoomManager.Instance.GetPlayers();
            if (roomPlayers != null)
            {
                foreach (var player in roomPlayers)
                {
                    if (!string.IsNullOrEmpty(player.playerName))
                    {
                        participants.Add(player.playerName);
                    }
                }
            }
        }

        // Si aucun participant trouve, ajouter au moins l'hote
        if (participants.Count == 0)
        {
            participants.Add(GetLocalPlayerName());
        }

        return participants;
    }

    private string GetLocalPlayerName()
    {
        if (VRRoomManager.Instance != null)
        {
            var players = VRRoomManager.Instance.GetPlayers();
            string localId = VRNetworkManager.LocalId;
            foreach (var player in players)
            {
                if (player.playerId == localId)
                {
                    return player.playerName ?? "Host";
                }
            }
        }
        return "Host";
    }

    private void SetState(RecordingState newState)
    {
        if (_state != newState)
        {
            _state = newState;
            OnStateChanged?.Invoke(_state);
            Debug.Log($"[RecordingManager] Etat: {_state}");
        }
    }

    #endregion

    #region Network

    private void SendRecordingStatus(bool isRecording)
    {
        if (VRNetworkManager.Instance == null) return;

        var statusMsg = new RecordingStatusMessage
        {
            isRecording = isRecording,
            hostId = VRNetworkManager.LocalId,
            hostName = GetLocalPlayerName(),
            startTimeUtc = isRecording ? _recordingStartTime.ToString("o") : ""
        };

        if (VRNetworkManager.Instance != null)
        {
            VRNetworkManager.Instance.Send("recording-status", statusMsg);
        }
    }

    private void SendMarkerToNetwork(RecordingMarker marker)
    {
        if (VRNetworkManager.Instance == null) return;

        var markerMsg = new RecordingMarkerMessage
        {
            roomId = VRRoomManager.Instance?.CurrentRoomId ?? "",
            timestamp = marker.timestamp,
            markerType = marker.markerType,
            userId = marker.userId,
            userName = marker.userName,
            note = marker.note
        };

        if (VRNetworkManager.Instance != null)
        {
            VRNetworkManager.Instance.Send("recording-marker", markerMsg);
        }
    }

    private void HandleNetworkMessage(NetworkMessage networkMsg)
    {
        if (networkMsg == null) return;

        switch (networkMsg.type)
        {
            case "recording-status":
                HandleRecordingStatus(networkMsg.data);
                break;
            case "recording-marker":
                HandleRemoteMarker(networkMsg.data);
                break;
        }
    }

    private void HandleRecordingStatus(string data)
    {
        var status = JsonUtility.FromJson<RecordingStatusMessage>(data);
        if (status == null) return;

        // Ne pas traiter si on est l'hote qui enregistre
        if (_isHost && _state == RecordingState.Recording) return;

        OnRemoteRecordingStatusChanged?.Invoke(status);

        if (status.isRecording && !_isRemoteRecording)
        {
            // Un enregistrement distant a demarre
            _isRemoteRecording = true;
            _remoteRecordingHostName = status.hostName ?? "Unknown";
            Debug.Log($"[RecordingManager] Enregistrement demarre par {_remoteRecordingHostName}");
            OnRemoteRecordingChanged?.Invoke(true, _remoteRecordingHostName);
        }
        else if (!status.isRecording && _isRemoteRecording)
        {
            // L'enregistrement distant s'est arrete
            Debug.Log($"[RecordingManager] Enregistrement arrete par {_remoteRecordingHostName}");
            _isRemoteRecording = false;
            _remoteRecordingHostName = "";
            OnRemoteRecordingChanged?.Invoke(false, "");
        }
    }

    private void HandleRemoteMarker(string data)
    {
        var markerMsg = JsonUtility.FromJson<RecordingMarkerMessage>(data);
        if (markerMsg == null) return;

        // Ajouter le marqueur distant a notre liste (pour l'hote qui enregistre)
        if (_isHost && _state == RecordingState.Recording)
        {
            var marker = new RecordingMarker
            {
                timestamp = markerMsg.timestamp,
                markerType = markerMsg.markerType,
                userId = markerMsg.userId,
                userName = markerMsg.userName,
                note = markerMsg.note
            };

            _markers.Add(marker);
            OnMarkerAdded?.Invoke(marker);

            Debug.Log($"[RecordingManager] Marqueur distant recu: {marker.markerType} de {marker.userName}");
        }
    }

    #endregion

    #region Editor Helpers

#if UNITY_EDITOR
    [ContextMenu("Test Start Recording")]
    private void TestStartRecording()
    {
        _isHost = true; // Force host pour le test
        StartRecordingInternal();
    }

    [ContextMenu("Test Stop Recording")]
    private void TestStopRecording()
    {
        StopRecordingInternal();
    }

    [ContextMenu("Test Add Marker")]
    private void TestAddMarker()
    {
        AddMarker(MarkerType.Important, "Test marker");
    }

    [ContextMenu("Open Recordings Folder")]
    private void OpenRecordingsFolder()
    {
        string path = Path.Combine(Application.persistentDataPath, settings.outputFolder);
        Application.OpenURL("file://" + path);
    }
#endif

    #endregion
}
