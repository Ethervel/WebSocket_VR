using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Unity.XR.CoreUtils;
using System.Collections.Generic;

/// <summary>
/// Fixes controller input by reading button states via Legacy XR API.
/// Required because Input System doesn't detect XR Controllers on Quest/OpenXR.
/// Works with XRI 3.x NearFarInteractor.
/// </summary>
public class ControllerInputFix : MonoBehaviour
{
    [Header("Debug")]
    public bool debugLogs = true;

    [Header("Input Thresholds")]
    [Tooltip("Threshold for trigger/grip to be considered pressed")]
    public float pressThreshold = 0.5f;

    // XR Devices
    private InputDevice _leftHandDevice;
    private InputDevice _rightHandDevice;

    // Interactors from the prefab
    private IXRSelectInteractor _leftInteractor;
    private IXRSelectInteractor _rightInteractor;

    // Button states
    private bool _leftGripPressed;
    private bool _leftTriggerPressed;
    private bool _rightGripPressed;
    private bool _rightTriggerPressed;

    // Previous states for edge detection
    private bool _prevLeftGrip;
    private bool _prevLeftTrigger;
    private bool _prevRightGrip;
    private bool _prevRightTrigger;

    private bool _initialized = false;
    private int _frameCount = 0;

    void Start()
    {
        // Subscribe to device events
        InputDevices.deviceConnected += OnDeviceConnected;
        InputDevices.deviceDisconnected += OnDeviceDisconnected;

        Invoke(nameof(Initialize), 0.5f);
    }

