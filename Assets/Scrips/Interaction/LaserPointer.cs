using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Laser pointer for presentations. Attached to local player.
/// VR: Ray from right hand controller, toggled with primary button (A).
/// Desktop: Ray from camera center, toggled with L key.
/// Visible to all players in the room via network sync.
/// </summary>
public class LaserPointer : MonoBehaviour
{
    [Header("Laser Settings")]
    public float maxDistance = 50f;
    public float beamWidth = 0.005f;
    public float dotSize = 0.02f;
    public Color laserColor = Color.red;

    [Header("Network")]
    public float syncRate = 10f; // Updates per second when active

    [Header("Input")]
    public KeyCode desktopToggleKey = KeyCode.L;

    // State
    private bool _isActive = false;
    private bool _isDesktopMode = false;
    private Transform _rayOrigin; // Right hand (VR) or camera (Desktop)
    private LineRenderer _lineRenderer;
    private GameObject _hitDot;
    private MeshRenderer _dotRenderer;
    private float _syncTimer;
    private LayerMask _raycastMask;

    // VR Input (new Input System)
    private InputAction _vrToggleAction;

    // Cached network data
    private readonly LaserPointerData _cachedData = new LaserPointerData();

    public bool IsActive => _isActive;

    void Start()
    {
        _isDesktopMode = VRGameManager.Instance != null && VRGameManager.Instance.IsDesktopMode;
        FindRayOrigin();
        CreateVisuals();
        SetupVRInput();

        // Raycast against everything except UI layer
        _raycastMask = ~LayerMask.GetMask("UI");

        // Start hidden
        SetVisualsActive(false);
    }

    void SetupVRInput()
    {
        if (_isDesktopMode) return;

        // Create InputAction for right controller primary button (A on Quest)
        _vrToggleAction = new InputAction("LaserToggle", InputActionType.Button,
            "<XRController>{RightHand}/primaryButton");
        _vrToggleAction.Enable();
        _vrToggleAction.performed += _ => ToggleLaser();

        Debug.Log("[LaserPointer] VR input action created (A button, right controller)");
    }

    void FindRayOrigin()
    {
        if (_isDesktopMode)
        {
            // Desktop: ray from camera
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) _rayOrigin = cam.transform;
        }
        else
        {
            // VR: ray from right hand controller
            _rayOrigin = FindChildRecursive(transform, "rightcontroller");
            if (_rayOrigin == null)
                _rayOrigin = FindChildRecursive(transform, "righthand");
            if (_rayOrigin == null)
                _rayOrigin = FindChildRecursive(transform, "right");
        }

        if (_rayOrigin == null)
        {
            // Fallback to main camera
            var cam = Camera.main;
            if (cam != null) _rayOrigin = cam.transform;
        }

