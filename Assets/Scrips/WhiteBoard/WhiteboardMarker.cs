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
    public float touchThreshold = 0.15f; // 15cm - plus permissif pour VR

    [Header("Network Settings")]
    [Tooltip("Intervalle d'envoi réseau (plus petit = plus fluide mais plus de bande passante)")]
    public float sendRate = 0.033f; // ~30fps pour fluidité
    [Tooltip("Nombre minimum de points avant envoi")]
    public int minPointsBeforeSend = 1; // Envoyer plus souvent pour éviter les coupures

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
    private Vector2 _lastSentPoint = Vector2.zero; // Pour continuité entre batches
    private bool _hasLastSentPoint = false;

    // VR grab state
    private XRGrabInteractable _grabInteractable;
    private bool _isHeld = false;

    // Desktop mode
    private bool _isDesktopMode = false;
    private Camera _mainCamera;

    // Stats
    private int _totalPointsSent = 0;
    private int _totalBatchesSent = 0;

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
            // Pas de grab = toujours actif (debug ou Desktop)
            _isHeld = true;
        }

        _tipHeight = tip.localScale.y;
        ApplyColor(currentColor);

        // Check desktop mode
        _isDesktopMode = VRGameManager.Instance == null || VRGameManager.Instance.IsDesktopMode;
        _mainCamera = Camera.main;

        if (_isDesktopMode)
        {
            Debug.Log("[WhiteboardMarker] Desktop mode - clic gauche pour dessiner");
        }
    }

    void OnGrabbed()
    {
        _isHeld = true;
    }

    void OnReleased()
    {
        _isHeld = false;
        _touchedLastFrame = false;

        // Envoyer les points restants
        if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
        {
            SendBatchToNetwork();
        }

        _currentSurface = null;
        _currentSurfaceId = null;
    }

    void Update()
    {
        // VR mode: requires holding the marker
        if (_isHeld)
        {
            DrawVR();
            NetworkUpdate();
        }
        // Desktop mode: draw with left mouse button
        else if (_isDesktopMode && Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            DrawDesktop();
            NetworkUpdate();
        }
        else if (_isDesktopMode && _touchedLastFrame)
        {
            // Mouse released - end stroke
            if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
            {
                SendBatchToNetwork();
            }
            _touchedLastFrame = false;
        }
    }

    void DrawVR()
    {
        // DEBUG: Log toutes les 60 frames pour voir l'état
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[WhiteboardMarker VR] isHeld={_isHeld}, tipHeight={_tipHeight}, layer={drawingSurfaceLayer.value}, threshold={touchThreshold}");
        }

        // Raycast depuis la pointe du marker - essayer plusieurs directions
        bool hit = Physics.Raycast(tip.position, transform.up, out _touch, _tipHeight * 2f, drawingSurfaceLayer);

        // Si pas de hit avec transform.up, essayer transform.forward (selon orientation du marker)
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
            // DEBUG: Essayer sans layer mask pour voir si on touche quelque chose
            if (Time.frameCount % 60 == 0)
            {
                RaycastHit debugHit;
                if (Physics.Raycast(tip.position, transform.up, out debugHit, 1f))
                {
                    Debug.Log($"[WhiteboardMarker VR] No hit on layer, but found '{debugHit.transform.name}' layer={debugHit.transform.gameObject.layer}");
                }
            }
            EndStroke();
            return;
        }

        // DEBUG: On a touché quelque chose
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"[WhiteboardMarker VR] HIT: {_touch.transform.name}, distance={_touch.distance:F3}, threshold={touchThreshold}");
        }

        // Vérifier la distance (plus permissif maintenant)
        if (_touch.distance > touchThreshold * 2f)
        {
            EndStroke();
            return;
        }

        // Chercher WhiteboardDrawingSurface
        WhiteboardDrawingSurface surface = _touch.transform.GetComponent<WhiteboardDrawingSurface>();
        if (surface == null)
        {
            Debug.LogWarning($"[WhiteboardMarker] Hit {_touch.transform.name} n'a pas de WhiteboardDrawingSurface!");
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
            {
                Debug.LogWarning("[WhiteboardMarker] Desktop: No main camera!");
                return;
            }
        }

        // Raycast depuis la caméra à travers la souris
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = _mainCamera.ScreenPointToRay(mousePos);

        // DEBUG: Log every 30 frames
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"[WhiteboardMarker] Desktop raycast: layerMask={drawingSurfaceLayer.value}, camera={_mainCamera.name}");
        }

        bool hit = Physics.Raycast(ray, out _touch, 100f, drawingSurfaceLayer);

        if (!hit)
        {
            // DEBUG: Try without layer mask to see what we're hitting
            if (Time.frameCount % 30 == 0)
            {
                RaycastHit debugHit;
                if (Physics.Raycast(ray, out debugHit, 100f))
                {
                    Debug.Log($"[WhiteboardMarker] No hit on layer {drawingSurfaceLayer.value}, but hit '{debugHit.transform.name}' on layer {debugHit.transform.gameObject.layer}");
                }
                else
                {
                    Debug.Log("[WhiteboardMarker] Raycast misses everything");
                }
            }
            EndStroke();
            return;
        }

        // DEBUG: We hit something!
        Debug.Log($"[WhiteboardMarker] HIT: {_touch.transform.name} at UV({_touch.textureCoord.x:F2}, {_touch.textureCoord.y:F2})");

        // Chercher WhiteboardDrawingSurface
        WhiteboardDrawingSurface surface = _touch.transform.GetComponent<WhiteboardDrawingSurface>();
        if (surface == null)
        {
            Debug.LogWarning($"[WhiteboardMarker] Desktop: Hit {_touch.transform.name} n'a pas de WhiteboardDrawingSurface!");
            EndStroke();
            return;
        }

        ProcessDrawing(surface, _touch.textureCoord);
    }

    void ProcessDrawing(WhiteboardDrawingSurface surface, Vector2 uv)
    {
        // Changement de surface ?
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

            Debug.Log($"[WhiteboardMarker] Switched to surface '{surface.id}'");
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

        // Dessiner localement
        if (_touchedLastFrame)
        {
            // Interpolation pour trait continu
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
            // Premier point du trait
            targetTexture.SetPixels(x, y, penSize, penSize, _colors);
        }

        targetTexture.Apply();

        // Ajouter au buffer réseau
        _pendingPointsFlat.Add(uv.x);
        _pendingPointsFlat.Add(uv.y);

        _lastTouchPos = new Vector2(x, y);
        _touchedLastFrame = true;
    }

    void EndStroke()
    {
        // IMPORTANT: Toujours envoyer les points restants quand on termine un trait
        if (_pendingPointsFlat.Count > 0 && _currentSurface != null)
        {
            SendBatchToNetwork();
        }

        // Reset complet pour le prochain trait
        _currentSurface = null;
        _currentSurfaceId = null;
        _touchedLastFrame = false;
        _lastTouchPos = Vector2.zero;
        _pendingPointsFlat.Clear(); // S'assurer que le buffer est vide
        _hasLastSentPoint = false; // Reset continuité pour nouveau trait
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

        // Créer liste avec continuité: inclure le dernier point envoyé au début
        List<float> pointsToSend = new List<float>();

        // Ajouter le dernier point envoyé pour continuité (interpolation)
        if (_hasLastSentPoint && _pendingPointsFlat.Count >= 2)
        {
            pointsToSend.Add(_lastSentPoint.x);
            pointsToSend.Add(_lastSentPoint.y);
        }

        // Ajouter les nouveaux points
        pointsToSend.AddRange(_pendingPointsFlat);

        // Sauvegarder le dernier point pour le prochain batch
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
            pointsFlat = pointsToSend.ToArray()
        };

        WhiteboardBatchData batch = new WhiteboardBatchData
        {
            whiteboardId = _currentSurfaceId,
            roomId = currentRoomId,
            draws = new List<WhiteboardPacket> { packet }
        };

        try
        {
            VRNetworkManager.Instance.Send("whiteboard-batch", batch);

            int pointCount = _pendingPointsFlat.Count / 2;
            _totalPointsSent += pointCount;
            _totalBatchesSent++;
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

        // Alpha = 1 pour visibilité
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
