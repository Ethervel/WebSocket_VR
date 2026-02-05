using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Toggle VR menu visibility with controller button (B button by default).
/// Attach to any persistent GameObject (e.g., in Bootstrap scene).
/// </summary>
public class VRMenuToggle : MonoBehaviour
{
    [Header("Menu Reference")]
    [Tooltip("The menu Canvas to toggle. If null, will search for VRFollowMenu component.")]
    public GameObject menuCanvas;

    [Header("Input")]
    [Tooltip("Input action for menu toggle (typically B button on right controller)")]
    public InputActionReference toggleAction;

    [Header("Settings")]
    [Tooltip("Start with menu hidden")]
    public bool startHidden = true;

    [Tooltip("Also toggle with keyboard key (for desktop testing)")]
    public Key keyboardToggle = Key.Tab;

    // Cached references
    private VRFollowMenu _followMenu;
    private bool _isMenuVisible;
    private Keyboard _keyboard;

    void Start()
    {
        // Find menu if not assigned
        if (menuCanvas == null)
        {
            _followMenu = FindFirstObjectByType<VRFollowMenu>();
            if (_followMenu != null)
            {
                menuCanvas = _followMenu.gameObject;
            }
        }
        else
        {
            _followMenu = menuCanvas.GetComponent<VRFollowMenu>();
        }

        if (menuCanvas == null)
        {
            Debug.LogWarning("[VRMenuToggle] No menu canvas found!");
            return;
        }

        // Set initial state
        if (startHidden)
        {
            menuCanvas.SetActive(false);
            _isMenuVisible = false;
        }
        else
        {
            _isMenuVisible = menuCanvas.activeSelf;
        }

        // Setup input action
        if (toggleAction != null && toggleAction.action != null)
        {
            toggleAction.action.actionMap?.Enable();
            toggleAction.action.Enable();
            toggleAction.action.performed += OnTogglePerformed;
            Debug.Log("[VRMenuToggle] Input action registered");
        }
        else
        {
            Debug.LogWarning("[VRMenuToggle] No toggle action assigned - using keyboard only");
        }
    }

    void OnDestroy()
    {
        if (toggleAction != null && toggleAction.action != null)
        {
            toggleAction.action.performed -= OnTogglePerformed;
        }
    }

    void Update()
    {
        // Keyboard fallback for desktop testing (using new Input System)
        if (_keyboard == null)
        {
            _keyboard = Keyboard.current;
        }

        if (_keyboard != null && _keyboard[keyboardToggle].wasPressedThisFrame)
        {
            ToggleMenu();
        }
    }

    void OnTogglePerformed(InputAction.CallbackContext context)
    {
        ToggleMenu();
    }

    public void ToggleMenu()
    {
        if (menuCanvas == null) return;

        _isMenuVisible = !_isMenuVisible;

        // Snap to front when opening
        if (_isMenuVisible && _followMenu != null)
        {
            _followMenu.SnapToFront();
        }

        menuCanvas.SetActive(_isMenuVisible);

        // Desktop mode: unlock cursor for mouse interaction
        if (_isMenuVisible && !UnityEngine.XR.XRSettings.isDeviceActive)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        Debug.Log($"[VRMenuToggle] Menu {(_isMenuVisible ? "opened" : "closed")}");
    }

    public void ShowMenu()
    {
        if (menuCanvas == null) return;

        _isMenuVisible = true;

        // Snap to front before showing
        if (_followMenu != null)
        {
            _followMenu.SnapToFront();
        }

        menuCanvas.SetActive(true);
    }

    public void HideMenu()
    {
        if (menuCanvas == null) return;

        _isMenuVisible = false;
        menuCanvas.SetActive(false);
    }

    public bool IsMenuVisible => _isMenuVisible;
}
