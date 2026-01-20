using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI component for file sharing. Standalone prefab that can be placed anywhere.
/// Follows patterns from VRMenuUi.cs and WhiteboardUIManager.cs.
/// </summary>
public class FileSharingUI : MonoBehaviour
{
    [Header("Main Button")]
    [Tooltip("Button to toggle the file panel")]
    public Button fileButton;

    [Header("Main Panel")]
    public GameObject mainPanel;
    public Button closeButton;
    public Button shareButton;
    public Transform fileListContainer;
    public GameObject fileListItemPrefab;
    public TextMeshProUGUI emptyListText;
    public TextMeshProUGUI statusText;

    [Header("Download Path")]
    public TMP_InputField downloadPathInput;
    public Button browsePathButton;
    public Button openFolderButton;

    [Header("Preview Panel")]
    public GameObject previewPanel;
    public TextMeshProUGUI previewFileName;
    public TextMeshProUGUI previewFileSize;
    public TextMeshProUGUI previewFileType;
    public Image previewImage;
    public GameObject previewImageContainer;
    public Button previewShareButton;
    public Button previewCancelButton;

    [Header("VR File Browser")]
    [Tooltip("VR File Browser component for in-VR file selection")]
    public VRFileBrowser vrFileBrowser;

    [Header("Whiteboard")]
    [Tooltip("Le whiteboard sur lequel présenter les fichiers. Si non assigné, cherche le premier disponible.")]
    public Whiteboard targetWhiteboard;

    [Header("Presentation Controls")]
    [Tooltip("Button to stop ongoing presentation")]
    public Button stopPresentationButton;
    [Tooltip("Button for previous page")]
    public Button prevPageButton;
    [Tooltip("Button for next page")]
    public Button nextPageButton;
    [Tooltip("Text showing page number")]
    public TextMeshProUGUI pageNumberText;
    [Tooltip("Text showing current presentation status")]
    public TextMeshProUGUI presentationStatusText;

    [Header("Zoom Controls")]
    [Tooltip("Button for zoom in")]
    public Button zoomInButton;
    [Tooltip("Button for zoom out")]
    public Button zoomOutButton;
    [Tooltip("Button for reset zoom")]
    public Button resetZoomButton;
    [Tooltip("Text showing zoom level")]
    public TextMeshProUGUI zoomLevelText;

    [Header("Pan Controls")]
    [Tooltip("Button for pan left")]
    public Button panLeftButton;
    [Tooltip("Button for pan right")]
    public Button panRightButton;
    [Tooltip("Button for pan up")]
    public Button panUpButton;
    [Tooltip("Button for pan down")]
    public Button panDownButton;

    [Header("Presentation Controls - Option 1: Prefab existant dans la scène")]
    [Tooltip("Panneau de contrôles déjà créé dans la scène. Si assigné, sera utilisé au lieu de créer dynamiquement. Peut être caché (inactive) - sera activé pendant la présentation.")]
    public GameObject existingPresentationControlsPanel;

    [Header("Presentation Controls - Option 2: Création dynamique")]
    [Tooltip("Transform parent où placer les contrôles de présentation. Si vide, utilise mainPanel. (Ignoré si existingPresentationControlsPanel est assigné)")]
    public Transform presentationControlsParent;
    [Tooltip("Position locale des contrôles dans le parent (Ignoré si existingPresentationControlsPanel est assigné)")]
    public Vector3 presentationControlsPosition = Vector3.zero;
    [Tooltip("Taille du panneau de contrôles (Ignoré si existingPresentationControlsPanel est assigné)")]
    public Vector2 presentationControlsSize = new Vector2(500, 50);

    // Panneau créé dynamiquement (uniquement si existingPresentationControlsPanel n'est pas assigné)
    private GameObject _presentationControlsPanel;
    private bool _usingExistingPanel = false;

    [Header("Settings")]
    [Tooltip("Maximum length for displayed file names")]
    public int maxFileNameLength = 20;

    // State
    private string _pendingFilePath;
    private Dictionary<string, GameObject> _fileListItems = new Dictionary<string, GameObject>();
    private bool _isOpen = false;
    private Texture2D _previewTexture;
    private bool _isPresentationActive = false;

    #region Lifecycle

    void OnEnable()
    {
        // Subscribe to events in OnEnable (before Start) to catch events from DontDestroyOnLoad managers
        FileShareManager.OnFileShared += OnFileShared;
        FileShareManager.OnFileRemoved += OnFileRemoved;
        FileShareManager.OnFileListUpdated += OnFileListUpdated;
        FileShareManager.OnFileDownloadStarted += OnDownloadStarted;
        FileShareManager.OnFileDownloadComplete += OnDownloadComplete;
        FileShareManager.OnFileShareError += OnError;

        // Subscribe to presentation events
        FilePresentationManager.OnPresentationStarted += OnPresentationStarted;
        FilePresentationManager.OnPresentationStopped += OnPresentationStopped;
        FilePresentationManager.OnPageChanged += OnPageChanged;
        FilePresentationManager.OnZoomPanChanged += OnZoomPanChanged;

        // Subscribe to screen share events (also blocks presenting)
        ScreenShareManager.OnScreenShareStarted += OnScreenShareStarted;
        ScreenShareManager.OnScreenShareStopped += OnScreenShareStopped;
    }

    void Start()
    {
        // Button listeners
        if (fileButton != null)
            fileButton.onClick.AddListener(TogglePanel);

        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);

        if (shareButton != null)
            shareButton.onClick.AddListener(OpenFileBrowser);

        if (previewShareButton != null)
            previewShareButton.onClick.AddListener(ConfirmShare);

        if (previewCancelButton != null)
            previewCancelButton.onClick.AddListener(CancelPreview);

        // Download path buttons
        if (browsePathButton != null)
            browsePathButton.onClick.AddListener(BrowseDownloadPath);

        if (openFolderButton != null)
            openFolderButton.onClick.AddListener(OpenDownloadFolder);

        if (downloadPathInput != null)
            downloadPathInput.onEndEdit.AddListener(OnDownloadPathChanged);

