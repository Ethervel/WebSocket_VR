using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

/// <summary>
/// Loads 3D controller models for VR controllers.
/// Attach this to the XR Origin or LocalPlayer prefab.
///
/// NOTE: Manual tracking has been disabled. Models are parented to controllers
/// and follow them via native XRI TrackedPoseDriver.
/// </summary>
public class ControllerModelLoader : MonoBehaviour
{
    [Header("Manual Tracking (DISABLED)")]
    [Tooltip("Manual tracking is now disabled to let native XRI work. Keep this false.")]
    public bool useManualTracking = false;

    [Header("Controller Model Prefabs")]
    [Tooltip("Left controller model prefab")]
    public GameObject leftControllerModelPrefab;

    [Tooltip("Right controller model prefab")]
    public GameObject rightControllerModelPrefab;

    [Header("Auto-Find Settings")]
    [Tooltip("Automatically find controller models from XRI Samples if prefabs not assigned")]
    public bool autoFindModels = true;

    [Header("Debug")]
    [Tooltip("Use simple cubes instead of prefabs for debugging visibility")]
    public bool useDebugCubes = true; // Test avec cubes pour vérifier le positionnement

    private GameObject _leftModelInstance;
    private GameObject _rightModelInstance;
    private bool _modelsLoaded = false;

    // Controller tracking
    private Transform _leftControllerTransform;
    private Transform _rightControllerTransform;
    private InputAction _leftPositionAction;
    private InputAction _leftRotationAction;
    private InputAction _rightPositionAction;
    private InputAction _rightRotationAction;

    void Start()
    {
        // Wait a frame for the XR Origin to be fully initialized
        Invoke(nameof(LoadControllerModels), 0.1f);
    }

