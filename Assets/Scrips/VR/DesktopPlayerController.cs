using UnityEngine;

// Desktop player controller with WASD movement and mouse look
// Simplified controller for non-VR testing and development
[RequireComponent(typeof(CharacterController))]
public class DesktopPlayerController : MonoBehaviour
{
    [Header("Movement")]

    // Base movement speed
    public float moveSpeed = 5f;

    // Rotation speed for keyboard turning
    public float rotationSpeed = 720f;

    // Mouse look sensitivity
    public float mouseSensitivity = 2f;
    
    // Component references
    private CharacterController _controller;
    private Transform _cameraTransform;

    // Camera pitch (vertical rotation)
    private float _pitch;

    // Vertical velocity for gravity
    private Vector3 _velocity;
    
    void Start()
    {
        _controller = GetComponent<CharacterController>();
        if (_controller == null)
        {
            _controller = gameObject.AddComponent<CharacterController>();
        }
        
        // Find camera transform
        _cameraTransform = GetComponentInChildren<Camera>()?.transform;
    }
    
    void Update()
    {
        // Handle player movement and physics
        HandleMovement();
        HandleGravity();
    }
    
    // Handles WASD movement with optional sprint
    void HandleMovement()
    {
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        
        // Calculate movement direction relative to player orientation
        Vector3 move = transform.right * horizontal + transform.forward * vertical;
        
        // Apply sprint multiplier if shift is held
        float speed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            speed *= 1.5f;
        }
        
        _controller.Move(move * speed * Time.deltaTime);
    }
    
    // Applies gravity to keep player grounded
    void HandleGravity()
    {
        if (_controller.isGrounded)
        {
            _velocity.y = -0.5f; // Small downward force to stay grounded
        }
        else
        {
            _velocity.y += -9.81f * Time.deltaTime;
        }
        
        _controller.Move(_velocity * Time.deltaTime);
    }
}
