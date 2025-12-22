using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gestionnaire global des whiteboards collaboratifs VR
/// Synchronise les dessins entre tous les joueurs en temps réel
/// </summary>
public class VRWhiteboardManager : MonoBehaviour
{
    public static VRWhiteboardManager Instance { get; private set; }
    
    [Header("Whiteboard Settings")]
    [Tooltip("Activer les whiteboards")]
    public bool whiteboardsEnabled = true;
    
    [Tooltip("Taille par défaut du pinceau")]
    [Range(0.001f, 0.05f)]
    public float defaultBrushSize = 0.01f;
    
    [Tooltip("Couleur par défaut")]
    public Color defaultBrushColor = Color.blue;
    
    [Tooltip("Résolution de la texture du whiteboard")]
    public int textureResolution = 2048;
    
    [Tooltip("Points par seconde à synchroniser")]
    [Range(5, 60)]
    public int strokeSyncRate = 20;
    
    [Header("Available Tools")]
    [Tooltip("Couleurs disponibles")]
    public Color[] availableColors = new Color[]
    {
        Color.black,
        Color.red,
        Color.blue,
        Color.green,
        Color.yellow,
        Color.magenta,
        Color.cyan,
        Color.white
    };
    
    [Tooltip("Tailles de pinceau disponibles")]
    public float[] availableBrushSizes = new float[]
    {
        0.005f,  // Fin
        0.01f,   // Normal
        0.02f,   // Épais
        0.03f    // Très épais
    };
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // État actuel des outils
    private Color _currentBrushColor;
    private float _currentBrushSize;
    private bool _isErasing = false;
    
    // Whiteboards enregistrés
    private Dictionary<string, WhiteboardSurface> _whiteboards = new Dictionary<string, WhiteboardSurface>();
    
    // Buffer pour le stroke en cours
    private List<WhiteboardStrokePoint> _currentStroke = new List<WhiteboardStrokePoint>();
    private string _currentWhiteboardId = null;
    private float _lastSyncTime = 0f;
    
    // Events
    public static event Action<Color> OnBrushColorChanged;
    public static event Action<float> OnBrushSizeChanged;
    public static event Action<bool> OnEraserToggled;
    public static event Action<string> OnWhiteboardCleared;
    
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    void Start()
    {
        _currentBrushColor = defaultBrushColor;
        _currentBrushSize = defaultBrushSize;
        
        LogDebug("[Whiteboard] Manager initialized");
    }
    