        // Presentation control buttons
        if (stopPresentationButton != null)
        {
            stopPresentationButton.onClick.AddListener(OnStopPresentationClicked);
            stopPresentationButton.gameObject.SetActive(false);
        }
        if (prevPageButton != null)
        {
            prevPageButton.onClick.AddListener(OnPrevPageClicked);
            prevPageButton.gameObject.SetActive(false);
        }
        if (nextPageButton != null)
        {
            nextPageButton.onClick.AddListener(OnNextPageClicked);
            nextPageButton.gameObject.SetActive(false);
        }
        if (pageNumberText != null)
            pageNumberText.gameObject.SetActive(false);
        if (presentationStatusText != null)
            presentationStatusText.gameObject.SetActive(false);

        // VR File Browser events
        if (vrFileBrowser != null)
        {
            vrFileBrowser.OnFileSelected += OnVRFileSelected;
            vrFileBrowser.OnFolderSelected += OnVRFolderSelected;
            vrFileBrowser.OnBrowserClosed += OnVRBrowserClosed;
        }

        // Initial state
        if (mainPanel != null) mainPanel.SetActive(false);
        if (previewPanel != null) previewPanel.SetActive(false);

        // Cacher le panneau de contrôles existant au démarrage (sera activé pendant la présentation)
        if (existingPresentationControlsPanel != null)
            existingPresentationControlsPanel.SetActive(false);

        // Initialize download path display
        UpdateDownloadPathDisplay();

        // Sync with current presentation state (in case presentation started before this UI loaded)
        SyncWithCurrentPresentationState();
    }

    void OnDisable()
    {
        // Unsubscribe from events in OnDisable
        FileShareManager.OnFileShared -= OnFileShared;
        FileShareManager.OnFileRemoved -= OnFileRemoved;
        FileShareManager.OnFileListUpdated -= OnFileListUpdated;
        FileShareManager.OnFileDownloadStarted -= OnDownloadStarted;
        FileShareManager.OnFileDownloadComplete -= OnDownloadComplete;
        FileShareManager.OnFileShareError -= OnError;

        // Unsubscribe presentation events
        FilePresentationManager.OnPresentationStarted -= OnPresentationStarted;
        FilePresentationManager.OnPresentationStopped -= OnPresentationStopped;
        FilePresentationManager.OnPageChanged -= OnPageChanged;
        FilePresentationManager.OnZoomPanChanged -= OnZoomPanChanged;

        // Unsubscribe screen share events
        ScreenShareManager.OnScreenShareStarted -= OnScreenShareStarted;
        ScreenShareManager.OnScreenShareStopped -= OnScreenShareStopped;
    }

    void OnDestroy()
    {
        // Cleanup button listeners
        if (fileButton != null) fileButton.onClick.RemoveAllListeners();
        if (closeButton != null) closeButton.onClick.RemoveAllListeners();
        if (shareButton != null) shareButton.onClick.RemoveAllListeners();
        if (previewShareButton != null) previewShareButton.onClick.RemoveAllListeners();
        if (previewCancelButton != null) previewCancelButton.onClick.RemoveAllListeners();
        if (browsePathButton != null) browsePathButton.onClick.RemoveAllListeners();
        if (openFolderButton != null) openFolderButton.onClick.RemoveAllListeners();
        if (downloadPathInput != null) downloadPathInput.onEndEdit.RemoveAllListeners();

        // Unsubscribe VR browser
        if (vrFileBrowser != null)
        {
            vrFileBrowser.OnFileSelected -= OnVRFileSelected;
            vrFileBrowser.OnFolderSelected -= OnVRFolderSelected;
            vrFileBrowser.OnBrowserClosed -= OnVRBrowserClosed;
        }

        // Cleanup presentation button listeners
        if (stopPresentationButton != null)
            stopPresentationButton.onClick.RemoveAllListeners();
        if (prevPageButton != null)
            prevPageButton.onClick.RemoveAllListeners();
        if (nextPageButton != null)
            nextPageButton.onClick.RemoveAllListeners();
        if (zoomInButton != null)
            zoomInButton.onClick.RemoveAllListeners();
        if (zoomOutButton != null)
            zoomOutButton.onClick.RemoveAllListeners();
        if (resetZoomButton != null)
            resetZoomButton.onClick.RemoveAllListeners();
        if (panLeftButton != null)
            panLeftButton.onClick.RemoveAllListeners();
        if (panRightButton != null)
            panRightButton.onClick.RemoveAllListeners();
        if (panUpButton != null)
            panUpButton.onClick.RemoveAllListeners();
        if (panDownButton != null)
            panDownButton.onClick.RemoveAllListeners();

        // Cleanup dynamic controls panel (ne pas détruire si c'est le panneau existant de la scène)
        if (_presentationControlsPanel != null && !_usingExistingPanel)
            Destroy(_presentationControlsPanel);

        // Destroy list items
        foreach (var item in _fileListItems.Values)
        {
            if (item != null) Destroy(item);
        }
        _fileListItems.Clear();

        // Cleanup preview texture
        if (_previewTexture != null)
        {
            Destroy(_previewTexture);
            _previewTexture = null;
        }
    }

    /// <summary>
    /// Synchronise l'état de l'UI avec l'état actuel de présentation.
    /// Appelé au démarrage pour gérer le cas où la présentation a commencé avant que cette UI soit chargée.
    /// </summary>
    void SyncWithCurrentPresentationState()
    {
        // Vérifier si une présentation est en cours via FilePresentationManager
        if (FilePresentationManager.Instance != null)
        {
            // Vérifier si le manager local est en train de présenter
            if (FilePresentationManager.Instance.IsPresenting)
            {
                _isPresentationActive = true;
                ShowPresentationControls(true);

                if (presentationStatusText != null)
                {
                    presentationStatusText.gameObject.SetActive(true);
                    presentationStatusText.text = "You are presenting";
                }
            }
            else
            {
                // Vérifier si on reçoit une présentation (quelqu'un d'autre présente)
                string wbId = targetWhiteboard != null ? targetWhiteboard.id : null;
                if (!string.IsNullOrEmpty(wbId) && FilePresentationManager.Instance.IsWhiteboardReceiving(wbId))
                {
                    _isPresentationActive = true;
                    string presenterName = FilePresentationManager.Instance.GetPresenterName(wbId);

                    if (presentationStatusText != null)
                    {
                        presentationStatusText.gameObject.SetActive(true);
                        presentationStatusText.text = $"{presenterName ?? "Someone"} is presenting";
                    }
                }
            }
        }

        // Vérifier aussi ScreenShareManager pour le screen sharing
        if (ScreenShareManager.Instance != null)
        {
            bool isScreenSharing = ScreenShareManager.Instance.IsSharing;
            bool isReceivingScreenShare = false;
            if (targetWhiteboard != null)
            {
                isReceivingScreenShare = ScreenShareManager.Instance.IsWhiteboardReceiving(targetWhiteboard.id);
            }

            if (isScreenSharing || isReceivingScreenShare)
            {
                _isPresentationActive = true;
            }
        }

        Debug.Log($"[FileSharingUI] SyncWithCurrentPresentationState: _isPresentationActive = {_isPresentationActive}");
    }

    #endregion

    #region Panel Navigation

    void TogglePanel()
    {
        if (_isOpen)
            ClosePanel();
        else
            OpenPanel();
    }

    public void OpenPanel()
    {
        _isOpen = true;
        if (mainPanel != null) mainPanel.SetActive(true);
        if (previewPanel != null) previewPanel.SetActive(false);

        RefreshFileList();

        bool inRoom = VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom;
        SetStatus(inRoom ? "Files shared in this room" : "Join a room to share files");

        if (shareButton != null)
            shareButton.interactable = inRoom;

        // Hide "Open" folder button in VR mode (can't see Windows Explorer in VR)
        if (openFolderButton != null)
            openFolderButton.gameObject.SetActive(!IsInVRMode());
    }

    public void ClosePanel()
    {
        _isOpen = false;
        if (mainPanel != null) mainPanel.SetActive(false);
        if (previewPanel != null) previewPanel.SetActive(false);
        _pendingFilePath = null;

        CleanupPreviewTexture();
    }

    void ShowPreviewPanel(string filePath)
    {
        _pendingFilePath = filePath;

        var fileInfo = new FileInfo(filePath);

        if (previewFileName != null)
            previewFileName.text = fileInfo.Name;

        if (previewFileSize != null)
            previewFileSize.text = FileShareManager.FormatFileSize(fileInfo.Length);

        if (previewFileType != null)
        {
            string ext = fileInfo.Extension.TrimStart('.').ToUpper();
            previewFileType.text = ext;
        }

        // Show image preview for image files
        bool isImage = IsImageFile(fileInfo.Extension);
        if (previewImageContainer != null)
            previewImageContainer.SetActive(isImage);

        if (isImage && previewImage != null)
        {
            LoadImagePreview(filePath);
        }

        if (mainPanel != null) mainPanel.SetActive(false);
        if (previewPanel != null) previewPanel.SetActive(true);
    }

    void CancelPreview()
    {
        _pendingFilePath = null;
        CleanupPreviewTexture();

        if (previewPanel != null) previewPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);
    }

    #endregion

    #region File Browser

    void OpenFileBrowser()
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            SetStatus("Join a room first");
            return;
        }

        // Check if we're in VR mode
        if (IsInVRMode() && vrFileBrowser != null)
        {
            // Use VR file browser
            OpenVRFileBrowser();
            return;
        }

