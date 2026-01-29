using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Unity.XR.CoreUtils;
using System.Collections.Generic;

/// <summary>
/// Bridges Legacy XR API button input to XRI interactors (NearFarInteractor, XRDirectInteractor, XRRayInteractor).
/// Manually triggers select/unselect when grip is pressed, and activate when trigger is pressed.
/// Required because XRI's InputActionReferences don't work when Input System doesn't detect controllers.
/// </summary>
public class XRInteractorInputBridge : MonoBehaviour
{
    [Header("Debug")]
    public bool debugLogs = true;

    private ControllerInputFix _inputFix;

    // Interactors
    private IXRSelectInteractor _leftSelectInteractor;
    private IXRSelectInteractor _rightSelectInteractor;
    private IXRActivateInteractor _leftActivateInteractor;
    private IXRActivateInteractor _rightActivateInteractor;

    // Interaction Manager
    private XRInteractionManager _interactionManager;

    // Track previous button states for edge detection
    private bool _prevLeftGrip;
    private bool _prevRightGrip;
    private bool _prevLeftTrigger;
    private bool _prevRightTrigger;

    // Currently selected interactables
    private UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable _leftSelectedInteractable;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable _rightSelectedInteractable;

    private bool _initialized = false;

    void Start()
    {
        Invoke(nameof(Initialize), 0.7f);
    }

    void Initialize()
    {
        // Find ControllerInputFix
        _inputFix = FindFirstObjectByType<ControllerInputFix>();
        if (_inputFix == null)
        {
            Debug.LogWarning("[XRInteractorInputBridge] No ControllerInputFix found!");
            return;
        }

        // Find XR Interaction Manager
        _interactionManager = FindFirstObjectByType<XRInteractionManager>();
        if (_interactionManager == null)
        {
            Debug.LogWarning("[XRInteractorInputBridge] No XRInteractionManager found!");
            return;
        }

        // Find XR Origin and controllers
        var xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogWarning("[XRInteractorInputBridge] No XROrigin found!");
            return;
        }

        Transform leftController = FindChildRecursive(xrOrigin.transform, "Left Controller");
        Transform rightController = FindChildRecursive(xrOrigin.transform, "Right Controller");

        // Find interactors on controllers
        if (leftController != null)
        {
            // Try NearFarInteractor first (XRI 3.x), then fall back to others
            var nearFar = leftController.GetComponentInChildren<NearFarInteractor>();
            if (nearFar != null)
            {
                _leftSelectInteractor = nearFar;
                _leftActivateInteractor = nearFar;
            }
            else
            {
                _leftSelectInteractor = leftController.GetComponentInChildren<XRDirectInteractor>();
                if (_leftSelectInteractor == null)
                    _leftSelectInteractor = leftController.GetComponentInChildren<XRRayInteractor>();

                _leftActivateInteractor = leftController.GetComponentInChildren<IXRActivateInteractor>();
            }
        }

        if (rightController != null)
        {
            var nearFar = rightController.GetComponentInChildren<NearFarInteractor>();
            if (nearFar != null)
            {
                _rightSelectInteractor = nearFar;
                _rightActivateInteractor = nearFar;
            }
            else
            {
                _rightSelectInteractor = rightController.GetComponentInChildren<XRDirectInteractor>();
                if (_rightSelectInteractor == null)
                    _rightSelectInteractor = rightController.GetComponentInChildren<XRRayInteractor>();

                _rightActivateInteractor = rightController.GetComponentInChildren<IXRActivateInteractor>();
            }
        }

        _initialized = true;

        if (debugLogs)
        {
            Debug.Log($"[XRInteractorInputBridge] Initialized - Left: {_leftSelectInteractor?.GetType().Name ?? "NULL"}, Right: {_rightSelectInteractor?.GetType().Name ?? "NULL"}");
        }
    }

    void Update()
    {
        if (!_initialized || _inputFix == null) return;

        // Read current button states
        bool leftGrip = _inputFix.IsLeftGripPressed();
        bool rightGrip = _inputFix.IsRightGripPressed();
        bool leftTrigger = _inputFix.IsLeftTriggerPressed();
        bool rightTrigger = _inputFix.IsRightTriggerPressed();

        // Process left controller grip (select/grab)
        if (leftGrip && !_prevLeftGrip)
        {
            TrySelect(_leftSelectInteractor, ref _leftSelectedInteractable, "Left");
        }
        else if (!leftGrip && _prevLeftGrip)
        {
            TryDeselect(_leftSelectInteractor, ref _leftSelectedInteractable, "Left");
        }

        // Process right controller grip (select/grab)
        if (rightGrip && !_prevRightGrip)
        {
            TrySelect(_rightSelectInteractor, ref _rightSelectedInteractable, "Right");
        }
        else if (!rightGrip && _prevRightGrip)
        {
            TryDeselect(_rightSelectInteractor, ref _rightSelectedInteractable, "Right");
        }

        // Update previous states
        _prevLeftGrip = leftGrip;
        _prevRightGrip = rightGrip;
        _prevLeftTrigger = leftTrigger;
        _prevRightTrigger = rightTrigger;
    }

    void TrySelect(IXRSelectInteractor interactor, ref UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable selectedInteractable, string hand)
    {
        if (interactor == null) return;

        // Get the interactor's valid targets (what it's hovering over)
        var validTargets = new List<UnityEngine.XR.Interaction.Toolkit.Interactables.IXRInteractable>();
        interactor.GetValidTargets(validTargets);

        if (validTargets.Count > 0)
        {
            var target = validTargets[0] as UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable;
            if (target != null && target.IsSelectableBy(interactor))
            {
                // Use the interaction manager to select
                _interactionManager.SelectEnter(interactor, target);
                selectedInteractable = target;

                if (debugLogs)
                {
                    var go = (target as Component)?.gameObject;
                    Debug.Log($"[XRInteractorInputBridge] {hand} SELECT: {(go != null ? go.name : "Unknown")}");
                }
            }
        }
    }

    void TryDeselect(IXRSelectInteractor interactor, ref UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable selectedInteractable, string hand)
    {
        if (interactor == null || selectedInteractable == null) return;

        // Use the interaction manager to deselect
        _interactionManager.SelectExit(interactor, selectedInteractable);

        if (debugLogs)
        {
            var go = (selectedInteractable as Component)?.gameObject;
            Debug.Log($"[XRInteractorInputBridge] {hand} DESELECT: {(go != null ? go.name : "Unknown")}");
        }

        selectedInteractable = null;
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
