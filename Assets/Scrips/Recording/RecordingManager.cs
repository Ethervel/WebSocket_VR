using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Unity.Collections;

/// <summary>
/// Gestionnaire principal de l'enregistrement des reunions.
/// Singleton - Orchestration de la capture video/audio.
/// Seul l'hote peut demarrer/arreter l'enregistrement.
///
/// OPTIMISATION VR:
/// - AsyncGPUReadback pour lecture GPU non-bloquante
/// - Encodage JPEG dans un thread background
/// - Pipeline a 3 etages: Capture -> Encode -> Write
/// </summary>
public class RecordingManager : MonoBehaviour
{
    public static RecordingManager Instance { get; private set; }

    [Header("=== Settings ===")]
    public RecordingSettings settings = new RecordingSettings();

    [Header("=== Output Path ===")]
    [Tooltip("Utiliser un chemin personnalise au lieu de AppData")]
    public bool useCustomOutputPath = false;

    [Tooltip("Chemin absolu pour les enregistrements (ex: D:/Recordings)")]
    public string customOutputPath = "";

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

    [Header("=== Performance Stats ===")]
    [SerializeField] private int _framesRequested = 0;
    [SerializeField] private int _framesEncoded = 0;
    [SerializeField] private int _framesWritten = 0;
    [SerializeField] private int _encodeQueueSize = 0;
    [SerializeField] private int _writeQueueSize = 0;

    // Metadonnees de l'enregistrement en cours
    private RecordingMetadata _currentMetadata;
    private List<RecordingMarker> _markers = new List<RecordingMarker>();

    // Capture
    private Coroutine _recordingCoroutine;
    private AudioCapture _audioCapture;
    private string _currentRecordingPath;
    private DateTime _recordingStartTime;

    // Pipeline async a 3 etages
    // Etage 1: Raw pixel data du GPU (NativeArray)
    private ConcurrentQueue<RawFrameData> _encodeQueue = new ConcurrentQueue<RawFrameData>();

    // Etage 2: Encoded JPEG data
    private ConcurrentQueue<EncodedFrameData> _writeQueue = new ConcurrentQueue<EncodedFrameData>();

    // Thread control
    private volatile bool _isProcessingFrames = false;
    private Thread _encodeThread;
    private Thread _writeThread;
    private int _frameIndex = 0;

    // Struct pour les donnees brutes (avant encodage)
    private struct RawFrameData
    {
        public byte[] pixelData;  // Copie des pixels (managed array)
        public int width;
        public int height;
        public int frameIndex;
    }

    // Struct pour les donnees encodees (apres encodage JPEG)
    private struct EncodedFrameData
    {
        public byte[] jpegData;
        public string filePath;
    }

    // Events
    public static event Action OnRecordingStarted;
    public static event Action OnRecordingStopped;
    public static event Action<RecordingState> OnStateChanged;
    public static event Action<float> OnTimeUpdated;
    public static event Action<RecordingMarker> OnMarkerAdded;
    public static event Action<RecordingStatusMessage> OnRemoteRecordingStatusChanged;
    public static event Action<bool, string> OnRemoteRecordingChanged;

