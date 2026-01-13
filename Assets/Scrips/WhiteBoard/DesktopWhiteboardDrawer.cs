using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Script de dessin Desktop - à mettre sur le joueur local.
/// Dessine sur WhiteboardDrawingSurface avec clic gauche/molette de la souris.
/// </summary>
public class DesktopWhiteboardDrawer : MonoBehaviour
{
    [Header("Configuration")]
    public int penSize = 10;
    public Color currentColor = Color.blue;
    public LayerMask drawingSurfaceLayer;

    [Header("Network Settings")]
    public float sendRate = 0.05f;
    public int minPointsBeforeSend = 3;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // State
    private Camera _camera;
    private WhiteboardDrawingSurface _currentSurface;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private RaycastHit _touch;

    // Network batching
    private float _networkTimer;
    private List<float> _pendingPointsFlat = new List<float>();
    private string _currentSurfaceId;

    // Drawing colors
    private Color[] _colors;

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
                Log($"Auto-detected layer: Whiteboard ({layer}), mask={drawingSurfaceLayer.value}");
            }
            else
            {
                Log("WARNING: Layer 'Whiteboard' not found!");
            }
        }

        ApplyColor(currentColor);
        Log($"Initialized - Camera: {_camera?.name}, LayerMask: {drawingSurfaceLayer.value}");
    }

    private float _debugTimer = 0f;
    private bool _loggedOnce = false;

    private int _frameCount = 0;

    void Update()
    {
        _frameCount++;

        // Log absolutely FIRST thing every 60 frames
        if (_frameCount % 60 == 0)
        {
            bool gameFocused = UnityEngine.Application.isFocused;
            Debug.Log($"[DD] Update F{_frameCount} cam={_camera != null} mouse={Mouse.current != null} focused={gameFocused}");
        }

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                if (_frameCount % 60 == 0) Debug.Log("[DD] No camera!");
                return;
            }
        }

        // Check for input - using new Input System only
        if (Mouse.current == null)
        {
            if (_frameCount % 60 == 0) Debug.Log("[DD] Mouse.current is NULL!");
            return;
        }

        // Check ALL mouse buttons - try MULTIPLE methods
        var mouse = Mouse.current;

        // Method 1: isPressed (bool)
        bool leftPressed1 = mouse.leftButton.isPressed;
        bool rightPressed1 = mouse.rightButton.isPressed;

        // Method 2: ReadValue (float)
        float leftVal = mouse.leftButton.ReadValue();
        float rightVal = mouse.rightButton.ReadValue();

        // Method 3: wasPressedThisFrame
        bool leftJust = mouse.leftButton.wasPressedThisFrame;

        // Debug every 30 frames with ALL info
        if (_frameCount % 30 == 0)
        {
            Vector2 pos = mouse.position.ReadValue();
            Debug.Log($"[DD] F{_frameCount} isP(L={leftPressed1},R={rightPressed1}) Val(L={leftVal:F1},R={rightVal:F1}) just={leftJust} pos=({pos.x:F0},{pos.y:F0})");
        }

        // Also check middle button as UI doesn't consume it
        bool middlePressed1 = mouse.middleButton.isPressed;
        float middleVal = mouse.middleButton.ReadValue();

        // Log when ANY detection method succeeds
        if (leftPressed1 || rightPressed1 || middlePressed1 || leftVal > 0.5f || rightVal > 0.5f || middleVal > 0.5f || leftJust)
        {
            Debug.Log($"[DD] BUTTON DETECTED! L=({leftPressed1},{leftVal:F0}) R=({rightPressed1},{rightVal:F0}) M=({middlePressed1},{middleVal:F0}) just={leftJust}");
        }

        // Try MIDDLE mouse button for drawing (UI doesn't consume it)
        bool drawButton = middlePressed1 || middleVal > 0.5f;
        // Also accept left if it ever works
        bool leftPressed = leftPressed1 || leftVal > 0.5f || drawButton;
        if (leftPressed)
        {
            Draw();
            NetworkUpdate();
        }
        else if (_touchedLastFrame)
        {
            // Mouse released - end stroke
            EndStroke();
        }
    }

    void Draw()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(mousePos);

        // DEBUG: Log every click attempt
        if (showDebugLogs && !_touchedLastFrame)
        {
            Log($"CLICK at screen({mousePos.x:F0},{mousePos.y:F0}) ray origin={ray.origin} dir={ray.direction}");
        }

        bool hit = Physics.Raycast(ray, out _touch, 100f, drawingSurfaceLayer);

        if (!hit)
        {
            // Debug: what are we hitting without layer filter?
            if (showDebugLogs && Time.frameCount % 10 == 0)
            {
                RaycastHit debugHit;
                if (Physics.Raycast(ray, out debugHit, 100f))
                {
                    Log($"MISS layer {drawingSurfaceLayer.value}, but hit '{debugHit.transform.name}' (layer {debugHit.transform.gameObject.layer})");
                }
                else
                {
                    Log($"MISS - raycast hit NOTHING at all");
                }
            }

            if (_touchedLastFrame)
            {
                EndStroke();
            }
            return;
        }

        // DEBUG: Log successful hit
        if (showDebugLogs && !_touchedLastFrame)
        {
            Log($"HIT '{_touch.transform.name}' at UV({_touch.textureCoord.x:F3},{_touch.textureCoord.y:F3})");
        }

        // Get WhiteboardDrawingSurface
        WhiteboardDrawingSurface surface = _touch.transform.GetComponent<WhiteboardDrawingSurface>();
        if (surface == null)
        {
            Log($"Hit '{_touch.transform.name}' but no WhiteboardDrawingSurface component!");
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

            Log($"Drawing on surface '{surface.id}'");
        }

        Vector2 uv = _touch.textureCoord;
        Texture2D tex = surface.drawingTexture;

        if (tex == null)
        {
            Log($"Surface {surface.id} has no drawingTexture!");
            return;
        }

        int maxX = (int)surface.textureSize.x - penSize;
        int maxY = (int)surface.textureSize.y - penSize;

        int x = Mathf.Clamp((int)(uv.x * surface.textureSize.x - penSize / 2), 0, maxX);
        int y = Mathf.Clamp((int)(uv.y * surface.textureSize.y - penSize / 2), 0, maxY);

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
                tex.SetPixels(lerpX, lerpY, penSize, penSize, _colors);
            }
        }
        else
        {
            tex.SetPixels(x, y, penSize, penSize, _colors);
        }

        tex.Apply();

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

        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentSurfaceId,
            roomId = roomId,
            r = currentColor.r,
            g = currentColor.g,
            b = currentColor.b,
            a = currentColor.a,
            penSize = penSize,
            pointsFlat = _pendingPointsFlat.ToArray()
        };

        WhiteboardBatchData batch = new WhiteboardBatchData
        {
            whiteboardId = _currentSurfaceId,
            roomId = roomId,
            draws = new List<WhiteboardPacket> { packet }
        };

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

    void ApplyColor(Color color)
    {
        Color c = new Color(color.r, color.g, color.b, 1f);
        int count = penSize * penSize;
        _colors = new Color[count];
        for (int i = 0; i < count; i++)
            _colors[i] = c;
    }

    void Log(string msg)
    {
        if (showDebugLogs)
            Debug.Log($"[DesktopDrawer] {msg}");
    }
}
