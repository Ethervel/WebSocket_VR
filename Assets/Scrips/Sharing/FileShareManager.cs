using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

/// <summary>
/// Gestionnaire de partage de fichiers.
/// - Envoi de fichiers en chunks via WebSocket
/// - Réception et réassemblage des fichiers
/// - Gestion de la liste des fichiers partagés
/// </summary>
public class FileShareManager : MonoBehaviour
{
    public static FileShareManager Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("Taille max d'un chunk en bytes (64KB recommandé)")]
    public int chunkSize = 65536; // 64KB

    [Tooltip("Taille max d'un fichier en bytes (50MB)")]
    public long maxFileSize = 52428800; // 50MB

    [Tooltip("Délai entre l'envoi de chaque chunk (pour ne pas saturer)")]
    public float chunkSendDelay = 0.05f; // 50ms

    [Header("Debug")]
    public bool showDebugLogs = true;

    // Events
    public static event Action<FileMetadata> OnFileAnnounced;          // Nouveau fichier annoncé
    public static event Action<string, float> OnFileProgress;           // Progression (fileId, 0-1)
    public static event Action<string, string> OnFileComplete;          // Fichier reçu (fileId, localPath)
    public static event Action<string, string> OnFileError;             // Erreur (fileId, message)
    public static event Action<List<FileMetadata>> OnFileListUpdated;   // Liste mise à jour

    // State
    private Dictionary<string, LocalFileInfo> _receivingFiles = new Dictionary<string, LocalFileInfo>();
    private Dictionary<string, FileMetadata> _availableFiles = new Dictionary<string, FileMetadata>();
    private string _sharedFilesPath;

    // Sending state
    private bool _isSending = false;
    private string _currentSendingFileId;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Setup shared files directory
        _sharedFilesPath = Path.Combine(Application.persistentDataPath, "SharedFiles");
        if (!Directory.Exists(_sharedFilesPath))
        {
            Directory.CreateDirectory(_sharedFilesPath);
        }

        if (showDebugLogs)
            Debug.Log($"[FileShare] Initialized. Storage: {_sharedFilesPath}");
    }

    void OnEnable()
    {
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
    }

    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
    }

    #region Room Events

    void OnRoomJoined(string roomId)
    {
        if (showDebugLogs)
            Debug.Log($"[FileShare] Joined room {roomId}, requesting file list...");

        // Clear previous state
        _receivingFiles.Clear();
        _availableFiles.Clear();

        // Request file list from other peers
        StartCoroutine(RequestFileListDelayed(roomId));
    }

    IEnumerator RequestFileListDelayed(string roomId)
    {
        yield return new WaitForSeconds(1f); // Wait for room to stabilize

        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            yield break;

        var request = new FileListRequestData
        {
            roomId = roomId,
            requesterId = VRNetworkManager.LocalId
        };

        VRNetworkManager.Instance.Send("file-list-request", request);
    }

    void OnRoomLeft()
    {
        if (showDebugLogs)
            Debug.Log("[FileShare] Left room, clearing files...");

        // Cancel any ongoing transfers
        _isSending = false;
        _receivingFiles.Clear();
        _availableFiles.Clear();

        OnFileListUpdated?.Invoke(new List<FileMetadata>());
    }

    #endregion

    #region Send File

    /// <summary>
    /// Partage un fichier avec la room actuelle
    /// </summary>
    public void ShareFile(string filePath)
    {
        if (_isSending)
        {
            Debug.LogWarning("[FileShare] Already sending a file, please wait...");
            return;
        }

        if (!File.Exists(filePath))
        {
            Debug.LogError($"[FileShare] File not found: {filePath}");
            return;
        }

        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            Debug.LogError("[FileShare] Not in a room!");
            return;
        }

        StartCoroutine(SendFileCoroutine(filePath));
    }

    IEnumerator SendFileCoroutine(string filePath)
    {
        _isSending = true;

        FileInfo fileInfo = new FileInfo(filePath);

        // Check file size
        if (fileInfo.Length > maxFileSize)
        {
            Debug.LogError($"[FileShare] File too large: {fileInfo.Length} bytes (max: {maxFileSize})");
            _isSending = false;
            yield break;
        }

        // Read file
        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(filePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileShare] Failed to read file: {e.Message}");
            _isSending = false;
            yield break;
        }

        // Generate file ID and calculate chunks
        string fileId = Guid.NewGuid().ToString();
        _currentSendingFileId = fileId;
        int totalChunks = Mathf.CeilToInt((float)fileBytes.Length / chunkSize);
        string checksum = CalculateMD5(fileBytes);

        string roomId = VRRoomManager.Instance?.CurrentRoomId ?? "";
        string playerName = PlayerPrefs.GetString("PlayerName", "Unknown");

        // Send announce
        var announce = new FileAnnounceData
        {
            fileId = fileId,
            roomId = roomId,
            fileName = fileInfo.Name,
            fileExtension = fileInfo.Extension,
            fileSize = fileInfo.Length,
            totalChunks = totalChunks,
            senderId = VRNetworkManager.LocalId,
            senderName = playerName
        };

        VRNetworkManager.Instance.Send("file-announce", announce);

        if (showDebugLogs)
            Debug.Log($"[FileShare] Sending '{fileInfo.Name}' ({totalChunks} chunks)...");

        // Add to our own available files list
        AddToAvailableFiles(announce);

        yield return new WaitForSeconds(0.1f);

        // Send chunks
        for (int i = 0; i < totalChunks; i++)
        {
            if (!_isSending) // Cancelled
            {
                Debug.Log("[FileShare] Send cancelled");
                yield break;
            }

            int offset = i * chunkSize;
            int length = Mathf.Min(chunkSize, fileBytes.Length - offset);
            byte[] chunkBytes = new byte[length];
            Array.Copy(fileBytes, offset, chunkBytes, 0, length);

            var chunk = new FileChunkData
            {
                fileId = fileId,
                roomId = roomId,
                chunkIndex = i,
                data = Convert.ToBase64String(chunkBytes)
            };

            VRNetworkManager.Instance.Send("file-chunk", chunk);

            // Update progress locally
            float progress = (float)(i + 1) / totalChunks;
            OnFileProgress?.Invoke(fileId, progress);

            yield return new WaitForSeconds(chunkSendDelay);
        }

        // Send complete
        var complete = new FileCompleteData
        {
            fileId = fileId,
            roomId = roomId,
            checksum = checksum
        };

        VRNetworkManager.Instance.Send("file-complete", complete);

        if (showDebugLogs)
            Debug.Log($"[FileShare] File '{fileInfo.Name}' sent successfully!");

        _isSending = false;
        _currentSendingFileId = null;
    }

    /// <summary>
    /// Annule l'envoi en cours
    /// </summary>
    public void CancelSend()
    {
        _isSending = false;
    }

    #endregion

    #region Receive File

    void HandleNetworkMessage(NetworkMessage msg)
    {
        switch (msg.type)
        {
            case "file-announce":
                HandleFileAnnounce(msg.data, msg.senderId);
                break;
            case "file-chunk":
                HandleFileChunk(msg.data, msg.senderId);
                break;
            case "file-complete":
                HandleFileComplete(msg.data, msg.senderId);
                break;
            case "file-list-request":
                HandleFileListRequest(msg.data, msg.senderId);
                break;
            case "file-list-response":
                HandleFileListResponse(msg.data);
                break;
        }
    }

    void HandleFileAnnounce(string dataJson, string senderId)
    {
        if (senderId == VRNetworkManager.LocalId) return; // Ignore our own

        var announce = JsonUtility.FromJson<FileAnnounceData>(dataJson);

        if (VRRoomManager.Instance == null || announce.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Create local file info for receiving
        var localInfo = new LocalFileInfo
        {
            fileId = announce.fileId,
            fileName = announce.fileName,
            fileExtension = announce.fileExtension,
            fileSize = announce.fileSize,
            totalChunks = announce.totalChunks,
            receivedChunks = 0,
            senderId = announce.senderId,
            senderName = announce.senderName,
            isComplete = false,
            progress = 0f,
            chunkBuffer = new Dictionary<int, byte[]>()
        };

        _receivingFiles[announce.fileId] = localInfo;

        // Add to available files
        AddToAvailableFiles(announce);

        if (showDebugLogs)
            Debug.Log($"[FileShare] Receiving '{announce.fileName}' from {announce.senderName} ({announce.totalChunks} chunks)");

        OnFileAnnounced?.Invoke(CreateMetadata(localInfo));
    }

    void HandleFileChunk(string dataJson, string senderId)
    {
        if (senderId == VRNetworkManager.LocalId) return;

        var chunk = JsonUtility.FromJson<FileChunkData>(dataJson);

        if (!_receivingFiles.TryGetValue(chunk.fileId, out var localInfo))
            return;

        if (VRRoomManager.Instance == null || chunk.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Decode and buffer chunk
        try
        {
            byte[] chunkBytes = Convert.FromBase64String(chunk.data);

            if (!localInfo.chunkBuffer.ContainsKey(chunk.chunkIndex))
            {
                localInfo.chunkBuffer[chunk.chunkIndex] = chunkBytes;
                localInfo.receivedChunks++;
                localInfo.UpdateProgress();

                OnFileProgress?.Invoke(chunk.fileId, localInfo.progress);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileShare] Failed to decode chunk {chunk.chunkIndex}: {e.Message}");
        }
    }

    void HandleFileComplete(string dataJson, string senderId)
    {
        if (senderId == VRNetworkManager.LocalId) return;

        var complete = JsonUtility.FromJson<FileCompleteData>(dataJson);

        if (!_receivingFiles.TryGetValue(complete.fileId, out var localInfo))
            return;

        if (VRRoomManager.Instance == null || complete.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Reassemble file
        StartCoroutine(ReassembleFile(localInfo, complete.checksum));
    }

    IEnumerator ReassembleFile(LocalFileInfo localInfo, string expectedChecksum)
    {
        yield return null; // Let Unity breathe

        // Check if we have all chunks
        if (localInfo.receivedChunks < localInfo.totalChunks)
        {
            Debug.LogWarning($"[FileShare] Missing chunks for '{localInfo.fileName}': {localInfo.receivedChunks}/{localInfo.totalChunks}");
            OnFileError?.Invoke(localInfo.fileId, "Missing chunks");
            yield break;
        }

        // Reassemble
        List<byte> fileBytes = new List<byte>();
        for (int i = 0; i < localInfo.totalChunks; i++)
        {
            if (localInfo.chunkBuffer.TryGetValue(i, out var chunkBytes))
            {
                fileBytes.AddRange(chunkBytes);
            }
            else
            {
                Debug.LogError($"[FileShare] Missing chunk {i} for '{localInfo.fileName}'");
                OnFileError?.Invoke(localInfo.fileId, $"Missing chunk {i}");
                yield break;
            }
        }

        byte[] finalBytes = fileBytes.ToArray();

        // Verify checksum
        string actualChecksum = CalculateMD5(finalBytes);
        if (!string.IsNullOrEmpty(expectedChecksum) && actualChecksum != expectedChecksum)
        {
            Debug.LogError($"[FileShare] Checksum mismatch for '{localInfo.fileName}'");
            OnFileError?.Invoke(localInfo.fileId, "Checksum mismatch");
            yield break;
        }

        // Save file
        string safeName = SanitizeFileName(localInfo.fileName);
        string localPath = Path.Combine(_sharedFilesPath, $"{localInfo.fileId}_{safeName}");

        try
        {
            File.WriteAllBytes(localPath, finalBytes);
            localInfo.localPath = localPath;
            localInfo.isComplete = true;

            // Update available files
            if (_availableFiles.TryGetValue(localInfo.fileId, out var metadata))
            {
                metadata.isComplete = true;
            }

            if (showDebugLogs)
                Debug.Log($"[FileShare] File saved: {localPath}");

            OnFileComplete?.Invoke(localInfo.fileId, localPath);
            NotifyFileListUpdated();
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileShare] Failed to save file: {e.Message}");
            OnFileError?.Invoke(localInfo.fileId, e.Message);
        }

        // Clean up buffer
        localInfo.chunkBuffer.Clear();
    }

    #endregion

    #region File List Management

    void HandleFileListRequest(string dataJson, string senderId)
    {
        if (senderId == VRNetworkManager.LocalId) return;

        var request = JsonUtility.FromJson<FileListRequestData>(dataJson);

        if (VRRoomManager.Instance == null || request.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Send our file list
        var files = new List<FileMetadata>(_availableFiles.Values);

        var response = new FileListResponseData
        {
            roomId = request.roomId,
            targetId = request.requesterId,
            files = files.ToArray()
        };

        VRNetworkManager.Instance.Send("file-list-response", response);

        if (showDebugLogs)
            Debug.Log($"[FileShare] Sent file list to {request.requesterId} ({files.Count} files)");
    }

    void HandleFileListResponse(string dataJson)
    {
        var response = JsonUtility.FromJson<FileListResponseData>(dataJson);

        // Only process if it's for us
        if (response.targetId != VRNetworkManager.LocalId)
            return;

        if (VRRoomManager.Instance == null || response.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        // Merge with our list
        foreach (var file in response.files)
        {
            if (!_availableFiles.ContainsKey(file.fileId))
            {
                _availableFiles[file.fileId] = file;
            }
        }

        if (showDebugLogs)
            Debug.Log($"[FileShare] Received file list: {response.files.Length} files");

        NotifyFileListUpdated();
    }

    void AddToAvailableFiles(FileAnnounceData announce)
    {
        var metadata = new FileMetadata
        {
            fileId = announce.fileId,
            fileName = announce.fileName,
            fileExtension = announce.fileExtension,
            fileSize = announce.fileSize,
            senderId = announce.senderId,
            senderName = announce.senderName,
            isComplete = announce.senderId == VRNetworkManager.LocalId // Our own files are complete
        };

        _availableFiles[announce.fileId] = metadata;
        NotifyFileListUpdated();
    }

    FileMetadata CreateMetadata(LocalFileInfo localInfo)
    {
        return new FileMetadata
        {
            fileId = localInfo.fileId,
            fileName = localInfo.fileName,
            fileExtension = localInfo.fileExtension,
            fileSize = localInfo.fileSize,
            senderId = localInfo.senderId,
            senderName = localInfo.senderName,
            isComplete = localInfo.isComplete
        };
    }

    void NotifyFileListUpdated()
    {
        OnFileListUpdated?.Invoke(new List<FileMetadata>(_availableFiles.Values));
    }

    #endregion

    #region Public API

    /// <summary>
    /// Retourne la liste des fichiers disponibles
    /// </summary>
    public List<FileMetadata> GetAvailableFiles()
    {
        return new List<FileMetadata>(_availableFiles.Values);
    }

    /// <summary>
    /// Retourne le chemin local d'un fichier reçu
    /// </summary>
    public string GetLocalPath(string fileId)
    {
        if (_receivingFiles.TryGetValue(fileId, out var localInfo) && localInfo.isComplete)
        {
            return localInfo.localPath;
        }
        return null;
    }

    /// <summary>
    /// Vérifie si un fichier est complètement reçu
    /// </summary>
    public bool IsFileComplete(string fileId)
    {
        if (_receivingFiles.TryGetValue(fileId, out var localInfo))
        {
            return localInfo.isComplete;
        }
        // Check if it's our own file
        if (_availableFiles.TryGetValue(fileId, out var metadata))
        {
            return metadata.isComplete;
        }
        return false;
    }

    /// <summary>
    /// Retourne la progression d'un transfert (0-1)
    /// </summary>
    public float GetProgress(string fileId)
    {
        if (_receivingFiles.TryGetValue(fileId, out var localInfo))
        {
            return localInfo.progress;
        }
        return 0f;
    }

    /// <summary>
    /// Ouvre le dossier de fichiers partagés
    /// </summary>
    public void OpenSharedFilesFolder()
    {
        Application.OpenURL("file://" + _sharedFilesPath);
    }

    #endregion

    #region Utilities

    string CalculateMD5(byte[] data)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName;
    }

    #endregion
}
