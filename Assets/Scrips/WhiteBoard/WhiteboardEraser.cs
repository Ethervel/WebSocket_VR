using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Effaceur VR pour effacer sur les surfaces de dessin (WhiteboardDrawingSurface).
/// Similaire au WhiteboardMarker mais efface au lieu de dessiner.
/// </summary>
public class WhiteboardEraser : MonoBehaviour
{
    [Header("Configuration")]
    public Transform eraserTip;
    public int eraserSizeX = 80;
    public int eraserSizeY = 40;
    public LayerMask drawingSurfaceLayer;

    [Header("Touch Detection")]
    [Tooltip("Distance maximale pour considérer que l'effaceur touche la surface (en mètres)")]
    public float touchThreshold = 0.15f;

    [Header("Network Settings")]
    public float sendRate = 0.033f;
    public int minPointsBeforeSend = 1;

    // Couleur transparente pour effacer
    private Color _eraserColor = new Color(0, 0, 0, 0);
    private Color[] _colors;

    // Current erasing state
    private WhiteboardDrawingSurface _currentSurface;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private RaycastHit _touch;
    private float _tipHeight;

    // Network batching
    private float _networkTimer;
    private List<float> _pendingPointsFlat = new List<float>();
    private string _currentSurfaceId;
    private Vector2 _lastSentPoint = Vector2.zero;
    private bool _hasLastSentPoint = false;
    private bool _isNewStroke = true;

    // Deferred Apply()
    private bool _textureDirty = false;

    // VR grab state
    private XRGrabInteractable _grabInteractable;
    private bool _isHeld = false;
    private bool _isSubscribedToGrab = false;

    /// <summary>
    /// Dynamic desktop mode check - always reflects current state
    /// </summary>
    private bool IsDesktopMode =>
        DesktopWhiteboardDrawer.IsActive ||
        (VRGameManager.Instance != null && VRGameManager.Instance.IsDesktopMode);

    void Start()
    {
        if (eraserTip == null)
        {
            // Essayer de trouver automatiquement
            eraserTip = transform.Find("Tip");
            if (eraserTip == null)
            {
                eraserTip = transform; // Utiliser ce transform si pas de tip
            }
        }

        _grabInteractable = GetComponent<XRGrabInteractable>();

        if (_grabInteractable != null)
        {
            SubscribeToGrabEvents();
        }
        else
        {
            _isHeld = true; // Si pas de grab interactable, toujours actif
        }

        _tipHeight = eraserTip.localScale.y;
        ApplyEraserColor();

        // Desktop mode is now checked dynamically via IsDesktopMode property
        Debug.Log($"[WhiteboardEraser] Start: IsDesktopMode={IsDesktopMode}, DesktopDrawerIsActive={DesktopWhiteboardDrawer.IsActive}");
    }

    void SubscribeToGrabEvents()
    {
        if (_grabInteractable == null || _isSubscribedToGrab) return;
        _grabInteractable.selectEntered.AddListener(OnGrabSelectEntered);
        _grabInteractable.selectExited.AddListener(OnGrabSelectExited);
        _isSubscribedToGrab = true;
    }

    void UnsubscribeFromGrabEvents()
    {
        if (_grabInteractable == null || !_isSubscribedToGrab) return;
        _grabInteractable.selectEntered.RemoveListener(OnGrabSelectEntered);
        _grabInteractable.selectExited.RemoveListener(OnGrabSelectExited);
        _isSubscribedToGrab = false;
    }

