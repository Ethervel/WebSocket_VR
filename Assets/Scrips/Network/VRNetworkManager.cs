using System;
using UnityEngine;
using NativeWebSocket;

// Main network manager for VR / WebGL
// Handles WebSocket connection, reconnection, and message dispatching

public class VRNetworkManager : MonoBehaviour
{
    // Singleton instance (one network manager for the whole app)
    public static VRNetworkManager Instance { get; private set; }

    
}