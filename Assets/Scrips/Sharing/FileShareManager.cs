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

    // State
    private Dictionary<string, FileMetadata> _sharedFiles = new Dictionary<string, FileMetadata>();
    private Dictionary<string, byte[]> _fileContents = new Dictionary<string, byte[]>();
    private Coroutine _pendingRequestCoroutine;
    private bool _hasRequestedList = false;

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
        ReadError
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

    #endregion

    #region Network Message Handling

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
        }
    }

    void HandleFileUpload(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FileShareUploadData>(msg.data);

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Ignore our own uploads
        if (data.sharerId == VRNetworkManager.LocalId) return;

        var metadata = new FileMetadata
        {
            fileId = data.fileId,
            roomId = data.roomId,
            fileName = data.fileName,
            fileExtension = data.fileExtension,
            mimeType = data.mimeType,
            fileSize = data.fileSize,
            sharerId = data.sharerId,
            sharerName = data.sharerName,
            sharedTimestamp = data.timestamp
        };

        // Store metadata and content
        _sharedFiles[data.fileId] = metadata;

        try
        {
            _fileContents[data.fileId] = Convert.FromBase64String(data.fileDataBase64);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileShare] Failed to decode file content: {e.Message}");
        }

        OnFileShared?.Invoke(metadata);
        OnFileListUpdated?.Invoke(GetSharedFilesList());

        Debug.Log($"[FileShare] Received file: {metadata.fileName} from {metadata.sharerName}");
    }

    void HandleFileListRequest(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FileListRequestData>(msg.data);

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
        var data = JsonUtility.FromJson<FileListResponseData>(msg.data);

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Only process if targeted at us
        if (data.targetPlayerId != VRNetworkManager.LocalId) return;

        try
        {
            var fileList = JsonUtility.FromJson<FileMetadataList>(data.filesJson);
            if (fileList?.files == null) return;

            int addedCount = 0;
            foreach (var file in fileList.files)
            {
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
        catch (Exception e)
        {
            Debug.LogError($"[FileShare] Failed to parse file list response: {e.Message}");
        }
    }

    void HandleDownloadRequest(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<FileDownloadRequestData>(msg.data);

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Only respond if we have the file content
        if (!_fileContents.TryGetValue(data.fileId, out var content)) return;
        if (!_sharedFiles.TryGetValue(data.fileId, out var metadata)) return;

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
        var data = JsonUtility.FromJson<FileDownloadResponseData>(msg.data);

        // Room filtering
        if (data.roomId != VRRoomManager.Instance.CurrentRoomId) return;

        // Only process if targeted at us
        if (data.targetPlayerId != VRNetworkManager.LocalId) return;

        try
        {
            byte[] fileData = Convert.FromBase64String(data.fileDataBase64);
            _fileContents[data.fileId] = fileData;

            SaveFileLocally(data.fileId, data.fileName, fileData);
        }
        catch (Exception e)
        {
            OnFileShareError?.Invoke(data.fileId, $"Download failed: {e.Message}");
            Debug.LogError($"[FileShare] Download decode error: {e.Message}");
        }
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
            _hasRequestedList = true;

            VRNetworkManager.Instance.Send("file-list-request", new FileListRequestData
            {
                roomId = VRRoomManager.Instance.CurrentRoomId,
                requesterId = VRNetworkManager.LocalId
            });

            Debug.Log("[FileShare] Requested file list from room");
        }

        _pendingRequestCoroutine = null;
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

    void SaveFileLocally(string fileId, string fileName, byte[] data)
    {
        string downloadPath = Path.Combine(Application.persistentDataPath, "Downloads");

        try
        {
            Directory.CreateDirectory(downloadPath);

            string filePath = Path.Combine(downloadPath, fileName);

            // Handle duplicate filenames
            int counter = 1;
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(downloadPath, $"{baseName}_{counter}{ext}");
                counter++;
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