    /// <summary>
    /// Retourne le chemin de base pour les enregistrements.
    /// Utilise customOutputPath si active, sinon persistentDataPath.
    /// </summary>
    private string GetOutputBasePath()
    {
        if (useCustomOutputPath && !string.IsNullOrEmpty(customOutputPath))
        {
            return customOutputPath;
        }
        return Path.Combine(Application.persistentDataPath, settings.outputFolder);
    }

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
        string outputPath = GetOutputBasePath();
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
            Debug.Log($"[RecordingManager] Dossier cree: {outputPath}");
        }
    }

    void Start()
    {
        // Setup audio capture
        _audioCapture = gameObject.AddComponent<AudioCapture>();
        _audioCapture.Initialize(settings);

        // S'abonner aux evenements reseau
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;

        Debug.Log("[RecordingManager] Initialise (Pipeline async optimise VR).");
    }

    /// <summary>
    /// Recherche la SpectatorCamera dans la scene Meet (pas Bootstrap/Lobby).
    /// Priorise les cameras dans la scene "Meet" ou avec "Meet" dans le nom.
    /// </summary>
    private bool FindSpectatorCamera()
    {
        if (spectatorCamera != null) return true;

        var cameras = FindObjectsByType<SpectatorCameraController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (cameras.Length == 0)
        {
            Debug.LogError("[RecordingManager] SpectatorCamera non trouvee! Ajoutez un SpectatorCameraController dans la scene Meet.");
            return false;
        }

        // Si une seule camera, la prendre
        if (cameras.Length == 1)
        {
            spectatorCamera = cameras[0];
            Debug.Log($"[RecordingManager] SpectatorCamera trouvee: {spectatorCamera.gameObject.name}");
            return true;
        }

        // Plusieurs cameras - prioriser celle dans la scene Meet
        foreach (var cam in cameras)
        {
            // Verifier si la camera est dans la scene Meet
            if (cam.gameObject.scene.name == "Meet" ||
                cam.gameObject.scene.name.Contains("Meet"))
            {
                spectatorCamera = cam;
                Debug.Log($"[RecordingManager] SpectatorCamera trouvee dans Meet: {spectatorCamera.gameObject.name}");
                return true;
            }
        }

        // Fallback: chercher par nom contenant "Meet" ou "Spectator"
        foreach (var cam in cameras)
        {
            string objName = cam.gameObject.name.ToLower();
            if (objName.Contains("meet") || objName.Contains("spectator") || objName.Contains("record"))
            {
                spectatorCamera = cam;
                Debug.Log($"[RecordingManager] SpectatorCamera trouvee par nom: {spectatorCamera.gameObject.name}");
                return true;
            }
        }

        // Dernier recours: prendre la premiere qui n'est PAS dans Bootstrap
        foreach (var cam in cameras)
        {
            if (cam.gameObject.scene.name != "Bootstrap" &&
                !cam.gameObject.scene.name.Contains("Lobby"))
            {
                spectatorCamera = cam;
                Debug.Log($"[RecordingManager] SpectatorCamera trouvee (non-Bootstrap): {spectatorCamera.gameObject.name}");
                return true;
            }
        }

        // Vraiment dernier recours
        spectatorCamera = cameras[0];
        Debug.LogWarning($"[RecordingManager] Plusieurs SpectatorCameras trouvees, utilisation de: {spectatorCamera.gameObject.name} (scene: {spectatorCamera.gameObject.scene.name})");
        return true;
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

            // Update stats pour debug
            _encodeQueueSize = _encodeQueue.Count;
            _writeQueueSize = _writeQueue.Count;
        }
    }

    #region Public API

    /// <summary>
    /// Demarre l'enregistrement (hote uniquement).
    /// Note: Non disponible sur Android/Quest (AsyncGPUReadback + FFmpeg non supportes).
    /// </summary>
    public void StartRecording()
    {
#if UNITY_ANDROID
        Debug.LogWarning("[RecordingManager] Recording non disponible sur Android/Quest.");
        return;
#endif

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

        // Chercher la SpectatorCamera (lazy loading car Meet scene chargee apres Bootstrap)
        if (!FindSpectatorCamera())
        {
            Debug.LogError("[RecordingManager] Impossible de demarrer sans SpectatorCamera.");
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
    /// Retourne false sur Android/Quest.
    /// </summary>
    public bool CanRecord()
    {
#if UNITY_ANDROID
        return false;
#else
        // Tenter de trouver la camera si pas encore assignee
        if (spectatorCamera == null)
        {
            FindSpectatorCamera();
        }
        return spectatorCamera != null && spectatorCamera.IsReady();
#endif
    }

    #endregion

    #region Internal Recording Logic

    private void StartRecordingInternal()
    {
        SetState(RecordingState.Starting);

        _recordingStartTime = DateTime.UtcNow;
        _elapsedTime = 0f;
        _markers.Clear();
        _frameIndex = 0;
        _framesRequested = 0;
        _framesEncoded = 0;
        _framesWritten = 0;

        // Vider les queues
        while (_encodeQueue.TryDequeue(out _)) { }
        while (_writeQueue.TryDequeue(out _)) { }

        // Creer le dossier pour cet enregistrement
        string timestamp = _recordingStartTime.ToString("yyyy-MM-dd_HH-mm-ss");
        string roomId = VRRoomManager.Instance?.CurrentRoomId ?? "local";
        string folderName = $"Meeting_{roomId}_{timestamp}";
        _currentRecordingPath = Path.Combine(GetOutputBasePath(), folderName);
        Directory.CreateDirectory(_currentRecordingPath);

        string framesPath = Path.Combine(_currentRecordingPath, "frames");
        Directory.CreateDirectory(framesPath);

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

        // Demarrer la capture de la camera spectateur avec le callback async
        if (spectatorCamera != null)
        {
            spectatorCamera.renderWidth = settings.width;
            spectatorCamera.renderHeight = settings.height;
            spectatorCamera.StartCapture(OnFrameCapturedAsync);
        }

        // Demarrer la capture audio
        if (settings.captureAudio && _audioCapture != null)
        {
            _audioCapture.StartCapture();
        }

        // Demarrer les threads de traitement background
        _isProcessingFrames = true;
        _encodeThread = new Thread(EncodeThreadLoop);
        _encodeThread.Name = "RecordingEncodeThread";
        _encodeThread.IsBackground = true;
        _encodeThread.Start();

        _writeThread = new Thread(WriteThreadLoop);
        _writeThread.Name = "RecordingWriteThread";
        _writeThread.IsBackground = true;
        _writeThread.Start();

        // IMPORTANT: Mettre l'etat a Recording AVANT de lancer la coroutine
        SetState(RecordingState.Recording);
        OnRecordingStarted?.Invoke();

        Debug.Log($"[RecordingManager] Enregistrement demarre (pipeline async): {_currentRecordingPath}");

        // Demarrer la coroutine de demande de frames (non-bloquante)
        _recordingCoroutine = StartCoroutine(RequestFramesCoroutine());
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

        // Signaler aux threads de s'arreter et attendre qu'ils finissent
        _isProcessingFrames = false;

        // Finaliser les metadonnees
        _currentMetadata.endTimeUtc = DateTime.UtcNow.ToString("o");
        _currentMetadata.durationSeconds = _elapsedTime;
        _currentMetadata.markers = new List<RecordingMarker>(_markers);

        // Sauvegarder
        SetState(RecordingState.Saving);
        StartCoroutine(SaveRecordingCoroutine());
    }

    /// <summary>
    /// Coroutine qui demande des frames a intervalles reguliers (non-bloquante).
    /// </summary>
    private IEnumerator RequestFramesCoroutine()
    {
        float frameInterval = 1f / settings.frameRate;
        float nextCaptureTime = 0f;

        // Attendre une frame pour que tout soit initialise
        yield return null;

        while (_state == RecordingState.Recording)
        {
            if (Time.time >= nextCaptureTime)
            {
                // Demander une frame de maniere async (ne bloque pas!)
                if (spectatorCamera != null && spectatorCamera.RequestFrameAsync())
                {
                    _framesRequested++;
                }

                nextCaptureTime = Time.time + frameInterval;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Callback appele quand une frame est capturee de maniere async.
    /// ATTENTION: Peut etre appele depuis un thread different du main thread!
    /// </summary>
    private void OnFrameCapturedAsync(NativeArray<byte> pixelData, int width, int height)
    {
        if (!_isProcessingFrames)
        {
            // Retourner le buffer au pool
            spectatorCamera?.ReturnBuffer(pixelData);
            return;
        }

        // Copier les donnees dans un managed array pour le thread d'encodage
        byte[] dataCopy = new byte[pixelData.Length];
        pixelData.CopyTo(dataCopy);

        // Retourner le buffer au pool immediatement
        spectatorCamera?.ReturnBuffer(pixelData);

        // Ajouter a la queue d'encodage
        int currentFrame = Interlocked.Increment(ref _frameIndex) - 1;
        _encodeQueue.Enqueue(new RawFrameData
        {
            pixelData = dataCopy,
            width = width,
            height = height,
            frameIndex = currentFrame
        });
    }

    /// <summary>
    /// Thread d'encodage JPEG en arriere-plan.
    /// </summary>
    private void EncodeThreadLoop()
    {
        string framesPath = Path.Combine(_currentRecordingPath, "frames");

        while (_isProcessingFrames || !_encodeQueue.IsEmpty)
        {
            if (_encodeQueue.TryDequeue(out RawFrameData rawFrame))
            {
                try
                {
                    // Encoder en JPEG (CPU-intensive mais hors du main thread!)
                    byte[] jpegData = EncodeToJPEG(rawFrame.pixelData, rawFrame.width, rawFrame.height, settings.jpegQuality);

                    if (jpegData != null && jpegData.Length > 0)
                    {
                        string framePath = Path.Combine(framesPath, $"frame_{rawFrame.frameIndex:D6}.jpg");

                        // Ajouter a la queue d'ecriture
                        _writeQueue.Enqueue(new EncodedFrameData
                        {
                            jpegData = jpegData,
                            filePath = framePath
                        });

                        Interlocked.Increment(ref _framesEncoded);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RecordingManager] Erreur encodage frame {rawFrame.frameIndex}: {e.Message}");
                }
            }
            else
            {
                // Queue vide, petite pause pour eviter busy-waiting
                Thread.Sleep(1);
            }
        }

        Debug.Log($"[RecordingManager] Thread d'encodage termine. {_framesEncoded} frames encodees.");
    }

    /// <summary>
    /// Encode un tableau de pixels RGB en JPEG.
    /// Utilise une implementation basique sans Unity (pour thread background).
    /// </summary>
    private byte[] EncodeToJPEG(byte[] rgbData, int width, int height, int quality)
    {
        // Note: On utilise une Texture2D temporaire sur le main thread via une queue
        // Pour un vrai encoding hors main thread, il faudrait une lib native comme libjpeg-turbo
        // Ici on utilise une approche hybride: les donnees sont deja copiees, on encode sur ce thread

        // WORKAROUND: Comme EncodeToJPG de Unity n'est pas thread-safe,
        // on utilise un encodeur JPEG pur C# ou on revient sur le main thread
        // Pour l'instant, on utilise la queue vers le main thread avec un traitement batch

        // Encodage simplifie: utiliser les donnees brutes avec un header JPEG basique
        // En production, utiliser une vraie lib JPEG thread-safe
        return EncodeRGBToJPEGSimple(rgbData, width, height, quality);
    }

    /// <summary>
    /// Encodeur JPEG simplifie thread-safe.
    /// Note: Pour une meilleure qualite/compression, utiliser libjpeg-turbo via native plugin.
    /// </summary>
    private byte[] EncodeRGBToJPEGSimple(byte[] rgbData, int width, int height, int quality)
    {
        // Utilisation de System.Drawing si disponible, sinon fallback sur raw
        // Pour Unity, on va plutot utiliser ImageConversion sur le main thread via dispatcher

        // SOLUTION: Utiliser un dispatcher pour encoder sur le main thread de maniere batch
        // Mais pour eviter de bloquer, on stocke les frames raw et on encode en batch apres

        // Pour l'instant, sauvegarder en format PPM (simple, pas de compression mais thread-safe)
        // puis convertir en JPEG via FFmpeg a la fin

        // Ou mieux: utiliser le format TGA qui est simple a ecrire
        return EncodeToTGA(rgbData, width, height);
    }

    /// <summary>
    /// Encode en format TGA (Targa) - simple et thread-safe.
    /// FFmpeg peut lire les TGA pour creer le MP4.
    /// </summary>
    private byte[] EncodeToTGA(byte[] rgbData, int width, int height)
    {
        // TGA header (18 bytes)
        byte[] header = new byte[18];
        header[2] = 2; // Uncompressed RGB
        header[12] = (byte)(width & 0xFF);
        header[13] = (byte)((width >> 8) & 0xFF);
        header[14] = (byte)(height & 0xFF);
        header[15] = (byte)((height >> 8) & 0xFF);
        header[16] = 24; // 24 bits per pixel
        header[17] = 0x20; // Top-left origin

        // TGA utilise BGR, pas RGB, et est inverse verticalement
        byte[] bgrData = new byte[rgbData.Length];
        int rowSize = width * 3;

        for (int y = 0; y < height; y++)
        {
            int srcRow = (height - 1 - y) * rowSize; // Inverser Y
            int dstRow = y * rowSize;

            for (int x = 0; x < width; x++)
            {
                int srcPixel = srcRow + x * 3;
                int dstPixel = dstRow + x * 3;

                // RGB -> BGR
                bgrData[dstPixel + 0] = rgbData[srcPixel + 2]; // B
                bgrData[dstPixel + 1] = rgbData[srcPixel + 1]; // G
                bgrData[dstPixel + 2] = rgbData[srcPixel + 0]; // R
            }
        }

        // Combiner header et data
        byte[] tgaData = new byte[header.Length + bgrData.Length];
        Buffer.BlockCopy(header, 0, tgaData, 0, header.Length);
        Buffer.BlockCopy(bgrData, 0, tgaData, header.Length, bgrData.Length);

        return tgaData;
    }

    /// <summary>
    /// Thread d'ecriture des fichiers en arriere-plan.
    /// </summary>
    private void WriteThreadLoop()
    {
        while (_isProcessingFrames || !_writeQueue.IsEmpty)
        {
            if (_writeQueue.TryDequeue(out EncodedFrameData frame))
            {
                try
                {
                    // Changer l'extension en .tga puisqu'on encode en TGA
                    string filePath = frame.filePath.Replace(".jpg", ".tga");
                    File.WriteAllBytes(filePath, frame.jpegData);
                    Interlocked.Increment(ref _framesWritten);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RecordingManager] Erreur ecriture: {e.Message}");
                }
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        Debug.Log($"[RecordingManager] Thread d'ecriture termine. {_framesWritten} frames ecrites.");
    }

    private IEnumerator SaveRecordingCoroutine()
    {
        Debug.Log("[RecordingManager] Sauvegarde en cours...");

        // Attendre que les threads de traitement terminent
        float timeout = 30f;
        float elapsed = 0f;

        while ((_encodeThread != null && _encodeThread.IsAlive) ||
               (_writeThread != null && _writeThread.IsAlive))
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;

            if (elapsed > timeout)
            {
                Debug.LogWarning("[RecordingManager] Timeout en attendant les threads de traitement.");
                break;
            }
        }

        Debug.Log($"[RecordingManager] Stats finales - Demandees: {_framesRequested}, Encodees: {_framesEncoded}, Ecrites: {_framesWritten}");

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

        // Generer le script d'encodage (mis a jour pour TGA)
        GenerateEncodeScriptTGA(_currentRecordingPath, settings.frameRate);

        // Tenter l'encodage automatique si FFmpeg est disponible
        if (FFmpegEncoder.IsAvailable())
        {
            Debug.Log("[RecordingManager] FFmpeg detecte, encodage automatique...");
            bool encodingComplete = false;

            // Lancer l'encodage en tache de fond (avec TGA au lieu de JPG)
            _ = EncodeToMp4AsyncTGA(
                _currentRecordingPath,
                settings.frameRate,
                progress => Debug.Log($"[RecordingManager] Encodage: {progress:F0}%"),
                (success, result) =>
                {
                    encodingComplete = true;
                    if (success)
                    {
                        Debug.Log($"[RecordingManager] MP4 cree: {result}");
                    }
                    else
                    {
                        Debug.LogWarning($"[RecordingManager] Echec encodage: {result}");
                    }
                }
            );

            // Attendre la fin de l'encodage (avec timeout)
            float encodeTimeout = 300f;
            elapsed = 0f;
            while (!encodingComplete && elapsed < encodeTimeout)
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

    /// <summary>
    /// Genere un script d'encodage pour les fichiers TGA.
    /// </summary>
    private void GenerateEncodeScriptTGA(string recordingPath, int frameRate)
    {
        bool hasAudio = File.Exists(Path.Combine(recordingPath, "audio.wav"));

        string command;
        if (hasAudio)
        {
            command = $"ffmpeg -y -framerate {frameRate} -i \"frames/frame_%06d.tga\" -i \"audio.wav\" " +
                     $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p " +
                     $"-c:a aac -b:a 128k -shortest \"recording.mp4\"";
        }
        else
        {
            command = $"ffmpeg -y -framerate {frameRate} -i \"frames/frame_%06d.tga\" " +
                     $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p \"recording.mp4\"";
        }

#if UNITY_STANDALONE_WIN
        string scriptPath = Path.Combine(recordingPath, "encode.bat");
        string scriptContent = $"@echo off\ncd /d \"{recordingPath}\"\n{command}\npause";
#else
        string scriptPath = Path.Combine(recordingPath, "encode.sh");
        string scriptContent = $"#!/bin/bash\ncd \"{recordingPath}\"\n{command}";
#endif

        File.WriteAllText(scriptPath, scriptContent);
        Debug.Log($"[RecordingManager] Script d'encodage genere: {scriptPath}");
    }

    /// <summary>
    /// Encode les TGA en MP4 via FFmpeg.
    /// </summary>
    private async Task EncodeToMp4AsyncTGA(string recordingPath, int frameRate,
        Action<float> onProgress, Action<bool, string> onComplete)
    {
        string framesPath = Path.Combine(recordingPath, "frames");
        string audioPath = Path.Combine(recordingPath, "audio.wav");
        string outputPath = Path.Combine(recordingPath, "recording.mp4");

        if (!Directory.Exists(framesPath))
        {
            onComplete?.Invoke(false, "Dossier frames non trouve");
            return;
        }

        string[] frames = Directory.GetFiles(framesPath, "*.tga");
        if (frames.Length == 0)
        {
            onComplete?.Invoke(false, "Aucune frame TGA trouvee");
            return;
        }

        bool hasAudio = File.Exists(audioPath);

        string framePattern = Path.Combine(framesPath, "frame_%06d.tga");
        string arguments;

        if (hasAudio)
        {
            arguments = $"-y -framerate {frameRate} -i \"{framePattern}\" -i \"{audioPath}\" " +
                       $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p " +
                       $"-c:a aac -b:a 128k -shortest \"{outputPath}\"";
        }
        else
        {
            arguments = $"-y -framerate {frameRate} -i \"{framePattern}\" " +
                       $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p \"{outputPath}\"";
        }

        Debug.Log($"[RecordingManager] Encodage TGA->MP4: {frames.Length} frames");

        try
        {
            await Task.Run(() =>
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data) && e.Data.Contains("frame="))
                    {
                        try
                        {
                            int frameIndex = e.Data.IndexOf("frame=");
                            string frameStr = e.Data.Substring(frameIndex + 6).TrimStart();
                            int spaceIndex = frameStr.IndexOf(' ');
                            if (spaceIndex > 0)
                            {
                                frameStr = frameStr.Substring(0, spaceIndex);
                                if (int.TryParse(frameStr, out int currentFrame))
                                {
                                    float progress = (float)currentFrame / frames.Length * 100f;
                                    onProgress?.Invoke(progress);
                                }
                            }
                        }
                        catch { }
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    onComplete?.Invoke(true, outputPath);
                }
                else
                {
                    onComplete?.Invoke(false, $"FFmpeg exit code: {process.ExitCode}");
                }
            });
        }
        catch (Exception e)
        {
            onComplete?.Invoke(false, e.Message);
        }
    }

    private List<string> GetCurrentParticipants()
    {
        var participants = new List<string>();

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

        VRNetworkManager.Instance.Send("recording-status", statusMsg);
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

        VRNetworkManager.Instance.Send("recording-marker", markerMsg);
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

        if (_isHost && _state == RecordingState.Recording) return;

        OnRemoteRecordingStatusChanged?.Invoke(status);

        if (status.isRecording && !_isRemoteRecording)
        {
            _isRemoteRecording = true;
            _remoteRecordingHostName = status.hostName ?? "Unknown";
            Debug.Log($"[RecordingManager] Enregistrement demarre par {_remoteRecordingHostName}");
            OnRemoteRecordingChanged?.Invoke(true, _remoteRecordingHostName);
        }
        else if (!status.isRecording && _isRemoteRecording)
        {
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
        _isHost = true;
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
        string path = GetOutputBasePath();
        Application.OpenURL("file://" + path);
    }
#endif

    #endregion
}
