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
            }
        }

        ApplyColor(currentColor);
    }

    void Update()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        if (Mouse.current == null) return;

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
}
