using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;

// Avoid ambiguity with UnityEngine.InputSystem
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

// VR player controller with locomotion support
// Handles continuous movement (joystick), snap/smooth turning, and teleportation
[RequireComponent(typeof(CharacterController))]
public class VRPlayerController : MonoBehaviour
{
    [Header("Movement Settings")]

    // Base movement speed
    [Tooltip("Vitesse de déplacement")]
    public float moveSpeed = 2f;
    
    // Sprint speed multiplier
    [Tooltip("Multiplicateur de sprint")]
    public float sprintMultiplier = 1.5f;
    
    // Enable/disable gravity
    [Tooltip("Appliquer la gravité")]
    public bool useGravity = true;
    
    // Gravity force magnitude
    [Tooltip("Force de gravité")]
    public float gravity = -9.81f;
    
    [Header("Rotation Settings")]

    // Use snap turn (instant rotation) instead of smooth turn
    [Tooltip("Utiliser Snap Turn (sinon Smooth Turn)")]
    public bool useSnapTurn = true;
    
    // Rotation angle per snap turn
    [Tooltip("Angle de Snap Turn (degrés)")]
    public float snapTurnAngle = 45f;
    
    // Smooth turn rotation speed (degrees per second)
    [Tooltip("Vitesse de Smooth Turn (degrés/seconde)")]
    public float smoothTurnSpeed = 90f;
    
    // Joystick deadzone threshold for turning
    [Tooltip("Seuil du joystick pour le turn")]
    public float turnThreshold = 0.5f;
    
    [Header("Input Settings")]

    // XR controller used for movement (left hand recommended)
    [Tooltip("Main utilisée pour le mouvement (gauche recommandée)")]
    public XRNode moveHand = XRNode.LeftHand;
    
    // XR controller used for rotation (right hand recommended)
    [Tooltip("Main utilisée pour la rotation (droite recommandée)")]
    public XRNode turnHand = XRNode.RightHand;
    
    [Header("References")]

    // VR headset/camera transform for direction-based movement
    public Transform headTransform;
    
    [Header("Debug")]

    // Display debug information on screen
    public bool showDebugInfo = false;
    
    // Component references
    private CharacterController _characterController;
    
    // Movement state
    private Vector3 _velocity;
    private bool _canSnapTurn = true;
    private XRInputDevice _moveDevice;
    private XRInputDevice _turnDevice;
    
    // Current input values
    private Vector2 _moveInput;
    private Vector2 _turnInput;
    
    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        
        // Find head/camera transform if not assigned
        if (headTransform == null)
        {
            var camera = GetComponentInChildren<Camera>();
            if (camera != null)
            {
                headTransform = camera.transform;
            }
        }
        
        // Initialize XR input devices
        UpdateInputDevices();
    }
    
    void Update()
    {
        // Refresh devices if disconnected
        if (!_moveDevice.isValid || !_turnDevice.isValid)
        {
            UpdateInputDevices();
        }
        
        // Read controller inputs
        ReadInputs();
        
        // Apply movement and rotation
        HandleMovement();
        HandleRotation();
        HandleGravity();
    }
    
    // Finds and caches XR input devices for each hand
    void UpdateInputDevices()
    {
        var devices = new List<XRInputDevice>();
        
        InputDevices.GetDevicesAtXRNode(moveHand, devices);
        if (devices.Count > 0) _moveDevice = devices[0];
        
        devices.Clear();
        InputDevices.GetDevicesAtXRNode(turnHand, devices);
        if (devices.Count > 0) _turnDevice = devices[0];
    }
    
    // Reads joystick inputs from VR controllers (with keyboard fallback)
    void ReadInputs()
    {
        // Read movement joystick
        if (_moveDevice.isValid)
        {
            _moveDevice.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out _moveInput);
        }
        else
        {
            // Keyboard fallback for testing
            _moveInput = new Vector2(
                Input.GetAxis("Horizontal"),
                Input.GetAxis("Vertical")
            );
        }
        
        // Read rotation joystick
        if (_turnDevice.isValid)
        {
            _turnDevice.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out _turnInput);
        }
        else
        {
            // Keyboard fallback
            float turnKeyboard = 0f;
            if (Input.GetKey(KeyCode.Q)) turnKeyboard -= 1f;
            if (Input.GetKey(KeyCode.E)) turnKeyboard += 1f;
            _turnInput = new Vector2(turnKeyboard, 0);
        }
    }
    
    // Handles continuous locomotion based on joystick input
    void HandleMovement()
    {
        if (_moveInput.magnitude < 0.1f) return;
        
        // Calculate direction based on head orientation
        Vector3 forward = headTransform != null ? headTransform.forward : transform.forward;
        Vector3 right = headTransform != null ? headTransform.right : transform.right;
        
        // Project onto horizontal plane
        forward.y = 0;
        forward.Normalize();
        right.y = 0;
        right.Normalize();
        
        // Calculate movement vector
        Vector3 moveDirection = forward * _moveInput.y + right * _moveInput.x;
        
        // Check for sprint input (grip button or shift key)
        bool isSprinting = false;
        if (_moveDevice.isValid)
        {
            _moveDevice.TryGetFeatureValue(XRCommonUsages.gripButton, out isSprinting);
        }
        isSprinting = isSprinting || Input.GetKey(KeyCode.LeftShift);
        
        float currentSpeed = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        
        // Apply movement via CharacterController
        Vector3 movement = moveDirection * currentSpeed * Time.deltaTime;
        _characterController.Move(movement);
    }
    
    // Handles player rotation (snap turn or smooth turn)
    void HandleRotation()
    {
        float turnValue = _turnInput.x;
        
        // Reset snap turn when joystick returns to center
        if (Mathf.Abs(turnValue) < turnThreshold)
        {
            _canSnapTurn = true;
            return;
        }
        
        if (useSnapTurn)
        {
            // Snap Turn: instant rotation by fixed angle
            if (_canSnapTurn)
            {
                float snapDirection = turnValue > 0 ? snapTurnAngle : -snapTurnAngle;
                transform.Rotate(0, snapDirection, 0);
                _canSnapTurn = false;
            }
        }
        else
        {
            // Smooth Turn: continuous rotation
            float turnAmount = turnValue * smoothTurnSpeed * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0);
        }
    }
    
    // Applies gravity to keep player grounded
    void HandleGravity()
    {
        if (!useGravity) return;
        
        if (_characterController.isGrounded)
        {
            _velocity.y = -0.5f; // Small downward force to stay grounded
        }
        else
        {
            _velocity.y += gravity * Time.deltaTime;
        }
        
        _characterController.Move(_velocity * Time.deltaTime);
    }
    
    // Debug UI overlay
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label($"Move Input: {_moveInput}");
        GUILayout.Label($"Turn Input: {_turnInput}");
        GUILayout.Label($"Grounded: {_characterController.isGrounded}");
        GUILayout.Label($"Velocity: {_velocity}");
        GUILayout.EndArea();
    }
}