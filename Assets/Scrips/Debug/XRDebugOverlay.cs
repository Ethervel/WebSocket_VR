using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Debug overlay that shows XR status on screen.
/// Attach to a Canvas with a TextMeshProUGUI to see XR debug info on Quest.
/// </summary>
public class XRDebugOverlay : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Text component to display debug info (auto-created if null)")]
    public TextMeshProUGUI debugText;

    [Header("Settings")]
    public bool showOnScreen = true;
    public KeyCode toggleKey = KeyCode.F12;

    private float _updateInterval = 0.5f;
    private float _timer = 0f;

    void Start()
    {
        if (debugText == null && showOnScreen)
        {
            CreateDebugUI();
        }

        // Log initial state
        LogXRStatus();
    }

    void CreateDebugUI()
    {
        // Create canvas
        var canvasGO = new GameObject("XRDebugCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();

        // Create text
        var textGO = new GameObject("DebugText");
        textGO.transform.SetParent(canvasGO.transform, false);

        debugText = textGO.AddComponent<TextMeshProUGUI>();
        debugText.fontSize = 14;
        debugText.color = Color.yellow;
        debugText.alignment = TextAlignmentOptions.TopLeft;

        var rect = debugText.rectTransform;
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(10, 10);
        rect.offsetMax = new Vector2(-10, -10);

        DontDestroyOnLoad(canvasGO);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            showOnScreen = !showOnScreen;
            if (debugText != null)
                debugText.gameObject.SetActive(showOnScreen);
        }

        _timer += Time.deltaTime;
        if (_timer >= _updateInterval)
        {
            _timer = 0f;
            UpdateDebugText();
        }
    }

    void LogXRStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== XR DEBUG STATUS ===");
        sb.AppendLine($"Platform: {Application.platform}");
        sb.AppendLine($"Is Editor: {Application.isEditor}");

        var xrSettings = XRGeneralSettings.Instance;
        sb.AppendLine($"XRGeneralSettings.Instance: {(xrSettings != null ? "OK" : "NULL")}");

        if (xrSettings != null)
        {
            sb.AppendLine($"  Manager: {(xrSettings.Manager != null ? "OK" : "NULL")}");
            if (xrSettings.Manager != null)
            {
                sb.AppendLine($"  ActiveLoader: {(xrSettings.Manager.activeLoader != null ? xrSettings.Manager.activeLoader.name : "NULL")}");
                sb.AppendLine($"  IsInitializationComplete: {xrSettings.Manager.isInitializationComplete}");
            }
        }

        // Check XR displays
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        sb.AppendLine($"XR Displays: {displays.Count}");
        foreach (var d in displays)
        {
            sb.AppendLine($"  - {d.subsystemDescriptor.id} running={d.running}");
        }

        // Check HMD
        var hmdDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmdDevices);
        sb.AppendLine($"HMD Devices: {hmdDevices.Count}");
        foreach (var hmd in hmdDevices)
        {
            sb.AppendLine($"  - {hmd.name} valid={hmd.isValid}");
            if (hmd.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
                sb.AppendLine($"    Pos: {pos}");
            if (hmd.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state))
                sb.AppendLine($"    TrackingState: {state}");
        }

        // Check Controllers
        var controllers = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, controllers);
        sb.AppendLine($"Controllers: {controllers.Count}");
        foreach (var ctrl in controllers)
        {
            sb.AppendLine($"  - {ctrl.name} valid={ctrl.isValid}");
        }

        // Check XR Interaction Simulator
        var simulator = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator>();
        sb.AppendLine($"XR Interaction Simulator: {(simulator != null ? (simulator.gameObject.activeInHierarchy ? "ACTIVE" : "DISABLED") : "NOT FOUND")}");

        Debug.Log(sb.ToString());
    }

    void UpdateDebugText()
    {
        if (debugText == null || !showOnScreen) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>XR DEBUG</b>");
        sb.AppendLine($"Platform: {Application.platform}");

        var xrSettings = XRGeneralSettings.Instance;
        if (xrSettings?.Manager != null)
        {
            var loader = xrSettings.Manager.activeLoader;
            sb.AppendLine($"Loader: {(loader != null ? loader.name : "NULL")}");
            sb.AppendLine($"Init: {xrSettings.Manager.isInitializationComplete}");
        }
        else
        {
            sb.AppendLine("<color=red>XR NOT INITIALIZED</color>");
        }

        // HMD
        var hmdDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmdDevices);
        if (hmdDevices.Count > 0 && hmdDevices[0].isValid)
        {
            hmdDevices[0].TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos);
            sb.AppendLine($"HMD: {pos:F2}");
        }
        else
        {
            sb.AppendLine("<color=red>HMD: NOT FOUND</color>");
        }

        // Controllers
        var controllers = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, controllers);
        sb.AppendLine($"Controllers: {controllers.Count}");

        // Simulator status
        var simulator = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator>();
        if (simulator != null && simulator.gameObject.activeInHierarchy)
        {
            sb.AppendLine("<color=red>SIMULATOR ACTIVE!</color>");
        }

        debugText.text = sb.ToString();
    }
}
