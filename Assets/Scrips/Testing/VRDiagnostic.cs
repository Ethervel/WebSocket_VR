using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using System.Collections.Generic;
using UnityEngine.InputSystem.XR;

/// <summary>
/// VR Diagnostic Script - Attach to any GameObject in the scene
/// Shows real-time VR tracking status in the console and on-screen
/// </summary>
public class VRDiagnostic : MonoBehaviour
{
    [Header("Settings")]
    public bool showOnScreenGUI = true;
    public bool logEveryFrame = false;

    private float _logInterval = 1f;
    private float _lastLogTime;

    private XRInputSubsystem _inputSubsystem;
    private bool _xrRunning;
    private string _diagnosticText = "Initializing...";

    void Start()
    {
        Debug.Log("=== VR DIAGNOSTIC START ===");
        CheckXRStatus();
        InvokeRepeating(nameof(CheckXRStatus), 1f, 2f);
    }

    void Update()
    {
        if (logEveryFrame || Time.time - _lastLogTime > _logInterval)
        {
            _lastLogTime = Time.time;
            UpdateDiagnostic();
        }
    }

    void CheckXRStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== VR DIAGNOSTIC ===");

        // 1. Check XR Management
        var xrManager = XRGeneralSettings.Instance;
        if (xrManager == null)
        {
            sb.AppendLine("[ERREUR] XRGeneralSettings.Instance est NULL!");
            sb.AppendLine("-> XR n'est pas configure dans Project Settings > XR Plug-in Management");
            _diagnosticText = sb.ToString();
            Debug.LogError(_diagnosticText);
            return;
        }

        sb.AppendLine($"[OK] XRGeneralSettings trouve");

        var loader = xrManager.Manager?.activeLoader;
        if (loader == null)
        {
            sb.AppendLine("[ERREUR] Aucun XR Loader actif!");
            sb.AppendLine("-> Verifiez Project Settings > XR Plug-in Management");
            sb.AppendLine("-> OpenXR doit etre coche pour Standalone/Android");
            _xrRunning = false;
        }
        else
        {
            sb.AppendLine($"[OK] XR Loader actif: {loader.name}");
            _xrRunning = true;
        }

        // 2. Check XR Display
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        sb.AppendLine($"[INFO] XR Displays: {displays.Count}");
        foreach (var display in displays)
        {
            sb.AppendLine($"  - {display.SubsystemDescriptor.id} running={display.running}");
        }

        // 3. Check XR Input
        var inputs = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputs);
        sb.AppendLine($"[INFO] XR Input Subsystems: {inputs.Count}");
        foreach (var input in inputs)
        {
            sb.AppendLine($"  - {input.SubsystemDescriptor.id} running={input.running}");
            _inputSubsystem = input;
        }

        // 4. Check connected devices
        var devices = new List<InputDevice>();
        InputDevices.GetDevices(devices);
        sb.AppendLine($"[INFO] Input Devices: {devices.Count}");
        foreach (var device in devices)
        {
            sb.AppendLine($"  - {device.name} (valid={device.isValid})");
        }

        // 5. Specifically check HMD
        var hmdDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmdDevices);
        if (hmdDevices.Count == 0)
        {
            sb.AppendLine("[ERREUR] Aucun casque VR detecte!");
        }
        else
        {
            foreach (var hmd in hmdDevices)
            {
                sb.AppendLine($"[OK] Casque VR: {hmd.name}");

                // Try to get position
                if (hmd.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
                {
                    sb.AppendLine($"  Position: {pos}");
                }
                else
                {
                    sb.AppendLine("  [WARN] Position non disponible");
                }

                if (hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
                {
                    sb.AppendLine($"  Rotation: {rot.eulerAngles}");
                }
                else
                {
                    sb.AppendLine("  [WARN] Rotation non disponible");
                }

                if (hmd.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state))
                {
                    sb.AppendLine($"  Tracking State: {state}");
                }
            }
        }

        // 6. Check controllers
        var leftControllers = new List<InputDevice>();
        var rightControllers = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, leftControllers);
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rightControllers);

        sb.AppendLine($"[INFO] Left Controllers: {leftControllers.Count}");
        sb.AppendLine($"[INFO] Right Controllers: {rightControllers.Count}");

        // 7. Check TrackedPoseDriver on cameras
        var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        sb.AppendLine($"[INFO] Cameras in scene: {cameras.Length}");
        foreach (var cam in cameras)
        {
            if (!cam.enabled) continue;

            var tpd = cam.GetComponent<TrackedPoseDriver>();
            if (tpd != null)
            {
                sb.AppendLine($"  - {cam.name}: TrackedPoseDriver present, enabled={tpd.enabled}");
                sb.AppendLine($"    TrackingType: {tpd.trackingType}");
            }
            else
            {
                sb.AppendLine($"  - {cam.name}: NO TrackedPoseDriver!");
            }
        }

        // 8. Check XROrigin
        var xrOrigins = FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsSortMode.None);
        sb.AppendLine($"[INFO] XROrigins: {xrOrigins.Length}");
        foreach (var origin in xrOrigins)
        {
            sb.AppendLine($"  - {origin.name}");
            sb.AppendLine($"    Camera: {(origin.Camera != null ? origin.Camera.name : "NULL")}");
            sb.AppendLine($"    CameraFloorOffsetObject: {(origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.name : "NULL")}");
            sb.AppendLine($"    TrackingOriginMode: {origin.RequestedTrackingOriginMode}");
            sb.AppendLine($"    CameraYOffset: {origin.CameraYOffset}");
        }

        _diagnosticText = sb.ToString();
        Debug.Log(_diagnosticText);
    }

    void UpdateDiagnostic()
    {
        // Quick update for position tracking
        var hmdDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmdDevices);

        if (hmdDevices.Count > 0)
        {
            var hmd = hmdDevices[0];
            if (hmd.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos) &&
                hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            {
                if (logEveryFrame)
                    Debug.Log($"[VR] HMD Pos: {pos}, Rot: {rot.eulerAngles}");
            }
        }
    }

    void OnGUI()
    {
        if (!showOnScreenGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 14;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.white;

        // Background
        GUI.backgroundColor = new Color(0, 0, 0, 0.7f);

        // Draw diagnostic text
        GUI.Box(new Rect(10, 10, 500, 400), _diagnosticText, style);

        // XR Status indicator
        GUI.backgroundColor = _xrRunning ? Color.green : Color.red;
        GUI.Box(new Rect(10, 420, 200, 30), _xrRunning ? "XR RUNNING" : "XR NOT RUNNING", style);
    }
}
