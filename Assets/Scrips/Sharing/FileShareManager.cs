using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Singleton manager for file sharing in VR rooms.
/// Handles file upload, download, validation, and network synchronization.
/// Follows patterns from ScreenShareManager.cs and WhiteboardDrawingSurface.cs.
/// </summary>
public class FileShareManager : MonoBehaviour
{
    public static FileShareManager Instance { get; private set; }

    // Configuration
    [Header("File Settings")]
    [Tooltip("Maximum file size in bytes (default 10 MB)")]
    public long maxFileSizeBytes = 10 * 1024 * 1024;

    [Tooltip("Supported file extensions")]
    public string[] supportedExtensions = { "pdf", "doc", "docx", "xls", "xlsx", "png", "jpg", "jpeg", "gif" };

    // Download path
    private const string PREF_DOWNLOAD_PATH = "FileShare_DownloadPath";
    private string _downloadPath;

    // State
    private Dictionary<string, FileMetadata> _sharedFiles = new Dictionary<string, FileMetadata>();
    private Dictionary<string, byte[]> _fileContents = new Dictionary<string, byte[]>();
    private Coroutine _pendingRequestCoroutine;
    private bool _hasRequestedList = false;

    // IMPORTANT FIX: Timeout handling for late joiner state requests
    [Header("State Request Timeout")]
    [Tooltip("Timeout in seconds waiting for file list response")]
    public float stateRequestTimeout = 10f;
    [Tooltip("Maximum retry attempts for file list requests")]
    public int maxStateRequestRetries = 2;
    private int _stateRequestRetries = 0;
    private bool _waitingForListResponse = false;

    // MINOR FIX: Constants for magic numbers
    private const int MAX_FILENAME_LENGTH = 200;
    private const int MAX_DUPLICATE_ATTEMPTS = 1000;
    private const int MAX_PATH_LENGTH = 260; // Windows MAX_PATH

    /// <summary>
    /// Gets or sets the download path. Saved to PlayerPrefs.
    /// </summary>
    public string DownloadPath
    {
        get
        {
            if (string.IsNullOrEmpty(_downloadPath))
            {
                _downloadPath = PlayerPrefs.GetString(PREF_DOWNLOAD_PATH, GetDefaultDownloadPath());
            }
            return _downloadPath;
        }
        set
        {
            _downloadPath = value;
            PlayerPrefs.SetString(PREF_DOWNLOAD_PATH, value);
            PlayerPrefs.Save();
            Debug.Log($"[FileShare] Download path set to: {value}");
        }
    }

    /// <summary>
    /// Gets the default download path.
    /// </summary>
    public static string GetDefaultDownloadPath()
    {
        return Path.Combine(Application.persistentDataPath, "Downloads");
    }

    // Events
    public static event Action<FileMetadata> OnFileShared;
    public static event Action<string> OnFileRemoved;
    public static event Action<List<FileMetadata>> OnFileListUpdated;
    public static event Action<string, string> OnFileDownloadStarted;   // fileId, fileName
    public static event Action<string, string> OnFileDownloadComplete;  // fileId, localPath
    public static event Action<string, string> OnFileShareError;        // context, errorMessage

    // Properties
    public bool HasSharedFiles => _sharedFiles.Count > 0;
    public int SharedFileCount => _sharedFiles.Count;

    // Validation results
    public enum FileValidationResult
    {
        Valid,
        FileTooLarge,
        UnsupportedType,
        FileNotFound,
        ReadError,
        PathTooLong  // MINOR FIX: Added for path length validation
    }

    #region Singleton & Lifecycle

