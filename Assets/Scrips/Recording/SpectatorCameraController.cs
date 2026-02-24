using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections.Generic;
using Unity.Collections;

/// <summary>
/// Controle la camera spectateur utilisee pour l'enregistrement des reunions.
/// Cette camera capture une vue d'ensemble de la salle de reunion.
///
/// OPTIMISATION VR: Utilise AsyncGPUReadback pour ne pas bloquer le main thread.
/// </summary>
public class SpectatorCameraController : MonoBehaviour
{
    public static SpectatorCameraController Instance { get; private set; }

    [Header("=== Camera Settings ===")]
    [Tooltip("Resolution de la RenderTexture (largeur)")]
    public int renderWidth = 1920;

    [Tooltip("Resolution de la RenderTexture (hauteur)")]
    public int renderHeight = 1080;

    [Tooltip("Depth buffer bits (16, 24, ou 32)")]
    public int depthBits = 24;

    [Header("=== References ===")]
    [Tooltip("Camera spectateur (auto-detectee si null)")]
    public Camera spectatorCamera;

    [Tooltip("AudioListener pour capturer l'audio (auto-detecte si null)")]
    public AudioListener audioListener;

    [Header("=== Async Settings ===")]
    [Tooltip("Nombre de buffers dans le pool (double/triple buffering)")]
    public int bufferPoolSize = 3;

    [Tooltip("Nombre max de requetes async en attente")]
    public int maxPendingRequests = 2;

    [Header("=== Status ===")]
    [SerializeField] private bool _isRecording = false;
    public bool IsRecording => _isRecording;

    [SerializeField] private int _pendingRequests = 0;
    public int PendingRequests => _pendingRequests;

    [SerializeField] private int _framesCaptures = 0;
    public int FramesCaptured => _framesCaptures;

    // RenderTexture pour la capture video
    private RenderTexture _renderTexture;
    public RenderTexture RenderTexture => _renderTexture;

    // Pool de buffers pour eviter les allocations
    private Queue<NativeArray<byte>> _bufferPool = new Queue<NativeArray<byte>>();
    private int _bufferSize;

    // Callback pour les frames capturees
    private Action<NativeArray<byte>, int, int> _onFrameCaptured;

