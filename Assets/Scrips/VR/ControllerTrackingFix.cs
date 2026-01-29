using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

/// <summary>
/// Fixes controller tracking by manually updating controller positions from Input System.
/// Uses Legacy XR API as fallback when Input System doesn't bind to controllers (Quest/OpenXR issue).
/// Add this to the XR Origin or LocalPlayer.
///
/// IMPORTANT: This script is now DISABLED by default to let native XRI TrackedPoseDriver work.
/// Only enable if you have tracking issues with the native system.
/// </summary>
public class ControllerTrackingFix : MonoBehaviour
{
    [Header("Enable Manual Tracking")]
    [Tooltip("Enable this ONLY if native XRI tracking doesn't work. Keep disabled for Quest 3.")]
    public bool enableManualTracking = false; // DISABLED by default - let native XRI work
    [Header("Rotation Offset (Quest Controllers)")]
    [Tooltip("Rotation offset to align controller orientation with physical pose")]
    public Vector3 rotationOffset = new Vector3(0f, 0f, 0f); // Set to 0 - let the prefab's model offset handle it

    [Tooltip("Apply rotation offset before device rotation (tracking space) vs after (local space)")]
    public bool applyOffsetInTrackingSpace = false;

    private Transform _leftController;
    private Transform _rightController;
    private Transform _cameraFloorOffset;
    private Quaternion _rotationOffsetQuat;

    private InputAction _leftPositionAction;
    private InputAction _leftRotationAction;
    private InputAction _rightPositionAction;
    private InputAction _rightRotationAction;

    private bool _initialized = false;
    private bool _useLegacyXR = false; // Fallback to legacy XR API if Input System fails
    private XRInputDevice _leftHandDevice;
    private XRInputDevice _rightHandDevice;

    void Start()
    {
        if (!enableManualTracking)
        {
            Debug.Log("[ControllerTrackingFix] Manual tracking DISABLED - using native XRI TrackedPoseDriver. Enable 'enableManualTracking' in Inspector if you have tracking issues.");
            enabled = false;
            return;
        }

        Debug.Log("[ControllerTrackingFix] Manual tracking ENABLED - this may conflict with native XRI tracking!");

        // Subscribe to device connection events
        InputDevices.deviceConnected += OnDeviceConnected;
        InputDevices.deviceDisconnected += OnDeviceDisconnected;

        // Pre-calculate rotation offset quaternion
        _rotationOffsetQuat = Quaternion.Euler(rotationOffset);

        Invoke(nameof(Initialize), 0.2f);
    }

