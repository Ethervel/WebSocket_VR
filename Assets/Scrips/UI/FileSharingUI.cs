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

    [Header("Presentation Controls")]
    [Tooltip("Button to stop ongoing presentation")]
    public Button stopPresentationButton;
    [Tooltip("Text showing current presentation status")]
    public TextMeshProUGUI presentationStatusText;

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

        // Subscribe to events
        FileShareManager.OnFileShared += OnFileShared;
        FileShareManager.OnFileRemoved += OnFileRemoved;
        FileShareManager.OnFileListUpdated += OnFileListUpdated;
        FileShareManager.OnFileDownloadStarted += OnDownloadStarted;
        FileShareManager.OnFileDownloadComplete += OnDownloadComplete;
        FileShareManager.OnFileShareError += OnError;

        // Subscribe to presentation events
        FilePresentationManager.OnPresentationStarted += OnPresentationStarted;
        FilePresentationManager.OnPresentationStopped += OnPresentationStopped;

        // Stop presentation button
        if (stopPresentationButton != null)
        {
            stopPresentationButton.onClick.AddListener(OnStopPresentationClicked);
            stopPresentationButton.gameObject.SetActive(false);
        }
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

        // Initialize download path display
        UpdateDownloadPathDisplay();
    }

    void OnDestroy()
    {
        // Cleanup listeners
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

        // Unsubscribe
        FileShareManager.OnFileShared -= OnFileShared;
        FileShareManager.OnFileRemoved -= OnFileRemoved;
        FileShareManager.OnFileListUpdated -= OnFileListUpdated;
        FileShareManager.OnFileDownloadStarted -= OnDownloadStarted;
        FileShareManager.OnFileDownloadComplete -= OnDownloadComplete;
        FileShareManager.OnFileShareError -= OnError;

        // Unsubscribe presentation events
        FilePresentationManager.OnPresentationStarted -= OnPresentationStarted;
        FilePresentationManager.OnPresentationStopped -= OnPresentationStopped;
        if (stopPresentationButton != null)
            stopPresentationButton.onClick.RemoveAllListeners();

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

        // Find the whiteboard to present on
        Whiteboard targetWhiteboard = FindAnyObjectByType<Whiteboard>();
        if (targetWhiteboard != null)
        {
            FilePresentationManager.Instance.StartPresentation(fileId, targetWhiteboard);
            Debug.Log($"[FileSharingUI] Starting presentation of {fileId}");
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

        // Show stop button only if we are the presenter
        bool isLocalPresenter = (presenterId == VRNetworkManager.LocalId);

        // Create stop button dynamically if not configured
        if (stopPresentationButton == null && isLocalPresenter && mainPanel != null)
        {
            CreateDynamicStopButton();
        }

        if (stopPresentationButton != null)
            stopPresentationButton.gameObject.SetActive(isLocalPresenter);

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

    void CreateDynamicStopButton()
    {
        if (mainPanel == null) return;

        GameObject stopBtn = new GameObject("StopPresentationButton");
        stopBtn.transform.SetParent(mainPanel.transform, false);

        RectTransform rt = stopBtn.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1);
        rt.anchorMax = new Vector2(0.5f, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -10);
        rt.sizeDelta = new Vector2(150, 40);

        Image img = stopBtn.AddComponent<Image>();
        img.color = new Color(0.9f, 0.2f, 0.2f, 1f);  // Red color

        stopPresentationButton = stopBtn.AddComponent<Button>();
        stopPresentationButton.targetGraphic = img;
        stopPresentationButton.onClick.AddListener(OnStopPresentationClicked);

        // Add text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(stopBtn.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "Stop Presentation";
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    void OnPresentationStopped(string whiteboardId, string presenterId)
    {
        _isPresentationActive = false;

        if (stopPresentationButton != null)
            stopPresentationButton.gameObject.SetActive(false);

        if (presentationStatusText != null)
            presentationStatusText.gameObject.SetActive(false);

        // Refresh list to show present buttons again
        if (_isOpen)
            RefreshFileList();
    }

    void OnStopPresentationClicked()
    {
        FilePresentationManager.Instance?.StopPresentation();
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