        Debug.Log($"[LaserPointer] Ray origin: {(_rayOrigin != null ? _rayOrigin.name : "NULL")}, mode: {(_isDesktopMode ? "Desktop" : "VR")}");
    }

    Transform FindChildRecursive(Transform parent, string nameContains)
    {
        string search = nameContains.ToLower().Replace(" ", "");
        foreach (Transform child in parent)
        {
            string childName = child.name.ToLower().Replace(" ", "");
            if (childName.Contains(search))
                return child;

            var result = FindChildRecursive(child, nameContains);
            if (result != null) return result;
        }
        return null;
    }

    void CreateVisuals()
    {
        // Create LineRenderer for beam
        GameObject beamObj = new GameObject("LaserBeam");
        beamObj.transform.SetParent(transform, false);
        _lineRenderer = beamObj.AddComponent<LineRenderer>();
        _lineRenderer.positionCount = 2;
        _lineRenderer.startWidth = beamWidth;
        _lineRenderer.endWidth = beamWidth;
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _lineRenderer.startColor = laserColor;
        _lineRenderer.endColor = laserColor;
        _lineRenderer.receiveShadows = false;
        _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Create hit dot (small sphere)
        _hitDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _hitDot.name = "LaserDot";
        _hitDot.transform.SetParent(transform, false);
        _hitDot.transform.localScale = Vector3.one * dotSize;

        // Remove collider
        var col = _hitDot.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Set material
        _dotRenderer = _hitDot.GetComponent<MeshRenderer>();
        if (_dotRenderer != null)
        {
            _dotRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _dotRenderer.material.color = laserColor;
            _dotRenderer.receiveShadows = false;
            _dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void Update()
    {
        HandleInput();

        if (_isActive && _rayOrigin != null)
        {
            UpdateLaser();
            UpdateNetworkSync();
        }
    }

    void HandleInput()
    {
        // Desktop: L key toggle (VR is handled via InputAction callback)
        if (_isDesktopMode && Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
        {
            ToggleLaser();
        }
    }

    public void ToggleLaser()
    {
        _isActive = !_isActive;
        SetVisualsActive(_isActive);

        if (!_isActive)
        {
            // Send deactivation message
            SendLaserUpdate(false, Vector3.zero, Vector3.zero);
        }

        Debug.Log($"[LaserPointer] Laser {(_isActive ? "ON" : "OFF")}");
    }

    public void SetLaserActive(bool active)
    {
        if (_isActive == active) return;
        _isActive = active;
        SetVisualsActive(active);

        if (!active)
        {
            SendLaserUpdate(false, Vector3.zero, Vector3.zero);
        }
    }

    void SetVisualsActive(bool active)
    {
        if (_lineRenderer != null) _lineRenderer.enabled = active;
        if (_hitDot != null) _hitDot.SetActive(active);
    }

    void UpdateLaser()
    {
        Vector3 origin = _rayOrigin.position;
        Vector3 direction = _rayOrigin.forward;

        Vector3 endPoint;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, _raycastMask))
        {
            endPoint = hit.point;

            // Show and position dot at hit point
            if (_hitDot != null)
            {
                _hitDot.SetActive(true);
                _hitDot.transform.position = hit.point;
                // Orient dot to face surface normal
                _hitDot.transform.rotation = Quaternion.LookRotation(-hit.normal);
            }
        }
        else
        {
            endPoint = origin + direction * maxDistance;

            // Hide dot when no hit
            if (_hitDot != null) _hitDot.SetActive(false);
        }

        // Update line renderer
        if (_lineRenderer != null)
        {
            _lineRenderer.SetPosition(0, origin);
            _lineRenderer.SetPosition(1, endPoint);
        }
    }

    void UpdateNetworkSync()
    {
        _syncTimer += Time.deltaTime;
        if (_syncTimer < 1f / syncRate) return;
        _syncTimer = 0f;

        if (_rayOrigin == null) return;

        Vector3 origin = _rayOrigin.position;
        Vector3 direction = _rayOrigin.forward;
        Vector3 hitPoint;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, _raycastMask))
        {
            hitPoint = hit.point;
        }
        else
        {
            hitPoint = origin + direction * maxDistance;
        }

        SendLaserUpdate(true, origin, hitPoint);
    }

    void SendLaserUpdate(bool active, Vector3 origin, Vector3 hitPoint)
    {
        if (VRNetworkManager.Instance == null) return;
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        _cachedData.roomId = VRRoomManager.Instance.CurrentRoomId;
        _cachedData.isActive = active;
        _cachedData.originX = origin.x;
        _cachedData.originY = origin.y;
        _cachedData.originZ = origin.z;
        _cachedData.hitX = hitPoint.x;
        _cachedData.hitY = hitPoint.y;
        _cachedData.hitZ = hitPoint.z;
        _cachedData.colorR = laserColor.r;
        _cachedData.colorG = laserColor.g;
        _cachedData.colorB = laserColor.b;

        VRNetworkManager.Instance.Send("laser-pointer", _cachedData);
    }

    void OnDisable()
    {
        if (_isActive)
        {
            _isActive = false;
            SetVisualsActive(false);
            SendLaserUpdate(false, Vector3.zero, Vector3.zero);
        }
    }

    void OnDestroy()
    {
        if (_isActive)
        {
            SendLaserUpdate(false, Vector3.zero, Vector3.zero);
        }

        if (_vrToggleAction != null)
        {
            _vrToggleAction.Disable();
            _vrToggleAction.Dispose();
            _vrToggleAction = null;
        }
    }
}