    void OnDeviceConnected(XRInputDevice device)
    {
        Debug.Log($"[ControllerTrackingFix] Device connected: {device.name}, characteristics: {device.characteristics}");

        // Check if it's a controller we need
        if ((device.characteristics & InputDeviceCharacteristics.Left) != 0 &&
            (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
        {
            _leftHandDevice = device;
            Debug.Log($"[ControllerTrackingFix] Left controller connected: {device.name}");
        }
        if ((device.characteristics & InputDeviceCharacteristics.Right) != 0 &&
            (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
        {
            _rightHandDevice = device;
            Debug.Log($"[ControllerTrackingFix] Right controller connected: {device.name}");
        }
    }

    void OnDeviceDisconnected(XRInputDevice device)
    {
        Debug.Log($"[ControllerTrackingFix] Device disconnected: {device.name}");
    }

    void Initialize()
    {
        // Find XR Origin
        var xrOrigin = GetComponentInChildren<XROrigin>();
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();

        if (xrOrigin == null)
        {
            Debug.LogWarning("[ControllerTrackingFix] No XROrigin found!");
            return;
        }

        _cameraFloorOffset = xrOrigin.CameraFloorOffsetObject?.transform ?? xrOrigin.transform;

        // Find controllers
        _leftController = FindChildRecursive(xrOrigin.transform, "Left Controller");
        if (_leftController == null)
            _leftController = FindChildRecursive(xrOrigin.transform, "Left Hand");

        _rightController = FindChildRecursive(xrOrigin.transform, "Right Controller");
        if (_rightController == null)
            _rightController = FindChildRecursive(xrOrigin.transform, "Right Hand");

        Debug.Log($"[ControllerTrackingFix] Left: {(_leftController != null ? _leftController.name : "NULL")}, Right: {(_rightController != null ? _rightController.name : "NULL")}");

        // CRITICAL: Disable the built-in TrackedPoseDrivers on controllers
        // They override our positions with (0,0,0) because Input System doesn't detect XR controllers
        DisableControllerTrackedPoseDrivers();

        // Create input actions for controllers
        _leftPositionAction = new InputAction("LeftPosition", InputActionType.Value, "<XRController>{LeftHand}/devicePosition");
        _leftRotationAction = new InputAction("LeftRotation", InputActionType.Value, "<XRController>{LeftHand}/deviceRotation");
        _rightPositionAction = new InputAction("RightPosition", InputActionType.Value, "<XRController>{RightHand}/devicePosition");
        _rightRotationAction = new InputAction("RightRotation", InputActionType.Value, "<XRController>{RightHand}/deviceRotation");

        _leftPositionAction.Enable();
        _leftRotationAction.Enable();
        _rightPositionAction.Enable();
        _rightRotationAction.Enable();

        // Log binding information to verify actions are properly bound
        Debug.Log($"[ControllerTrackingFix] Left Position bound controls: {_leftPositionAction.controls.Count}");
        Debug.Log($"[ControllerTrackingFix] Right Position bound controls: {_rightPositionAction.controls.Count}");

        // List available XR devices and find controllers for legacy fallback
        var xrDevices = new System.Collections.Generic.List<XRInputDevice>();
        InputDevices.GetDevices(xrDevices);
        Debug.Log($"[ControllerTrackingFix] XR Devices found: {xrDevices.Count}");
        foreach (var device in xrDevices)
        {
            Debug.Log($"[ControllerTrackingFix] Device: {device.name}, characteristics: {device.characteristics}");

            // Find left and right hand devices for legacy fallback
            if ((device.characteristics & InputDeviceCharacteristics.Left) != 0 &&
                (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
            {
                _leftHandDevice = device;
                Debug.Log($"[ControllerTrackingFix] Found left controller: {device.name}");
            }
            if ((device.characteristics & InputDeviceCharacteristics.Right) != 0 &&
                (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
            {
                _rightHandDevice = device;
                Debug.Log($"[ControllerTrackingFix] Found right controller: {device.name}");
            }
        }

        // If Input System didn't bind any controls, use legacy XR as fallback
        if (_leftPositionAction.controls.Count == 0 && _rightPositionAction.controls.Count == 0)
        {
            _useLegacyXR = true;
            Debug.Log("[ControllerTrackingFix] Input System has no bound controls, using Legacy XR API fallback");
        }

        _initialized = true;
        Debug.Log($"[ControllerTrackingFix] Controller tracking initialized (useLegacyXR={_useLegacyXR})");
    }

    private int _frameCount = 0;

    void Update()
    {
        if (!_initialized) return;

        _frameCount++;

        if (_useLegacyXR)
        {
            UpdateWithLegacyXR();
        }
        else
        {
            UpdateWithInputSystem();
        }
    }

    void UpdateWithInputSystem()
    {
        // Update left controller
        if (_leftController != null && _leftPositionAction != null)
        {
            Vector3 pos = _leftPositionAction.ReadValue<Vector3>();
            Quaternion rot = _leftRotationAction.ReadValue<Quaternion>();

            // Debug every 60 frames
            if (_frameCount % 60 == 0)
            {
                Debug.Log($"[ControllerTrackingFix] Left InputSystem: pos={pos}, rot={rot.eulerAngles}");
            }

            // Only update if we have valid data
            if (pos != Vector3.zero || rot != Quaternion.identity)
            {
                _leftController.position = _cameraFloorOffset.TransformPoint(pos);
                // Apply rotation with offset to correct controller orientation
                _leftController.rotation = _cameraFloorOffset.rotation * rot * _rotationOffsetQuat;
            }
        }

        // Update right controller
        if (_rightController != null && _rightPositionAction != null)
        {
            Vector3 pos = _rightPositionAction.ReadValue<Vector3>();
            Quaternion rot = _rightRotationAction.ReadValue<Quaternion>();

            // Debug every 60 frames
            if (_frameCount % 60 == 0)
            {
                Debug.Log($"[ControllerTrackingFix] Right InputSystem: pos={pos}, rot={rot.eulerAngles}");
            }

            // Only update if we have valid data
            if (pos != Vector3.zero || rot != Quaternion.identity)
            {
                _rightController.position = _cameraFloorOffset.TransformPoint(pos);
                // Apply rotation with offset to correct controller orientation
                _rightController.rotation = _cameraFloorOffset.rotation * rot * _rotationOffsetQuat;
            }
        }
    }

    void UpdateWithLegacyXR()
    {
        // Update left controller using Legacy XR API
        if (_leftController != null && _leftHandDevice.isValid)
        {
            Vector3 pos;
            Quaternion rot;

            bool hasPos = _leftHandDevice.TryGetFeatureValue(XRCommonUsages.devicePosition, out pos);
            bool hasRot = _leftHandDevice.TryGetFeatureValue(XRCommonUsages.deviceRotation, out rot);

            // Debug every 60 frames
            if (_frameCount % 60 == 0)
            {
                Debug.Log($"[ControllerTrackingFix] Left LegacyXR: pos={pos} (valid={hasPos}), rot={rot.eulerAngles} (valid={hasRot})");
            }

            if (hasPos && hasRot)
            {
                _leftController.position = _cameraFloorOffset.TransformPoint(pos);
                // Apply rotation - offset can be adjusted in Inspector if needed
                if (applyOffsetInTrackingSpace)
                    _leftController.rotation = _cameraFloorOffset.rotation * _rotationOffsetQuat * rot;
                else
                    _leftController.rotation = _cameraFloorOffset.rotation * rot * _rotationOffsetQuat;
            }
        }

        // Update right controller using Legacy XR API
        if (_rightController != null && _rightHandDevice.isValid)
        {
            Vector3 pos;
            Quaternion rot;

            bool hasPos = _rightHandDevice.TryGetFeatureValue(XRCommonUsages.devicePosition, out pos);
            bool hasRot = _rightHandDevice.TryGetFeatureValue(XRCommonUsages.deviceRotation, out rot);

            // Debug every 60 frames
            if (_frameCount % 60 == 0)
            {
                Debug.Log($"[ControllerTrackingFix] Right LegacyXR: pos={pos} (valid={hasPos}), rot={rot.eulerAngles} (valid={hasRot})");
            }

            if (hasPos && hasRot)
            {
                _rightController.position = _cameraFloorOffset.TransformPoint(pos);
                // Apply rotation - offset can be adjusted in Inspector if needed
                if (applyOffsetInTrackingSpace)
                    _rightController.rotation = _cameraFloorOffset.rotation * _rotationOffsetQuat * rot;
                else
                    _rightController.rotation = _cameraFloorOffset.rotation * rot * _rotationOffsetQuat;
            }
        }
    }

    /// <summary>
    /// Disables TrackedPoseDrivers on controllers to prevent them from overriding our manual tracking.
    /// The built-in TrackedPoseDrivers read (0,0,0) because Input System doesn't detect XR controllers.
    /// </summary>
    void DisableControllerTrackedPoseDrivers()
    {
        int disabledCount = 0;

        if (_leftController != null)
        {
            var tpd = _leftController.GetComponent<TrackedPoseDriver>();
            if (tpd != null)
            {
                tpd.enabled = false;
                disabledCount++;
                Debug.Log($"[ControllerTrackingFix] Disabled TrackedPoseDriver on {_leftController.name}");
            }
        }

        if (_rightController != null)
        {
            var tpd = _rightController.GetComponent<TrackedPoseDriver>();
            if (tpd != null)
            {
                tpd.enabled = false;
                disabledCount++;
                Debug.Log($"[ControllerTrackingFix] Disabled TrackedPoseDriver on {_rightController.name}");
            }
        }

        Debug.Log($"[ControllerTrackingFix] Disabled {disabledCount} TrackedPoseDrivers on controllers");
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
        // Unsubscribe from device events
        InputDevices.deviceConnected -= OnDeviceConnected;
        InputDevices.deviceDisconnected -= OnDeviceDisconnected;

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