    void Update()
    {
        if (!_modelsLoaded) return;

        // Only update controller positions manually if enabled (DISABLED by default)
        // Native XRI TrackedPoseDriver handles tracking, models follow as children
        if (useManualTracking)
        {
            UpdateControllerTracking();
        }

        // Debug: Log controller positions every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            if (_leftModelInstance != null)
            {
                Debug.Log($"[ControllerModelLoader] Left model world pos: {_leftModelInstance.transform.position}, active: {_leftModelInstance.activeInHierarchy}");
            }
            if (_rightModelInstance != null)
            {
                Debug.Log($"[ControllerModelLoader] Right model world pos: {_rightModelInstance.transform.position}, active: {_rightModelInstance.activeInHierarchy}");
            }
        }
    }

    void UpdateControllerTracking()
    {
        // Get XROrigin for coordinate space conversion
        var xrOrigin = GetComponentInChildren<XROrigin>();
        if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();

        Transform originTransform = xrOrigin != null ? xrOrigin.transform : transform;
        Transform cameraFloorOffset = xrOrigin?.CameraFloorOffsetObject?.transform ?? originTransform;

        // Update left controller
        if (_leftControllerTransform != null && _leftPositionAction != null && _leftRotationAction != null)
        {
            Vector3 localPos = _leftPositionAction.ReadValue<Vector3>();
            Quaternion localRot = _leftRotationAction.ReadValue<Quaternion>();

            // Convert to world space through the camera floor offset
            _leftControllerTransform.position = cameraFloorOffset.TransformPoint(localPos);
            _leftControllerTransform.rotation = cameraFloorOffset.rotation * localRot;
        }

        // Update right controller
        if (_rightControllerTransform != null && _rightPositionAction != null && _rightRotationAction != null)
        {
            Vector3 localPos = _rightPositionAction.ReadValue<Vector3>();
            Quaternion localRot = _rightRotationAction.ReadValue<Quaternion>();

            // Convert to world space through the camera floor offset
            _rightControllerTransform.position = cameraFloorOffset.TransformPoint(localPos);
            _rightControllerTransform.rotation = cameraFloorOffset.rotation * localRot;
        }
    }

    void SetupControllerTracking(Transform leftController, Transform rightController)
    {
        _leftControllerTransform = leftController;
        _rightControllerTransform = rightController;

        // Create input actions for left controller
        _leftPositionAction = new InputAction("LeftControllerPosition", InputActionType.Value, "<XRController>{LeftHand}/devicePosition");
        _leftRotationAction = new InputAction("LeftControllerRotation", InputActionType.Value, "<XRController>{LeftHand}/deviceRotation");

        // Create input actions for right controller
        _rightPositionAction = new InputAction("RightControllerPosition", InputActionType.Value, "<XRController>{RightHand}/devicePosition");
        _rightRotationAction = new InputAction("RightControllerRotation", InputActionType.Value, "<XRController>{RightHand}/deviceRotation");

        // Enable all actions
        _leftPositionAction.Enable();
        _leftRotationAction.Enable();
        _rightPositionAction.Enable();
        _rightRotationAction.Enable();

        Debug.Log("[ControllerModelLoader] Controller tracking input actions created and enabled");
    }

    void LoadControllerModels()
    {
        if (_modelsLoaded) return;

        Debug.Log($"[ControllerModelLoader] Starting LoadControllerModels...");
        Debug.Log($"[ControllerModelLoader] Left prefab assigned: {(leftControllerModelPrefab != null ? leftControllerModelPrefab.name : "NULL")}");
        Debug.Log($"[ControllerModelLoader] Right prefab assigned: {(rightControllerModelPrefab != null ? rightControllerModelPrefab.name : "NULL")}");

        // Find the XR Origin
        var xrOrigin = GetComponentInChildren<XROrigin>();
        if (xrOrigin == null)
        {
            xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        if (xrOrigin == null)
        {
            Debug.LogWarning("[ControllerModelLoader] No XROrigin found!");
            return;
        }

        Debug.Log($"[ControllerModelLoader] XROrigin found: {xrOrigin.name}");

        // Find Left and Right controllers - try multiple naming conventions
        Transform leftController = FindChildByNames(xrOrigin.transform, new string[] {
            "Left Controller",
            "LeftHand Controller",
            "Left Hand",           // XR Origin Hands uses this
            "LeftHand"
        });

        Transform rightController = FindChildByNames(xrOrigin.transform, new string[] {
            "Right Controller",
            "RightHand Controller",
            "Right Hand",          // XR Origin Hands uses this
            "RightHand"
        });

        Debug.Log($"[ControllerModelLoader] Left controller transform: {(leftController != null ? leftController.name : "NOT FOUND")}");
        Debug.Log($"[ControllerModelLoader] Right controller transform: {(rightController != null ? rightController.name : "NOT FOUND")}");

        // Setup controller tracking input actions ONLY if manual tracking is enabled
        if (useManualTracking && (leftController != null || rightController != null))
        {
            SetupControllerTracking(leftController, rightController);
            Debug.Log("[ControllerModelLoader] Manual controller tracking enabled (may conflict with XRI)");
        }
        else
        {
            Debug.Log("[ControllerModelLoader] Using native XRI tracking - models parented to controller transforms");
        }

        // Try to auto-find model prefabs if not assigned
        if (autoFindModels)
        {
            if (leftControllerModelPrefab == null)
                leftControllerModelPrefab = FindControllerModelPrefab("Left");
            if (rightControllerModelPrefab == null)
                rightControllerModelPrefab = FindControllerModelPrefab("Right");
        }

        // Instantiate left controller model
        if (leftController != null)
        {
            if (useDebugCubes)
            {
                _leftModelInstance = CreateDebugCube(Color.blue, "DebugCube_Left");
            }
            else if (leftControllerModelPrefab != null)
            {
                _leftModelInstance = Instantiate(leftControllerModelPrefab, leftController);
            }

            if (_leftModelInstance != null)
            {
                _leftModelInstance.name = "ControllerModel_Left";
                _leftModelInstance.transform.SetParent(leftController, false);
                _leftModelInstance.transform.localPosition = Vector3.zero;
                _leftModelInstance.transform.localRotation = Quaternion.identity;
                _leftModelInstance.transform.localScale = useDebugCubes ? Vector3.one * 0.05f : Vector3.one;

                // Ensure all renderers are enabled
                foreach (var renderer in _leftModelInstance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                    renderer.gameObject.SetActive(true);
                }

                Debug.Log($"[ControllerModelLoader] Left controller model loaded at {leftController.position}, scale={_leftModelInstance.transform.lossyScale}, renderers={_leftModelInstance.GetComponentsInChildren<Renderer>(true).Length}");
            }
            else
            {
                Debug.LogWarning("[ControllerModelLoader] Left controller found but no model prefab assigned");
            }
        }

        // Instantiate right controller model
        if (rightController != null)
        {
            if (useDebugCubes)
            {
                _rightModelInstance = CreateDebugCube(Color.red, "DebugCube_Right");
            }
            else if (rightControllerModelPrefab != null)
            {
                _rightModelInstance = Instantiate(rightControllerModelPrefab, rightController);
            }

            if (_rightModelInstance != null)
            {
                _rightModelInstance.name = "ControllerModel_Right";
                _rightModelInstance.transform.SetParent(rightController, false);
                _rightModelInstance.transform.localPosition = Vector3.zero;
                _rightModelInstance.transform.localRotation = Quaternion.identity;
                _rightModelInstance.transform.localScale = useDebugCubes ? Vector3.one * 0.05f : Vector3.one;

                // Ensure all renderers are enabled
                foreach (var renderer in _rightModelInstance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                    renderer.gameObject.SetActive(true);
                }

                Debug.Log($"[ControllerModelLoader] Right controller model loaded at {rightController.position}, scale={_rightModelInstance.transform.lossyScale}, renderers={_rightModelInstance.GetComponentsInChildren<Renderer>(true).Length}");
            }
            else
            {
                Debug.LogWarning("[ControllerModelLoader] Right controller found but no model prefab assigned");
            }
        }

        _modelsLoaded = true;

        if (_leftModelInstance != null || _rightModelInstance != null)
        {
            Debug.Log("[ControllerModelLoader] Controller models loaded successfully");
        }
    }

    GameObject FindControllerModelPrefab(string hand)
    {
        // Try to find controller model prefabs in Resources
        string[] searchPaths = new string[]
        {
            $"XR Controller {hand}",
            $"Controller_{hand}",
            $"{hand}Controller"
        };

        foreach (var path in searchPaths)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null)
            {
                Debug.Log($"[ControllerModelLoader] Found {hand} controller model in Resources: {path}");
                return prefab;
            }
        }

        // Try to find in loaded assets (for prefabs in project but not in Resources)
        var allPrefabs = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var prefab in allPrefabs)
        {
            if (prefab.name.Contains($"XR Controller {hand}") ||
                prefab.name.Contains($"Controller {hand}"))
            {
                // Make sure it's a prefab and has a mesh
                if (prefab.GetComponentInChildren<MeshRenderer>() != null ||
                    prefab.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                {
                    Debug.Log($"[ControllerModelLoader] Found {hand} controller model: {prefab.name}");
                    return prefab;
                }
            }
        }

        return null;
    }

    GameObject CreateDebugCube(Color color, string name)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;

        // Remove collider to avoid physics issues
        var collider = cube.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        // Set color
        var renderer = cube.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Create a simple unlit material
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (material.shader == null)
                material = new Material(Shader.Find("Unlit/Color"));
            material.color = color;
            renderer.material = material;
        }

        Debug.Log($"[ControllerModelLoader] Created debug cube: {name} with color {color}");
        return cube;
    }

    Transform FindChildByNames(Transform parent, string[] names)
    {
        foreach (var name in names)
        {
            var found = FindChildRecursive(parent, name);
            if (found != null)
                return found;
        }
        return null;
    }

    Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            var found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    void OnDestroy()
    {
        if (_leftModelInstance != null)
            Destroy(_leftModelInstance);
        if (_rightModelInstance != null)
            Destroy(_rightModelInstance);

        // Clean up input actions
        _leftPositionAction?.Disable();
        _leftPositionAction?.Dispose();
        _leftRotationAction?.Disable();
        _leftRotationAction?.Dispose();
        _rightPositionAction?.Disable();
        _rightPositionAction?.Dispose();
        _rightRotationAction?.Disable();
        _rightRotationAction?.Dispose();
    }
}
