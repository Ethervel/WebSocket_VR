using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes the VR menu follow the player's head position.
/// Attach to the menu Canvas root.
/// </summary>
public class VRFollowMenu : MonoBehaviour
{
    [Header("Follow Settings")]
    [Tooltip("Distance from player's head (in meters)")]
    [Range(0.5f, 5f)]
    public float distanceFromPlayer = 1.5f;

    [Tooltip("Height offset from eye level (negative = lower)")]
    [Range(-1f, 1f)]
    public float heightOffset = -0.1f;

    [Tooltip("Smooth follow speed (higher = snappier)")]
    [Range(1f, 20f)]
    public float followSpeed = 5f;

    [Tooltip("Only follow horizontally (ignore vertical head movement)")]
    public bool lockVertical = true;

    [Tooltip("Menu scale multiplier")]
    [Range(0.5f, 2f)]
    public float menuScale = 1f;

    [Header("Behavior")]
    [Tooltip("Lock position when menu opens (stop following)")]
    public bool lockOnOpen = false;

    [Tooltip("Reposition to front when reopening")]
    public bool repositionOnOpen = true;

    // References
    private Transform _playerCamera;
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private bool _isLocked = false;

    void Start()
    {
        FindPlayerCamera();

        // Desktop mode: enable mouse interaction with World Space canvas
        if (!UnityEngine.XR.XRSettings.isDeviceActive)
        {
            SetupDesktopMouseInteraction();
        }
    }

    void SetupDesktopMouseInteraction()
    {
        // Get or add Canvas component
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) return;

        // Ensure GraphicRaycaster exists for UI interaction
        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
            Debug.Log("[VRFollowMenu] Added GraphicRaycaster for desktop mouse interaction");
        }

        // Set the event camera for World Space canvas
        if (canvas.renderMode == RenderMode.WorldSpace && _playerCamera != null)
        {
            canvas.worldCamera = _playerCamera.GetComponent<Camera>();
            Debug.Log("[VRFollowMenu] Set worldCamera for desktop mouse interaction");
        }
    }

    void OnEnable()
    {
        // Reposition in front of player when menu opens
        if (repositionOnOpen)
        {
            FindPlayerCamera();
            SnapToFront();
        }

        _isLocked = lockOnOpen;

        // Desktop mode: ensure mouse interaction is set up
        if (!UnityEngine.XR.XRSettings.isDeviceActive)
        {
            SetupDesktopMouseInteraction();
        }
    }

    void Update()
    {
        if (_playerCamera == null)
        {
            FindPlayerCamera();
            if (_playerCamera == null) return;
        }

        if (_isLocked) return;

        UpdateTargetPosition();
        SmoothFollow();
    }

    void FindPlayerCamera()
    {
        // Try to find the main camera (player's head)
        if (Camera.main != null)
        {
            _playerCamera = Camera.main.transform;
            return;
        }

        // Fallback: search for XR camera
        var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in cameras)
        {
            if (cam.CompareTag("MainCamera") || cam.name.Contains("Main") || cam.name.Contains("Head"))
            {
                _playerCamera = cam.transform;
                return;
            }
        }
    }

    void UpdateTargetPosition()
    {
        Vector3 forward = _playerCamera.forward;

        if (lockVertical)
        {
            // Keep menu at consistent height, only follow horizontal rotation
            forward.y = 0;

            // FIX: Prevent NaN when looking straight up/down (forward becomes zero vector)
            if (forward.sqrMagnitude < 0.001f)
            {
                // Use camera's right vector to determine forward direction
                forward = Vector3.Cross(Vector3.up, _playerCamera.right).normalized;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = Vector3.forward; // Ultimate fallback
                }
            }
            else
            {
                forward.Normalize();
            }
        }

        // Position in front of player
        _targetPosition = _playerCamera.position + forward * distanceFromPlayer;

        // Apply height offset
        if (lockVertical)
        {
            _targetPosition.y = _playerCamera.position.y + heightOffset;
        }
        else
        {
            _targetPosition += _playerCamera.up * heightOffset;
        }

        // Face the player
        Vector3 lookDirection = _targetPosition - _playerCamera.position;
        if (lookDirection != Vector3.zero)
        {
            _targetRotation = Quaternion.LookRotation(lookDirection);
        }
    }

    void SmoothFollow()
    {
        // FIX: Validate target values to prevent AABB errors
        if (!IsValidVector(_targetPosition) || !IsValidQuaternion(_targetRotation))
        {
            Debug.LogWarning("[VRFollowMenu] Invalid target values detected, skipping frame");
            return;
        }

        transform.position = Vector3.Lerp(transform.position, _targetPosition, followSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, followSpeed * Time.deltaTime);

        // Apply scale
        float baseScale = 0.001f; // Default canvas scale
        transform.localScale = Vector3.one * baseScale * menuScale;
    }

    // Helper to check if Vector3 is valid (not NaN or Infinity)
    bool IsValidVector(Vector3 v)
    {
        return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
               !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }

    // Helper to check if Quaternion is valid
    bool IsValidQuaternion(Quaternion q)
    {
        return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w);
    }

    /// <summary>
    /// Instantly position menu in front of player
    /// </summary>
    public void SnapToFront()
    {
        if (_playerCamera == null)
        {
            FindPlayerCamera();
            if (_playerCamera == null) return;
        }

        UpdateTargetPosition();
        transform.position = _targetPosition;
        transform.rotation = _targetRotation;

        // Apply scale
        float baseScale = 0.001f;
        transform.localScale = Vector3.one * baseScale * menuScale;
    }

    /// <summary>
    /// Lock/unlock position following
    /// </summary>
    public void SetLocked(bool locked)
    {
        _isLocked = locked;
    }

    /// <summary>
    /// Toggle position lock
    /// </summary>
    public void ToggleLock()
    {
        _isLocked = !_isLocked;
    }

    public bool IsLocked => _isLocked;
}
