using System;

[Serializable]
public class LaserPointerData
{
    public string roomId;
    public bool isActive;

    // Origin point (hand or camera position)
    public float originX, originY, originZ;

    // Hit point (where laser ends)
    public float hitX, hitY, hitZ;

    // Laser color
    public float colorR, colorG, colorB;
}