#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel(
            "Select File to Share",
            "",
            "pdf,doc,docx,xls,xlsx,png,jpg,jpeg,gif");

        if (!string.IsNullOrEmpty(path))
            OnFileSelected(path);
#else
        // Pour le runtime standalone, utiliser SFB (SimpleFileBrowser) ou similaire
        // Alternative: ouvrir le dossier Downloads
        OpenFileBrowserRuntime();
#endif
    }

    bool IsInVRMode()
    {
        // Check if XR is active
        var xrDisplaySubsystems = new List<UnityEngine.XR.XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(xrDisplaySubsystems);

        foreach (var xrDisplay in xrDisplaySubsystems)
        {
            if (xrDisplay.running)
                return true;
        }

        return false;
    }

    void OpenVRFileBrowser()
    {
        if (vrFileBrowser == null)
            return;

        // Hide main panel while browsing
        if (mainPanel != null)
            mainPanel.SetActive(false);

        // Open VR browser at default or last used path
        string startPath = FileShareManager.Instance?.DownloadPath;
        vrFileBrowser.Open(startPath);

        SetStatus("Select a file...");
    }

    void OnVRFileSelected(string filePath)
    {
        Debug.Log($"[FileSharingUI] VR file selected: {filePath}");

        // Show main panel again
        if (mainPanel != null)
            mainPanel.SetActive(true);

        // Process the selected file
        OnFileSelected(filePath);
    }

    void OnVRBrowserClosed()
    {
        // Show main panel again
        if (mainPanel != null && _isOpen)
            mainPanel.SetActive(true);

        SetStatus("");
    }

    void OpenFileBrowserRuntime()
    {
#if UNITY_STANDALONE_WIN
        try
        {
            string path = WindowsFileBrowser.OpenFileDialog(
                "Select File to Share",
                "",
                "Supported Files\0*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.png;*.jpg;*.jpeg;*.gif\0All Files\0*.*\0\0"
            );

            if (!string.IsNullOrEmpty(path))
            {
                OnFileSelected(path);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FileSharingUI] File browser error: {e.Message}");
            SetStatus("Could not open file browser");
        }
#else
        SetStatus("File browser not available on this platform");
        Debug.Log("[FileSharingUI] Use drag-and-drop or Editor mode for file selection");
#endif
    }

    /// <summary>
    /// Called when a file is selected (from browser or drag-drop).
    /// Can be called externally for drag-and-drop support.
    /// </summary>
    public void OnFileSelected(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        Debug.Log($"[FileSharingUI] File selected: {filePath}");

        // Validate file
        if (FileShareManager.Instance == null)
        {
            SetStatus("FileShareManager not initialized");
            return;
        }

        var result = FileShareManager.Instance.ValidateFile(filePath, out var metadata);

        if (result != FileShareManager.FileValidationResult.Valid)
        {
            SetStatus(FileShareManager.GetValidationErrorMessage(result));
            return;
        }

        ShowPreviewPanel(filePath);
    }

    void ConfirmShare()
    {
        if (string.IsNullOrEmpty(_pendingFilePath))
            return;

        if (FileShareManager.Instance == null)
        {
            SetStatus("FileShareManager not initialized");
            return;
        }

        FileShareManager.Instance.ShareFile(_pendingFilePath);

        _pendingFilePath = null;
        CleanupPreviewTexture();

        if (previewPanel != null) previewPanel.SetActive(false);
        if (mainPanel != null) mainPanel.SetActive(true);

        SetStatus("File shared!");
    }

    #endregion

    #region Download Path

    void UpdateDownloadPathDisplay()
    {
        if (downloadPathInput != null && FileShareManager.Instance != null)
        {
            downloadPathInput.text = FileShareManager.Instance.DownloadPath;
        }
    }

    void OnDownloadPathChanged(string newPath)
    {
        if (string.IsNullOrWhiteSpace(newPath))
        {
            // Reset to default
            newPath = FileShareManager.GetDefaultDownloadPath();
        }

        if (FileShareManager.Instance != null)
        {
            // Validate path
            try
            {
                if (!Directory.Exists(newPath))
                {
                    Directory.CreateDirectory(newPath);
                }
                FileShareManager.Instance.DownloadPath = newPath;
                SetStatus("Download path updated");
            }
            catch (Exception e)
            {
                SetStatus($"Invalid path: {e.Message}");
                UpdateDownloadPathDisplay(); // Revert display
            }
        }
    }

    void BrowseDownloadPath()
    {
        // Check if we're in VR mode
        if (IsInVRMode() && vrFileBrowser != null)
        {
            OpenVRFolderBrowser();
            return;
        }

#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFolderPanel(
            "Select Download Folder",
            FileShareManager.Instance?.DownloadPath ?? "",
            "");

        if (!string.IsNullOrEmpty(path))
        {
            if (FileShareManager.Instance != null)
                FileShareManager.Instance.DownloadPath = path;
            UpdateDownloadPathDisplay();
            SetStatus("Download path updated");
        }
