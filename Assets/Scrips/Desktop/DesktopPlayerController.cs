using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Contrôleur FPS pour le mode Desktop (non-VR)
/// - Clic droit maintenu = rotation caméra
/// - Clic gauche = libre pour UI
/// - WASD = déplacement (toujours actif)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class DesktopPlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Vitesse de déplacement de base")]
    public float moveSpeed = 5f;

    [Tooltip("Multiplicateur de vitesse en sprint (Shift)")]
    public float sprintMultiplier = 1.5f;

    [Tooltip("Force de gravité")]
    public float gravity = -9.81f;

    [Header("Mouse Look")]
    [Tooltip("Sensibilité de la souris (multiplied by settings)")]
    public float baseSensitivity = 0.1f;

    [Tooltip("Angle vertical minimum (regarder vers le haut)")]
    public float minPitch = -90f;

    [Tooltip("Angle vertical maximum (regarder vers le bas)")]
    public float maxPitch = 90f;

    [Header("References")]
    [Tooltip("Transform de la tête/caméra (pour la rotation verticale)")]
    public Transform headTransform;

    private CharacterController _controller;
    private float _verticalVelocity;
    private float _cameraPitch;

    // Input System
    private Mouse _mouse;
    private Keyboard _keyboard;

    // Track if right mouse is held for camera rotation
    private bool _isLooking = false;

    // Settings
    private float _mouseSensitivity = 2f;
    private bool _invertY = false;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();

        // Auto-find head if not assigned
        if (headTransform == null)
        {
            var cam = GetComponentInChildren<Camera>();
            if (cam != null)
                headTransform = cam.transform;
            else
                headTransform = transform.Find("Head");
        }

        // Get Input System devices
        _mouse = Mouse.current;
        _keyboard = Keyboard.current;
    }

    void Start()
    {
        // Cursor always visible by default for UI interaction
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Load settings
        LoadSettings();
    }

    void OnEnable()
    {
        // Subscribe to settings changes
        MainMenuSettings.OnMouseSensitivityChanged += OnMouseSensitivityChanged;
        MainMenuSettings.OnInvertYChanged += OnInvertYChanged;
    }

    void OnDisable()
    {
        // Unsubscribe from settings changes
        MainMenuSettings.OnMouseSensitivityChanged -= OnMouseSensitivityChanged;
        MainMenuSettings.OnInvertYChanged -= OnInvertYChanged;
    }

    void LoadSettings()
    {
        _mouseSensitivity = MainMenuSettings.GetMouseSensitivity();
        _invertY = MainMenuSettings.GetInvertY();
    }

    void OnMouseSensitivityChanged(float value)
    {
        _mouseSensitivity = value;
    }

    void OnInvertYChanged(bool value)
    {
        _invertY = value;
    }

    void Update()
    {
        // Refresh device references if needed
        if (_mouse == null) _mouse = Mouse.current;
        if (_keyboard == null) _keyboard = Keyboard.current;

        if (_mouse == null || _keyboard == null)
        {
            Debug.LogWarning("[DesktopPlayer] No mouse or keyboard detected");
            return;
        }

        HandleMouseLook();
        HandleMovement();
    }

    void HandleMouseLook()
    {
        // Right mouse button held = look around
        bool rightMouseHeld = _mouse.rightButton.isPressed;

        if (rightMouseHeld && !_isLooking)
        {
            // Started looking - hide cursor
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _isLooking = true;
        }
        else if (!rightMouseHeld && _isLooking)
        {
            // Stopped looking - show cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _isLooking = false;
        }

        // Only rotate camera while right mouse is held
        if (_isLooking)
        {
            Vector2 mouseDelta = _mouse.delta.ReadValue();

            // Apply sensitivity from settings
            float sensitivity = baseSensitivity * _mouseSensitivity;
            float mouseX = mouseDelta.x * sensitivity;
            float mouseY = mouseDelta.y * sensitivity;

            // Apply invert Y if enabled
            if (_invertY)
            {
                mouseY = -mouseY;
            }

            // Horizontal rotation: rotate the whole player
            transform.Rotate(Vector3.up * mouseX);

            // Vertical rotation: rotate only the head/camera
            _cameraPitch -= mouseY;
            _cameraPitch = Mathf.Clamp(_cameraPitch, minPitch, maxPitch);

            if (headTransform != null)
            {
                headTransform.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
            }
        }
    }

    void HandleMovement()
    {
        // Movement always active (WASD)
        Vector2 moveInput = Vector2.zero;

        if (_keyboard.wKey.isPressed) moveInput.y += 1f;
        if (_keyboard.sKey.isPressed) moveInput.y -= 1f;
        if (_keyboard.aKey.isPressed) moveInput.x -= 1f;
        if (_keyboard.dKey.isPressed) moveInput.x += 1f;

        // Normalize diagonal movement
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        // Calculate move direction relative to player facing
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        // Apply sprint
        float speed = moveSpeed;
        if (_keyboard.leftShiftKey.isPressed || _keyboard.rightShiftKey.isPressed)
        {
            speed *= sprintMultiplier;
        }

        // Apply gravity
        if (_controller.isGrounded && _verticalVelocity < 0)
        {
            _verticalVelocity = -2f;
        }
        _verticalVelocity += gravity * Time.deltaTime;

        // Combine horizontal and vertical movement
        Vector3 velocity = move * speed + Vector3.up * _verticalVelocity;

        // Move
        _controller.Move(velocity * Time.deltaTime);
    }

    /// <summary>
    /// Check if currently in look mode (right mouse held)
    /// </summary>
    public bool IsLooking => _isLooking;
}
