using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI pour afficher et gérer les fichiers partagés.
/// - Liste des fichiers disponibles
/// - Bouton pour partager un fichier
/// - Progression des transferts
/// </summary>
public class SharedFileUI : MonoBehaviour
{
    [Header("Panel References")]
    [Tooltip("Le panel principal du gestionnaire de fichiers")]
    public GameObject fileManagerPanel;

    [Tooltip("Bouton pour afficher/masquer le panel")]
    public UnityEngine.UI.Button toggleButton;

    [Header("File List")]
    [Tooltip("Container pour les items de fichiers")]
    public Transform fileListContainer;

    [Tooltip("Prefab pour un item de fichier dans la liste")]
    public GameObject fileItemPrefab;

    [Header("Actions")]
    [Tooltip("Bouton pour partager un nouveau fichier")]
    public UnityEngine.UI.Button shareFileButton;

    [Tooltip("Bouton pour ouvrir le dossier des fichiers")]
    public UnityEngine.UI.Button openFolderButton;

    [Header("Status")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI fileCountText;

    [Header("Settings")]
    public bool autoRefresh = true;
    public float refreshInterval = 2f;

    // State
    private Dictionary<string, FileItemUI> _fileItems = new Dictionary<string, FileItemUI>();
    private float _refreshTimer;
    private bool _isPanelVisible;

    void Start()
    {
        // Setup buttons
        if (toggleButton != null)
            toggleButton.onClick.AddListener(TogglePanel);

        if (shareFileButton != null)
            shareFileButton.onClick.AddListener(OnShareFileClicked);

        if (openFolderButton != null)
            openFolderButton.onClick.AddListener(OnOpenFolderClicked);

        // Subscribe to events
        FileShareManager.OnFileListUpdated += OnFileListUpdated;
        FileShareManager.OnFileProgress += OnFileProgress;
        FileShareManager.OnFileComplete += OnFileComplete;
        FileShareManager.OnFileError += OnFileError;

        // Initial state
        if (fileManagerPanel != null)
            fileManagerPanel.SetActive(false);

        UpdateStatus("Ready");
    }

    void OnDestroy()
    {
        FileShareManager.OnFileListUpdated -= OnFileListUpdated;
        FileShareManager.OnFileProgress -= OnFileProgress;
        FileShareManager.OnFileComplete -= OnFileComplete;
        FileShareManager.OnFileError -= OnFileError;
    }

    void Update()
    {
        if (!autoRefresh || !_isPanelVisible) return;

        _refreshTimer += Time.deltaTime;
        if (_refreshTimer >= refreshInterval)
        {
            _refreshTimer = 0f;
            RefreshFileList();
        }
    }

    #region Panel Control

    public void TogglePanel()
    {
        _isPanelVisible = !_isPanelVisible;

        if (fileManagerPanel != null)
            fileManagerPanel.SetActive(_isPanelVisible);

        if (_isPanelVisible)
        {
            RefreshFileList();
        }
    }

    public void ShowPanel()
    {
        _isPanelVisible = true;
        if (fileManagerPanel != null)
            fileManagerPanel.SetActive(true);
        RefreshFileList();
    }

    public void HidePanel()
    {
        _isPanelVisible = false;
        if (fileManagerPanel != null)
            fileManagerPanel.SetActive(false);
    }

    #endregion

    #region File List Management

    void OnFileListUpdated(List<FileMetadata> files)
    {
        RefreshFileListInternal(files);
    }

    void RefreshFileList()
    {
        if (FileShareManager.Instance == null) return;

        var files = FileShareManager.Instance.GetAvailableFiles();
        RefreshFileListInternal(files);
    }

    void RefreshFileListInternal(List<FileMetadata> files)
    {
        // Update file count
        if (fileCountText != null)
            fileCountText.text = $"{files.Count} fichier(s)";

        // Remove items for files that no longer exist
        var toRemove = new List<string>();
        foreach (var kvp in _fileItems)
        {
            bool exists = files.Exists(f => f.fileId == kvp.Key);
            if (!exists)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var fileId in toRemove)
        {
            if (_fileItems.TryGetValue(fileId, out var item))
            {
                if (item != null && item.gameObject != null)
                    Destroy(item.gameObject);
                _fileItems.Remove(fileId);
            }
        }

        // Add or update items
        foreach (var file in files)
        {
            if (_fileItems.TryGetValue(file.fileId, out var existingItem))
            {
                // Update existing
                existingItem.UpdateData(file);
            }
            else
            {
                // Create new
                CreateFileItem(file);
            }
        }
    }

    void CreateFileItem(FileMetadata file)
    {
        if (fileItemPrefab == null || fileListContainer == null)
            return;

        var itemGo = Instantiate(fileItemPrefab, fileListContainer);
        var itemUI = itemGo.GetComponent<FileItemUI>();

        if (itemUI == null)
        {
            itemUI = itemGo.AddComponent<FileItemUI>();
        }

        itemUI.Initialize(file, OnFileItemClicked, OnFileItemOpenClicked);
        _fileItems[file.fileId] = itemUI;
    }

    #endregion

    #region Progress Updates

    void OnFileProgress(string fileId, float progress)
    {
        if (_fileItems.TryGetValue(fileId, out var item))
        {
            item.UpdateProgress(progress);
        }
    }

    void OnFileComplete(string fileId, string localPath)
    {
        if (_fileItems.TryGetValue(fileId, out var item))
        {
            item.SetComplete(localPath);
        }

        UpdateStatus($"Fichier reçu!");
    }

    void OnFileError(string fileId, string error)
    {
        UpdateStatus($"Erreur: {error}");

        if (_fileItems.TryGetValue(fileId, out var item))
        {
            item.SetError(error);
        }
    }

    #endregion

    #region Actions

    void OnShareFileClicked()
    {
        // Open file dialog (Windows only, simplified for other platforms)
        OpenFileDialog();
    }

    void OpenFileDialog()
    {
#if UNITY_EDITOR
        // In editor, use EditorUtility
        string path = UnityEditor.EditorUtility.OpenFilePanel("Sélectionner un fichier", "", "");
        if (!string.IsNullOrEmpty(path))
        {
            ShareFile(path);
        }
#else
        // Other platforms - show message or use drag & drop
        UpdateStatus("Glissez un fichier ou utilisez ShareFile()");
        Debug.Log("[SharedFileUI] Use ShareFile(path) method directly or implement drag & drop");
#endif
    }

    void ShareFile(string filePath)
    {
        if (FileShareManager.Instance == null)
        {
            Debug.LogError("[SharedFileUI] FileShareManager not found!");
            return;
        }

        FileShareManager.Instance.ShareFile(filePath);
        UpdateStatus("Envoi en cours...");
    }

    void OnOpenFolderClicked()
    {
        if (FileShareManager.Instance != null)
        {
            FileShareManager.Instance.OpenSharedFilesFolder();
        }
    }

    void OnFileItemClicked(string fileId)
    {
        // Get file info and show details or options
        Debug.Log($"[SharedFileUI] File clicked: {fileId}");
    }

    void OnFileItemOpenClicked(string fileId)
    {
        if (FileShareManager.Instance == null) return;

        string localPath = FileShareManager.Instance.GetLocalPath(fileId);
        if (!string.IsNullOrEmpty(localPath))
        {
            if (FileViewer.Instance != null)
            {
                FileViewer.Instance.OpenFile(localPath);
            }
            else
            {
                UnityEngine.Application.OpenURL("file:///" + localPath.Replace("\\", "/"));
            }
        }
        else
        {
            UpdateStatus("Fichier pas encore téléchargé");
        }
    }

    #endregion

    #region Status

    void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Partage un fichier depuis un chemin (pour appel externe)
    /// </summary>
    public void ShareFileFromPath(string filePath)
    {
        ShareFile(filePath);
    }

    #endregion
}

/// <summary>
/// UI pour un item de fichier dans la liste
/// </summary>
public class FileItemUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI fileNameText;
    public TextMeshProUGUI fileSizeText;
    public TextMeshProUGUI senderText;
    public Image progressBar;
    public Image statusIcon;
    public UnityEngine.UI.Button openButton;
    public UnityEngine.UI.Button mainButton;

