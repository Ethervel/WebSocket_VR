using System;
using System.Collections.Generic;

/// <summary>
/// TOUTES les structures de données réseau pour le Whiteboard
/// Ce fichier doit être le SEUL à définir ces classes
/// </summary>

// ========================================
// DESSIN
// ========================================

/// <summary>
/// Représente un seul trait de dessin
/// </summary>
[Serializable]
public class WhiteboardPacket
{
    public string whiteboardId;
    public float r, g, b, a;
    public int penSize;
    public List<float[]> points; // Liste de [uvX, uvY]
}

/// <summary>
/// Batch de plusieurs traits (optimisation réseau)
/// </summary>
[Serializable]
public class WhiteboardBatchData
{
    public string whiteboardId;
    public List<WhiteboardPacket> draws;
}

// ========================================
// CLEAR
// ========================================

/// <summary>
/// Message pour effacer un tableau
/// </summary>
[Serializable]
public class WhiteboardClearData
{
    public string whiteboardId;
    public string senderId;
}

// ========================================
// SYNCHRONISATION (Nouveaux joueurs)
// ========================================

/// <summary>
/// Demande d'état actuel du tableau
/// </summary>
[Serializable]
public class WhiteboardRequestData
{
    public string whiteboardId;
    public string requesterId;
}

/// <summary>
/// État complet du tableau (texture PNG en base64)
/// </summary>
[Serializable]
public class WhiteboardStateData
{
    public string whiteboardId;
    public string textureData; // PNG encodé en base64
    public int width;
    public int height;
}

/// <summary>
/// Historique incrémental (alternative à la texture complète)
/// </summary>
[Serializable]
public class WhiteboardHistoryData
{
    public string whiteboardId;
    public List<WhiteboardPacket> packets;
}