    void Awake()
    {
        if (Instance != null && Instance != this)
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
        if (_pendingRequestCoroutine != null)
        {
            StopCoroutine(_pendingRequestCoroutine);
            _pendingRequestCoroutine = null;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Validates a file for sharing.
    /// </summary>
    public FileValidationResult ValidateFile(string filePath, out FileMetadata metadata)
    {
        metadata = null;

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return FileValidationResult.FileNotFound;

        // MINOR FIX: Validate path length to avoid Windows MAX_PATH issues
        if (filePath.Length > MAX_PATH_LENGTH)
            return FileValidationResult.PathTooLong;

        try
        {
            var fileInfo = new FileInfo(filePath);

            if (fileInfo.Length > maxFileSizeBytes)
                return FileValidationResult.FileTooLarge;

            string ext = fileInfo.Extension.TrimStart('.').ToLower();
            if (!IsExtensionSupported(ext))
                return FileValidationResult.UnsupportedType;

            metadata = new FileMetadata
            {
                fileId = Guid.NewGuid().ToString(),
                fileName = fileInfo.Name,
                fileExtension = ext,
                mimeType = GetMimeType(ext),
                fileSize = fileInfo.Length,
                sharerId = VRNetworkManager.LocalId,
                sharerName = PlayerPrefs.GetString("PlayerName", "Player"),
                sharedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            return FileValidationResult.Valid;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileShare] Validation error: {e.Message}");
            return FileValidationResult.ReadError;
        }
    }

    /// <summary>
    /// Shares a file with the room.
    /// </summary>
    public void ShareFile(string filePath)
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            OnFileShareError?.Invoke("share", "Not in a room");
            return;
        }

        var result = ValidateFile(filePath, out var metadata);
        if (result != FileValidationResult.Valid)
        {
            OnFileShareError?.Invoke("share", GetValidationErrorMessage(result));
            return;
        }

        try
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            string base64Data = Convert.ToBase64String(fileBytes);

            metadata.roomId = VRRoomManager.Instance.CurrentRoomId;

            // Store locally
            _sharedFiles[metadata.fileId] = metadata;
            _fileContents[metadata.fileId] = fileBytes;

            // MINOR FIX: Check connection before sending
            if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null)
            {
                OnFileShareError?.Invoke("share", "Cannot share file - not connected");
                return;
            }

            // Broadcast to room
            VRNetworkManager.Instance.Send("file-share-upload", new FileShareUploadData
            {
                roomId = metadata.roomId,
                fileId = metadata.fileId,
                fileName = metadata.fileName,
                fileExtension = metadata.fileExtension,
                mimeType = metadata.mimeType,
                fileSize = metadata.fileSize,
                sharerId = metadata.sharerId,
                sharerName = metadata.sharerName,
                fileDataBase64 = base64Data,
                timestamp = metadata.sharedTimestamp
            });

            OnFileShared?.Invoke(metadata);
            OnFileListUpdated?.Invoke(GetSharedFilesList());

