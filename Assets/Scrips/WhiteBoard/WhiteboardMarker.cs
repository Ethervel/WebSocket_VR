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
    public float touchThreshold = 0.0001f;

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
    private bool _isNewStroke = true; // Premier trait après levée du stylo

    // P1 FIX: Deferred Apply() to batch all SetPixels in a single Apply() per frame
    private bool _textureDirty = false;

    // VR grab state
    private XRGrabInteractable _grabInteractable;
    private bool _isHeld = false;

    // Desktop mode - NOTE: Use IsDesktopMode property for dynamic check
    private Camera _mainCamera;

    /// <summary>
    /// Dynamic desktop mode check - always reflects current state
    /// </summary>
    private bool IsDesktopMode =>
        DesktopWhiteboardDrawer.IsActive ||
        (VRGameManager.Instance != null && VRGameManager.Instance.IsDesktopMode);

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
            // P2 FIX: Use named methods for events to allow proper unsubscription
            SubscribeToGrabEvents();
        }
        else
        {
            _isHeld = true;
        }

        _tipHeight = tip.localScale.y;
        ApplyColor(currentColor);

        // Desktop mode is now checked dynamically via IsDesktopMode property
        _mainCamera = Camera.main;

        Debug.Log($"[WhiteboardMarker] Start: IsDesktopMode={IsDesktopMode}, DesktopDrawerIsActive={DesktopWhiteboardDrawer.IsActive}");
    }

    // P2 FIX: Track subscription state to prevent duplicate listeners
    private bool _isSubscribedToGrab = false;

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

    // P2 FIX: Named event handlers for proper subscription management
    void OnGrabSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args) => OnGrabbed();
    void OnGrabSelectExited(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args) => OnReleased();

    void OnEnable()
    {
        if (_grabInteractable != null)
            SubscribeToGrabEvents();
    }

    void OnDisable()
    {
        UnsubscribeFromGrabEvents();
    }

    void OnGrabbed()
    {
        _isHeld = true;
    }

    void OnReleased()
    {
        _isHeld = false;
        _touchedLastFrame = false;

        // CRITICAL FIX: Don't send in desktop mode - just clear
        // SendBatchToNetwork() now has its own guard, but be explicit here too
        if (_pendingPointsFlat.Count > 0 && !string.IsNullOrEmpty(_currentSurfaceId))
        {
            if (IsDesktopMode)
            {
                Debug.Log($"[WhiteboardMarker] OnReleased: discarding {_pendingPointsFlat.Count / 2} points (desktop mode)");
                _pendingPointsFlat.Clear(); // Just discard
            }
            else
            {
                SendBatchToNetwork();
            }
        }

        _currentSurface = null;
        _currentSurfaceId = null;
    }

    void Update()
    {
        // COMPREHENSIVE GUARD: WhiteboardMarker is ONLY for VR mode.
        // In desktop mode, DesktopWhiteboardDrawer handles ALL drawing.
        // Using a dynamic property ensures we always have the correct state.
        if (IsDesktopMode)
        {
            // CRITICAL FIX: Just CLEAR pending data without sending - DO NOT flush!
            // Flushing would send with WhiteboardMarker's default blue color,
            // causing blue dots on remote players.
            if (_pendingPointsFlat.Count > 0)
            {
                Debug.Log($"[WhiteboardMarker] GUARD: discarding {_pendingPointsFlat.Count / 2} pending points (desktop mode)");
                _pendingPointsFlat.Clear();
            }
            _touchedLastFrame = false;
            _currentSurface = null;
            _currentSurfaceId = null;
            return;
        }

        if (_isHeld)
        {
            DrawVR();
            NetworkUpdate();
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
        bool hit = Physics.Raycast(tip.position, transform.up, out _touch, touchThreshold, drawingSurfaceLayer);

        if (!hit)
        {
            hit = Physics.Raycast(tip.position, transform.forward, out _touch, touchThreshold, drawingSurfaceLayer);
        }
        if (!hit)
        {
            hit = Physics.Raycast(tip.position, -transform.up, out _touch, touchThreshold, drawingSurfaceLayer);
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
            // FIX: Use _currentSurfaceId for consistency (SendBatchToNetwork has desktop mode guard)
            if (_pendingPointsFlat.Count > 0 && !string.IsNullOrEmpty(_currentSurfaceId))
            {
                SendBatchToNetwork(); // Will be blocked if in desktop mode
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
        // FIX: Use _currentSurfaceId for consistency (SendBatchToNetwork has desktop mode guard)
        if (_pendingPointsFlat.Count > 0 && !string.IsNullOrEmpty(_currentSurfaceId))
        {
            SendBatchToNetwork(); // Will be blocked if in desktop mode
        }

        _currentSurface = null;
        _currentSurfaceId = null;
        _touchedLastFrame = false;
        _lastTouchPos = Vector2.zero;
        _pendingPointsFlat.Clear();
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
        // CRITICAL FIX: NEVER send from WhiteboardMarker in desktop mode
        // This prevents duplicate batches and blue dots from appearing on remote players
        // (DesktopWhiteboardDrawer handles ALL drawing in desktop mode)
        if (IsDesktopMode)
        {
            Debug.Log($"[WhiteboardMarker] SendBatchToNetwork: BLOCKED - desktop mode active, discarding {_pendingPointsFlat.Count / 2} points");
            _pendingPointsFlat.Clear();
            return;
        }

        if (_pendingPointsFlat.Count == 0) return;
        if (string.IsNullOrEmpty(_currentSurfaceId)) return;
        if (!VRNetworkManager.IsConnected) return;
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        string currentRoomId = VRRoomManager.Instance.CurrentRoomId;

        // FIX: Send points directly without overlap point prepending.
        // The receiver already handles cross-batch continuity via _lastReceivedPoint gap-fill.
        // The previous overlap point mechanism was redundant and could contribute to doubled strokes
        // when combined with the receiver's own gap-fill interpolation.
        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentSurfaceId,
            roomId = currentRoomId,
            r = currentColor.r,
            g = currentColor.g,
            b = currentColor.b,
            a = currentColor.a,
            penSize = penSize,
            isNewStroke = _isNewStroke,
            pointsFlat = _pendingPointsFlat.ToArray()
        };

        Debug.Log($"[WhiteboardMarker] SEND batch: surface={_currentSurfaceId}, points={_pendingPointsFlat.Count / 2}, isNewStroke={_isNewStroke}, sender={VRNetworkManager.LocalId}");

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
        // FIX: Flush pending points with the CURRENT color BEFORE changing
        // Without this, pending points drawn in RED would be sent as BLUE (the new color)
        // causing "blue dots following the pen" on remote players
        if (_pendingPointsFlat.Count > 0 && !string.IsNullOrEmpty(_currentSurfaceId))
        {
            Debug.Log($"[WhiteboardMarker] SetColor: flushing {_pendingPointsFlat.Count / 2} pending points with current RGBA=({currentColor.r:F2},{currentColor.g:F2},{currentColor.b:F2},{currentColor.a:F2}) before changing to RGBA=({newColor.r:F2},{newColor.g:F2},{newColor.b:F2},{newColor.a:F2})");
            SendBatchToNetwork(); // Will be blocked if in desktop mode
        }

        Debug.Log($"[WhiteboardMarker] SetColor: changing from RGBA=({currentColor.r:F2},{currentColor.g:F2},{currentColor.b:F2},{currentColor.a:F2}) to RGBA=({newColor.r:F2},{newColor.g:F2},{newColor.b:F2},{newColor.a:F2}), IsDesktopMode={IsDesktopMode}");
        currentColor = newColor;
        ApplyColor(newColor);
    }

    // P2 FIX: Cache last penSize to avoid reallocation if only color changes
    private int _lastPenSize = -1;

    void ApplyColor(Color color)
    {
        if (_renderer != null)
            _renderer.material.color = color;

        Color colorWithAlpha = new Color(color.r, color.g, color.b, 1f);

        int pixelCount = penSize * penSize;

        // P2 FIX: Only reallocate array if penSize changed
        if (_colors == null || _lastPenSize != penSize)
        {
            _colors = new Color[pixelCount];
            _lastPenSize = penSize;
        }

        // Fill with new color (always needed even if array reused)
        for (int i = 0; i < pixelCount; i++)
            _colors[i] = colorWithAlpha;
    }

    void OnDestroy()
    {
        // P2 FIX: Use proper unsubscription instead of RemoveAllListeners
        // (RemoveAllListeners removes ALL listeners, not just ours)
        UnsubscribeFromGrabEvents();
    }
}
