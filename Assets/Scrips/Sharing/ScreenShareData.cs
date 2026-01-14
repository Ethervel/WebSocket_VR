using System;

/// <summary>
/// Classes sérialisables pour les messages réseau du Screen Share.
/// Compatible JsonUtility (pas d'objets imbriqués).
/// Chaque message inclut whiteboardId pour supporter plusieurs whiteboards.
/// </summary>

[Serializable]
public class ScreenShareStartData
{
    public string roomId;
    public string whiteboardId;  // ID du whiteboard cible
    public string sharerId;
    public string sharerName;
    public int width;
    public int height;
}

[Serializable]
public class ScreenShareStopData
{
    public string roomId;
    public string whiteboardId;
    public string sharerId;
}

[Serializable]
public class ScreenShareFrameData
{
    public string roomId;
    public string whiteboardId;
    public string sharerId;
    public string imageData;  // Base64 encoded JPEG
    public int frameIndex;
    public long timestamp;
}

[Serializable]
public class ScreenShareRequestData
{
    public string roomId;
    public string whiteboardId;  // Optionnel - si vide, demande l'état de tous les whiteboards
    public string requesterId;
}

[Serializable]
public class ScreenShareStateData
{
    public string roomId;
    public string whiteboardId;
    public bool isSharing;
    public string sharerId;
    public string sharerName;
}
