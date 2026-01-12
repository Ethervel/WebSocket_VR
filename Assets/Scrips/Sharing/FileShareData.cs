using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Classes de données pour le partage de fichiers.
/// Toutes les classes sont sérialisables pour JsonUtility.
/// </summary>

// ============================================
// FILE SHARING
// ============================================

/// <summary>
/// Annonce d'un nouveau fichier partagé
/// </summary>
[Serializable]
public class FileAnnounceData
{
    public string fileId;           // UUID unique du fichier
    public string roomId;           // Room où le fichier est partagé
    public string fileName;         // Nom du fichier (ex: "presentation.pdf")
    public string fileExtension;    // Extension (ex: ".pdf")
    public long fileSize;           // Taille en bytes
    public int totalChunks;         // Nombre total de chunks
    public string senderId;         // ID du peer qui partage
    public string senderName;       // Nom du peer qui partage
}

/// <summary>
/// Chunk de données d'un fichier
/// </summary>
[Serializable]
public class FileChunkData
{
    public string fileId;           // ID du fichier
    public string roomId;           // Room
    public int chunkIndex;          // Index du chunk (0-based)
    public string data;             // Données en Base64
}

/// <summary>
/// Confirmation que le fichier est complet
/// </summary>
[Serializable]
public class FileCompleteData
{
    public string fileId;           // ID du fichier
    public string roomId;           // Room
    public string checksum;         // MD5 pour vérification d'intégrité
}

/// <summary>
/// Demande de re-envoi d'un chunk ou fichier complet
/// </summary>
[Serializable]
public class FileRequestData
{
    public string fileId;           // ID du fichier demandé
    public string roomId;           // Room
    public string requesterId;      // Qui demande
    public int chunkIndex;          // -1 pour tout le fichier, sinon index du chunk
}

/// <summary>
/// Demande de la liste des fichiers disponibles (pour late-joiner)
/// </summary>
[Serializable]
public class FileListRequestData
{
    public string roomId;
    public string requesterId;
}

/// <summary>
/// Réponse avec la liste des fichiers disponibles
/// </summary>
[Serializable]
public class FileListResponseData
{
    public string roomId;
    public string targetId;         // Destinataire de la réponse
    public FileMetadata[] files;    // Liste des fichiers
}

/// <summary>
/// Métadonnées d'un fichier (version légère pour la liste)
/// </summary>
[Serializable]
public class FileMetadata
{
    public string fileId;
    public string fileName;
    public string fileExtension;
    public long fileSize;
    public string senderId;
    public string senderName;
    public bool isComplete;         // true si le fichier est entièrement reçu
}

// ============================================
// SCREEN SHARING
// ============================================

/// <summary>
/// Notification de début/fin de partage d'écran
/// </summary>
[Serializable]
public class ScreenShareData
{
    public string roomId;
    public string sharerId;         // Qui partage
    public string sharerName;       // Nom du présentateur
    public bool isSharing;          // true = start, false = stop
    public int width;               // Résolution
    public int height;
}

// ============================================
// LOCAL FILE INFO (non-network)
// ============================================

/// <summary>
/// Informations locales sur un fichier en cours de réception
/// </summary>
[Serializable]
public class LocalFileInfo
{
    public string fileId;
    public string fileName;
    public string fileExtension;
    public long fileSize;
    public int totalChunks;
    public int receivedChunks;
    public string senderId;
    public string senderName;
    public string localPath;        // Chemin local une fois sauvegardé
    public bool isComplete;
    public float progress;          // 0-1

    // Buffer pour les chunks reçus (non sérialisé)
    [NonSerialized]
    public Dictionary<int, byte[]> chunkBuffer;

    public LocalFileInfo()
    {
        chunkBuffer = new Dictionary<int, byte[]>();
    }

    public void UpdateProgress()
    {
        if (totalChunks > 0)
        {
            progress = (float)receivedChunks / totalChunks;
        }
    }
}

// ============================================
// ENUMS
// ============================================

/// <summary>
/// État d'un transfert de fichier
/// </summary>
public enum FileTransferState
{
    Pending,        // En attente
    Transferring,   // En cours de transfert
    Complete,       // Terminé avec succès
    Failed,         // Échec
    Cancelled       // Annulé
}

/// <summary>
/// Types de fichiers supportés pour le viewer intégré
/// </summary>
public enum FileViewerType
{
    Unknown,        // Ouvrir avec app externe
    Image,          // Viewer image intégré
    Text,           // Viewer texte intégré
    PDF,            // Ouvrir avec app externe (pas de viewer PDF intégré)
    Document        // Ouvrir avec app externe
}

/// <summary>
/// Helper pour déterminer le type de viewer
/// </summary>
public static class FileTypeHelper
{
    public static FileViewerType GetViewerType(string extension)
    {
        switch (extension.ToLower())
        {
            case ".png":
            case ".jpg":
            case ".jpeg":
            case ".gif":
            case ".bmp":
                return FileViewerType.Image;

            case ".txt":
            case ".log":
            case ".md":
            case ".json":
            case ".xml":
            case ".csv":
                return FileViewerType.Text;

            case ".pdf":
                return FileViewerType.PDF;

            case ".doc":
            case ".docx":
            case ".xls":
            case ".xlsx":
            case ".ppt":
            case ".pptx":
                return FileViewerType.Document;

            default:
                return FileViewerType.Unknown;
        }
    }

    public static string GetFileIcon(string extension)
    {
        var type = GetViewerType(extension);
        switch (type)
        {
            case FileViewerType.Image: return "image";
            case FileViewerType.Text: return "text";
            case FileViewerType.PDF: return "pdf";
            case FileViewerType.Document: return "document";
            default: return "file";
        }
    }
}