    void OnDeviceConnected(InputDevice device)
    {
        if ((device.characteristics & InputDeviceCharacteristics.Left) != 0 &&
            (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
        {
            _leftHandDevice = device;
            if (debugLogs) Debug.Log($"[ControllerInputFix] Left controller connected: {device.name}");
        }
        if ((device.characteristics & InputDeviceCharacteristics.Right) != 0 &&
            (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
        {
            _rightHandDevice = device;
            if (debugLogs) Debug.Log($"[ControllerInputFix] Right controller connected: {device.name}");
        }
    }

    void OnDeviceDisconnected(InputDevice device)
    {
        if (debugLogs) Debug.Log($"[ControllerInputFix] Device disconnected: {device.name}");
    }

    void Initialize()
    {
        if (_initialized) return;

        // Find XR Origin
        var xrOrigin = GetComponentInChildren<XROrigin>();
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();

        if (xrOrigin == null)
        {
            Debug.LogWarning("[ControllerInputFix] No XROrigin found!");
            return;
        }

        // Find controller transforms
        Transform leftController = FindChildRecursive(xrOrigin.transform, "Left Controller");
        Transform rightController = FindChildRecursive(xrOrigin.transform, "Right Controller");

        // Find interactors on controllers
        if (leftController != null)
        {
            // Try NearFarInteractor first (XRI 3.x), then fall back to others
            _leftInteractor = leftController.GetComponentInChildren<NearFarInteractor>();
            if (_leftInteractor == null)
                _leftInteractor = leftController.GetComponentInChildren<XRDirectInteractor>();
            if (_leftInteractor == null)
                _leftInteractor = leftController.GetComponentInChildren<XRRayInteractor>();

            if (debugLogs) Debug.Log($"[ControllerInputFix] Left interactor: {(_leftInteractor != null ? _leftInteractor.GetType().Name : "NULL")}");
        }

        if (rightController != null)
        {
            _rightInteractor = rightController.GetComponentInChildren<NearFarInteractor>();
            if (_rightInteractor == null)
                _rightInteractor = rightController.GetComponentInChildren<XRDirectInteractor>();
            if (_rightInteractor == null)
                _rightInteractor = rightController.GetComponentInChildren<XRRayInteractor>();

            if (debugLogs) Debug.Log($"[ControllerInputFix] Right interactor: {(_rightInteractor != null ? _rightInteractor.GetType().Name : "NULL")}");
        }

        // Find XR devices
        var devices = new List<InputDevice>();
        InputDevices.GetDevices(devices);

        foreach (var device in devices)
        {
            if ((device.characteristics & InputDeviceCharacteristics.Left) != 0 &&
                (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
            {
                _leftHandDevice = device;
                if (debugLogs) Debug.Log($"[ControllerInputFix] Found left device: {device.name}");
            }
            if ((device.characteristics & InputDeviceCharacteristics.Right) != 0 &&
                (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
            {
                _rightHandDevice = device;
                if (debugLogs) Debug.Log($"[ControllerInputFix] Found right device: {device.name}");
            }
        }

        _initialized = true;
        if (debugLogs) Debug.Log("[ControllerInputFix] Initialized - reading buttons via Legacy XR API");
    }

    void Update()
    {
        if (!_initialized) return;

        _frameCount++;

        // Read button states from Legacy XR API
        ReadButtonStates();

        // Log button states periodically
        if (debugLogs && _frameCount % 120 == 0)
        {
            Debug.Log($"[ControllerInputFix] Left - Grip: {_leftGripPressed}, Trigger: {_leftTriggerPressed} | Right - Grip: {_rightGripPressed}, Trigger: {_rightTriggerPressed}");
        }
    }

    void ReadButtonStates()
    {
        // Store previous states
        _prevLeftGrip = _leftGripPressed;
        _prevLeftTrigger = _leftTriggerPressed;
        _prevRightGrip = _rightGripPressed;
        _prevRightTrigger = _rightTriggerPressed;

        // Read left controller
        if (_leftHandDevice.isValid)
        {
            // Grip (Select in XRI)
            if (_leftHandDevice.TryGetFeatureValue(CommonUsages.grip, out float leftGrip))
            {
                _leftGripPressed = leftGrip > pressThreshold;
            }

            // Trigger (Activate in XRI)
            if (_leftHandDevice.TryGetFeatureValue(CommonUsages.trigger, out float leftTrigger))
            {
                _leftTriggerPressed = leftTrigger > pressThreshold;
            }
        }

        // Read right controller
        if (_rightHandDevice.isValid)
        {
            // Grip (Select in XRI)
            if (_rightHandDevice.TryGetFeatureValue(CommonUsages.grip, out float rightGrip))
            {
                _rightGripPressed = rightGrip > pressThreshold;
            }

            // Trigger (Activate in XRI)
            if (_rightHandDevice.TryGetFeatureValue(CommonUsages.trigger, out float rightTrigger))
            {
                _rightTriggerPressed = rightTrigger > pressThreshold;
            }
        }

        // Detect button press/release edges and log them
        if (_leftGripPressed != _prevLeftGrip)
        {
            if (debugLogs) Debug.Log($"[ControllerInputFix] Left Grip {(_leftGripPressed ? "PRESSED" : "RELEASED")}");
        }
        if (_leftTriggerPressed != _prevLeftTrigger)
        {
            if (debugLogs) Debug.Log($"[ControllerInputFix] Left Trigger {(_leftTriggerPressed ? "PRESSED" : "RELEASED")}");
        }
        if (_rightGripPressed != _prevRightGrip)
        {
            if (debugLogs) Debug.Log($"[ControllerInputFix] Right Grip {(_rightGripPressed ? "PRESSED" : "RELEASED")}");
        }
        if (_rightTriggerPressed != _prevRightTrigger)
        {
            if (debugLogs) Debug.Log($"[ControllerInputFix] Right Trigger {(_rightTriggerPressed ? "PRESSED" : "RELEASED")}");
        }
    }

    // Public getters for other scripts to read button states
    public bool IsLeftGripPressed() => _leftGripPressed;
    public bool IsLeftTriggerPressed() => _leftTriggerPressed;
    public bool IsRightGripPressed() => _rightGripPressed;
    public bool IsRightTriggerPressed() => _rightTriggerPressed;

    public float GetLeftGrip()
    {
        if (_leftHandDevice.isValid && _leftHandDevice.TryGetFeatureValue(CommonUsages.grip, out float value))
            return value;
        return 0f;
    }

    public float GetLeftTrigger()
    {
        if (_leftHandDevice.isValid && _leftHandDevice.TryGetFeatureValue(CommonUsages.trigger, out float value))
            return value;
        return 0f;
    }

    public float GetRightGrip()
    {
        if (_rightHandDevice.isValid && _rightHandDevice.TryGetFeatureValue(CommonUsages.grip, out float value))
            return value;
        return 0f;
    }

    public float GetRightTrigger()
    {
        if (_rightHandDevice.isValid && _rightHandDevice.TryGetFeatureValue(CommonUsages.trigger, out float value))
            return value;
        return 0f;
    }

    // Get primary button (A/X) state
    public bool IsLeftPrimaryPressed()
    {
        if (_leftHandDevice.isValid && _leftHandDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool value))
            return value;
        return false;
    }

    public bool IsRightPrimaryPressed()
    {
        if (_rightHandDevice.isValid && _rightHandDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool value))
            return value;
        return false;
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
        InputDevices.deviceConnected -= OnDeviceConnected;
        InputDevices.deviceDisconnected -= OnDeviceDisconnected;
    }
}
