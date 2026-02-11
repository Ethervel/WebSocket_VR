using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Structures de donnees pour le systeme d'enregistrement.
/// </summary>

[Serializable]
public class RecordingMetadata
{
    public string recordingId;
    public string roomId;
    public string roomType;
    public string hostId;
    public string hostName;
    public string startTimeUtc;
    public string endTimeUtc;
    public float durationSeconds;
    public int width;
    public int height;
    public int frameRate;
    public List<string> participants;
    public List<RecordingMarker> markers;
}

[Serializable]
public class RecordingMarker
{
    public float timestamp;      // Secondes depuis le debut
    public string markerType;    // Important, Question, Todo, Idea
    public string userId;
    public string userName;
    public string note;          // Note optionnelle
}

[Serializable]
public class RecordingSettings
{
    public int width = 1920;
    public int height = 1080;
    public int frameRate = 30;
    public int audioBitRate = 128000;
    public int videoBitRate = 5000000;
    public bool captureAudio = true;
    public string outputFolder = "Recordings";
}

[Serializable]
public class RecordingStatus
{
    public bool isRecording;
    public string hostId;
    public string startTimeUtc;
    public float elapsedSeconds;
}

/// <summary>
/// Messages reseau pour l'enregistrement.
/// </summary>
[Serializable]
public class RecordingStartMessage
{
    public string roomId;
    public string hostId;
    public string startTimeUtc;
}

[Serializable]
public class RecordingStopMessage
{
    public string roomId;
    public string hostId;
    public float durationSeconds;
}

[Serializable]
public class RecordingStatusMessage
{
    public bool isRecording;
    public string hostId;
    public string hostName;
    public string startTimeUtc;
}

[Serializable]
public class RecordingMarkerMessage
{
    public string roomId;
    public float timestamp;
    public string markerType;
    public string userId;
    public string userName;
    public string note;
}

/// <summary>
/// Types de marqueurs disponibles.
/// </summary>
public enum MarkerType
{
    Important,
    Question,
    Todo,
    Idea
}

/// <summary>
/// Etat de l'enregistrement.
/// </summary>
public enum RecordingState
{
    Idle,
    Starting,
    Recording,
    Stopping,
    Saving,
    Error
}
