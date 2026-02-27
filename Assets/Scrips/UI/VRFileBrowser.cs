using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// In-VR file browser for selecting files without leaving VR.
/// Displays folders and files in a scrollable list with navigation controls.
/// </summary>
public class VRFileBrowser : MonoBehaviour
{
    [Header("UI References")]
    public GameObject browserPanel;
    public TextMeshProUGUI currentPathText;
    public TextMeshProUGUI titleText;
    public Button closeButton;
    public Button parentFolderButton;
    public Button refreshButton;
    public Button selectFolderButton; // For folder selection mode
    public Transform driveButtonsContainer;
    public Transform fileListContainer;
    public GameObject fileItemPrefab;
    public GameObject folderItemPrefab;
    public TextMeshProUGUI filterInfoText;
    public Button cancelButton;

    [Header("Settings")]
    public Color folderColor = new Color(0.3f, 0.5f, 0.8f, 1f);
    public Color fileColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    public Color selectedColor = new Color(0.4f, 0.7f, 0.4f, 1f);
    public float itemHeight = 50f;

    [Header("File Filtering")]
    public string[] allowedExtensions = { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".gif" };

    public enum BrowserMode
    {
        SelectFile,
        SelectFolder
    }

    // Events
    public event Action<string> OnFileSelected;
    public event Action<string> OnFolderSelected;
    public event Action OnBrowserClosed;

    // State
    private string _currentPath;
    private List<GameObject> _currentItems = new List<GameObject>();
    private Stack<string> _navigationHistory = new Stack<string>();
    private string _selectedFilePath;
    private BrowserMode _currentMode = BrowserMode.SelectFile;

    void Start()
    {
        SetupButtonListeners();

        if (browserPanel != null)
            browserPanel.SetActive(false);
    }

    void SetupButtonListeners()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(Close);

