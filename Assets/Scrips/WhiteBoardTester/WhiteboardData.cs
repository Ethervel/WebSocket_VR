using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DrawPoint
{
    public float x; // UV x (0-1)
    public float y; // UV y (0-1)
}

[Serializable]
public class WhiteboardBatchData
{
    public string whiteboardId;
    public string senderId;
    public int penSize;
    public float r, g, b, a; // Couleur
    public List<DrawPoint> points;
}

[Serializable]
public class WhiteboardStateData
{
    public string whiteboardId;
    public byte[] textureData; // PNG compressé
}

[Serializable]
public class WhiteboardRequestData
{
    public string whiteboardId;
}