            Debug.Log($"[FileShare] Shared file: {metadata.fileName} ({FormatFileSize(metadata.fileSize)})");
        }
        catch (Exception e)
        {
            OnFileShareError?.Invoke("share", $"Failed to read file: {e.Message}");
            Debug.LogError($"[FileShare] Share error: {e.Message}");
        }
    }

    /// <summary>
    /// Requests download of a shared file.
    /// </summary>
    public void RequestFileDownload(string fileId)
    {
        if (!_sharedFiles.TryGetValue(fileId, out var metadata))
        {
            OnFileShareError?.Invoke(fileId, "File not found");
            return;
        }

        OnFileDownloadStarted?.Invoke(fileId, metadata.fileName);

        // If we have the content locally (we shared it or already received it), save directly
        if (_fileContents.ContainsKey(fileId))
        {
            SaveFileLocally(fileId, metadata.fileName, _fileContents[fileId]);
            return;
        }

        // MINOR FIX: Check connection before sending
        if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null)
        {
            OnFileShareError?.Invoke(fileId, "Cannot download - not connected");
            return;
        }

        // Request from network
        VRNetworkManager.Instance.Send("file-download-request", new FileDownloadRequestData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            fileId = fileId,
            requesterId = VRNetworkManager.LocalId
        });

        Debug.Log($"[FileShare] Requesting download: {metadata.fileName}");
    }

    /// <summary>
    /// Gets the list of shared files in current room.
    /// </summary>
    public List<FileMetadata> GetSharedFilesList()
    {
        return new List<FileMetadata>(_sharedFiles.Values);
    }

    /// <summary>
    /// Gets metadata for a specific file.
    /// </summary>
    public FileMetadata GetFileMetadata(string fileId)
    {
        _sharedFiles.TryGetValue(fileId, out var metadata);
        return metadata;
    }

    /// <summary>
    /// Gets the raw content of a shared file.
    /// Used by FilePresentationManager to display files on whiteboard.
    /// </summary>
    public byte[] GetFileContent(string fileId)
    {
        if (_fileContents.TryGetValue(fileId, out var content))
            return content;
        return null;
    }

    /// <summary>
    /// Checks if the local player can remove a file (only the sharer can remove).
    /// </summary>
    public bool CanRemoveFile(string fileId)
    {
        if (!_sharedFiles.TryGetValue(fileId, out var metadata))
            return false;

        return metadata.sharerId == VRNetworkManager.LocalId;
    }

    /// <summary>
    /// Removes a shared file (only works if you are the sharer).
    /// </summary>
    public void RemoveSharedFile(string fileId)
    {
        if (!CanRemoveFile(fileId))
        {
            OnFileShareError?.Invoke(fileId, "You can only remove files you shared");
            return;
        }

        if (!_sharedFiles.TryGetValue(fileId, out var metadata))
            return;

        // Remove locally
        _sharedFiles.Remove(fileId);
        _fileContents.Remove(fileId);

        // MINOR FIX: Check connection before sending
        if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null)
        {
            Debug.LogWarning("[FileShare] Cannot broadcast removal - not connected");
            OnFileRemoved?.Invoke(fileId);
            OnFileListUpdated?.Invoke(GetSharedFilesList());
            return;
        }

        // Broadcast removal to room
        VRNetworkManager.Instance.Send("file-removed", new FileRemovedData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            fileId = fileId,
            removerId = VRNetworkManager.LocalId
        });

        OnFileRemoved?.Invoke(fileId);
        OnFileListUpdated?.Invoke(GetSharedFilesList());

        Debug.Log($"[FileShare] Removed file: {metadata.fileName}");
    }

    /// <summary>
    /// Opens the download folder in file explorer.
    /// </summary>
    public void OpenDownloadFolder()
    {
        string path = DownloadPath;

        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

#if UNITY_STANDALONE_WIN
            System.Diagnostics.Process.Start("explorer.exe", path);
#elif UNITY_STANDALONE_OSX
            System.Diagnostics.Process.Start("open", path);
#elif UNITY_STANDALONE_LINUX
            System.Diagnostics.Process.Start("xdg-open", path);
#endif
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FileShare] Could not open folder: {e.Message}");
        }
    }

    #endregion

    #region Network Message Handling

    /// <summary>
    /// IMPORTANT FIX: Safe JSON deserialization with validation.
    /// Returns null if deserialization fails or data is invalid.
    /// </summary>
    private T TryDeserialize<T>(string json, string context) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning($"[FileShare] Empty JSON data for {context}");
            return null;
        }

        try
        {
            T result = JsonUtility.FromJson<T>(json);
            if (result == null)
            {
                Debug.LogWarning($"[FileShare] Null result from JSON for {context}");
                return null;
            }
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileShare] JSON parse error for {context}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// IMPORTANT FIX: Safe Base64 decode with validation.
    /// Returns null if decode fails.
    /// </summary>
    private byte[] TryDecodeBase64(string base64Data, string context)
    {
        if (string.IsNullOrEmpty(base64Data))
        {
            Debug.LogWarning($"[FileShare] Empty Base64 data for {context}");
            return null;
        }

        try
        {
            return Convert.FromBase64String(base64Data);
        }
        catch (FormatException e)
        {
            Debug.LogError($"[FileShare] Base64 decode error for {context}: {e.Message}");
            return null;
        }
    }

    void HandleNetworkMessage(NetworkMessage msg)
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            return;

        switch (msg.type)
        {
            case "file-share-upload":
                HandleFileUpload(msg);
                break;
            case "file-list-request":
                HandleFileListRequest(msg);
                break;
            case "file-list-response":
                HandleFileListResponse(msg);
                break;
            case "file-download-request":
                HandleDownloadRequest(msg);
                break;
            case "file-download-response":
                HandleDownloadResponse(msg);
                break;
            case "file-removed":
                HandleFileRemoved(msg);
                break;
        }
    }

    void HandleFileRemoved(NetworkMessage msg)
    {
        var data = TryDeserialize<FileRemovedData>(msg.data, "file-removed");
        if (data == null) return;

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Ignore our own removal (already handled locally)
        if (data.removerId == VRNetworkManager.LocalId) return;

        // Remove from local state
        if (_sharedFiles.ContainsKey(data.fileId))
        {
            _sharedFiles.Remove(data.fileId);
            _fileContents.Remove(data.fileId);

            OnFileRemoved?.Invoke(data.fileId);
            OnFileListUpdated?.Invoke(GetSharedFilesList());

            Debug.Log($"[FileShare] File removed by sharer: {data.fileId}");
        }
    }

    void HandleFileUpload(NetworkMessage msg)
    {
        var data = TryDeserialize<FileShareUploadData>(msg.data, "file-share-upload");
        if (data == null || string.IsNullOrEmpty(data.fileId)) return;

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Ignore our own uploads
        if (data.sharerId == VRNetworkManager.LocalId) return;

        // IMPORTANT FIX: Validate file data before processing
        byte[] fileContent = TryDecodeBase64(data.fileDataBase64, $"file-upload:{data.fileId}");
        if (fileContent == null) return;

        // IMPORTANT FIX: Validate file size matches claimed size (prevent truncation attacks)
        if (fileContent.Length != data.fileSize)
        {
            Debug.LogWarning($"[FileShare] File size mismatch for {data.fileName}: claimed {data.fileSize}, actual {fileContent.Length}");
        }

        var metadata = new FileMetadata
        {
            fileId = data.fileId,
            roomId = data.roomId,
            fileName = data.fileName,
            fileExtension = data.fileExtension,
            mimeType = data.mimeType,
            fileSize = fileContent.Length, // Use actual size
            sharerId = data.sharerId,
            sharerName = data.sharerName,
            sharedTimestamp = data.timestamp
        };

        // Store metadata and content
        _sharedFiles[data.fileId] = metadata;
        _fileContents[data.fileId] = fileContent;

        OnFileShared?.Invoke(metadata);
        OnFileListUpdated?.Invoke(GetSharedFilesList());

        Debug.Log($"[FileShare] Received file: {metadata.fileName} from {metadata.sharerName}");
    }

    void HandleFileListRequest(NetworkMessage msg)
    {
        var data = TryDeserialize<FileListRequestData>(msg.data, "file-list-request");
        if (data == null || string.IsNullOrEmpty(data.roomId)) return;

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Don't respond to own request
        if (data.requesterId == VRNetworkManager.LocalId) return;

        // Only respond if we have files we shared
        var myFiles = new List<FileMetadata>();
        foreach (var kvp in _sharedFiles)
        {
            if (kvp.Value.sharerId == VRNetworkManager.LocalId)
                myFiles.Add(kvp.Value);
        }

        if (myFiles.Count == 0) return;

        // MINOR FIX: Check connection before sending
        if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null) return;

        // Serialize file list (workaround for JsonUtility nested object limitation)
        var fileList = new FileMetadataList(myFiles);
        string filesJson = JsonUtility.ToJson(fileList);

        VRNetworkManager.Instance.Send("file-list-response", new FileListResponseData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            targetPlayerId = data.requesterId,
            filesJson = filesJson
        });

        Debug.Log($"[FileShare] Sent file list response ({myFiles.Count} files) to {data.requesterId}");
    }

    void HandleFileListResponse(NetworkMessage msg)
    {
        var data = TryDeserialize<FileListResponseData>(msg.data, "file-list-response");
        if (data == null || string.IsNullOrEmpty(data.roomId)) return;

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Only process if targeted at us
        if (data.targetPlayerId != VRNetworkManager.LocalId) return;

        // IMPORTANT FIX: Mark list response as received for timeout handling
        _waitingForListResponse = false;

        var fileList = TryDeserialize<FileMetadataList>(data.filesJson, "file-list-inner");
        if (fileList?.files == null) return;

        int addedCount = 0;
        foreach (var file in fileList.files)
        {
            // IMPORTANT FIX: Validate file metadata
            if (string.IsNullOrEmpty(file.fileId)) continue;

            if (!_sharedFiles.ContainsKey(file.fileId))
            {
                _sharedFiles[file.fileId] = file;
                addedCount++;
            }
        }

        if (addedCount > 0)
        {
            OnFileListUpdated?.Invoke(GetSharedFilesList());
            Debug.Log($"[FileShare] Added {addedCount} files from list response");
        }
    }

    void HandleDownloadRequest(NetworkMessage msg)
    {
        var data = TryDeserialize<FileDownloadRequestData>(msg.data, "file-download-request");
        if (data == null || string.IsNullOrEmpty(data.fileId)) return;

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Only respond if we have the file content
        if (!_fileContents.TryGetValue(data.fileId, out var content)) return;
        if (!_sharedFiles.TryGetValue(data.fileId, out var metadata)) return;

        // IMPORTANT FIX: Check connection before sending
        if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null) return;

        VRNetworkManager.Instance.Send("file-download-response", new FileDownloadResponseData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            fileId = data.fileId,
            targetPlayerId = data.requesterId,
            fileName = metadata.fileName,
            fileDataBase64 = Convert.ToBase64String(content)
        });

        Debug.Log($"[FileShare] Sent file content: {metadata.fileName} to {data.requesterId}");
    }

    void HandleDownloadResponse(NetworkMessage msg)
    {
        var data = TryDeserialize<FileDownloadResponseData>(msg.data, "file-download-response");
        if (data == null || string.IsNullOrEmpty(data.fileId)) return;

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Only process if targeted at us
        if (data.targetPlayerId != VRNetworkManager.LocalId) return;

        // IMPORTANT FIX: Safe Base64 decode
        byte[] fileData = TryDecodeBase64(data.fileDataBase64, $"file-download:{data.fileId}");
        if (fileData == null)
        {
            OnFileShareError?.Invoke(data.fileId, "Download failed: Invalid data");
            return;
        }

        _fileContents[data.fileId] = fileData;
        SaveFileLocally(data.fileId, data.fileName, fileData);
    }

    #endregion

    #region Room Events

    void OnRoomJoined(string roomId)
    {
        _hasRequestedList = false;

        if (_pendingRequestCoroutine != null)
            StopCoroutine(_pendingRequestCoroutine);

        _pendingRequestCoroutine = StartCoroutine(RequestFileListDelayed());
    }

    IEnumerator RequestFileListDelayed()
    {
        yield return new WaitForSeconds(1.5f);

        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom && !_hasRequestedList)
        {
            _stateRequestRetries = 0;
            yield return StartCoroutine(RequestFileListWithTimeout());
        }

        _pendingRequestCoroutine = null;
    }

    // IMPORTANT FIX: Request file list with timeout and retry logic
    IEnumerator RequestFileListWithTimeout()
    {
        while (_stateRequestRetries < maxStateRequestRetries)
        {
            _stateRequestRetries++;
            _waitingForListResponse = true;
            _hasRequestedList = true;

            Debug.Log($"[FileShare] IMPORTANT FIX: Requesting file list (attempt {_stateRequestRetries}/{maxStateRequestRetries})");

            // IMPORTANT FIX: Check connection before sending
            if (!VRNetworkManager.IsConnected || VRNetworkManager.Instance == null)
            {
                Debug.LogWarning("[FileShare] Cannot request file list - not connected");
                _waitingForListResponse = false;
                yield break;
            }

            VRNetworkManager.Instance.Send("file-list-request", new FileListRequestData
            {
                roomId = VRRoomManager.Instance.CurrentRoomId,
                requesterId = VRNetworkManager.LocalId
            });

            // Wait for response or timeout
            float timer = 0f;
            while (_waitingForListResponse && timer < stateRequestTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            if (!_waitingForListResponse)
            {
                // Response received
                Debug.Log("[FileShare] IMPORTANT FIX: File list response received");
                yield break;
            }

            // Timeout - retry if attempts remaining
            Debug.LogWarning($"[FileShare] IMPORTANT FIX: File list request timeout after {stateRequestTimeout}s (attempt {_stateRequestRetries}/{maxStateRequestRetries})");
        }

        // All retries exhausted - no files in room or network issue
        _waitingForListResponse = false;
        Debug.Log("[FileShare] IMPORTANT FIX: File list request completed - no shared files detected or timeout");
    }

    void OnRoomLeft()
    {
        if (_pendingRequestCoroutine != null)
        {
            StopCoroutine(_pendingRequestCoroutine);
            _pendingRequestCoroutine = null;
        }

        // Clear all shared files when leaving room
        _sharedFiles.Clear();
        _fileContents.Clear();
        _hasRequestedList = false;

        OnFileListUpdated?.Invoke(new List<FileMetadata>());

        Debug.Log("[FileShare] Cleared file list on room leave");
    }

    void OnPlayerLeft(string playerId)
    {
        // Remove files shared by the player who left
        var filesToRemove = new List<string>();
        foreach (var kvp in _sharedFiles)
        {
            if (kvp.Value.sharerId == playerId)
                filesToRemove.Add(kvp.Key);
        }

        foreach (var fileId in filesToRemove)
        {
            _sharedFiles.Remove(fileId);
            _fileContents.Remove(fileId);
            OnFileRemoved?.Invoke(fileId);
        }

        if (filesToRemove.Count > 0)
        {
            OnFileListUpdated?.Invoke(GetSharedFilesList());
            Debug.Log($"[FileShare] Removed {filesToRemove.Count} files from player {playerId}");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// SECURITY FIX: Sanitizes filename to prevent path traversal attacks.
    /// Removes directory separators, parent references, and invalid characters.
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "unnamed_file";

        // Remove any directory components (path traversal prevention)
        fileName = Path.GetFileName(fileName);

        // Remove parent directory references
        fileName = fileName.Replace("..", "");

        // Remove invalid filename characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }

        // Ensure filename isn't empty after sanitization
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed_file";

        // MINOR FIX: Use constant for max filename length
        if (fileName.Length > MAX_FILENAME_LENGTH)
            fileName = fileName.Substring(0, MAX_FILENAME_LENGTH);

        return fileName;
    }

    /// <summary>
    /// SECURITY FIX: Validates that the final path is within the allowed download directory.
    /// </summary>
    private bool IsPathWithinDownloadDirectory(string fullPath, string downloadPath)
    {
        try
        {
            string normalizedFullPath = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedDownloadPath = Path.GetFullPath(downloadPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return normalizedFullPath.StartsWith(normalizedDownloadPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    void SaveFileLocally(string fileId, string fileName, byte[] data)
    {
        string downloadPath = DownloadPath;

        try
        {
            Directory.CreateDirectory(downloadPath);

            // SECURITY FIX: Sanitize filename to prevent path traversal
            string sanitizedFileName = SanitizeFileName(fileName);

            string filePath = Path.Combine(downloadPath, sanitizedFileName);

            // SECURITY FIX: Verify path is within download directory
            if (!IsPathWithinDownloadDirectory(filePath, downloadPath))
            {
                OnFileShareError?.Invoke(fileId, "Invalid file path detected");
                Debug.LogError($"[FileShare] SECURITY: Path traversal attempt blocked for: {fileName}");
                return;
            }

            // Handle duplicate filenames
            int counter = 1;
            string baseName = Path.GetFileNameWithoutExtension(sanitizedFileName);
            string ext = Path.GetExtension(sanitizedFileName);
            while (File.Exists(filePath))
            {
                string newFileName = $"{baseName}_{counter}{ext}";
                filePath = Path.Combine(downloadPath, newFileName);

                // Re-validate path after modification
                if (!IsPathWithinDownloadDirectory(filePath, downloadPath))
                {
                    OnFileShareError?.Invoke(fileId, "Invalid file path detected");
                    return;
                }

                counter++;
                // MINOR FIX: Use constant for max duplicate attempts
                if (counter > MAX_DUPLICATE_ATTEMPTS)
                {
                    OnFileShareError?.Invoke(fileId, "Too many duplicate files");
                    return;
                }
            }

            File.WriteAllBytes(filePath, data);

            Debug.Log($"[FileShare] Downloaded: {filePath}");
            OnFileDownloadComplete?.Invoke(fileId, filePath);
        }
        catch (Exception e)
        {
            OnFileShareError?.Invoke(fileId, $"Save failed: {e.Message}");
            Debug.LogError($"[FileShare] Save error: {e.Message}");
        }
    }

    bool IsExtensionSupported(string ext)
    {
        if (string.IsNullOrEmpty(ext)) return false;

        ext = ext.ToLower();
        foreach (var supported in supportedExtensions)
        {
            if (supported.Equals(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    string GetMimeType(string ext)
    {
        switch (ext.ToLower())
        {
            case "pdf": return "application/pdf";
            case "doc": return "application/msword";
            case "docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            case "xls": return "application/vnd.ms-excel";
            case "xlsx": return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            case "png": return "image/png";
            case "jpg":
            case "jpeg": return "image/jpeg";
            case "gif": return "image/gif";
            default: return "application/octet-stream";
        }
    }

    /// <summary>
    /// Gets a user-friendly error message for validation results.
    /// </summary>
    public static string GetValidationErrorMessage(FileValidationResult result)
    {
        switch (result)
        {
            case FileValidationResult.FileTooLarge:
                return "File exceeds 10 MB limit";
            case FileValidationResult.UnsupportedType:
                return "Unsupported file type";
            case FileValidationResult.FileNotFound:
                return "File not found";
            case FileValidationResult.ReadError:
                return "Cannot read file";
            case FileValidationResult.PathTooLong:
                return "File path exceeds maximum length";
            default:
                return "Unknown error";
        }
    }

    /// <summary>
    /// Formats file size in human-readable format.
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Abbreviates a filename if it's too long.
    /// </summary>
    public static string AbbreviateFileName(string fileName, int maxLength = 15)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length <= maxLength)
            return fileName;

        string ext = Path.GetExtension(fileName);
        string name = Path.GetFileNameWithoutExtension(fileName);
        int availableLength = maxLength - ext.Length - 3; // 3 for "..."

        if (availableLength <= 0)
            return fileName.Substring(0, maxLength - 3) + "...";

        return name.Substring(0, Math.Min(availableLength, name.Length)) + "..." + ext;
    }

    #endregion
}