#elif UNITY_STANDALONE_WIN
        string path = WindowsFileBrowser.OpenFolderDialog(
            "Select Download Folder",
            FileShareManager.Instance?.DownloadPath ?? "");

        if (!string.IsNullOrEmpty(path))
        {
            if (FileShareManager.Instance != null)
                FileShareManager.Instance.DownloadPath = path;
            UpdateDownloadPathDisplay();
            SetStatus("Download path updated");
        }
#else
        SetStatus("Folder browser not available");
#endif
    }

    void OpenVRFolderBrowser()
    {
        if (vrFileBrowser == null)
            return;

        // Hide main panel while browsing
        if (mainPanel != null)
            mainPanel.SetActive(false);

        // Open VR browser in folder selection mode
        string startPath = FileShareManager.Instance?.DownloadPath;
        vrFileBrowser.OpenFolderBrowser(startPath);

        SetStatus("Select download folder...");
    }

    void OnVRFolderSelected(string folderPath)
    {
        Debug.Log($"[FileSharingUI] VR folder selected: {folderPath}");

        // Update download path
        if (FileShareManager.Instance != null)
        {
            FileShareManager.Instance.DownloadPath = folderPath;
        }

        UpdateDownloadPathDisplay();
        SetStatus("Download path updated");

        // Show main panel again
        if (mainPanel != null && _isOpen)
            mainPanel.SetActive(true);
    }

    void OpenDownloadFolder()
    {
        FileShareManager.Instance?.OpenDownloadFolder();
    }

    #endregion

    #region File List Management

    void RefreshFileList()
    {
        // Clear existing items
        foreach (var item in _fileListItems.Values)
        {
            if (item != null) Destroy(item);
        }
        _fileListItems.Clear();

        var files = FileShareManager.Instance?.GetSharedFilesList() ?? new List<FileMetadata>();

        if (emptyListText != null)
            emptyListText.gameObject.SetActive(files.Count == 0);

        foreach (var file in files)
        {
            CreateFileListItem(file);
        }
    }

    void CreateFileListItem(FileMetadata file)
    {
        if (fileListContainer == null || fileListItemPrefab == null)
            return;

        GameObject item = Instantiate(fileListItemPrefab, fileListContainer);
        item.SetActive(true);
        _fileListItems[file.fileId] = item;

        // Find and configure UI elements
        var texts = item.GetComponentsInChildren<TextMeshProUGUI>(true);

        if (texts.Length > 0)
        {
            // File name (abbreviated)
            texts[0].text = FileShareManager.AbbreviateFileName(file.fileName, maxFileNameLength);
        }

        if (texts.Length > 1)
        {
            // Shared by
            texts[1].text = $"by {file.sharerName}";
        }

        if (texts.Length > 2)
        {
            // File size
            texts[2].text = FileShareManager.FormatFileSize(file.fileSize);
        }

        // Add click handler for download on the main button
        var mainButton = item.GetComponent<Button>();
        if (mainButton == null)
            mainButton = item.AddComponent<Button>();

        string fileId = file.fileId;
        mainButton.onClick.RemoveAllListeners();
        mainButton.onClick.AddListener(() => OnFileItemClicked(fileId));

        // Find or create delete button (only for files shared by local player)
        bool canDelete = FileShareManager.Instance != null && FileShareManager.Instance.CanRemoveFile(file.fileId);

        Transform deleteButtonTransform = item.transform.Find("DeleteButton");
        if (deleteButtonTransform != null)
        {
            // Show/hide existing delete button
            deleteButtonTransform.gameObject.SetActive(canDelete);

            if (canDelete)
            {
                Button deleteBtn = deleteButtonTransform.GetComponent<Button>();
                if (deleteBtn != null)
                {
                    deleteBtn.onClick.RemoveAllListeners();
                    deleteBtn.onClick.AddListener(() => OnDeleteFileClicked(fileId));
                }
            }
        }
        else if (canDelete)
        {
            // Create delete button dynamically if it doesn't exist
            CreateDeleteButton(item, fileId);
        }

        // Add present button for presentable files (images and PDFs)
        // Hide during active presentation
        bool canPresent = FilePresentationManager.Instance != null &&
                          FilePresentationManager.Instance.CanPresentFile(file.fileId) &&
                          !_isPresentationActive;
        bool inRoom = VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom;

        Transform presentButtonTransform = item.transform.Find("PresentButton");
        if (presentButtonTransform != null)
        {
            presentButtonTransform.gameObject.SetActive(canPresent && inRoom);

            if (canPresent && inRoom)
            {
                Button presentBtn = presentButtonTransform.GetComponent<Button>();
                if (presentBtn != null)
                {
                    presentBtn.onClick.RemoveAllListeners();
                    presentBtn.onClick.AddListener(() => OnPresentFileClicked(fileId));
                }
            }
        }
        else if (canPresent && inRoom)
        {
            // Create present button dynamically if it doesn't exist
            CreatePresentButton(item, fileId);
        }
    }

    void CreatePresentButton(GameObject parent, string fileId)
    {
        GameObject presentBtn = new GameObject("PresentButton");
        presentBtn.transform.SetParent(parent.transform, false);

        RectTransform rt = presentBtn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-40, 0);  // Left of delete button
        rt.sizeDelta = new Vector2(60, 25);

        Image img = presentBtn.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 0.9f, 1f);  // Blue color

        Button btn = presentBtn.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => OnPresentFileClicked(fileId));

        // Add text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(presentBtn.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "Present";
        text.fontSize = 12;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    void OnPresentFileClicked(string fileId)
    {
        if (FilePresentationManager.Instance == null)
            return;

        // Utiliser le whiteboard assigné, ou en trouver un si non assigné
        Whiteboard wb = targetWhiteboard;
        if (wb == null)
        {
            wb = FindAnyObjectByType<Whiteboard>();
            Debug.LogWarning("[FileSharingUI] No whiteboard assigned, using first found");
        }

        if (wb != null)
        {
            FilePresentationManager.Instance.StartPresentation(fileId, wb);
            Debug.Log($"[FileSharingUI] Starting presentation of {fileId} on whiteboard {wb.id}");
        }
        else
        {
            Debug.LogWarning("[FileSharingUI] No whiteboard found for presentation");
        }
    }

    void CreateDeleteButton(GameObject parent, string fileId)
    {
        GameObject deleteBtn = new GameObject("DeleteButton");
        deleteBtn.transform.SetParent(parent.transform, false);

        RectTransform rt = deleteBtn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 0.5f);
        rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.anchoredPosition = new Vector2(-5, 0);
        rt.sizeDelta = new Vector2(30, 30);

        Image img = deleteBtn.AddComponent<Image>();
        img.color = new Color(0.8f, 0.2f, 0.2f, 1f);

        Button btn = deleteBtn.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => OnDeleteFileClicked(fileId));

        // Add X text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(deleteBtn.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "X";
        text.fontSize = 16;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    void OnDeleteFileClicked(string fileId)
    {
        if (FileShareManager.Instance == null)
            return;

        FileShareManager.Instance.RemoveSharedFile(fileId);
    }

    void OnFileItemClicked(string fileId)
    {
        if (FileShareManager.Instance == null)
            return;

        FileShareManager.Instance.RequestFileDownload(fileId);
    }

    #endregion

    #region Event Handlers

    void OnFileShared(FileMetadata file)
    {
        if (!_isOpen) return;

        // Check if item already exists (we might have created it ourselves)
        if (!_fileListItems.ContainsKey(file.fileId))
        {
            CreateFileListItem(file);
        }

        if (emptyListText != null)
            emptyListText.gameObject.SetActive(false);
    }

    void OnFileRemoved(string fileId)
    {
        if (_fileListItems.TryGetValue(fileId, out var item))
        {
            if (item != null) Destroy(item);
            _fileListItems.Remove(fileId);
        }

        // Update empty text
        if (_isOpen && emptyListText != null)
        {
            emptyListText.gameObject.SetActive(_fileListItems.Count == 0);
        }
    }

    void OnFileListUpdated(List<FileMetadata> files)
    {
        if (_isOpen)
            RefreshFileList();
    }

    void OnDownloadStarted(string fileId, string fileName)
    {
        SetStatus($"Downloading {fileName}...");
    }

    void OnDownloadComplete(string fileId, string localPath)
    {
        SetStatus($"Downloaded!");

        // Open folder in explorer (Windows)
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{localPath}\"");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FileSharingUI] Could not open explorer: {e.Message}");
        }