    void OnGrabSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        _isHeld = true;
        Debug.Log("[WhiteboardEraser] Attrapé");
    }

    void OnGrabSelectExited(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        _isHeld = false;
        _touchedLastFrame = false;

        // CRITICAL FIX: Don't send in desktop mode
        if (_pendingPointsFlat.Count > 0 && !string.IsNullOrEmpty(_currentSurfaceId))
        {
            if (IsDesktopMode)
            {
                Debug.Log($"[WhiteboardEraser] OnGrabSelectExited: discarding {_pendingPointsFlat.Count / 2} points (desktop mode)");
                _pendingPointsFlat.Clear();
            }
            else
            {
                SendBatchToNetwork();
            }
        }

        _currentSurface = null;
        _currentSurfaceId = null;
        Debug.Log("[WhiteboardEraser] Relâché");
    }

    void OnEnable()
    {
        if (_grabInteractable != null)
            SubscribeToGrabEvents();
    }

    void OnDisable()
    {
        UnsubscribeFromGrabEvents();
    }

    void Update()
    {
        // COMPREHENSIVE GUARD: WhiteboardEraser is ONLY for VR mode.
        // In desktop mode, DesktopWhiteboardDrawer handles ALL erasing.
        // Using a dynamic property ensures we always have the correct state.
        if (IsDesktopMode)
        {
            // CRITICAL FIX: Just CLEAR pending data without sending
            if (_pendingPointsFlat.Count > 0)
            {
                Debug.Log($"[WhiteboardEraser] GUARD: discarding {_pendingPointsFlat.Count / 2} pending points (desktop mode)");
                _pendingPointsFlat.Clear();
            }
            _touchedLastFrame = false;
            _currentSurface = null;
            _currentSurfaceId = null;
            return;
        }

        if (_isHeld)
        {
            Erase();
            NetworkUpdate();
        }
    }

    void LateUpdate()
    {
        if (_textureDirty && _currentSurface != null && _currentSurface.drawingTexture != null)
        {
            _currentSurface.drawingTexture.Apply();
            _textureDirty = false;
        }
    }

    void Erase()
    {
        // Raycast depuis le centre de l'effaceur pour centrer l'effacement
        Vector3 center = eraserTip.position;
        float rayDist = _tipHeight * 2f;

        bool hit = Physics.Raycast(center, eraserTip.up, out _touch, rayDist, drawingSurfaceLayer);

        if (!hit)
        {
            hit = Physics.Raycast(center, eraserTip.forward, out _touch, rayDist, drawingSurfaceLayer);
        }
        if (!hit)
        {
            hit = Physics.Raycast(center, -eraserTip.up, out _touch, rayDist, drawingSurfaceLayer);
        }
        if (!hit)
        {
            hit = Physics.Raycast(center, -eraserTip.forward, out _touch, rayDist, drawingSurfaceLayer);
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

        ProcessErasing(surface, _touch.textureCoord);
    }

    void ProcessErasing(WhiteboardDrawingSurface surface, Vector2 uv)
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
            Debug.LogError($"[WhiteboardEraser] Surface {surface.id} n'a pas de drawingTexture!");
            return;
        }

        int maxX = (int)surface.textureSize.x - eraserSizeX;
        int maxY = (int)surface.textureSize.y - eraserSizeY;

        int x = Mathf.Clamp((int)(uv.x * surface.textureSize.x - eraserSizeX / 2), 0, maxX);
        int y = Mathf.Clamp((int)(uv.y * surface.textureSize.y - eraserSizeY / 2), 0, maxY);

        if (_touchedLastFrame)
        {
            Vector2 start = _lastTouchPos;
            Vector2 end = new Vector2(x, y);
            float dist = Vector2.Distance(start, end);

            int minSize = Mathf.Min(eraserSizeX, eraserSizeY);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (minSize * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int lerpX = Mathf.Clamp((int)Mathf.Lerp(start.x, end.x, t), 0, maxX);
                int lerpY = Mathf.Clamp((int)Mathf.Lerp(start.y, end.y, t), 0, maxY);
                targetTexture.SetPixels(lerpX, lerpY, eraserSizeX, eraserSizeY, _colors);
            }
        }
        else
        {
            targetTexture.SetPixels(x, y, eraserSizeX, eraserSizeY, _colors);
        }

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
        _isNewStroke = true;
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
        // CRITICAL FIX: Never send in desktop mode
        if (IsDesktopMode)
        {
            Debug.Log($"[WhiteboardEraser] SendBatchToNetwork: BLOCKED - desktop mode active, discarding {_pendingPointsFlat.Count / 2} points");
            _pendingPointsFlat.Clear();
            return;
        }

        if (_pendingPointsFlat.Count == 0) return;
        if (string.IsNullOrEmpty(_currentSurfaceId)) return;
        if (!VRNetworkManager.IsConnected) return;
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        string currentRoomId = VRRoomManager.Instance.CurrentRoomId;

        List<float> pointsToSend = new List<float>();

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

        // Envoyer avec couleur transparente pour effacer
        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentSurfaceId,
            roomId = currentRoomId,
            r = _eraserColor.r,
            g = _eraserColor.g,
            b = _eraserColor.b,
            a = _eraserColor.a,
            penSize = eraserSizeX,
            penSizeY = eraserSizeY,
            isNewStroke = _isNewStroke,
            pointsFlat = pointsToSend.ToArray()
        };

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
            Debug.LogError($"[WhiteboardEraser] Erreur envoi: {e.Message}");
        }

        _pendingPointsFlat.Clear();
    }

    private int _lastEraserSizeX = -1;
    private int _lastEraserSizeY = -1;

    void ApplyEraserColor()
    {
        int pixelCount = eraserSizeX * eraserSizeY;

        if (_colors == null || _lastEraserSizeX != eraserSizeX || _lastEraserSizeY != eraserSizeY)
        {
            _colors = new Color[pixelCount];
            _lastEraserSizeX = eraserSizeX;
            _lastEraserSizeY = eraserSizeY;
        }

        for (int i = 0; i < pixelCount; i++)
            _colors[i] = _eraserColor;
    }

    void OnDestroy()
    {
        UnsubscribeFromGrabEvents();
    }
}