        if (parentFolderButton != null)
            parentFolderButton.onClick.AddListener(NavigateToParent);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshCurrentDirectory);

        if (selectFolderButton != null)
            selectFolderButton.onClick.AddListener(SelectCurrentFolder);
    }

    /// <summary>
    /// Opens the file browser at the specified path or default location.
    /// </summary>
    public void Open(string startPath = null)
    {
        OpenWithMode(startPath, BrowserMode.SelectFile);
    }

    /// <summary>
    /// Opens the folder browser at the specified path for selecting a folder.
    /// </summary>
    public void OpenFolderBrowser(string startPath = null)
    {
        OpenWithMode(startPath, BrowserMode.SelectFolder);
    }

    void OpenWithMode(string startPath, BrowserMode mode)
    {
        Debug.Log($"[VRFileBrowser] OpenWithMode called - startPath: {startPath}, mode: {mode}");

        if (browserPanel == null)
        {
            Debug.LogError("[VRFileBrowser] browserPanel is not assigned!");
            return;
        }

        _currentMode = mode;
        _navigationHistory.Clear();
        _selectedFilePath = null;

        // Determine starting path
        if (string.IsNullOrEmpty(startPath) || !Directory.Exists(startPath))
        {
            startPath = GetDefaultStartPath();
            Debug.Log($"[VRFileBrowser] Using default start path: {startPath}");
        }

        // Update UI based on mode
        if (titleText != null)
        {
            titleText.text = mode == BrowserMode.SelectFolder ? "Select Folder" : "Select File";
        }

        // Show/hide select folder button based on mode
        if (selectFolderButton != null)
        {
            selectFolderButton.gameObject.SetActive(mode == BrowserMode.SelectFolder);
        }

        browserPanel.SetActive(true);
        Debug.Log($"[VRFileBrowser] browserPanel activated - activeInHierarchy: {browserPanel.activeInHierarchy}, position: {browserPanel.transform.position}");

        CreateDriveButtons();
        NavigateTo(startPath);
        UpdateFilterInfo();
    }

    /// <summary>
    /// Closes the file browser.
    /// </summary>
    public void Close()
    {
        if (browserPanel != null)
            browserPanel.SetActive(false);

        OnBrowserClosed?.Invoke();
    }

    string GetDefaultStartPath()
    {
        // Try common locations
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Directory.Exists(documents))
            return documents;

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (Directory.Exists(desktop))
            return desktop;

        // Fallback to first available drive
        DriveInfo[] drives = DriveInfo.GetDrives();
        foreach (var drive in drives)
        {
            if (drive.IsReady)
                return drive.RootDirectory.FullName;
        }

        return "C:\\";
    }

    void CreateDriveButtons()
    {
        if (driveButtonsContainer == null)
            return;

        // Clear existing
        foreach (Transform child in driveButtonsContainer)
        {
            Destroy(child.gameObject);
        }

        // Create drive buttons
        DriveInfo[] drives = DriveInfo.GetDrives();
        foreach (var drive in drives)
        {
            if (!drive.IsReady)
                continue;

            CreateDriveButton(drive);
        }
    }

    void CreateDriveButton(DriveInfo drive)
    {
        GameObject btnObj = new GameObject(drive.Name);
        btnObj.transform.SetParent(driveButtonsContainer, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(50, 30);

        Image bg = btnObj.AddComponent<Image>();
        bg.color = new Color(0.25f, 0.25f, 0.3f, 1f);

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bg;

        string drivePath = drive.RootDirectory.FullName;
        btn.onClick.AddListener(() => NavigateTo(drivePath));

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = drive.Name.TrimEnd('\\');
        text.fontSize = 14;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>
    /// Navigate to a specific directory.
    /// </summary>
    public void NavigateTo(string path)
    {
        if (!Directory.Exists(path))
        {
            Debug.LogWarning($"[VRFileBrowser] Directory not found: {path}");
            return;
        }

        // Save current path to history
        if (!string.IsNullOrEmpty(_currentPath))
        {
            _navigationHistory.Push(_currentPath);
        }

        _currentPath = path;
        UpdatePathDisplay();
        PopulateFileList();
    }

    void NavigateToParent()
    {
        if (string.IsNullOrEmpty(_currentPath))
            return;

        DirectoryInfo parent = Directory.GetParent(_currentPath);
        if (parent != null)
        {
            NavigateTo(parent.FullName);
        }
    }

    public void NavigateBack()
    {
        if (_navigationHistory.Count > 0)
        {
            string previousPath = _navigationHistory.Pop();
            _currentPath = previousPath;
            UpdatePathDisplay();
            PopulateFileList();
        }
    }

    void RefreshCurrentDirectory()
    {
        if (!string.IsNullOrEmpty(_currentPath))
        {
            PopulateFileList();
        }
    }

    void UpdatePathDisplay()
    {
        if (currentPathText != null)
        {
            // Abbreviate long paths
            string displayPath = _currentPath;
            if (displayPath.Length > 40)
            {
                displayPath = "..." + displayPath.Substring(displayPath.Length - 37);
            }
            currentPathText.text = displayPath;
        }
    }

    void UpdateFilterInfo()
    {
        if (filterInfoText != null)
        {
            if (_currentMode == BrowserMode.SelectFolder)
            {
                filterInfoText.text = "Navigate to folder and click 'Select'";
            }
            else
            {
                string extensions = string.Join(", ", allowedExtensions);
                filterInfoText.text = $"Types: {extensions}";
            }
        }
    }

    void PopulateFileList()
    {
        ClearFileList();

        Debug.Log($"[VRFileBrowser] PopulateFileList - currentPath: {_currentPath}");
        Debug.Log($"[VRFileBrowser] fileListContainer: {(fileListContainer != null ? fileListContainer.name : "NULL")}");

        if (string.IsNullOrEmpty(_currentPath) || !Directory.Exists(_currentPath))
        {
            Debug.LogWarning($"[VRFileBrowser] Invalid path: {_currentPath}");
            return;
        }

        try
        {
            // Get directories
            string[] directories = Directory.GetDirectories(_currentPath);
            Debug.Log($"[VRFileBrowser] Found {directories.Length} directories");
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);

            foreach (string dir in directories)
            {
                DirectoryInfo dirInfo = new DirectoryInfo(dir);

                // Skip hidden and system folders
                if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                    (dirInfo.Attributes & FileAttributes.System) != 0)
                    continue;

                CreateFolderItem(dirInfo);
            }

            // Get files
            string[] files = Directory.GetFiles(_currentPath);
            Debug.Log($"[VRFileBrowser] Found {files.Length} files");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                FileInfo fileInfo = new FileInfo(file);

                // Skip hidden files
                if ((fileInfo.Attributes & FileAttributes.Hidden) != 0)
                    continue;

                // Check extension filter
                if (!IsAllowedExtension(fileInfo.Extension))
                    continue;

                CreateFileItem(fileInfo);
            }
        }
        catch (UnauthorizedAccessException)
        {
            Debug.LogWarning($"[VRFileBrowser] Access denied to: {_currentPath}");
            CreateErrorItem("Access denied to this folder");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRFileBrowser] Error reading directory: {ex.Message}");
            CreateErrorItem("Error reading folder");
        }
    }

    bool IsAllowedExtension(string extension)
    {
        if (allowedExtensions == null || allowedExtensions.Length == 0)
            return true;

        string ext = extension.ToLowerInvariant();
        foreach (string allowed in allowedExtensions)
        {
            if (ext == allowed.ToLowerInvariant())
                return true;
        }
        return false;
    }

    void ClearFileList()
    {
        foreach (var item in _currentItems)
        {
            if (item != null)
                Destroy(item);
        }
        _currentItems.Clear();
    }

    void CreateFolderItem(DirectoryInfo dirInfo)
    {
        GameObject item = CreateListItem(dirInfo.Name, true);

        Button btn = item.GetComponent<Button>();
        if (btn != null)
        {
            string fullPath = dirInfo.FullName;
            btn.onClick.AddListener(() => NavigateTo(fullPath));
        }

        // Folder icon/color
        Image bg = item.GetComponent<Image>();
        if (bg != null)
            bg.color = folderColor;

        _currentItems.Add(item);
    }

    void CreateFileItem(FileInfo fileInfo)
    {
        string displayName = fileInfo.Name;
        string sizeText = FormatFileSize(fileInfo.Length);

        GameObject item = CreateListItem($"{displayName}  ({sizeText})", false);

        Button btn = item.GetComponent<Button>();
        if (btn != null)
        {
            string fullPath = fileInfo.FullName;
            btn.onClick.AddListener(() => SelectFile(fullPath));
        }

        // File color
        Image bg = item.GetComponent<Image>();
        if (bg != null)
            bg.color = fileColor;

        _currentItems.Add(item);
    }

    void CreateErrorItem(string message)
    {
        GameObject item = CreateListItem(message, false);

        // Disable button
        Button btn = item.GetComponent<Button>();
        if (btn != null)
            btn.interactable = false;

        // Error color
        Image bg = item.GetComponent<Image>();
        if (bg != null)
            bg.color = new Color(0.5f, 0.2f, 0.2f, 1f);

        _currentItems.Add(item);
    }

    GameObject CreateListItem(string text, bool isFolder)
    {
        if (fileListContainer == null)
        {
            Debug.LogError("[VRFileBrowser] fileListContainer is not assigned!");
            return new GameObject("Error");
        }

        // Use prefab if available, otherwise create dynamically
        GameObject item;
        if (isFolder && folderItemPrefab != null)
        {
            item = Instantiate(folderItemPrefab, fileListContainer);
        }
        else if (!isFolder && fileItemPrefab != null)
        {
            item = Instantiate(fileItemPrefab, fileListContainer);
        }
        else
        {
            item = CreateDefaultListItem(text, isFolder);
        }

        item.SetActive(true);

        // Set text
        TextMeshProUGUI[] texts = item.GetComponentsInChildren<TextMeshProUGUI>();
        if (texts.Length > 0)
        {
            texts[0].text = (isFolder ? "[Folder] " : "") + text;
        }

        return item;
    }

    GameObject CreateDefaultListItem(string text, bool isFolder)
    {
        Debug.Log($"[VRFileBrowser] CreateDefaultListItem: {text}, isFolder: {isFolder}");

        GameObject item = new GameObject(isFolder ? "Folder" : "File");
        item.transform.SetParent(fileListContainer, false);

        RectTransform rt = item.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, itemHeight);

        // Force width to stretch
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);

        Image bg = item.AddComponent<Image>();
        bg.color = isFolder ? folderColor : fileColor;

        Button btn = item.AddComponent<Button>();
        btn.targetGraphic = bg;

        // Add layout element
        LayoutElement le = item.AddComponent<LayoutElement>();
        le.minHeight = itemHeight;
        le.preferredHeight = itemHeight;
        le.flexibleWidth = 1; // Allow to stretch horizontally

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(item.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(15, 5);
        textRt.offsetMax = new Vector2(-15, -5);

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 14;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.overflowMode = TextOverflowModes.Ellipsis;

        return item;
    }

    void SelectFile(string filePath)
    {
        _selectedFilePath = filePath;
        Debug.Log($"[VRFileBrowser] File selected: {filePath}");

        // Close browser and notify
        if (browserPanel != null)
            browserPanel.SetActive(false);

        OnFileSelected?.Invoke(filePath);
    }

    void SelectCurrentFolder()
    {
        if (string.IsNullOrEmpty(_currentPath))
            return;

        Debug.Log($"[VRFileBrowser] Folder selected: {_currentPath}");

        // Close browser and notify
        if (browserPanel != null)
            browserPanel.SetActive(false);

        OnFolderSelected?.Invoke(_currentPath);
    }

    string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024f:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024f * 1024f):F1} MB";
        return $"{bytes / (1024f * 1024f * 1024f):F1} GB";
    }

    void OnDestroy()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveAllListeners();
        if (cancelButton != null)
            cancelButton.onClick.RemoveAllListeners();
        if (parentFolderButton != null)
            parentFolderButton.onClick.RemoveAllListeners();
        if (refreshButton != null)
            refreshButton.onClick.RemoveAllListeners();
        if (selectFolderButton != null)
            selectFolderButton.onClick.RemoveAllListeners();

        ClearFileList();
    }
}