#endif

        Debug.Log($"[FileSharingUI] File downloaded to: {localPath}");
    }

    void OnError(string context, string errorMessage)
    {
        SetStatus($"Error: {errorMessage}");
        Debug.LogWarning($"[FileSharingUI] Error ({context}): {errorMessage}");
    }

    #endregion

    #region Presentation Event Handlers

    void OnPresentationStarted(string whiteboardId, string fileId, string presenterId, string presenterName)
    {
        _isPresentationActive = true;

        // Fermer le VR file browser s'il est ouvert
        if (vrFileBrowser != null && vrFileBrowser.browserPanel != null && vrFileBrowser.browserPanel.activeSelf)
        {
            vrFileBrowser.Close();
        }

        // Show controls only if we are the presenter
        bool isLocalPresenter = (presenterId == VRNetworkManager.LocalId);

        if (isLocalPresenter)
        {
            ShowPresentationControls(true);
        }

        if (presentationStatusText != null)
        {
            presentationStatusText.gameObject.SetActive(true);
            if (isLocalPresenter)
                presentationStatusText.text = "You are presenting";
            else
                presentationStatusText.text = $"{presenterName} is presenting";
        }

        // Refresh list to hide present buttons during presentation
        if (_isOpen)
            RefreshFileList();
    }

    void ShowPresentationControls(bool show)
    {
        if (show)
        {
            // Option 1: Utiliser le panneau existant dans la scène
            if (existingPresentationControlsPanel != null)
            {
                _usingExistingPanel = true;
                existingPresentationControlsPanel.SetActive(true);

                // Réactiver les boutons qui ont été désactivés dans Start()
                if (stopPresentationButton != null)
                    stopPresentationButton.gameObject.SetActive(true);
                if (prevPageButton != null)
                    prevPageButton.gameObject.SetActive(true);
                if (nextPageButton != null)
                    nextPageButton.gameObject.SetActive(true);
                if (pageNumberText != null)
                    pageNumberText.gameObject.SetActive(true);
                if (presentationStatusText != null)
                    presentationStatusText.gameObject.SetActive(true);

                // Récupérer les références des boutons depuis le panneau existant
                BindExistingPanelButtons();

                UpdatePageDisplay();
                UpdateZoomDisplay();
            }
            else
            {
                // Option 2: Créer dynamiquement
                _usingExistingPanel = false;
                CreateDynamicPresentationControls();
            }
        }
        else
        {
            if (_usingExistingPanel && existingPresentationControlsPanel != null)
            {
                // Juste cacher le panneau existant
                existingPresentationControlsPanel.SetActive(false);
            }
            else if (_presentationControlsPanel != null)
            {
                // Détruire le panneau créé dynamiquement
                Destroy(_presentationControlsPanel);
                _presentationControlsPanel = null;
            }
        }
    }

    /// <summary>
    /// Lie les boutons du panneau existant aux fonctions de contrôle.
    /// Les boutons doivent déjà être assignés via l'Inspector.
    /// </summary>
    void BindExistingPanelButtons()
    {
        // Prev/Next page
        if (prevPageButton != null)
        {
            prevPageButton.onClick.RemoveAllListeners();
            prevPageButton.onClick.AddListener(OnPrevPageClicked);
        }
        if (nextPageButton != null)
        {
            nextPageButton.onClick.RemoveAllListeners();
            nextPageButton.onClick.AddListener(OnNextPageClicked);
        }

        // Zoom
        if (zoomInButton != null)
        {
            zoomInButton.onClick.RemoveAllListeners();
            zoomInButton.onClick.AddListener(OnZoomInClicked);
        }
        if (zoomOutButton != null)
        {
            zoomOutButton.onClick.RemoveAllListeners();
            zoomOutButton.onClick.AddListener(OnZoomOutClicked);
        }
        if (resetZoomButton != null)
        {
            resetZoomButton.onClick.RemoveAllListeners();
            resetZoomButton.onClick.AddListener(OnResetZoomClicked);
        }

        // Pan
        if (panLeftButton != null)
        {
            panLeftButton.onClick.RemoveAllListeners();
            panLeftButton.onClick.AddListener(OnPanLeftClicked);
        }
        if (panRightButton != null)
        {
            panRightButton.onClick.RemoveAllListeners();
            panRightButton.onClick.AddListener(OnPanRightClicked);
        }
        if (panUpButton != null)
        {
            panUpButton.onClick.RemoveAllListeners();
            panUpButton.onClick.AddListener(OnPanUpClicked);
        }
        if (panDownButton != null)
        {
            panDownButton.onClick.RemoveAllListeners();
            panDownButton.onClick.AddListener(OnPanDownClicked);
        }

        // Stop
        if (stopPresentationButton != null)
        {
            stopPresentationButton.onClick.RemoveAllListeners();
            stopPresentationButton.onClick.AddListener(OnStopPresentationClicked);
        }
    }

    void CreateDynamicPresentationControls()
    {
        // Destroy existing panel if any
        if (_presentationControlsPanel != null)
            Destroy(_presentationControlsPanel);

        // Determine parent - use custom parent if set, otherwise mainPanel
        Transform parent = presentationControlsParent != null ? presentationControlsParent : (mainPanel != null ? mainPanel.transform : null);
        if (parent == null) return;

        // Create container panel
        _presentationControlsPanel = new GameObject("PresentationControlsPanel");
        _presentationControlsPanel.transform.SetParent(parent, false);

        RectTransform panelRt = _presentationControlsPanel.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition3D = presentationControlsPosition;
        panelRt.sizeDelta = presentationControlsSize;

        Image panelBg = _presentationControlsPanel.AddComponent<Image>();
        panelBg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

        // Horizontal layout
        HorizontalLayoutGroup layout = _presentationControlsPanel.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Previous button
        prevPageButton = CreateControlButton(_presentationControlsPanel.transform, "PrevBtn", "<", 35, new Color(0.3f, 0.5f, 0.8f));
        prevPageButton.onClick.AddListener(OnPrevPageClicked);

        // Page number text
        GameObject pageNumObj = new GameObject("PageNumber");
        pageNumObj.transform.SetParent(_presentationControlsPanel.transform, false);
        RectTransform pageNumRt = pageNumObj.AddComponent<RectTransform>();
        pageNumRt.sizeDelta = new Vector2(60, 40);
        pageNumberText = pageNumObj.AddComponent<TextMeshProUGUI>();
        pageNumberText.text = "1 / 1";
        pageNumberText.fontSize = 14;
        pageNumberText.color = Color.white;
        pageNumberText.alignment = TextAlignmentOptions.Center;

        // Next button
        nextPageButton = CreateControlButton(_presentationControlsPanel.transform, "NextBtn", ">", 35, new Color(0.3f, 0.5f, 0.8f));
        nextPageButton.onClick.AddListener(OnNextPageClicked);

        // Separator
        CreateSeparator(_presentationControlsPanel.transform);

        // Zoom out button
        zoomOutButton = CreateControlButton(_presentationControlsPanel.transform, "ZoomOutBtn", "-", 30, new Color(0.4f, 0.4f, 0.6f));
        zoomOutButton.onClick.AddListener(OnZoomOutClicked);

        // Zoom level text
        GameObject zoomObj = new GameObject("ZoomLevel");
        zoomObj.transform.SetParent(_presentationControlsPanel.transform, false);
        RectTransform zoomRt = zoomObj.AddComponent<RectTransform>();
        zoomRt.sizeDelta = new Vector2(50, 40);
        zoomLevelText = zoomObj.AddComponent<TextMeshProUGUI>();
        zoomLevelText.text = "100%";
        zoomLevelText.fontSize = 12;
        zoomLevelText.color = Color.white;
        zoomLevelText.alignment = TextAlignmentOptions.Center;

        // Zoom in button
        zoomInButton = CreateControlButton(_presentationControlsPanel.transform, "ZoomInBtn", "+", 30, new Color(0.4f, 0.4f, 0.6f));
        zoomInButton.onClick.AddListener(OnZoomInClicked);

        // Reset zoom button
        resetZoomButton = CreateControlButton(_presentationControlsPanel.transform, "ResetZoomBtn", "1:1", 35, new Color(0.4f, 0.4f, 0.6f));
        resetZoomButton.onClick.AddListener(OnResetZoomClicked);

        // Separator
        CreateSeparator(_presentationControlsPanel.transform);

        // Pan controls
        panLeftButton = CreateControlButton(_presentationControlsPanel.transform, "PanLeftBtn", "\u25C0", 28, new Color(0.5f, 0.5f, 0.5f));
        panLeftButton.onClick.AddListener(OnPanLeftClicked);

        panUpButton = CreateControlButton(_presentationControlsPanel.transform, "PanUpBtn", "\u25B2", 28, new Color(0.5f, 0.5f, 0.5f));
        panUpButton.onClick.AddListener(OnPanUpClicked);

        panDownButton = CreateControlButton(_presentationControlsPanel.transform, "PanDownBtn", "\u25BC", 28, new Color(0.5f, 0.5f, 0.5f));
        panDownButton.onClick.AddListener(OnPanDownClicked);

        panRightButton = CreateControlButton(_presentationControlsPanel.transform, "PanRightBtn", "\u25B6", 28, new Color(0.5f, 0.5f, 0.5f));
        panRightButton.onClick.AddListener(OnPanRightClicked);

        // Separator
        CreateSeparator(_presentationControlsPanel.transform);

        // Stop button
        stopPresentationButton = CreateControlButton(_presentationControlsPanel.transform, "StopBtn", "Stop", 50, new Color(0.8f, 0.2f, 0.2f));
        stopPresentationButton.onClick.AddListener(OnStopPresentationClicked);

        UpdatePageDisplay();
        UpdateZoomDisplay();
    }

    void CreateSeparator(Transform parent)
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);
        RectTransform rt = sep.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(2, 25);
        Image img = sep.AddComponent<Image>();
        img.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    }

    Button CreateControlButton(Transform parent, string name, string label, float width, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 35);

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;

        // Add text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 14;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        return btn;
    }

    void UpdatePageDisplay()
    {
        if (pageNumberText == null) return;

        var mgr = FilePresentationManager.Instance;
        if (mgr != null && mgr.IsPresenting)
        {
            int current = mgr.CurrentPage + 1;
            int total = mgr.TotalPages;
            pageNumberText.text = $"{current} / {total}";

            // Enable/disable prev/next based on position
            if (prevPageButton != null)
                prevPageButton.interactable = mgr.CurrentPage > 0;
            if (nextPageButton != null)
                nextPageButton.interactable = mgr.CurrentPage < total - 1;
        }
    }

    void OnPresentationStopped(string whiteboardId, string presenterId)
    {
        // Hide the controls panel
        ShowPresentationControls(false);

        // Only reset if no screen share is active
        bool screenShareActive = false;
        if (ScreenShareManager.Instance != null)
        {
            screenShareActive = ScreenShareManager.Instance.IsSharing;
            if (!screenShareActive && targetWhiteboard != null)
            {
                screenShareActive = ScreenShareManager.Instance.IsWhiteboardReceiving(targetWhiteboard.id);
            }
        }

        if (!screenShareActive)
        {
            _isPresentationActive = false;

            if (presentationStatusText != null)
                presentationStatusText.gameObject.SetActive(false);
        }

        // Refresh list to show present buttons again
        if (_isOpen)
            RefreshFileList();

        Debug.Log($"[FileSharingUI] Presentation stopped, _isPresentationActive = {_isPresentationActive}");
    }

    void OnPageChanged(string fileId, int currentPage, int totalPages)
    {
        UpdatePageDisplay();
    }

    void OnPrevPageClicked()
    {
        FilePresentationManager.Instance?.PreviousPage();
    }

    void OnNextPageClicked()
    {
        FilePresentationManager.Instance?.NextPage();
    }

    void OnStopPresentationClicked()
    {
        FilePresentationManager.Instance?.StopPresentation();
    }

    void OnZoomInClicked()
    {
        FilePresentationManager.Instance?.ZoomIn();
    }

    void OnZoomOutClicked()
    {
        FilePresentationManager.Instance?.ZoomOut();
    }

    void OnResetZoomClicked()
    {
        FilePresentationManager.Instance?.ResetZoomPan();
    }

    void OnPanLeftClicked()
    {
        FilePresentationManager.Instance?.Pan(new Vector2(-0.1f, 0f));
    }

    void OnPanRightClicked()
    {
        FilePresentationManager.Instance?.Pan(new Vector2(0.1f, 0f));
    }

    void OnPanUpClicked()
    {
        FilePresentationManager.Instance?.Pan(new Vector2(0f, 0.1f));
    }

    void OnPanDownClicked()
    {
        FilePresentationManager.Instance?.Pan(new Vector2(0f, -0.1f));
    }

    void OnZoomPanChanged(float zoomLevel, Vector2 panOffset)
    {
        UpdateZoomDisplay();
    }

    void UpdateZoomDisplay()
    {
        if (zoomLevelText == null) return;

        var mgr = FilePresentationManager.Instance;
        if (mgr != null && mgr.IsPresenting)
        {
            int zoomPercent = Mathf.RoundToInt(mgr.ZoomLevel * 100);
            zoomLevelText.text = $"{zoomPercent}%";

            // Enable/disable zoom buttons based on limits
            if (zoomOutButton != null)
                zoomOutButton.interactable = mgr.ZoomLevel > 0.5f;
            if (zoomInButton != null)
                zoomInButton.interactable = mgr.ZoomLevel < 4f;
            if (resetZoomButton != null)
                resetZoomButton.interactable = !Mathf.Approximately(mgr.ZoomLevel, 1f) || mgr.PanOffset != Vector2.zero;

            // Pan buttons only work when zoomed in
            bool canPan = mgr.ZoomLevel > 1f;
            if (panLeftButton != null)
                panLeftButton.interactable = canPan;
            if (panRightButton != null)
                panRightButton.interactable = canPan;
            if (panUpButton != null)
                panUpButton.interactable = canPan;
            if (panDownButton != null)
                panDownButton.interactable = canPan;
        }
    }

    #endregion

    #region Screen Share Event Handlers

    void OnScreenShareStarted(string whiteboardId, string sharerId, string sharerName)
    {
        _isPresentationActive = true;

        // Fermer le VR file browser s'il est ouvert
        if (vrFileBrowser != null && vrFileBrowser.browserPanel != null && vrFileBrowser.browserPanel.activeSelf)
        {
            vrFileBrowser.Close();
        }

        if (presentationStatusText != null)
        {
            presentationStatusText.gameObject.SetActive(true);
            bool isLocalSharer = (sharerId == VRNetworkManager.LocalId);
            if (isLocalSharer)
                presentationStatusText.text = "You are sharing screen";
            else
                presentationStatusText.text = $"{sharerName} is sharing screen";
        }

        // Refresh list to hide present buttons during screen share
        if (_isOpen)
            RefreshFileList();

        Debug.Log($"[FileSharingUI] Screen share started by {sharerName}, _isPresentationActive = true");
    }

    void OnScreenShareStopped(string whiteboardId, string sharerId)
    {
        // Only reset if no file presentation is active
        bool filePresenting = FilePresentationManager.Instance != null && FilePresentationManager.Instance.IsPresenting;
        bool fileReceiving = false;
        if (FilePresentationManager.Instance != null && targetWhiteboard != null)
        {
            fileReceiving = FilePresentationManager.Instance.IsWhiteboardReceiving(targetWhiteboard.id);
        }

        if (!filePresenting && !fileReceiving)
        {
            _isPresentationActive = false;

            if (presentationStatusText != null)
                presentationStatusText.gameObject.SetActive(false);
        }

        // Refresh list to show present buttons again
        if (_isOpen)
            RefreshFileList();

        Debug.Log($"[FileSharingUI] Screen share stopped, _isPresentationActive = {_isPresentationActive}");
    }

    #endregion

    #region Helpers

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    bool IsImageFile(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;

        string ext = extension.TrimStart('.').ToLower();
        return ext == "png" || ext == "jpg" || ext == "jpeg" || ext == "gif";
    }

    void LoadImagePreview(string filePath)
    {
        CleanupPreviewTexture();

        try
        {
            byte[] imageData = File.ReadAllBytes(filePath);
            _previewTexture = new Texture2D(2, 2);
            _previewTexture.LoadImage(imageData);

            if (previewImage != null)
            {
                previewImage.sprite = Sprite.Create(
                    _previewTexture,
                    new Rect(0, 0, _previewTexture.width, _previewTexture.height),
                    new Vector2(0.5f, 0.5f)
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FileSharingUI] Failed to load image preview: {e.Message}");
        }
    }

    void CleanupPreviewTexture()
    {
        if (_previewTexture != null)
        {
            // Clear sprite first
            if (previewImage != null && previewImage.sprite != null)
            {
                Destroy(previewImage.sprite);
                previewImage.sprite = null;
            }

            Destroy(_previewTexture);
            _previewTexture = null;
        }
    }

    #endregion
}