    void OnEnable()
    {
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
    }
    
    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
    }
    
    #region Whiteboard Registration
    
    public void RegisterWhiteboard(string whiteboardId, WhiteboardSurface surface)
    {
        if (!_whiteboards.ContainsKey(whiteboardId))
        {
            _whiteboards[whiteboardId] = surface;
            LogDebug($"[Whiteboard] Registered: {whiteboardId}");
        }
    }
    
    public void UnregisterWhiteboard(string whiteboardId)
    {
        if (_whiteboards.Remove(whiteboardId))
        {
            LogDebug($"[Whiteboard] Unregistered: {whiteboardId}");
        }
    }
    
    public WhiteboardSurface GetWhiteboard(string whiteboardId)
    {
        _whiteboards.TryGetValue(whiteboardId, out WhiteboardSurface surface);
        return surface;
    }
    
    public Dictionary<string, WhiteboardSurface> GetAllWhiteboards()
    {
        return new Dictionary<string, WhiteboardSurface>(_whiteboards);
    }
    
    #endregion
    
    #region Drawing Tools
    
    public void SetBrushColor(Color color)
    {
        _currentBrushColor = color;
        _isErasing = false;
        OnBrushColorChanged?.Invoke(color);
        LogDebug($"[Whiteboard] Brush color: {color}");
    }
    
    public void SetBrushSize(float size)
    {
        _currentBrushSize = Mathf.Clamp(size, 0.001f, 0.1f);
        OnBrushSizeChanged?.Invoke(_currentBrushSize);
        LogDebug($"[Whiteboard] Brush size: {_currentBrushSize:F3}");
    }
    
    public void SetEraser(bool enabled)
    {
        _isErasing = enabled;
        OnEraserToggled?.Invoke(enabled);
        LogDebug($"[Whiteboard] Eraser: {enabled}");
    }
    
    public void ToggleEraser()
    {
        SetEraser(!_isErasing);
    }
    
    public void NextColor()
    {
        int currentIndex = Array.IndexOf(availableColors, _currentBrushColor);
        int nextIndex = (currentIndex + 1) % availableColors.Length;
        SetBrushColor(availableColors[nextIndex]);
    }
    
    public void NextBrushSize()
    {
        int currentIndex = Array.FindIndex(availableBrushSizes, s => Mathf.Approximately(s, _currentBrushSize));
        int nextIndex = (currentIndex + 1) % availableBrushSizes.Length;
        SetBrushSize(availableBrushSizes[nextIndex]);
    }
    
    public Color CurrentBrushColor => _currentBrushColor;
    public float CurrentBrushSize => _currentBrushSize;
    public bool IsErasing => _isErasing;
    
    #endregion
    
    #region Drawing Actions
    
    public void BeginStroke(string whiteboardId, Vector2 uv)
    {
        if (!whiteboardsEnabled) return;
        
        _currentStroke.Clear();
        _currentWhiteboardId = whiteboardId;
        _lastSyncTime = Time.time;
        
        AddStrokePoint(whiteboardId, uv);
        
        LogDebug($"[Whiteboard] Begin stroke on {whiteboardId}");
    }
    
    public void AddStrokePoint(string whiteboardId, Vector2 uv)
    {
        if (!whiteboardsEnabled) return;
        if (_currentWhiteboardId != whiteboardId) return;
        
        var point = new WhiteboardStrokePoint
        {
            uv = uv,
            color = _isErasing ? Color.white : _currentBrushColor,
            size = _currentBrushSize,
            isEraser = _isErasing
        };
        
        _currentStroke.Add(point);
        
        // Dessiner localement immédiatement
        var whiteboard = GetWhiteboard(whiteboardId);
        if (whiteboard != null)
        {
            whiteboard.DrawPoint(point);
        }
        
        // Synchroniser périodiquement
        float syncInterval = 1f / strokeSyncRate;
        if (Time.time - _lastSyncTime >= syncInterval)
        {
            SyncCurrentStroke();
            _lastSyncTime = Time.time;
        }
    }
    
    public void EndStroke()
    {
        if (_currentWhiteboardId == null) return;
        
        // Envoyer les derniers points
        if (_currentStroke.Count > 0)
        {
            SyncCurrentStroke();
        }
        
        LogDebug($"[Whiteboard] End stroke on {_currentWhiteboardId} ({_currentStroke.Count} total points)");
        
        _currentStroke.Clear();
        _currentWhiteboardId = null;
    }
    
    public void ClearWhiteboard(string whiteboardId)
    {
        var whiteboard = GetWhiteboard(whiteboardId);
        if (whiteboard != null)
        {
            whiteboard.Clear();
            SendClearCommand(whiteboardId);
            OnWhiteboardCleared?.Invoke(whiteboardId);
            
            LogDebug($"[Whiteboard] Cleared: {whiteboardId}");
        }
    }
    
    public void ClearAllWhiteboards()
    {
        foreach (var kvp in _whiteboards)
        {
            kvp.Value?.Clear();
        }
        LogDebug("[Whiteboard] Cleared all whiteboards");
    }
    
    #endregion
    
    #region Network Synchronization
    
    void SyncCurrentStroke()
    {
        if (_currentStroke.Count == 0) return;
        if (VRNetworkManager.Instance == null) return;
        if (!VRNetworkManager.IsConnected) return;
        
        var data = new WhiteboardStrokeData
        {
            whiteboardId = _currentWhiteboardId,
            roomId = VRRoomManager.Instance?.CurrentRoomId,
            points = _currentStroke.ToArray()
        };
        
        VRNetworkManager.Instance.Send("whiteboard-stroke", data);
        
        // Nettoyer le buffer après envoi
        _currentStroke.Clear();
    }
    
    void SendClearCommand(string whiteboardId)
    {
        if (VRNetworkManager.Instance == null) return;
        if (!VRNetworkManager.IsConnected) return;
        
        var data = new WhiteboardClearData
        {
            whiteboardId = whiteboardId,
            roomId = VRRoomManager.Instance?.CurrentRoomId
        };
        
        VRNetworkManager.Instance.Send("whiteboard-clear", data);
    }
    
    void HandleNetworkMessage(NetworkMessage msg)
    {
        switch (msg.type)
        {
            case "whiteboard-stroke":
                HandleStrokeReceived(msg);
                break;
                
            case "whiteboard-clear":
                HandleClearReceived(msg);
                break;
        }
    }
    
    void HandleStrokeReceived(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<WhiteboardStrokeData>(msg.data);
            
            // Vérifier la room
            if (VRRoomManager.Instance == null || 
                data.roomId != VRRoomManager.Instance.CurrentRoomId)
            {
                return;
            }
            
            // Ignorer nos propres messages
            if (msg.senderId == VRNetworkManager.LocalId)
            {
                return;
            }
            
            // Dessiner les points
            var whiteboard = GetWhiteboard(data.whiteboardId);
            if (whiteboard != null)
            {
                foreach (var point in data.points)
                {
                    whiteboard.DrawPoint(point);
                }
                
                LogDebug($"[Whiteboard] Received {data.points.Length} points from {msg.senderId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Whiteboard] Error handling stroke: {e.Message}");
        }
    }
    
    void HandleClearReceived(NetworkMessage msg)
    {
        try
        {
            var data = JsonUtility.FromJson<WhiteboardClearData>(msg.data);
            
            if (VRRoomManager.Instance == null || 
                data.roomId != VRRoomManager.Instance.CurrentRoomId)
            {
                return;
            }
            
            if (msg.senderId == VRNetworkManager.LocalId)
            {
                return;
            }
            
            var whiteboard = GetWhiteboard(data.whiteboardId);
            if (whiteboard != null)
            {
                whiteboard.Clear();
                OnWhiteboardCleared?.Invoke(data.whiteboardId);
                
                LogDebug($"[Whiteboard] Cleared by {msg.senderId}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Whiteboard] Error handling clear: {e.Message}");
        }
    }
    
    #endregion
    
    #region Room Events
    
    void OnRoomLeft()
    {
        // Effacer tous les whiteboards en quittant la room
        ClearAllWhiteboards();
        LogDebug("[Whiteboard] Left room, cleared all boards");
    }
    
    #endregion
    
    #region Debug
    
    void LogDebug(string message)
    {
        if (showDebugInfo)
            Debug.Log(message);
    }
    
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, Screen.height - 210, 300, 200));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("=== Whiteboard Debug ===");
        GUILayout.Label($"Enabled: {whiteboardsEnabled}");
        GUILayout.Label($"Whiteboards: {_whiteboards.Count}");
        GUILayout.Label($"Color: {_currentBrushColor}");
        GUILayout.Label($"Size: {_currentBrushSize:F3}");
        GUILayout.Label($"Erasing: {_isErasing}");
        GUILayout.Label($"Current Stroke: {_currentStroke.Count} points");
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    #endregion
}

#region Data Classes

[Serializable]
public class WhiteboardStrokePoint
{
    public Vector2 uv;
    public Color color;
    public float size;
    public bool isEraser;
}

[Serializable]
public class WhiteboardStrokeData
{
    public string whiteboardId;
    public string roomId;
    public WhiteboardStrokePoint[] points;
}

[Serializable]
public class WhiteboardClearData
{
    public string whiteboardId;
    public string roomId;
}

#endregion