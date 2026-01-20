using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRMeeting.Sharing;

/// <summary>
/// Gestionnaire de présentation de fichiers sur le whiteboard.
/// Supporte les images (PNG/JPG/GIF) et PDF (via conversion serveur).
/// </summary>
public class FilePresentationManager : MonoBehaviour
{
    public static FilePresentationManager Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("Qualité JPEG pour l'encodage (0-100)")]
    [Range(0, 100)]
    public int jpegQuality = 70;

    [Tooltip("Largeur max de l'image")]
    public int maxImageWidth = 1920;

    [Tooltip("Hauteur max de l'image")]
    public int maxImageHeight = 1080;

    // État local de présentation
    private bool _isPresenting = false;
    private string _presentingFileId;
    private string _presentingToWhiteboardId;
    private Whiteboard _presentingToWhiteboard;
    private int _totalPages = 1;
    private int _currentPage = 0;
    private Dictionary<int, byte[]> _pageCache = new Dictionary<int, byte[]>();
    private Texture2D _displayTexture;

    // Zoom et Pan
    private float _zoomLevel = 1f;
    private Vector2 _panOffset = Vector2.zero;
    private const float MIN_ZOOM = 0.5f;
    private const float MAX_ZOOM = 4f;
    private const float ZOOM_STEP = 0.25f;

    // État de réception par whiteboard
    private class WhiteboardPresentState
    {
        public string presenterId;
        public string presenterName;
        public string fileId;
        public string fileName;
        public int totalPages;
        public int currentPage;
        public Texture2D displayTexture;
    }
    private Dictionary<string, WhiteboardPresentState> _receivingStates = new Dictionary<string, WhiteboardPresentState>();

    // Coroutines
    private Coroutine _pendingRequestCoroutine;

    // Events
    public static event Action<string, string, string, string> OnPresentationStarted; // wbId, fileId, presenterId, presenterName
    public static event Action<string, string> OnPresentationStopped; // wbId, presenterId
    public static event Action<string, int, int> OnPageChanged; // fileId, currentPage, totalPages
    public static event Action<string, string> OnPresentationError; // context, error
    public static event Action<float, Vector2> OnZoomPanChanged; // zoomLevel, panOffset

    // Public properties
    public bool IsPresenting => _isPresenting;
    public string PresentingFileId => _presentingFileId;
    public int CurrentPage => _currentPage;
    public int TotalPages => _totalPages;
    public float ZoomLevel => _zoomLevel;
    public Vector2 PanOffset => _panOffset;

    public bool IsWhiteboardReceiving(string whiteboardId)
    {
        return _receivingStates.ContainsKey(whiteboardId);
    }

    public string GetPresenterName(string whiteboardId)
    {
        if (_receivingStates.TryGetValue(whiteboardId, out var state))
            return state.presenterName;
        return null;
    }

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

    void OnEnable()
    {
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
    }

    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
    }

    void OnDestroy()
    {
        StopPresentation();

        // MINOR FIX: Ensure pending request coroutine is stopped on destroy
        if (_pendingRequestCoroutine != null)
        {
            StopCoroutine(_pendingRequestCoroutine);
            _pendingRequestCoroutine = null;
        }

        CleanupAllReceivingStates();
    }

    #region Public API

    /// <summary>
    /// Vérifie si un fichier peut être présenté (image ou PDF)
    /// </summary>
    public bool CanPresentFile(string fileId)
    {
        if (FileShareManager.Instance == null) return false;

        var metadata = FileShareManager.Instance.GetFileMetadata(fileId);
        if (metadata == null) return false;

        string ext = metadata.fileExtension.ToLower();
        return ext == "png" || ext == "jpg" || ext == "jpeg" || ext == "gif" || ext == "pdf";
    }

    /// <summary>
    /// Démarre la présentation d'un fichier sur le whiteboard spécifié
    /// </summary>
    public void StartPresentation(string fileId, Whiteboard targetWhiteboard)
    {
        if (_isPresenting)
        {
            Debug.LogWarning("[FilePresent] Already presenting");
            return;
        }
        if (targetWhiteboard == null)
        {
            Debug.LogError("[FilePresent] Target whiteboard is null");
            return;
        }
        if (!CanPresentFile(fileId))
        {
            Debug.LogError("[FilePresent] File cannot be presented");
            OnPresentationError?.Invoke("start", "File type not supported");
            return;
        }
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            Debug.LogError("[FilePresent] Not in a room");
            return;
        }

        // Vérifier si le whiteboard est déjà utilisé
        if (ScreenShareManager.Instance != null && ScreenShareManager.Instance.IsWhiteboardReceiving(targetWhiteboard.id))
        {
            OnPresentationError?.Invoke("start", "Whiteboard is being used for screen share");
            return;
        }
        if (_receivingStates.ContainsKey(targetWhiteboard.id))
        {
            OnPresentationError?.Invoke("start", "Whiteboard is being used for another presentation");
            return;
        }

        var metadata = FileShareManager.Instance.GetFileMetadata(fileId);
        string ext = metadata.fileExtension.ToLower();

        _isPresenting = true;
        _presentingFileId = fileId;
        _presentingToWhiteboardId = targetWhiteboard.id;
        _presentingToWhiteboard = targetWhiteboard;
        _pageCache.Clear();

        string presenterName = PlayerPrefs.GetString("PlayerName", "Player");
        targetWhiteboard.EnterPresentationMode(presenterName);

        Debug.Log($"[FilePresent] Starting presentation of {metadata.fileName}");

        if (ext == "pdf")
        {
            StartPdfConversion(fileId);
        }
        else
        {
            StartImagePresentation(fileId);
        }
    }

    /// <summary>
    /// Navigue vers une page spécifique
    /// </summary>
    public void NavigateToPage(int pageNumber)
    {
        if (!_isPresenting) return;
        if (pageNumber < 0 || pageNumber >= _totalPages) return;
        if (pageNumber == _currentPage) return;

        _currentPage = pageNumber;

        // Envoyer message de navigation
        VRNetworkManager.Instance.Send("file-present-navigate", new FilePresentNavigateData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            whiteboardId = _presentingToWhiteboardId,
            fileId = _presentingFileId,
            newPageNumber = pageNumber,
            presenterId = VRNetworkManager.LocalId
        });

        // Envoyer aussi l'image de la page pour les autres joueurs
        if (_pageCache.TryGetValue(pageNumber, out byte[] pageBytes))
        {
            SendPage(pageNumber, pageBytes);
        }

        // Afficher la page localement
        DisplayPage(pageNumber);
        OnPageChanged?.Invoke(_presentingFileId, _currentPage, _totalPages);
    }

    public void NextPage() => NavigateToPage(_currentPage + 1);
    public void PreviousPage() => NavigateToPage(_currentPage - 1);

    #endregion

    #region Zoom and Pan

    /// <summary>
    /// Zoom avant
    /// </summary>
    public void ZoomIn()
    {
        SetZoom(_zoomLevel + ZOOM_STEP);
    }

    /// <summary>
    /// Zoom arrière
    /// </summary>
    public void ZoomOut()
    {
        SetZoom(_zoomLevel - ZOOM_STEP);
    }

    /// <summary>
    /// Définit le niveau de zoom
    /// </summary>
    public void SetZoom(float zoom)
    {
        float newZoom = Mathf.Clamp(zoom, MIN_ZOOM, MAX_ZOOM);
        if (Mathf.Approximately(newZoom, _zoomLevel)) return;

        _zoomLevel = newZoom;

        // Ajuster le pan pour rester dans les limites
        ClampPan();

        OnZoomPanChanged?.Invoke(_zoomLevel, _panOffset);
        RefreshDisplay();

        // Synchroniser avec les autres joueurs
        SendZoomPanUpdate();
    }

    /// <summary>
    /// Réinitialise le zoom et le pan
    /// </summary>
    public void ResetZoomPan()
    {
        _zoomLevel = 1f;
        _panOffset = Vector2.zero;
        OnZoomPanChanged?.Invoke(_zoomLevel, _panOffset);
        RefreshDisplay();

        // Synchroniser avec les autres joueurs
        SendZoomPanUpdate();
    }

    /// <summary>
    /// Déplace la vue (pan)
    /// </summary>
    public void Pan(Vector2 delta)
    {
        _panOffset += delta;
        ClampPan();
        OnZoomPanChanged?.Invoke(_zoomLevel, _panOffset);
        RefreshDisplay();

        // Synchroniser avec les autres joueurs
        SendZoomPanUpdate();
    }

    /// <summary>
    /// Définit la position de pan
    /// </summary>
    public void SetPan(Vector2 position)
    {
        _panOffset = position;
        ClampPan();
        OnZoomPanChanged?.Invoke(_zoomLevel, _panOffset);
        RefreshDisplay();

        // Synchroniser avec les autres joueurs
        SendZoomPanUpdate();
    }

    private void ClampPan()
    {
        // Limite le pan pour que l'image reste visible
        // À zoom 1x, pas de pan possible
        // À zoom 2x, on peut se déplacer de ±0.5 dans chaque direction (normalisé)
        float maxPan = Mathf.Max(0, (_zoomLevel - 1f) / (2f * _zoomLevel));
        _panOffset.x = Mathf.Clamp(_panOffset.x, -maxPan, maxPan);
        _panOffset.y = Mathf.Clamp(_panOffset.y, -maxPan, maxPan);
    }

    private void RefreshDisplay()
    {
        if (!_isPresenting || _displayTexture == null) return;

        // Réafficher la page courante avec le nouveau zoom/pan
        if (_presentingToWhiteboard != null)
        {
            _presentingToWhiteboard.UpdatePresentationTexture(_displayTexture);
        }
    }

    #endregion

    #region Stop Presentation

    /// <summary>
    /// Arrête la présentation
    /// </summary>
    public void StopPresentation()
    {
        if (!_isPresenting) return;

        Debug.Log("[FilePresent] Stopping presentation");

        VRNetworkManager.Instance?.Send("file-present-stop", new FilePresentStopData
        {
            roomId = VRRoomManager.Instance?.CurrentRoomId ?? "",
            whiteboardId = _presentingToWhiteboardId,
            fileId = _presentingFileId,
            presenterId = VRNetworkManager.LocalId
        });

        _presentingToWhiteboard?.ExitPresentationMode();

        string wbId = _presentingToWhiteboardId;
        CleanupPresentation();

        OnPresentationStopped?.Invoke(wbId, VRNetworkManager.LocalId);
    }

    #endregion

    #region Image Presentation

    private void StartImagePresentation(string fileId)
    {
        byte[] content = FileShareManager.Instance?.GetFileContent(fileId);
        if (content == null)
        {
            Debug.LogError("[FilePresent] File content not found");
            OnPresentationError?.Invoke("start", "File content not found");
            StopPresentation();
            return;
        }

        _totalPages = 1;
        _currentPage = 0;

        // Charger et encoder l'image
        Texture2D tex = new Texture2D(2, 2);
        if (!tex.LoadImage(content))
        {
            Debug.LogError("[FilePresent] Failed to load image");
            Destroy(tex);
            OnPresentationError?.Invoke("start", "Failed to load image");
            StopPresentation();
            return;
        }

        // Redimensionner si nécessaire et encoder en JPEG
        byte[] jpegBytes = ResizeAndEncodeImage(tex, maxImageWidth, maxImageHeight, jpegQuality);
        Destroy(tex);

        _pageCache[0] = jpegBytes;

        // Envoyer message de démarrage
        SendPresentationStart();

        // Envoyer la page
        SendPage(0, jpegBytes);

        // Afficher localement
        DisplayPage(0);

        Debug.Log($"[FilePresent] Image presentation started ({jpegBytes.Length} bytes)");
    }

    #endregion

    #region PDF Presentation

    private void StartPdfConversion(string fileId)
    {
        byte[] content = FileShareManager.Instance?.GetFileContent(fileId);
        if (content == null)
        {
            Debug.LogError("[FilePresent] PDF content not found");
            OnPresentationError?.Invoke("start", "PDF content not found");
            StopPresentation();
            return;
        }

        Debug.Log("[FilePresent] Requesting PDF conversion from server...");

        // Demander au serveur de convertir le PDF
        VRNetworkManager.Instance.Send("pdf-convert-request", new PdfConvertRequestData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            fileId = fileId,
            fileDataBase64 = Convert.ToBase64String(content),
            requesterId = VRNetworkManager.LocalId
        });
    }

    private void HandlePdfConvertResponse(PdfConvertResponseData data)
    {
        if (data.targetId != VRNetworkManager.LocalId) return;
        if (data.fileId != _presentingFileId) return;

        if (!data.success)
        {
            Debug.LogError($"[FilePresent] PDF conversion failed: {data.error}");
            OnPresentationError?.Invoke("pdf-convert", data.error);
            StopPresentation();
            return;
        }

        _totalPages = data.totalPages;
        _currentPage = 0;

        Debug.Log($"[FilePresent] PDF converted: {_totalPages} pages");

        // Envoyer message de démarrage maintenant qu'on connaît le nombre de pages
        SendPresentationStart();

        // Demander la première page
        RequestPdfPage(0);
    }

    private void RequestPdfPage(int pageNumber)
    {
        Debug.Log($"[FilePresent] Requesting PDF page {pageNumber}");

        VRNetworkManager.Instance.Send("pdf-page-request", new PdfPageRequestData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            fileId = _presentingFileId,
            pageNumber = pageNumber,
            requesterId = VRNetworkManager.LocalId
        });
    }

    private void HandlePdfPageResponse(PdfPageResponseData data)
    {
        if (data.targetId != VRNetworkManager.LocalId) return;
        if (data.fileId != _presentingFileId) return;

        try
        {
            byte[] imageBytes = Convert.FromBase64String(data.imageDataBase64);
            _pageCache[data.pageNumber] = imageBytes;

            Debug.Log($"[FilePresent] Received PDF page {data.pageNumber} ({imageBytes.Length} bytes)");

            // Envoyer à la room
            SendPage(data.pageNumber, imageBytes);

            // Afficher si c'est la page courante
            if (data.pageNumber == _currentPage)
            {
                DisplayPage(data.pageNumber);
            }

            OnPageChanged?.Invoke(_presentingFileId, _currentPage, _totalPages);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FilePresent] Failed to process PDF page: {e.Message}");
        }
    }

    #endregion

    #region Network Sending

    private void SendPresentationStart()
    {
        var metadata = FileShareManager.Instance.GetFileMetadata(_presentingFileId);

        VRNetworkManager.Instance.Send("file-present-start", new FilePresentStartData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            whiteboardId = _presentingToWhiteboardId,
            fileId = _presentingFileId,
            fileName = metadata.fileName,
            fileExtension = metadata.fileExtension,
            presenterId = VRNetworkManager.LocalId,
            presenterName = PlayerPrefs.GetString("PlayerName", "Player"),
            totalPages = _totalPages,
            currentPage = _currentPage
        });

        OnPresentationStarted?.Invoke(
            _presentingToWhiteboardId,
            _presentingFileId,
            VRNetworkManager.LocalId,
            PlayerPrefs.GetString("PlayerName", "Player")
        );
    }

    private void SendPage(int pageNumber, byte[] jpegBytes)
    {
        VRNetworkManager.Instance.Send("file-present-page", new FilePresentPageData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            whiteboardId = _presentingToWhiteboardId,
            fileId = _presentingFileId,
            pageNumber = pageNumber,
            imageDataBase64 = Convert.ToBase64String(jpegBytes),
            width = maxImageWidth,
            height = maxImageHeight,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private void SendZoomPanUpdate()
    {
        if (!_isPresenting) return;
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        VRNetworkManager.Instance.Send("file-present-zoom-pan", new FilePresentZoomPanData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            whiteboardId = _presentingToWhiteboardId,
            fileId = _presentingFileId,
            presenterId = VRNetworkManager.LocalId,
            zoomLevel = _zoomLevel,
            panOffsetX = _panOffset.x,
            panOffsetY = _panOffset.y
        });
    }

    #endregion

    #region Display

    private void DisplayPage(int pageNumber)
    {
        Debug.Log($"[FilePresent] DisplayPage({pageNumber}) called, cache has {_pageCache.Count} pages");

        if (!_pageCache.TryGetValue(pageNumber, out byte[] jpegBytes))
        {
            Debug.Log($"[FilePresent] Page {pageNumber} not in cache, requesting...");
            // Demander la page si pas en cache (PDF)
            if (_totalPages > 1)
            {
                RequestPdfPage(pageNumber);
            }
            return;
        }

        Debug.Log($"[FilePresent] Displaying page {pageNumber} ({jpegBytes.Length} bytes)");

        if (_displayTexture == null)
        {
            _displayTexture = new Texture2D(2, 2);
        }

        bool loadSuccess = _displayTexture.LoadImage(jpegBytes);
        Debug.Log($"[FilePresent] Texture loaded: {loadSuccess}, size: {_displayTexture.width}x{_displayTexture.height}");

        if (_presentingToWhiteboard != null)
        {
            // Vérifier que l'objet existe toujours
            if (_presentingToWhiteboard.gameObject == null)
            {
                Debug.LogError("[FilePresent] Whiteboard GameObject was destroyed!");
                return;
            }
            if (!_presentingToWhiteboard.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[FilePresent] Whiteboard is inactive in hierarchy!");
            }

            Debug.Log($"[FilePresent] Whiteboard {_presentingToWhiteboard.id} isPresentationMode={_presentingToWhiteboard.IsPresentationMode}, active={_presentingToWhiteboard.gameObject.activeInHierarchy}");
            _presentingToWhiteboard.UpdatePresentationTexture(_displayTexture);
            Debug.Log($"[FilePresent] Texture sent to whiteboard {_presentingToWhiteboard.id}");
        }
        else
        {
            Debug.LogError("[FilePresent] Whiteboard reference is NULL!");
        }
    }

    #endregion

    #region Network Handlers

    private void HandleNetworkMessage(NetworkMessage msg)
    {
        // Debug: afficher les messages file-present et pdf
        if (msg.type.StartsWith("file-present") || msg.type.StartsWith("pdf-"))
        {
            Debug.Log($"[FilePresent] Received message: {msg.type} from {msg.senderId}");
        }

        // Ignorer ses propres messages (sauf les réponses du serveur)
        if (msg.senderId == VRNetworkManager.LocalId && msg.senderId != "server") return;

        switch (msg.type)
        {
            case "file-present-start":
                HandlePresentStart(msg);
                break;
            case "file-present-page":
                HandlePresentPage(msg);
                break;
            case "file-present-navigate":
                HandlePresentNavigate(msg);
                break;
            case "file-present-stop":
                HandlePresentStop(msg);
                break;
            case "file-present-zoom-pan":
                HandleZoomPan(msg);
                break;
            case "file-present-request":
                HandlePresentRequest(msg);
                break;
            case "file-present-state":
                HandlePresentState(msg);
                break;
            case "pdf-convert-response":
                Debug.Log($"[FilePresent] Processing pdf-convert-response");
                var convertData = JsonUtility.FromJson<PdfConvertResponseData>(msg.data);
                HandlePdfConvertResponse(convertData);
                break;
            case "pdf-page-response":
                Debug.Log($"[FilePresent] Processing pdf-page-response");
                var pageData = JsonUtility.FromJson<PdfPageResponseData>(msg.data);
                HandlePdfPageResponse(pageData);
                break;
        }
    }

    private void HandlePresentStart(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FilePresentStartData>(msg.data);

        // Vérifier room
        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        Debug.Log($"[FilePresent] Received presentation start from {data.presenterName}: {data.fileName}");

        // Trouver le whiteboard
        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard == null)
        {
            Debug.LogWarning($"[FilePresent] Whiteboard not found: {data.whiteboardId}");
            return;
        }

        // Créer état de réception
        var state = new WhiteboardPresentState
        {
            presenterId = data.presenterId,
            presenterName = data.presenterName,
            fileId = data.fileId,
            fileName = data.fileName,
            totalPages = data.totalPages,
            currentPage = data.currentPage,
            displayTexture = new Texture2D(2, 2)
        };
        _receivingStates[data.whiteboardId] = state;

        // Entrer en mode présentation
        targetWhiteboard.EnterPresentationMode(data.presenterName);

        OnPresentationStarted?.Invoke(data.whiteboardId, data.fileId, data.presenterId, data.presenterName);
    }

    private void HandlePresentPage(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FilePresentPageData>(msg.data);

        // Vérifier room
        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        if (!_receivingStates.TryGetValue(data.whiteboardId, out var state))
            return;

        try
        {
            byte[] imageBytes = Convert.FromBase64String(data.imageDataBase64);
            state.displayTexture.LoadImage(imageBytes);

            Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
            targetWhiteboard?.UpdatePresentationTexture(state.displayTexture);

            state.currentPage = data.pageNumber;
            OnPageChanged?.Invoke(data.fileId, state.currentPage, state.totalPages);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FilePresent] Failed to display page: {e.Message}");
        }
    }

    private void HandlePresentNavigate(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FilePresentNavigateData>(msg.data);

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        if (_receivingStates.TryGetValue(data.whiteboardId, out var state))
        {
            state.currentPage = data.newPageNumber;
            OnPageChanged?.Invoke(data.fileId, state.currentPage, state.totalPages);
        }
    }

    private void HandleZoomPan(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FilePresentZoomPanData>(msg.data);

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Vérifier qu'on reçoit bien cette présentation
        if (!_receivingStates.TryGetValue(data.whiteboardId, out var state))
            return;

        // Vérifier que c'est bien le présentateur qui envoie
        if (state.presenterId != data.presenterId)
            return;

        Debug.Log($"[FilePresent] Received zoom/pan update: zoom={data.zoomLevel}, pan=({data.panOffsetX}, {data.panOffsetY})");

        // Mettre à jour le whiteboard avec le nouveau zoom/pan
        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard != null)
        {
            targetWhiteboard.SetPresentationZoomPan(data.zoomLevel, new Vector2(data.panOffsetX, data.panOffsetY));
        }

        // Notifier l'UI
        OnZoomPanChanged?.Invoke(data.zoomLevel, new Vector2(data.panOffsetX, data.panOffsetY));
    }

    private void HandlePresentStop(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FilePresentStopData>(msg.data);

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        Debug.Log($"[FilePresent] Presentation stopped by {data.presenterId}");

        if (_receivingStates.TryGetValue(data.whiteboardId, out var state))
        {
            Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
            targetWhiteboard?.ExitPresentationMode();

            if (state.displayTexture != null)
            {
                Destroy(state.displayTexture);
            }
            _receivingStates.Remove(data.whiteboardId);

            OnPresentationStopped?.Invoke(data.whiteboardId, data.presenterId);
        }
    }

    private void HandlePresentRequest(NetworkMessage msg)
    {
        // Répondre si on est en train de présenter
        if (!_isPresenting) return;

        var data = JsonUtility.FromJson<FilePresentRequestData>(msg.data);

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Filtrer par whiteboard si spécifié
        if (!string.IsNullOrEmpty(data.whiteboardId) && data.whiteboardId != _presentingToWhiteboardId)
            return;

        Debug.Log($"[FilePresent] Sending state to late joiner {data.requesterId}");

        var metadata = FileShareManager.Instance.GetFileMetadata(_presentingFileId);

        // Récupérer l'image de la page courante
        string currentPageImage = "";
        if (_pageCache.TryGetValue(_currentPage, out byte[] pageBytes))
        {
            currentPageImage = Convert.ToBase64String(pageBytes);
        }

        VRNetworkManager.Instance.Send("file-present-state", new FilePresentStateData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            whiteboardId = _presentingToWhiteboardId,
            targetId = data.requesterId,
            isPresenting = true,
            fileId = _presentingFileId,
            fileName = metadata?.fileName ?? "",
            presenterId = VRNetworkManager.LocalId,
            presenterName = PlayerPrefs.GetString("PlayerName", "Player"),
            totalPages = _totalPages,
            currentPage = _currentPage,
            currentPageImageBase64 = currentPageImage
        });
    }

    private void HandlePresentState(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FilePresentStateData>(msg.data);

        // Vérifier si c'est pour nous
        if (!string.IsNullOrEmpty(data.targetId) && data.targetId != VRNetworkManager.LocalId)
            return;

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        if (!data.isPresenting) return;

        Debug.Log($"[FilePresent] Received state: {data.presenterName} presenting {data.fileName}");

        // Déjà en réception pour ce whiteboard?
        if (_receivingStates.ContainsKey(data.whiteboardId)) return;

        Whiteboard targetWhiteboard = FindWhiteboardById(data.whiteboardId);
        if (targetWhiteboard == null) return;

        // Créer état de réception
        var state = new WhiteboardPresentState
        {
            presenterId = data.presenterId,
            presenterName = data.presenterName,
            fileId = data.fileId,
            fileName = data.fileName,
            totalPages = data.totalPages,
            currentPage = data.currentPage,
            displayTexture = new Texture2D(2, 2)
        };
        _receivingStates[data.whiteboardId] = state;

        targetWhiteboard.EnterPresentationMode(data.presenterName);

        // Afficher la page courante si fournie
        if (!string.IsNullOrEmpty(data.currentPageImageBase64))
        {
            try
            {
                byte[] imageBytes = Convert.FromBase64String(data.currentPageImageBase64);
                state.displayTexture.LoadImage(imageBytes);
                targetWhiteboard.UpdatePresentationTexture(state.displayTexture);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FilePresent] Failed to display state image: {e.Message}");
            }
        }

        OnPresentationStarted?.Invoke(data.whiteboardId, data.fileId, data.presenterId, data.presenterName);
        OnPageChanged?.Invoke(data.fileId, data.currentPage, data.totalPages);
    }

    #endregion

    #region Room Events

    private void OnRoomJoined(string roomId)
    {
        if (_pendingRequestCoroutine != null)
            StopCoroutine(_pendingRequestCoroutine);
        _pendingRequestCoroutine = StartCoroutine(RequestPresentStateDelayed());
    }

    private IEnumerator RequestPresentStateDelayed()
    {
        yield return new WaitForSeconds(1.5f);

        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            Debug.Log("[FilePresent] Requesting presentation state for late join");

            VRNetworkManager.Instance.Send("file-present-request", new FilePresentRequestData
            {
                roomId = VRRoomManager.Instance.CurrentRoomId,
                whiteboardId = "",
                requesterId = VRNetworkManager.LocalId
            });
        }
        _pendingRequestCoroutine = null;
    }

    private void OnRoomLeft()
    {
        if (_isPresenting)
        {
            StopPresentation();
        }
        CleanupAllReceivingStates();
    }

    private void OnPlayerLeft(string playerId)
    {
        // Arrêter la présentation si le présentateur a quitté
        var statesToRemove = new List<string>();
        foreach (var kvp in _receivingStates)
        {
            if (kvp.Value.presenterId == playerId)
            {
                statesToRemove.Add(kvp.Key);
            }
        }

        foreach (var wbId in statesToRemove)
        {
            Debug.Log($"[FilePresent] Presenter {playerId} left, stopping presentation on {wbId}");

            Whiteboard wb = FindWhiteboardById(wbId);
            wb?.ExitPresentationMode();

            if (_receivingStates[wbId].displayTexture != null)
            {
                Destroy(_receivingStates[wbId].displayTexture);
            }
            _receivingStates.Remove(wbId);

            OnPresentationStopped?.Invoke(wbId, playerId);
        }
    }

    #endregion

    #region Helpers

    private Whiteboard FindWhiteboardById(string whiteboardId)
    {
        Whiteboard[] whiteboards = FindObjectsByType<Whiteboard>(FindObjectsSortMode.None);
        foreach (var wb in whiteboards)
        {
            if (wb.id == whiteboardId)
                return wb;
        }
        return null;
    }

    private byte[] ResizeAndEncodeImage(Texture2D source, int maxW, int maxH, int quality)
    {
        int newWidth = source.width;
        int newHeight = source.height;

        // Calculer les nouvelles dimensions en gardant le ratio
        if (newWidth > maxW || newHeight > maxH)
        {
            float ratioW = (float)maxW / newWidth;
            float ratioH = (float)maxH / newHeight;
            float ratio = Mathf.Min(ratioW, ratioH);

            newWidth = Mathf.RoundToInt(newWidth * ratio);
            newHeight = Mathf.RoundToInt(newHeight * ratio);
        }

        // Redimensionner si nécessaire
        if (newWidth != source.width || newHeight != source.height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
            Graphics.Blit(source, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            byte[] result = resized.EncodeToJPG(quality);
            Destroy(resized);
            return result;
        }

        return source.EncodeToJPG(quality);
    }

    private void CleanupPresentation()
    {
        _isPresenting = false;
        _presentingFileId = null;
        _presentingToWhiteboardId = null;
        _presentingToWhiteboard = null;
        _totalPages = 1;
        _currentPage = 0;
        _pageCache.Clear();

        // Reset zoom and pan
        _zoomLevel = 1f;
        _panOffset = Vector2.zero;

        if (_displayTexture != null)
        {
            Destroy(_displayTexture);
            _displayTexture = null;
        }
    }

    private void CleanupAllReceivingStates()
    {
        foreach (var kvp in _receivingStates)
        {
            Whiteboard wb = FindWhiteboardById(kvp.Key);
            wb?.ExitPresentationMode();

            if (kvp.Value.displayTexture != null)
            {
                Destroy(kvp.Value.displayTexture);
            }
        }
        _receivingStates.Clear();
    }

    #endregion
}
