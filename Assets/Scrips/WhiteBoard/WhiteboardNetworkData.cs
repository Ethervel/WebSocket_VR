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
/// FIX: Utilise pointsFlat au lieu de points pour meilleure sérialisation JSON
/// </summary>
[Serializable]
public class WhiteboardPacket
{
    public string whiteboardId;
    public string roomId; // NOUVEAU: ID de la room
    public float r, g, b, a;
    public int penSize;

    // Indique si c'est un nouveau trait (stylo levé puis reposé)
    // Si true, ne pas interpoler depuis le dernier point du batch précédent
    public bool isNewStroke;

    // NOUVEAU FORMAT: Liste plate de floats [u1, v1, u2, v2, u3, v3, ...]
    public float[] pointsFlat;
    
    // ANCIEN FORMAT (gardé pour compatibilité)
    [Obsolete("Utilisez pointsFlat à la place")]
    public List<float[]> points;
}

/// <summary>
/// Batch de plusieurs traits (optimisation réseau)
/// </summary>
[Serializable]
public class WhiteboardBatchData
{
    public string whiteboardId;
    public string roomId; // NOUVEAU: ID de la room
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
    public string roomId; // ID de la room pour filtrer les clears
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
    public string roomId; // ID de la room pour filtrer les requêtes
    public string requesterId;
}

/// <summary>
/// État complet du tableau (texture PNG en base64)
/// </summary>
[Serializable]
public class WhiteboardStateData
{
    public string whiteboardId;
    public string roomId; // ID de la room pour filtrer les états
    public string textureData; // PNG encodé en base64
    public int width;
    public int height;
}

/// <summary>
/// Historique incrémental (alternative à la texture complète)
/// P1 FIX: Utilisé pour sync rapide des late joiners au lieu de PNG
/// </summary>
[Serializable]
public class WhiteboardHistoryData
{
    public string whiteboardId;
    public string roomId; // P1 FIX: ID de la room pour filtrer
    public List<WhiteboardPacket> packets;
}