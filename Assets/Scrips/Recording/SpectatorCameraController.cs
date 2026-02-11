using UnityEngine;
using UnityEngine.Rendering;
using System;

/// <summary>
/// Controle la camera spectateur utilisee pour l'enregistrement des reunions.
/// Cette camera capture une vue d'ensemble de la salle de reunion.
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

    [Header("=== Status ===")]
    [SerializeField] private bool _isRecording = false;
    public bool IsRecording => _isRecording;

    // RenderTexture pour la capture video
    private RenderTexture _renderTexture;
    public RenderTexture RenderTexture => _renderTexture;

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

        Debug.Log("[SpectatorCamera] Initialise.");
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        ReleaseRenderTexture();
    }

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
        _renderTexture.antiAliasing = 2;
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

    /// <summary>
    /// Active la camera spectateur et prepare la capture.
    /// </summary>
    public void StartCapture()
    {
        if (_isRecording)
        {
            Debug.LogWarning("[SpectatorCamera] Capture deja en cours.");
            return;
        }

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
        Debug.Log("[SpectatorCamera] Capture demarree.");
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

        _isRecording = false;

        // Desactiver le GameObject
        gameObject.SetActive(false);

        Debug.Log("[SpectatorCamera] Capture arretee.");
        OnRecordingStopped?.Invoke();
    }

    /// <summary>
    /// Capture une frame de la camera spectateur.
    /// </summary>
    /// <returns>Texture2D de la frame capturee</returns>
    public Texture2D CaptureFrame()
    {
        if (_renderTexture == null || !_isRecording)
        {
            Debug.LogWarning("[SpectatorCamera] Impossible de capturer: pas de RenderTexture ou pas en enregistrement.");
            return null;
        }

        // Creer une Texture2D temporaire
        Texture2D frame = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);

        // Lire les pixels de la RenderTexture
        RenderTexture.active = _renderTexture;
        frame.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
        frame.Apply();
        RenderTexture.active = null;

        return frame;
    }

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