    // Events
    public static event Action OnRecordingStarted;
    public static event Action OnRecordingStopped;
    public static event Action<RenderTexture> OnRenderTextureCreated;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SpectatorCamera] Instance deja existante, destruction du doublon.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-detect camera si non assignee
        if (spectatorCamera == null)
        {
            spectatorCamera = GetComponent<Camera>();
        }

        // Auto-detect AudioListener si non assigne
        if (audioListener == null)
        {
            audioListener = GetComponent<AudioListener>();
        }

        // La camera est desactivee par defaut
        if (spectatorCamera != null)
        {
            spectatorCamera.enabled = false;
        }

        Debug.Log("[SpectatorCamera] Initialise (AsyncGPUReadback optimise).");
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        ReleaseRenderTexture();
        ReleaseBufferPool();
    }

    #region Buffer Pool Management

    /// <summary>
    /// Initialise le pool de buffers pour les captures async.
    /// </summary>
    private void InitializeBufferPool()
    {
        ReleaseBufferPool();

        // RGB24 = 3 bytes par pixel
        _bufferSize = renderWidth * renderHeight * 3;

        for (int i = 0; i < bufferPoolSize; i++)
        {
            var buffer = new NativeArray<byte>(_bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _bufferPool.Enqueue(buffer);
        }

        Debug.Log($"[SpectatorCamera] Buffer pool initialise: {bufferPoolSize} buffers de {_bufferSize / 1024}KB");
    }

    /// <summary>
    /// Libere tous les buffers du pool.
    /// </summary>
    private void ReleaseBufferPool()
    {
        while (_bufferPool.Count > 0)
        {
            var buffer = _bufferPool.Dequeue();
            if (buffer.IsCreated)
            {
                buffer.Dispose();
            }
        }
    }

    /// <summary>
    /// Obtient un buffer du pool (ou en cree un nouveau si necessaire).
    /// </summary>
    private NativeArray<byte> GetBuffer()
    {
        if (_bufferPool.Count > 0)
        {
            return _bufferPool.Dequeue();
        }

        // Pool epuise, creer un buffer temporaire (devrait etre rare)
        Debug.LogWarning("[SpectatorCamera] Pool epuise, creation d'un buffer temporaire");
        return new NativeArray<byte>(_bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
    }

    /// <summary>
    /// Retourne un buffer au pool.
    /// </summary>
    public void ReturnBuffer(NativeArray<byte> buffer)
    {
        if (buffer.IsCreated)
        {
            if (_bufferPool.Count < bufferPoolSize)
            {
                _bufferPool.Enqueue(buffer);
            }
            else
            {
                // Pool plein, liberer le buffer
                buffer.Dispose();
            }
        }
    }

    #endregion

    #region RenderTexture Management

    /// <summary>
    /// Cree la RenderTexture pour la capture video.
    /// </summary>
    public void CreateRenderTexture()
    {
        if (_renderTexture != null)
        {
            ReleaseRenderTexture();
        }

        _renderTexture = new RenderTexture(renderWidth, renderHeight, depthBits, RenderTextureFormat.ARGB32);
        _renderTexture.name = "SpectatorCameraRT";
        _renderTexture.antiAliasing = 1; // Reduit pour performance (etait 2)
        _renderTexture.Create();

        if (spectatorCamera != null)
        {
            spectatorCamera.targetTexture = _renderTexture;
        }

        Debug.Log($"[SpectatorCamera] RenderTexture creee: {renderWidth}x{renderHeight}");
        OnRenderTextureCreated?.Invoke(_renderTexture);
    }

    /// <summary>
    /// Libere la RenderTexture.
    /// </summary>
    public void ReleaseRenderTexture()
    {
        if (spectatorCamera != null)
        {
            spectatorCamera.targetTexture = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
            Debug.Log("[SpectatorCamera] RenderTexture liberee.");
        }
    }

    #endregion

    #region Capture Control

    /// <summary>
    /// Active la camera spectateur et prepare la capture.
    /// </summary>
    /// <param name="onFrameCaptured">Callback appele quand une frame est capturee (data, width, height)</param>
    public void StartCapture(Action<NativeArray<byte>, int, int> onFrameCaptured = null)
    {
        if (_isRecording)
        {
            Debug.LogWarning("[SpectatorCamera] Capture deja en cours.");
            return;
        }

        _onFrameCaptured = onFrameCaptured;
        _framesCaptures = 0;
        _pendingRequests = 0;

        // Activer le GameObject s'il est desactive
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
            Debug.Log("[SpectatorCamera] GameObject active.");
        }

        // Creer la RenderTexture si necessaire
        if (_renderTexture == null)
        {
            CreateRenderTexture();
        }

        // Initialiser le pool de buffers
        InitializeBufferPool();

        // Activer la camera
        if (spectatorCamera != null)
        {
            spectatorCamera.enabled = true;
        }

        // Activer l'AudioListener (desactiver celui du joueur local)
        if (audioListener != null)
        {
            // Desactiver les autres AudioListeners
            AudioListener[] allListeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            foreach (var listener in allListeners)
            {
                if (listener != audioListener && listener.enabled)
                {
                    listener.enabled = false;
                    Debug.Log($"[SpectatorCamera] AudioListener desactive sur: {listener.gameObject.name}");
                }
            }
            audioListener.enabled = true;
        }

        _isRecording = true;
        Debug.Log("[SpectatorCamera] Capture demarree (mode AsyncGPUReadback).");
        OnRecordingStarted?.Invoke();
    }

    /// <summary>
    /// Arrete la capture.
    /// </summary>
    public void StopCapture()
    {
        if (!_isRecording)
        {
            Debug.LogWarning("[SpectatorCamera] Aucune capture en cours.");
            return;
        }

        _isRecording = false;
        _onFrameCaptured = null;

        // Desactiver la camera
        if (spectatorCamera != null)
        {
            spectatorCamera.enabled = false;
        }

        // Reactiver l'AudioListener du joueur local
        if (audioListener != null)
        {
            audioListener.enabled = false;

            // Trouver et reactiver l'AudioListener du joueur local
            var localPlayer = VRGameManager.Instance?.GetLocalPlayer();
            if (localPlayer != null)
            {
                var playerListener = localPlayer.GetComponentInChildren<AudioListener>();
                if (playerListener != null)
                {
                    playerListener.enabled = true;
                    Debug.Log("[SpectatorCamera] AudioListener du joueur local reactive.");
                }
            }
        }

        // Desactiver le GameObject
        gameObject.SetActive(false);

        Debug.Log($"[SpectatorCamera] Capture arretee. {_framesCaptures} frames capturees.");
        OnRecordingStopped?.Invoke();
    }

    #endregion

    #region Async Frame Capture

    /// <summary>
    /// Demande la capture d'une frame de maniere asynchrone (non-bloquante).
    /// La frame sera delivree via le callback fourni a StartCapture().
    /// </summary>
    /// <returns>True si la requete a ete envoyee, false si trop de requetes en attente</returns>
    public bool RequestFrameAsync()
    {
        if (_renderTexture == null || !_isRecording)
        {
            return false;
        }

        // Limiter le nombre de requetes en attente pour eviter l'accumulation
        if (_pendingRequests >= maxPendingRequests)
        {
            return false;
        }

        _pendingRequests++;

        // Demande de lecture asynchrone du GPU
        AsyncGPUReadback.Request(_renderTexture, 0, TextureFormat.RGB24, OnAsyncReadbackComplete);

        return true;
    }

    /// <summary>
    /// Callback appele quand la lecture async du GPU est terminee.
    /// ATTENTION: Peut etre appele sur un thread different!
    /// </summary>
    private void OnAsyncReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _pendingRequests--;

        if (request.hasError)
        {
            Debug.LogWarning("[SpectatorCamera] AsyncGPUReadback error");
            return;
        }

        if (!_isRecording)
        {
            // Enregistrement arrete pendant la requete
            return;
        }

        // Obtenir les donnees
        NativeArray<byte> data = request.GetData<byte>();

        if (data.Length == 0)
        {
            Debug.LogWarning("[SpectatorCamera] AsyncGPUReadback returned empty data");
            return;
        }

        // Copier dans un buffer du pool (car les donnees de la requete sont temporaires)
        NativeArray<byte> buffer = GetBuffer();
        NativeArray<byte>.Copy(data, buffer, data.Length);

        _framesCaptures++;

        // Appeler le callback avec les donnees
        _onFrameCaptured?.Invoke(buffer, renderWidth, renderHeight);
    }

    #endregion

    #region Legacy Synchronous Capture (Deprecated)

    /// <summary>
    /// [DEPRECATED] Capture une frame de maniere synchrone (bloquante).
    /// Utilisez RequestFrameAsync() a la place pour de meilleures performances VR.
    /// </summary>
    [Obsolete("Utilisez RequestFrameAsync() pour de meilleures performances VR")]
    public Texture2D CaptureFrame()
    {
        if (_renderTexture == null || !_isRecording)
        {
            Debug.LogWarning("[SpectatorCamera] Impossible de capturer: pas de RenderTexture ou pas en enregistrement.");
            return null;
        }

        // Creer une Texture2D temporaire
        Texture2D frame = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);

        // Lire les pixels de la RenderTexture (BLOQUANT!)
        RenderTexture.active = _renderTexture;
        frame.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
        frame.Apply();
        RenderTexture.active = null;

        return frame;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Obtient la position et rotation actuelles de la camera.
    /// </summary>
    public (Vector3 position, Quaternion rotation) GetCameraTransform()
    {
        if (spectatorCamera != null)
        {
            return (spectatorCamera.transform.position, spectatorCamera.transform.rotation);
        }
        return (Vector3.zero, Quaternion.identity);
    }

    /// <summary>
    /// Definit la position et rotation de la camera.
    /// </summary>
    public void SetCameraTransform(Vector3 position, Quaternion rotation)
    {
        if (spectatorCamera != null)
        {
            spectatorCamera.transform.position = position;
            spectatorCamera.transform.rotation = rotation;
            Debug.Log($"[SpectatorCamera] Position mise a jour: {position}");
        }
    }

    /// <summary>
    /// Verifie si la camera est prete pour l'enregistrement.
    /// </summary>
    public bool IsReady()
    {
        return spectatorCamera != null && audioListener != null;
    }

    #endregion

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Visualiser le champ de vision de la camera dans l'editeur
        if (spectatorCamera != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = spectatorCamera.transform.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, spectatorCamera.fieldOfView,
                spectatorCamera.farClipPlane, spectatorCamera.nearClipPlane,
                spectatorCamera.aspect);
        }
    }
#endif
}
