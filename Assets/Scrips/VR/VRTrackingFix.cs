using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using Unity.XR.CoreUtils;
using System.Collections;
using System.Linq;

/// <summary>
/// VR Tracking Fix - Ensures VR head and controller tracking works properly.
/// Attach this to the XR Origin GameObject or keep it in the scene.
/// </summary>
public class VRTrackingFix : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Log detailed tracking information")]
    public bool debugLogs = true;

    [Header("Settings")]
    [Tooltip("Force Floor tracking origin mode")]
    public bool forceFloorMode = true;

    [Tooltip("Camera Y offset when using Device tracking mode")]
    public float deviceModeYOffset = 1.36f;

    private XROrigin _xrOrigin;
    private Camera _mainCamera;
    private TrackedPoseDriver _headTrackedPoseDriver;
    private InputActionManager _inputActionManager;
    private bool _trackingInitialized = false;

    void Start()
    {
        StartCoroutine(InitializeTracking());
    }

    IEnumerator InitializeTracking()
    {
        // Wait a frame for everything to initialize
        yield return null;

        // Find XROrigin
        _xrOrigin = FindFirstObjectByType<XROrigin>();
        if (_xrOrigin == null)
        {
            Debug.LogError("[VRTrackingFix] No XROrigin found in scene!");
            yield break;
        }

        if (debugLogs)
            Debug.Log($"[VRTrackingFix] Found XROrigin: {_xrOrigin.name}");

        // Configure tracking origin mode
        ConfigureTrackingOriginMode();

        // Find and configure main camera
        _mainCamera = _xrOrigin.Camera;
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera == null)
        {
            Debug.LogError("[VRTrackingFix] No Main Camera found!");
            yield break;
        }

        if (debugLogs)
            Debug.Log($"[VRTrackingFix] Found Main Camera: {_mainCamera.name}");

        // Find TrackedPoseDriver on camera
        _headTrackedPoseDriver = _mainCamera.GetComponent<TrackedPoseDriver>();
        if (_headTrackedPoseDriver == null)
        {
            Debug.LogWarning("[VRTrackingFix] No TrackedPoseDriver on Main Camera! Adding one...");
            _headTrackedPoseDriver = AddTrackedPoseDriverToCamera();
        }

        // Ensure TrackedPoseDriver is enabled
        if (_headTrackedPoseDriver != null)
        {
            _headTrackedPoseDriver.enabled = true;
            if (debugLogs)
                Debug.Log($"[VRTrackingFix] TrackedPoseDriver enabled: {_headTrackedPoseDriver.enabled}, TrackingType: {_headTrackedPoseDriver.trackingType}");
        }

        // Find and activate InputActionManager
        _inputActionManager = FindFirstObjectByType<InputActionManager>();
        if (_inputActionManager != null)
        {
            // Force enable all action assets
            if (_inputActionManager.actionAssets != null)
            {
                foreach (var asset in _inputActionManager.actionAssets)
                {
                    if (asset != null)
                    {
                        asset.Enable();
                        if (debugLogs)
                            Debug.Log($"[VRTrackingFix] Enabled Input Action Asset: {asset.name}");
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("[VRTrackingFix] No InputActionManager found - Input Actions may not be activated!");
        }

        // Wait another frame and verify
        yield return null;
        VerifyTracking();

        _trackingInitialized = true;
    }

    void ConfigureTrackingOriginMode()
    {
        if (_xrOrigin == null) return;

        if (forceFloorMode)
        {
            // Set to Floor mode - the XR runtime will report positions relative to floor level
            _xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            if (debugLogs)
                Debug.Log("[VRTrackingFix] Set TrackingOriginMode to Floor");
        }
        else
        {
            // Device mode - camera starts at HMD position
            _xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            _xrOrigin.CameraYOffset = deviceModeYOffset;
            if (debugLogs)
                Debug.Log($"[VRTrackingFix] Set TrackingOriginMode to Device with Y offset: {deviceModeYOffset}");
        }
    }

    TrackedPoseDriver AddTrackedPoseDriverToCamera()
    {
        if (_mainCamera == null) return null;

        var tpd = _mainCamera.gameObject.AddComponent<TrackedPoseDriver>();

        // Configure for head tracking
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        // Create and configure position action
        var posAction = new InputAction("Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition");
        posAction.Enable();
        tpd.positionInput = new InputActionProperty(posAction);

        // Create and configure rotation action
        var rotAction = new InputAction("Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
        rotAction.Enable();
        tpd.rotationInput = new InputActionProperty(rotAction);

        if (debugLogs)
            Debug.Log("[VRTrackingFix] Added TrackedPoseDriver to Main Camera with centerEye bindings");

        return tpd;
    }

    void VerifyTracking()
    {
        if (!debugLogs) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== VR TRACKING FIX VERIFICATION ===");

        // Check XR subsystems
        var displays = new System.Collections.Generic.List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        sb.AppendLine($"XR Displays: {displays.Count}");
        foreach (var d in displays)
            sb.AppendLine($"  - {d.subsystemDescriptor.id} running={d.running}");

        // Check XROrigin
        if (_xrOrigin != null)
        {
            sb.AppendLine($"XROrigin: {_xrOrigin.name}");
            sb.AppendLine($"  TrackingOriginMode: {_xrOrigin.RequestedTrackingOriginMode} (Current: {_xrOrigin.CurrentTrackingOriginMode})");
            sb.AppendLine($"  CameraYOffset: {_xrOrigin.CameraYOffset}");
            sb.AppendLine($"  CameraFloorOffsetObject: {(_xrOrigin.CameraFloorOffsetObject != null ? _xrOrigin.CameraFloorOffsetObject.name : "NULL")}");
        }

        // Check Main Camera
        if (_mainCamera != null)
        {
            sb.AppendLine($"Main Camera: {_mainCamera.name}");
            sb.AppendLine($"  Position: {_mainCamera.transform.position}");
            sb.AppendLine($"  Rotation: {_mainCamera.transform.rotation.eulerAngles}");
        }

        // Check TrackedPoseDriver
        if (_headTrackedPoseDriver != null)
        {
            sb.AppendLine($"TrackedPoseDriver: enabled={_headTrackedPoseDriver.enabled}");
            sb.AppendLine($"  TrackingType: {_headTrackedPoseDriver.trackingType}");
            sb.AppendLine($"  UpdateType: {_headTrackedPoseDriver.updateType}");

            // Check if position input is working
            var posInput = _headTrackedPoseDriver.positionInput;
            if (posInput.action != null)
            {
                sb.AppendLine($"  PositionInput: enabled={posInput.action.enabled}, phase={posInput.action.phase}");
                if (posInput.action.controls.Count > 0)
                {
                    sb.AppendLine($"    Bound to: {posInput.action.controls[0].path}");
                    sb.AppendLine($"    Value: {posInput.action.ReadValue<Vector3>()}");
                }
                else
                {
                    sb.AppendLine("    WARNING: No controls bound!");
                }
            }
            else
            {
                sb.AppendLine("  WARNING: PositionInput action is NULL!");
            }

            var rotInput = _headTrackedPoseDriver.rotationInput;
            if (rotInput.action != null)
            {
                sb.AppendLine($"  RotationInput: enabled={rotInput.action.enabled}, phase={rotInput.action.phase}");
            }
        }
        else
        {
            sb.AppendLine("WARNING: No TrackedPoseDriver found!");
        }

        // Check HMD directly using XR namespace
        var hmdDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmdDevices);
        sb.AppendLine($"HMD Devices: {hmdDevices.Count}");
        foreach (var hmd in hmdDevices)
        {
            sb.AppendLine($"  - {hmd.name} (valid={hmd.isValid})");
            if (hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 pos))
                sb.AppendLine($"    Position: {pos}");
            if (hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rot))
                sb.AppendLine($"    Rotation: {rot.eulerAngles}");
            if (hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out InputTrackingState state))
                sb.AppendLine($"    TrackingState: {state}");
        }

        Debug.Log(sb.ToString());
    }

    void Update()
    {
        if (!_trackingInitialized) return;

        // Continuously verify tracking is working
        if (debugLogs && Time.frameCount % 300 == 0) // Every ~5 seconds at 60fps
        {
            DiagnoseTrackingIssue();
        }
    }

    void DiagnoseTrackingIssue()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== VR TRACKING DIAGNOSIS ===");

        // 1. Check if XR is running
        var xrManager = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        bool xrRunning = xrManager != null && xrManager.Manager != null && xrManager.Manager.activeLoader != null;
        sb.AppendLine($"XR Running: {xrRunning}");
        if (xrManager?.Manager?.activeLoader != null)
            sb.AppendLine($"  Loader: {xrManager.Manager.activeLoader.name}");

        // 2. Check HMD raw data from XR subsystem
        var hmdDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmdDevices);
        if (hmdDevices.Count > 0)
        {
            var hmd = hmdDevices[0];
            hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 hmdPos);
            hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion hmdRot);
            sb.AppendLine($"HMD Raw Data: Pos={hmdPos}, Rot={hmdRot.eulerAngles}");
        }
        else
        {
            sb.AppendLine("HMD: NOT DETECTED!");
        }

        // 3. Check Camera position
        if (_mainCamera != null)
        {
            sb.AppendLine($"Camera Transform: Pos={_mainCamera.transform.position}, Rot={_mainCamera.transform.rotation.eulerAngles}");
            sb.AppendLine($"Camera LocalPos: {_mainCamera.transform.localPosition}, LocalRot={_mainCamera.transform.localRotation.eulerAngles}");
        }

        // 4. Check TrackedPoseDriver status in detail
        if (_headTrackedPoseDriver != null)
        {
            sb.AppendLine($"TrackedPoseDriver: enabled={_headTrackedPoseDriver.enabled}");
            sb.AppendLine($"  TrackingType: {_headTrackedPoseDriver.trackingType}");
            sb.AppendLine($"  UpdateType: {_headTrackedPoseDriver.updateType}");

            // Check position input
            var posInput = _headTrackedPoseDriver.positionInput;
            if (posInput.action != null)
            {
                sb.AppendLine($"  PositionInput: enabled={posInput.action.enabled}, phase={posInput.action.phase}");
                sb.AppendLine($"    Bindings: {string.Join(", ", posInput.action.bindings.Select(b => b.path))}");
                sb.AppendLine($"    Controls: {posInput.action.controls.Count}");
                if (posInput.action.controls.Count > 0)
                {
                    var ctrl = posInput.action.controls[0];
                    sb.AppendLine($"    Control: {ctrl.path}, value={posInput.action.ReadValue<Vector3>()}");
                }
                else
                {
                    sb.AppendLine("    WARNING: No controls bound to position action!");
                }
            }
            else
            {
                sb.AppendLine("  PositionInput: ACTION IS NULL!");
            }

            // Check rotation input
            var rotInput = _headTrackedPoseDriver.rotationInput;
            if (rotInput.action != null)
            {
                sb.AppendLine($"  RotationInput: enabled={rotInput.action.enabled}, phase={rotInput.action.phase}");
                if (rotInput.action.controls.Count > 0)
                {
                    sb.AppendLine($"    Value: {rotInput.action.ReadValue<Quaternion>().eulerAngles}");
                }
            }
        }
        else
        {
            sb.AppendLine("TrackedPoseDriver: NOT FOUND!");
        }

        // 5. Check all active Input Actions
        sb.AppendLine("Active Input Actions:");
        var allActions = InputSystem.ListEnabledActions();
        var xrActions = allActions.Where(a => a.name.Contains("Position") || a.name.Contains("Rotation") || a.name.Contains("Head") || a.name.Contains("Eye")).Take(10);
        foreach (var action in xrActions)
        {
            sb.AppendLine($"  - {action.name}: enabled={action.enabled}, controls={action.controls.Count}");
        }

        // 6. Check XROrigin tracking
        if (_xrOrigin != null)
        {
            sb.AppendLine($"XROrigin: {_xrOrigin.name}");
            sb.AppendLine($"  RequestedMode: {_xrOrigin.RequestedTrackingOriginMode}");
            sb.AppendLine($"  CurrentMode: {_xrOrigin.CurrentTrackingOriginMode}");
            sb.AppendLine($"  CameraYOffset: {_xrOrigin.CameraYOffset}");
            if (_xrOrigin.CameraFloorOffsetObject != null)
            {
                sb.AppendLine($"  FloorOffset Pos: {_xrOrigin.CameraFloorOffsetObject.transform.localPosition}");
            }
        }

        Debug.Log(sb.ToString());
    }
}