    [Header("Status Colors")]
    public Color pendingColor = Color.yellow;
    public Color completeColor = Color.green;
    public Color errorColor = Color.red;

    // State
    private FileMetadata _metadata;
    private string _localPath;
    private System.Action<string> _onClicked;
    private System.Action<string> _onOpenClicked;

    public void Initialize(FileMetadata metadata, System.Action<string> onClicked, System.Action<string> onOpenClicked)
    {
        _metadata = metadata;
        _onClicked = onClicked;
        _onOpenClicked = onOpenClicked;

        // Auto-find components if not assigned
        if (fileNameText == null)
            fileNameText = transform.Find("FileName")?.GetComponent<TextMeshProUGUI>();
        if (fileSizeText == null)
            fileSizeText = transform.Find("FileSize")?.GetComponent<TextMeshProUGUI>();
        if (senderText == null)
            senderText = transform.Find("Sender")?.GetComponent<TextMeshProUGUI>();
        if (progressBar == null)
            progressBar = transform.Find("ProgressBar")?.GetComponent<Image>();
        if (openButton == null)
            openButton = transform.Find("OpenButton")?.GetComponent<UnityEngine.UI.Button>();
        if (mainButton == null)
            mainButton = GetComponent<UnityEngine.UI.Button>();

        // Setup buttons
        if (openButton != null)
            openButton.onClick.AddListener(() => _onOpenClicked?.Invoke(_metadata.fileId));

        if (mainButton != null)
            mainButton.onClick.AddListener(() => _onClicked?.Invoke(_metadata.fileId));

        UpdateUI();
    }

    public void UpdateData(FileMetadata metadata)
    {
        _metadata = metadata;
        UpdateUI();
    }

    void UpdateUI()
    {
        if (_metadata == null) return;

        if (fileNameText != null)
            fileNameText.text = _metadata.fileName;

        if (fileSizeText != null)
            fileSizeText.text = FormatFileSize(_metadata.fileSize);

        if (senderText != null)
            senderText.text = _metadata.senderName;

        // Update status
        if (statusIcon != null)
        {
            statusIcon.color = _metadata.isComplete ? completeColor : pendingColor;
        }

        // Show/hide progress bar
        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(!_metadata.isComplete);
        }

        // Enable/disable open button
        if (openButton != null)
        {
            openButton.interactable = _metadata.isComplete;
        }
    }

    public void UpdateProgress(float progress)
    {
        if (progressBar != null)
        {
            progressBar.fillAmount = progress;
        }
    }

    public void SetComplete(string localPath)
    {
        _localPath = localPath;
        _metadata.isComplete = true;
        UpdateUI();

        if (progressBar != null)
            progressBar.gameObject.SetActive(false);
    }

    public void SetError(string error)
    {
        if (statusIcon != null)
            statusIcon.color = errorColor;

        if (progressBar != null)
            progressBar.gameObject.SetActive(false);
    }

    string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