#if UNITY_STANDALONE_WIN
/// <summary>
/// Native Windows file browser using P/Invoke (comdlg32.dll and shell32.dll).
/// Works in Unity standalone builds without requiring Windows Forms.
/// </summary>
public static class WindowsFileBrowser
{
    // File dialog
    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    // Folder dialog
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHBrowseForFolder(ref BrowseInfo bi);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, IntPtr pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr ptr);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct BrowseInfo
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    /// <summary>
    /// Opens a native Windows file dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="initialDir">Initial directory (empty for default)</param>
    /// <param name="filter">Filter string in format "Description\0*.ext\0\0"</param>
    /// <returns>Selected file path, or empty string if cancelled</returns>
    public static string OpenFileDialog(string title, string initialDir, string filter)
    {
        OpenFileName ofn = new OpenFileName();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.hwndOwner = IntPtr.Zero;
        ofn.lpstrFilter = filter;
        ofn.lpstrFile = new string(new char[256]);
        ofn.nMaxFile = ofn.lpstrFile.Length;
        ofn.lpstrFileTitle = new string(new char[64]);
        ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
        ofn.lpstrInitialDir = string.IsNullOrEmpty(initialDir) ? null : initialDir;
        ofn.lpstrTitle = title;
        ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;

        if (GetOpenFileName(ref ofn))
        {
            return ofn.lpstrFile;
        }

        return string.Empty;
    }

    /// <summary>
    /// Opens a native Windows folder browser dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="initialDir">Initial directory (not used in this implementation)</param>
    /// <returns>Selected folder path, or empty string if cancelled</returns>
    public static string OpenFolderDialog(string title, string initialDir)
    {
        IntPtr bufferPtr = Marshal.AllocHGlobal(260 * 2); // MAX_PATH * sizeof(wchar_t)

        try
        {
            BrowseInfo bi = new BrowseInfo();
            bi.hwndOwner = IntPtr.Zero;
            bi.pidlRoot = IntPtr.Zero;
            bi.pszDisplayName = bufferPtr;
            bi.lpszTitle = title;
            bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
            bi.lpfn = IntPtr.Zero;
            bi.lParam = IntPtr.Zero;
            bi.iImage = 0;

            IntPtr pidl = SHBrowseForFolder(ref bi);

            if (pidl != IntPtr.Zero)
            {
                try
                {
                    IntPtr pathPtr = Marshal.AllocHGlobal(260 * 2);
                    try
                    {
                        if (SHGetPathFromIDList(pidl, pathPtr))
                        {
                            return Marshal.PtrToStringAuto(pathPtr);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pathPtr);
                    }
                }
                finally
                {
                    CoTaskMemFree(pidl);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(bufferPtr);
        }

        return string.Empty;
    }
}
#endif
