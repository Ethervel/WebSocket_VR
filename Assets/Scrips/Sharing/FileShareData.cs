using System;
using System.Collections.Generic;

/// <summary>
/// Network data classes for File Sharing feature.
/// Compatible with JsonUtility (no nested objects).
/// All messages include roomId for room-scoped filtering.
/// </summary>

// Metadata for a shared file
[Serializable]
public class FileMetadata
{
    public string fileId;           // Unique identifier (GUID)
    public string roomId;           // Room scope
    public string fileName;         // Original filename with extension
    public string fileExtension;    // Extension only (pdf, docx, etc.)
    public string mimeType;         // MIME type for proper handling
    public long fileSize;           // Size in bytes
    public string sharerId;         // Who shared it
    public string sharerName;       // Display name of sharer
    public long sharedTimestamp;    // Unix timestamp when shared
}

// Upload a new file to the room
[Serializable]
public class FileShareUploadData
{
    public string roomId;
    public string fileId;
    public string fileName;
    public string fileExtension;
    public string mimeType;
    public long fileSize;
    public string sharerId;
    public string sharerName;
    public string fileDataBase64;   // Base64 encoded file content
    public long timestamp;
}

// Request the list of shared files (late joiner)
[Serializable]
public class FileListRequestData
{
    public string roomId;
    public string requesterId;
}

// Response with current file list (metadata only, no content)
// Note: JsonUtility doesn't support List<T> directly in serialization,
// so we use a wrapper approach for the response
[Serializable]
public class FileListResponseData
{
    public string roomId;
    public string targetPlayerId;   // Who requested (for direct response)
    public string filesJson;        // JSON array of FileMetadata (workaround for nested objects)
}

// Helper class for serializing file metadata list
[Serializable]
public class FileMetadataList
{
    public List<FileMetadata> files;

    public FileMetadataList()
    {
        files = new List<FileMetadata>();
    }

    public FileMetadataList(List<FileMetadata> fileList)
    {
        files = fileList;
    }
}

// Request to download a specific file
[Serializable]
public class FileDownloadRequestData
{
    public string roomId;
    public string fileId;
    public string requesterId;
}

// Response with file content
[Serializable]
public class FileDownloadResponseData
{
    public string roomId;
    public string fileId;
    public string targetPlayerId;   // Who requested
    public string fileName;
    public string fileDataBase64;   // Base64 encoded content
}

// Notification when a file is removed (optional, for explicit removal)
[Serializable]
public class FileRemovedData
{
    public string roomId;
    public string fileId;
    public string removerId;
}
