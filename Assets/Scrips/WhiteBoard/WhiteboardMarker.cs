using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.InputSystem;

/// <summary>
/// Feutre VR pour dessiner sur les surfaces de dessin (WhiteboardDrawingSurface).
/// Fonctionne en VR (grab) et Desktop (clic gauche).
/// </summary>
public class WhiteboardMarker : MonoBehaviour
{
    [Header("Configuration")]
    public Transform tip;
    public int penSize = 10;
    public Color currentColor = Color.blue;
    public LayerMask drawingSurfaceLayer;

    [Header("Touch Detection")]
    [Tooltip("Distance maximale pour considérer que le stylo touche la surface (en mètres)")]
    public float touchThreshold = 0.15f;

    [Header("Network Settings")]
    [Tooltip("Intervalle d'envoi réseau (plus petit = plus fluide mais plus de bande passante)")]
    public float sendRate = 0.033f;
    [Tooltip("Nombre minimum de points avant envoi")]
    public int minPointsBeforeSend = 1;

    // Components
    private Renderer _renderer;
    private Color[] _colors;
    private float _tipHeight;

    // Current drawing state
    private WhiteboardDrawingSurface _currentSurface;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private RaycastHit _touch;

    // Network batching
    private float _networkTimer;
    private List<float> _pendingPointsFlat = new List<float>();
    private string _currentSurfaceId;
    private Vector2 _lastSentPoint = Vector2.zero;
    private bool _hasLastSentPoint = false;
    private bool _isNewStroke = true; // Premier trait après levée du stylo

    // P1 FIX: Deferred Apply() to batch all SetPixels in a single Apply() per frame
    private bool _textureDirty = false;

    // VR grab state
    private XRGrabInteractable _grabInteractable;
    private bool _isHeld = false;

    // Desktop mode
    private bool _isDesktopMode = false;
    private Camera _mainCamera;

    void Start()
    {
        if (tip == null)
        {
            Debug.LogError("[WhiteboardMarker] Tip non assigné!");
            enabled = false;
            return;
        }

        _renderer = tip.GetComponent<Renderer>();
        _grabInteractable = GetComponent<XRGrabInteractable>();

        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.AddListener(_ => OnGrabbed());
            _grabInteractable.selectExited.AddListener(_ => OnReleased());
        }
        else
        {
            _isHeld = true;
        }

        _tipHeight = tip.localScale.y;
        ApplyColor(currentColor);

