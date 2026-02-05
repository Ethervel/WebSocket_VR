using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Script de dessin Desktop - à mettre sur le joueur local.
/// Dessine sur WhiteboardDrawingSurface avec clic gauche/molette de la souris.
/// </summary>
public class DesktopWhiteboardDrawer : MonoBehaviour
{
    public enum DrawingMode
    {
        Draw,       // Mode dessin normal
        Cursor,     // Mode curseur - pas de dessin
        Eraser      // Mode gomme - efface ce qu'on touche
    }

    [Header("Configuration")]
    public int penSize = 10;
    public int eraserSize = 40;
    public Color currentColor = Color.blue;
    public LayerMask drawingSurfaceLayer;

    [Header("Mode")]
    public DrawingMode currentMode = DrawingMode.Draw;

    [Header("Network Settings")]
    public float sendRate = 0.05f;
    public int minPointsBeforeSend = 3;

    // State
    private Camera _camera;
    private WhiteboardDrawingSurface _currentSurface;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private RaycastHit _touch;
    private Color _eraserColor = new Color(0, 0, 0, 0); // Transparent pour effacer

    // Network batching
    private float _networkTimer;
    private List<float> _pendingPointsFlat = new List<float>();
    private string _currentSurfaceId;
    private bool _isNewStroke = true; // Premier trait après levée du clic

    // Drawing colors
    private Color[] _colors;
    private Color[] _eraserColors;

    // P2 FIX: Deferred Apply() to batch all SetPixels in a single Apply() per frame
    private bool _textureDirty = false;

    // Static flag so WhiteboardMarker knows not to send duplicate network messages
    public static bool IsActive { get; private set; }

    // Events pour notifier l'UI du changement de mode
    public static event System.Action<DrawingMode> OnModeChanged;

    void Start()
    {
        _camera = GetComponentInChildren<Camera>();
        if (_camera == null)
        {
            _camera = Camera.main;
        }

        // Auto-detect layer if not set
        if (drawingSurfaceLayer.value == 0)
        {
            int layer = LayerMask.NameToLayer("Whiteboard");
            if (layer >= 0)
            {
                drawingSurfaceLayer = 1 << layer;
            }
        }

        ApplyColor(currentColor);
        ApplyEraserColor();

        IsActive = true;
    }

    void OnEnable()
    {
        IsActive = true;
    }

    void OnDisable()
    {
        IsActive = false;
    }

    void OnDestroy()
    {
        IsActive = false;
    }

    void Update()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        if (Mouse.current == null) return;

        // En mode Cursor, ne pas dessiner
        if (currentMode == DrawingMode.Cursor)
        {
            // Terminer le stroke si on était en train de dessiner
            if (_touchedLastFrame)
            {
                EndStroke();
            }
            return;
        }

        var mouse = Mouse.current;
        bool middlePressed = mouse.middleButton.isPressed;
        bool leftPressed = mouse.leftButton.isPressed || middlePressed;

