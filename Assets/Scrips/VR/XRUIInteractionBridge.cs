using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using System.Collections.Generic;

/// <summary>
/// Bridges Legacy XR API button input to UI interaction.
/// Performs raycasts from controllers and triggers UI events when trigger is pressed.
/// Required because XRI's InputActionReferences don't work when Input System doesn't detect controllers.
/// </summary>
public class XRUIInteractionBridge : MonoBehaviour
{
    [Header("Settings")]
    public float raycastDistance = 10f;
    public LayerMask uiLayerMask = -1; // Default to all layers

    [Header("Debug")]
    public bool debugLogs = true;
    public bool showDebugRay = true;

    private ControllerInputFix _inputFix;
    private Transform _leftController;
    private Transform _rightController;
    private Camera _uiCamera;

    // Track previous trigger state for click detection
    private bool _prevLeftTrigger;
    private bool _prevRightTrigger;

    // Currently hovered UI elements
    private GameObject _leftHoveredObject;
    private GameObject _rightHoveredObject;

    private bool _initialized = false;

    void Start()
    {
        Invoke(nameof(Initialize), 0.6f);
    }

    void Initialize()
    {
        // Find ControllerInputFix
        _inputFix = FindFirstObjectByType<ControllerInputFix>();
        if (_inputFix == null)
        {
            Debug.LogWarning("[XRUIInteractionBridge] No ControllerInputFix found!");
            return;
        }

        // Find XR Origin and controllers
        var xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogWarning("[XRUIInteractionBridge] No XROrigin found!");
            return;
        }

        _leftController = FindChildRecursive(xrOrigin.transform, "Left Controller");
        _rightController = FindChildRecursive(xrOrigin.transform, "Right Controller");

        // Find UI camera (main camera usually works for world space UI)
        _uiCamera = Camera.main;

        _initialized = true;
        if (debugLogs)
            Debug.Log($"[XRUIInteractionBridge] Initialized - Left: {_leftController != null}, Right: {_rightController != null}");
    }

    void Update()
    {
        if (!_initialized || _inputFix == null) return;

        // Process left controller
        if (_leftController != null)
        {
            ProcessController(_leftController, _inputFix.IsLeftTriggerPressed(), ref _prevLeftTrigger, ref _leftHoveredObject, "Left");
        }

        // Process right controller
        if (_rightController != null)
        {
            ProcessController(_rightController, _inputFix.IsRightTriggerPressed(), ref _prevRightTrigger, ref _rightHoveredObject, "Right");
        }
    }

    void ProcessController(Transform controller, bool triggerPressed, ref bool prevTrigger, ref GameObject hoveredObject, string hand)
    {
        // Raycast from controller
        Ray ray = new Ray(controller.position, controller.forward);
        RaycastHit hit;

        if (showDebugRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * raycastDistance, triggerPressed ? Color.green : Color.red);
        }

        if (Physics.Raycast(ray, out hit, raycastDistance, uiLayerMask))
        {
            GameObject hitObject = hit.collider.gameObject;

            // Check if it's a UI element
            var button = hitObject.GetComponent<Button>();
            var selectable = hitObject.GetComponent<Selectable>();

            if (button != null || selectable != null)
            {
                // Hover enter
                if (hoveredObject != hitObject)
                {
                    // Exit previous
                    if (hoveredObject != null)
                    {
                        ExecutePointerExit(hoveredObject);
                    }

                    // Enter new
                    hoveredObject = hitObject;
                    ExecutePointerEnter(hitObject);

                    if (debugLogs)
                        Debug.Log($"[XRUIInteractionBridge] {hand} hover: {hitObject.name}");
                }

                // Click detection (trigger pressed this frame)
                if (triggerPressed && !prevTrigger)
                {
                    if (debugLogs)
                        Debug.Log($"[XRUIInteractionBridge] {hand} CLICK: {hitObject.name}");

                    // Execute click
                    if (button != null)
                    {
                        button.onClick.Invoke();
                    }
                    else
                    {
                        ExecutePointerClick(hitObject);
                    }
                }
            }
        }
        else
        {
            // Not hitting anything - clear hover
            if (hoveredObject != null)
            {
                ExecutePointerExit(hoveredObject);
                hoveredObject = null;
            }
        }

        // Also try GraphicRaycaster for Canvas UI
        ProcessCanvasUI(controller, triggerPressed, prevTrigger, hand);

        prevTrigger = triggerPressed;
    }

    void ProcessCanvasUI(Transform controller, bool triggerPressed, bool prevTrigger, string hand)
    {
        // Find all canvases and raycast against them
        var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);

        foreach (var canvas in canvases)
        {
            if (canvas.renderMode != RenderMode.WorldSpace) continue;

            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null) continue;

            // Create pointer event data
            var eventData = new PointerEventData(EventSystem.current);

            // Convert controller ray to screen point for the canvas
            Ray ray = new Ray(controller.position, controller.forward);

            // Find intersection with canvas plane
            Plane canvasPlane = new Plane(canvas.transform.forward, canvas.transform.position);
            float distance;

            if (canvasPlane.Raycast(ray, out distance))
            {
                Vector3 worldPoint = ray.GetPoint(distance);

                // Check if point is within canvas bounds (approximate)
                Vector3 localPoint = canvas.transform.InverseTransformPoint(worldPoint);
                RectTransform rectTransform = canvas.GetComponent<RectTransform>();

                if (rectTransform != null)
                {
                    // Convert to screen space for GraphicRaycaster
                    if (_uiCamera != null)
                    {
                        eventData.position = _uiCamera.WorldToScreenPoint(worldPoint);

                        var results = new List<RaycastResult>();
                        raycaster.Raycast(eventData, results);

                        foreach (var result in results)
                        {
                            var button = result.gameObject.GetComponent<Button>();
                            if (button != null && triggerPressed && !prevTrigger)
                            {
                                if (debugLogs)
                                    Debug.Log($"[XRUIInteractionBridge] {hand} Canvas CLICK: {result.gameObject.name}");
                                button.onClick.Invoke();
                            }
                        }
                    }
                }
            }
        }
    }

    void ExecutePointerEnter(GameObject target)
    {
        var eventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerEnterHandler);
    }

    void ExecutePointerExit(GameObject target)
    {
        var eventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerExitHandler);
    }

    void ExecutePointerClick(GameObject target)
    {
        var eventData = new PointerEventData(EventSystem.current);
        ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerClickHandler);
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
}