        _isDesktopMode = VRGameManager.Instance == null || VRGameManager.Instance.IsDesktopMode;
        _mainCamera = Camera.main;
    }

    void OnGrabbed()
    {
        _isHeld = true;
    }

    void OnReleased()
    {
        _isHeld = false;
        _touchedLastFrame = false;

        if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
        {
            SendBatchToNetwork();
        }

        _currentSurface = null;
        _currentSurfaceId = null;
    }

    void Update()
    {
        if (_isHeld)
        {
            DrawVR();
            NetworkUpdate();
        }
        else if (_isDesktopMode && Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            DrawDesktop();
            NetworkUpdate();
        }
        else if (_isDesktopMode && _touchedLastFrame)
        {
            if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
            {
                SendBatchToNetwork();
            }
            _touchedLastFrame = false;
        }
    }

    // P1 FIX: Batch all SetPixels into a single Apply() call per frame
    // This reduces GPU upload overhead from ~30ms to ~1ms during rapid drawing
    void LateUpdate()
    {
        if (_textureDirty && _currentSurface != null && _currentSurface.drawingTexture != null)
        {
            _currentSurface.drawingTexture.Apply();
            _textureDirty = false;
        }
    }

    void DrawVR()
    {
        bool hit = Physics.Raycast(tip.position, transform.up, out _touch, _tipHeight * 2f, drawingSurfaceLayer);

        if (!hit)
        {
            hit = Physics.Raycast(tip.position, transform.forward, out _touch, _tipHeight * 2f, drawingSurfaceLayer);
        }
        if (!hit)
        {
            hit = Physics.Raycast(tip.position, -transform.up, out _touch, _tipHeight * 2f, drawingSurfaceLayer);
        }

        if (!hit)
        {
            EndStroke();
            return;
        }

        if (_touch.distance > touchThreshold * 2f)
        {
            EndStroke();
            return;
        }

        WhiteboardDrawingSurface surface = _touch.transform.GetComponent<WhiteboardDrawingSurface>();
        if (surface == null)
        {
            EndStroke();
            return;
        }

        ProcessDrawing(surface, _touch.textureCoord);
    }

    void DrawDesktop()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
                return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _mainCamera.ScreenPointToRay(mousePos);

        bool hit = Physics.Raycast(ray, out _touch, 100f, drawingSurfaceLayer);

        if (!hit)
        {
            EndStroke();
            return;
        }

        WhiteboardDrawingSurface surface = _touch.transform.GetComponent<WhiteboardDrawingSurface>();
        if (surface == null)
        {
            EndStroke();
            return;
        }

        ProcessDrawing(surface, _touch.textureCoord);
    }

    void ProcessDrawing(WhiteboardDrawingSurface surface, Vector2 uv)
    {
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

        Texture2D targetTexture = surface.drawingTexture;
        if (targetTexture == null)
        {
            Debug.LogError($"[WhiteboardMarker] Surface {surface.id} n'a pas de drawingTexture!");
            return;
        }

        int maxX = (int)surface.textureSize.x - penSize;
        int maxY = (int)surface.textureSize.y - penSize;

        int x = Mathf.Clamp((int)(uv.x * surface.textureSize.x - penSize / 2), 0, maxX);
        int y = Mathf.Clamp((int)(uv.y * surface.textureSize.y - penSize / 2), 0, maxY);

        if (_touchedLastFrame)
        {
            Vector2 start = _lastTouchPos;
            Vector2 end = new Vector2(x, y);
            float dist = Vector2.Distance(start, end);

            int steps = Mathf.Max(1, Mathf.CeilToInt(dist));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int lerpX = Mathf.Clamp((int)Mathf.Lerp(start.x, end.x, t), 0, maxX);
                int lerpY = Mathf.Clamp((int)Mathf.Lerp(start.y, end.y, t), 0, maxY);
                targetTexture.SetPixels(lerpX, lerpY, penSize, penSize, _colors);
            }
        }
        else
        {
            targetTexture.SetPixels(x, y, penSize, penSize, _colors);
        }

        // P1 FIX: Mark texture as dirty instead of calling Apply() immediately
        // Apply() will be called once in LateUpdate() to batch all SetPixels
        _textureDirty = true;

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
        _currentSurfaceId = null;
        _touchedLastFrame = false;
        _lastTouchPos = Vector2.zero;
        _pendingPointsFlat.Clear();
        _hasLastSentPoint = false;
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

        string currentRoomId = VRRoomManager.Instance.CurrentRoomId;

        List<float> pointsToSend = new List<float>();

        // Ne pas inclure le dernier point si c'est un nouveau trait (stylo levé)
        if (_hasLastSentPoint && _pendingPointsFlat.Count >= 2 && !_isNewStroke)
        {
            pointsToSend.Add(_lastSentPoint.x);
            pointsToSend.Add(_lastSentPoint.y);
        }

        pointsToSend.AddRange(_pendingPointsFlat);

        if (_pendingPointsFlat.Count >= 2)
        {
            _lastSentPoint.x = _pendingPointsFlat[_pendingPointsFlat.Count - 2];
            _lastSentPoint.y = _pendingPointsFlat[_pendingPointsFlat.Count - 1];
            _hasLastSentPoint = true;
        }

        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentSurfaceId,
            roomId = currentRoomId,
            r = currentColor.r,
            g = currentColor.g,
            b = currentColor.b,
            a = currentColor.a,
            penSize = penSize,
            isNewStroke = _isNewStroke, // Indique si c'est un nouveau trait
            pointsFlat = pointsToSend.ToArray()
        };

        // Après le premier envoi, ce n'est plus un nouveau trait
        _isNewStroke = false;

        WhiteboardBatchData batch = new WhiteboardBatchData
        {
            whiteboardId = _currentSurfaceId,
            roomId = currentRoomId,
            draws = new List<WhiteboardPacket> { packet }
        };

        try
        {
            VRNetworkManager.Instance.Send("whiteboard-batch", batch);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WhiteboardMarker] Erreur envoi: {e.Message}");
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
        if (_renderer != null)
            _renderer.material.color = color;

        Color colorWithAlpha = new Color(color.r, color.g, color.b, 1f);

        int pixelCount = penSize * penSize;
        _colors = new Color[pixelCount];
        for (int i = 0; i < pixelCount; i++)
            _colors[i] = colorWithAlpha;
    }

    void OnDestroy()
    {
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.RemoveAllListeners();
            _grabInteractable.selectExited.RemoveAllListeners();
        }
    }
}
