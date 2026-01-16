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

    [Header("Preview Panel")]
    public GameObject previewPanel;
    public TextMeshProUGUI previewFileName;
    public TextMeshProUGUI previewFileSize;
    public TextMeshProUGUI previewFileType;
    public Image previewImage;
    public GameObject previewImageContainer;
    public Button previewShareButton;
    public Button previewCancelButton;

    [Header("Settings")]
    [Tooltip("Maximum length for displayed file names")]
    public int maxFileNameLength = 20;

    // State
    private string _pendingFilePath;
    private Dictionary<string, GameObject> _fileListItems = new Dictionary<string, GameObject>();
    private bool _isOpen = false;
    private Texture2D _previewTexture;

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

        // Subscribe to events
        FileShareManager.OnFileShared += OnFileShared;
        FileShareManager.OnFileRemoved += OnFileRemoved;
        FileShareManager.OnFileListUpdated += OnFileListUpdated;
        FileShareManager.OnFileDownloadStarted += OnDownloadStarted;
        FileShareManager.OnFileDownloadComplete += OnDownloadComplete;
        FileShareManager.OnFileShareError += OnError;

        // Initial state
        if (mainPanel != null) mainPanel.SetActive(false);
        if (previewPanel != null) previewPanel.SetActive(false);
    }

    void OnDestroy()
    {
        // Cleanup listeners
        if (fileButton != null) fileButton.onClick.RemoveAllListeners();
        if (closeButton != null) closeButton.onClick.RemoveAllListeners();
        if (shareButton != null) shareButton.onClick.RemoveAllListeners();
        if (previewShareButton != null) previewShareButton.onClick.RemoveAllListeners();
        if (previewCancelButton != null) previewCancelButton.onClick.RemoveAllListeners();

        // Unsubscribe
        FileShareManager.OnFileShared -= OnFileShared;
        FileShareManager.OnFileRemoved -= OnFileRemoved;
        FileShareManager.OnFileListUpdated -= OnFileListUpdated;
        FileShareManager.OnFileDownloadStarted -= OnDownloadStarted;
        FileShareManager.OnFileDownloadComplete -= OnDownloadComplete;
        FileShareManager.OnFileShareError -= OnError;

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

        // Set file icon based on type (if Image component exists)
        var images = item.GetComponentsInChildren<Image>(true);
        // Could set icon sprite based on file.fileExtension

        // Add click handler for download
        var button = item.GetComponent<Button>();
        if (button == null)
            button = item.AddComponent<Button>();

        // Capture fileId in closure
        string fileId = file.fileId;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnFileItemClicked(fileId));
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
/// Native Windows file browser using P/Invoke (comdlg32.dll).
/// Works in Unity standalone builds without requiring Windows Forms.
/// </summary>
public static class WindowsFileBrowser
{
    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

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
}
#endif
