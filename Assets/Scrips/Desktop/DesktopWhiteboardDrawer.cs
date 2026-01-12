using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Permet de dessiner sur le whiteboard en mode Desktop (style Paint)
/// - Clic gauche maintenu = dessiner
/// - Raycast depuis la position de la souris (pas le centre de l'écran)
/// </summary>
public class DesktopWhiteboardDrawer : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Taille du pinceau")]
    public int penSize = 10;

    [Tooltip("Couleur actuelle")]
    public Color currentColor = Color.black;

    [Tooltip("Layer du whiteboard")]
    public LayerMask whiteboardLayer;

    [Tooltip("Distance max du raycast")]
    public float maxRayDistance = 10f;

    [Header("Network Settings")]
    [Tooltip("Intervalle d'envoi réseau")]
    public float sendRate = 0.05f;

    [Tooltip("Nombre min de points avant envoi")]
    public int minPointsBeforeSend = 3;

    [Header("Visual Feedback")]
    [Tooltip("Afficher un curseur sur le whiteboard")]
    public bool showCursor = true;

    [Tooltip("Prefab du curseur (optionnel)")]
    public GameObject cursorPrefab;

    private Camera _camera;
    private Whiteboard _currentWhiteboard;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private Color[] _colors;

    // Network
    private float _networkTimer;
    private List<float> _pendingPointsFlat = new List<float>();
    private string _currentWhiteboardId;

    // Stats
    private int _totalPointsSent = 0;
    private int _totalBatchesSent = 0;

    // Cursor
    private GameObject _cursorInstance;
    private DesktopPlayerController _playerController;

    // Input System
    private Mouse _mouse;

    void Start()
    {
        _camera = GetComponent<Camera>();
        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_camera == null)
        {
            Debug.LogError("[DesktopWhiteboardDrawer] No camera found!");
            enabled = false;
            return;
        }

        _mouse = Mouse.current;

        ApplyColor(currentColor);

        // Find player controller
        _playerController = GetComponentInParent<DesktopPlayerController>();

        // Create cursor if needed
        if (showCursor && cursorPrefab != null)
        {
            _cursorInstance = Instantiate(cursorPrefab);
            _cursorInstance.SetActive(false);
        }
    }

    void Update()
    {
        // Refresh mouse reference if needed
        if (_mouse == null) _mouse = Mouse.current;
        if (_mouse == null) return;

        // Don't draw while looking around (right mouse held)
        if (_playerController != null && _playerController.IsLooking)
        {
            HideCursor();
            return;
        }

        // Don't draw if pointer is over UI
        if (IsPointerOverUI())
        {
            HideCursor();

            // If we were drawing, send remaining points
            if (_touchedLastFrame && _pendingPointsFlat.Count > 0)
            {
                SendBatchToNetwork();
            }
            _touchedLastFrame = false;
            return;
        }

        // Left mouse button held = draw
        if (_mouse.leftButton.isPressed)
        {
            Draw();
            NetworkUpdate();
        }
        else
        {
            // Mouse released
            if (_touchedLastFrame)
            {
                if (_pendingPointsFlat.Count > 0)
                {
                    SendBatchToNetwork();
                }
            }

            _touchedLastFrame = false;
            _currentWhiteboard = null;
            HideCursor();
        }
    }

    bool IsPointerOverUI()
    {
        // Check if mouse is over any UI element
        if (EventSystem.current == null) return false;
        return EventSystem.current.IsPointerOverGameObject();
    }

    void Draw()
    {
        // Raycast from mouse position (style Paint)
        Vector2 mousePos = _mouse.position.ReadValue();
        Ray ray = _camera.ScreenPointToRay(mousePos);
        RaycastHit hit;

        bool didHit = Physics.Raycast(ray, out hit, maxRayDistance, whiteboardLayer);

        if (!didHit)
        {
            if (_touchedLastFrame)
            {
                if (_pendingPointsFlat.Count > 0)
                {
                    SendBatchToNetwork();
                }
            }

            _currentWhiteboard = null;
            _touchedLastFrame = false;
            HideCursor();
            return;
        }

        // Show cursor at hit point
        ShowCursor(hit.point, hit.normal);

        Whiteboard wb = hit.transform.GetComponent<Whiteboard>();
        if (wb == null)
        {
            _touchedLastFrame = false;
            return;
        }

        // Changed whiteboard
        if (_currentWhiteboard != wb)
        {
            if (_pendingPointsFlat.Count > 0 && _currentWhiteboard != null)
            {
                SendBatchToNetwork();
            }

            _currentWhiteboard = wb;
            _currentWhiteboardId = wb.id;
            _pendingPointsFlat.Clear();
        }

        Vector2 uv = hit.textureCoord;

        int maxX = (int)wb.textureSize.x - penSize;
        int maxY = (int)wb.textureSize.y - penSize;

        int x = Mathf.Clamp((int)(uv.x * wb.textureSize.x - penSize / 2), 0, maxX);
        int y = Mathf.Clamp((int)(uv.y * wb.textureSize.y - penSize / 2), 0, maxY);

        // Local drawing
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
                wb.texture.SetPixels(lerpX, lerpY, penSize, penSize, _colors);
            }
            wb.texture.Apply();
        }
        else
        {
            wb.texture.SetPixels(x, y, penSize, penSize, _colors);
            wb.texture.Apply();
        }

        // Add to network buffer
        _pendingPointsFlat.Add(uv.x);
        _pendingPointsFlat.Add(uv.y);

        _lastTouchPos = new Vector2(x, y);
        _touchedLastFrame = true;
    }

    void NetworkUpdate()
    {
        if (_currentWhiteboard == null)
            return;

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
        if (_pendingPointsFlat.Count == 0)
            return;

        if (string.IsNullOrEmpty(_currentWhiteboardId))
        {
            _pendingPointsFlat.Clear();
            return;
        }

        if (!VRNetworkManager.IsConnected)
            return;

        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            return;

        string currentRoomId = VRRoomManager.Instance.CurrentRoomId;

        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentWhiteboardId,
            roomId = currentRoomId,
            r = currentColor.r,
            g = currentColor.g,
            b = currentColor.b,
            a = currentColor.a,
            penSize = penSize,
            pointsFlat = _pendingPointsFlat.ToArray()
        };

        WhiteboardBatchData batch = new WhiteboardBatchData
        {
            whiteboardId = _currentWhiteboardId,
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
            Debug.LogError($"[DesktopWhiteboardDrawer] Send error: {e.Message}");
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
        int pixelCount = penSize * penSize;
        _colors = new Color[pixelCount];
        for (int i = 0; i < pixelCount; i++)
            _colors[i] = color;
    }

    void ShowCursor(Vector3 position, Vector3 normal)
    {
        if (_cursorInstance != null)
        {
            _cursorInstance.SetActive(true);
            _cursorInstance.transform.position = position + normal * 0.01f;
            _cursorInstance.transform.rotation = Quaternion.LookRotation(-normal);
        }
    }

    void HideCursor()
    {
        if (_cursorInstance != null)
        {
            _cursorInstance.SetActive(false);
        }
    }

    void OnDestroy()
    {
        if (_cursorInstance != null)
        {
            Destroy(_cursorInstance);
        }
    }
}