        if (leftPressed)
        {
            Draw();
            NetworkUpdate();
        }
        else if (_touchedLastFrame)
        {
            EndStroke();
        }
    }

    // P2 FIX: Batch all SetPixels into a single Apply() call per frame
    // This reduces GPU upload overhead from ~30ms to ~1ms during rapid drawing
    void LateUpdate()
    {
        if (_textureDirty && _currentSurface != null && _currentSurface.drawingTexture != null)
        {
            _currentSurface.drawingTexture.Apply();
            _textureDirty = false;
        }
    }

    void Draw()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(mousePos);

        bool hit = Physics.Raycast(ray, out _touch, 100f, drawingSurfaceLayer);

        if (!hit)
        {
            if (_touchedLastFrame)
            {
                EndStroke();
            }
            return;
        }

        // Get WhiteboardDrawingSurface
        WhiteboardDrawingSurface surface = _touch.transform.GetComponent<WhiteboardDrawingSurface>();
        if (surface == null)
        {
            if (_touchedLastFrame) EndStroke();
            return;
        }

        // Surface change?
        if (_currentSurface != surface)
        {
            if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
            {
                SendBatchToNetwork();
            }

            _currentSurface = surface;
            _currentSurfaceId = surface.id;
            _pendingPointsFlat.Clear();
            _touchedLastFrame = false;
        }

        Vector2 uv = _touch.textureCoord;
        Texture2D tex = surface.drawingTexture;

        if (tex == null) return;

        // Utiliser la taille appropriée selon le mode
        int currentSize = (currentMode == DrawingMode.Eraser) ? eraserSize : penSize;
        Color[] colorsToUse = (currentMode == DrawingMode.Eraser) ? _eraserColors : _colors;

        int maxX = (int)surface.textureSize.x - currentSize;
        int maxY = (int)surface.textureSize.y - currentSize;

        int x = Mathf.Clamp((int)(uv.x * surface.textureSize.x - currentSize / 2), 0, maxX);
        int y = Mathf.Clamp((int)(uv.y * surface.textureSize.y - currentSize / 2), 0, maxY);

        // Draw locally
        if (_touchedLastFrame)
        {
            // Interpolate for smooth line
            Vector2 start = _lastTouchPos;
            Vector2 end = new Vector2(x, y);
            float dist = Vector2.Distance(start, end);

            int steps = Mathf.Max(1, Mathf.CeilToInt(dist));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int lerpX = Mathf.Clamp((int)Mathf.Lerp(start.x, end.x, t), 0, maxX);
                int lerpY = Mathf.Clamp((int)Mathf.Lerp(start.y, end.y, t), 0, maxY);
                tex.SetPixels(lerpX, lerpY, currentSize, currentSize, colorsToUse);
            }
        }
        else
        {
            tex.SetPixels(x, y, currentSize, currentSize, colorsToUse);
        }

        // P2 FIX: Mark texture as dirty instead of calling Apply() immediately
        // Apply() will be called once in LateUpdate() to batch all SetPixels
        _textureDirty = true;

        // Buffer for network
        _pendingPointsFlat.Add(uv.x);
        _pendingPointsFlat.Add(uv.y);

        _lastTouchPos = new Vector2(x, y);
        _touchedLastFrame = true;
    }

    void EndStroke()
    {
        if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
        {
            SendBatchToNetwork();
        }
        _currentSurface = null;
        _touchedLastFrame = false;
        _isNewStroke = true; // Prochain dessin sera un nouveau trait
    }

    void NetworkUpdate()
    {
        if (_currentSurface == null) return;

        _networkTimer += Time.deltaTime;

        int pointCount = _pendingPointsFlat.Count / 2;
        if (_networkTimer >= sendRate && pointCount >= minPointsBeforeSend)
        {
            SendBatchToNetwork();
            _networkTimer = 0f;
        }
    }

    void SendBatchToNetwork()
    {
        if (_pendingPointsFlat.Count == 0) return;
        if (string.IsNullOrEmpty(_currentSurfaceId)) return;
        if (!VRNetworkManager.IsConnected) return;
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        string roomId = VRRoomManager.Instance.CurrentRoomId;

        // Utiliser la couleur et la taille appropriées selon le mode
        bool isErasing = (currentMode == DrawingMode.Eraser);
        Color colorToSend = isErasing ? _eraserColor : currentColor;
        int sizeToSend = isErasing ? eraserSize : penSize;

        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentSurfaceId,
            roomId = roomId,
            r = colorToSend.r,
            g = colorToSend.g,
            b = colorToSend.b,
            a = colorToSend.a,
            penSize = sizeToSend,
            isNewStroke = _isNewStroke, // Indique si c'est un nouveau trait
            pointsFlat = _pendingPointsFlat.ToArray()
        };

        // Après le premier envoi, ce n'est plus un nouveau trait
        _isNewStroke = false;

        WhiteboardBatchData batch = new WhiteboardBatchData
        {
            whiteboardId = _currentSurfaceId,
            roomId = roomId,
            draws = new List<WhiteboardPacket> { packet }
        };

        Debug.Log($"[DesktopDrawer] SEND batch: surface={_currentSurfaceId}, points={_pendingPointsFlat.Count / 2}, isNewStroke={packet.isNewStroke}, mode={currentMode}, RGBA=({colorToSend.r:F2},{colorToSend.g:F2},{colorToSend.b:F2},{colorToSend.a:F2}), penSize={sizeToSend}, sender={VRNetworkManager.LocalId}");

        try
        {
            VRNetworkManager.Instance.Send("whiteboard-batch", batch);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DesktopDrawer] Send error: {e.Message}");
        }

        _pendingPointsFlat.Clear();
    }

    public void SetColor(Color newColor)
    {
        currentColor = newColor;
        ApplyColor(newColor);
    }

    // P2 FIX: Cache last penSize to avoid reallocation if only color changes
    private int _lastPenSize = -1;

    void ApplyColor(Color color)
    {
        Color c = new Color(color.r, color.g, color.b, 1f);
        int count = penSize * penSize;

        // P2 FIX: Only reallocate array if penSize changed
        if (_colors == null || _lastPenSize != penSize)
        {
            _colors = new Color[count];
            _lastPenSize = penSize;
        }

        // Fill with new color (always needed even if array reused)
        for (int i = 0; i < count; i++)
            _colors[i] = c;
    }

    private int _lastEraserSize = -1;

    void ApplyEraserColor()
    {
        int count = eraserSize * eraserSize;

        // Only reallocate if size changed
        if (_eraserColors == null || _lastEraserSize != eraserSize)
        {
            _eraserColors = new Color[count];
            _lastEraserSize = eraserSize;
        }

        // Fill with transparent color
        for (int i = 0; i < count; i++)
            _eraserColors[i] = _eraserColor;
    }

    #region Mode Methods

    /// <summary>
    /// Définit le mode de dessin
    /// </summary>
    public void SetMode(DrawingMode mode)
    {
        Debug.Log($"[DesktopDrawer] SetMode appelé: {mode} (actuel: {currentMode})");

        if (currentMode == mode)
        {
            Debug.Log($"[DesktopDrawer] Mode déjà actif, pas de changement");
            return;
        }

        // FIX: Flush pending points with the CURRENT mode's color/size BEFORE switching.
        // Without this, pending erase points would be sent with blue (draw) color
        // when switching Eraser→Cursor, causing blue dots on receivers.
        if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
        {
            Debug.Log($"[DesktopDrawer] SetMode: flushing {_pendingPointsFlat.Count / 2} pending points in mode={currentMode} before switching to {mode}");
            SendBatchToNetwork();
        }
        // Reset draw state so Update()'s cursor-mode EndStroke() doesn't re-flush
        _touchedLastFrame = false;
        _currentSurface = null;
        _isNewStroke = true; // New stroke boundary when switching modes

        currentMode = mode;
        Debug.Log($"[DesktopDrawer] Mode changé: {currentMode}");
        OnModeChanged?.Invoke(mode);
    }

    /// <summary>
    /// Active le mode curseur (pas de dessin)
    /// </summary>
    public void SetCursorMode()
    {
        SetMode(DrawingMode.Cursor);
    }

    /// <summary>
    /// Active le mode dessin
    /// </summary>
    public void SetDrawMode()
    {
        SetMode(DrawingMode.Draw);
    }

    /// <summary>
    /// Active le mode gomme
    /// </summary>
    public void SetEraserMode()
    {
        SetMode(DrawingMode.Eraser);
        // S'assurer que la taille de la gomme est initialisée
        ApplyEraserColor();
    }

    /// <summary>
    /// Retourne le mode actuel
    /// </summary>
    public DrawingMode GetCurrentMode()
    {
        return currentMode;
    }

    #endregion
}
