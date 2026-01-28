using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Management;

/// <summary>
/// Gère l'interface utilisateur du panneau Options.
/// Connecte les contrôles UI à MainMenuSettings.
/// </summary>
public class MainMenuOptionsUI : MonoBehaviour
{
    [Header("Tabs")]
    public Button audioTabButton;
    public Button graphicsTabButton;
    public Button controlsTabButton;
    public GameObject audioPanel;
    public GameObject graphicsPanel;
    public GameObject controlsPanel;

    [Header("Audio Settings")]
    public Slider masterVolumeSlider;
    public TextMeshProUGUI masterVolumeText;
    public Slider voiceVolumeSlider;
    public TextMeshProUGUI voiceVolumeText;
    public TMP_Dropdown microphoneDropdown;

    [Header("Graphics Settings")]
    public TMP_Dropdown qualityDropdown;
    public TMP_Dropdown resolutionDropdown;
    public Toggle fullscreenToggle;
    public GameObject graphicsDesktopOnly; // Container for desktop-only settings

    [Header("VR Controls")]
    public GameObject vrControlsPanel;
    public TMP_Dropdown turnModeDropdown;
    public Slider snapAngleSlider;
    public TextMeshProUGUI snapAngleText;
    public GameObject snapAngleContainer;
    public Slider smoothTurnSpeedSlider;
    public TextMeshProUGUI smoothTurnSpeedText;
    public GameObject smoothTurnContainer;

    [Header("Desktop Controls")]
    public GameObject desktopControlsPanel;
    public Slider mouseSensitivitySlider;
    public TextMeshProUGUI mouseSensitivityText;
    public Toggle invertYToggle;

    [Header("Buttons")]
    public Button resetButton;
    public Button applyButton;
    public Button backButton;

    [Header("Tab Colors")]
    public Color activeTabColor = new Color(0.3f, 0.6f, 1f, 1f);
    public Color inactiveTabColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    private bool _isVRMode;
    private List<Resolution> _availableResolutions = new List<Resolution>();
    private bool _isInitializing = false;

    void Awake()
    {
        DetectVRMode();
    }

    void OnEnable()
    {
        InitializeUI();
        ShowTab(0); // Audio tab by default
    }

    void DetectVRMode()
    {
        var xrSettings = XRGeneralSettings.Instance;
        _isVRMode = xrSettings != null &&
                    xrSettings.Manager != null &&
                    xrSettings.Manager.activeLoader != null;
    }

    void InitializeUI()
    {
        _isInitializing = true;

        // Setup tabs
        SetupTabs();

        // Setup audio
        SetupAudioControls();

        // Setup graphics
        SetupGraphicsControls();

        // Setup controls (VR/Desktop)
        SetupControlsSection();

        // Setup buttons
        SetupButtons();

        _isInitializing = false;
    }

    // ========== TABS ==========

    void SetupTabs()
    {
        if (audioTabButton != null)
            audioTabButton.onClick.AddListener(() => ShowTab(0));
        if (graphicsTabButton != null)
            graphicsTabButton.onClick.AddListener(() => ShowTab(1));
        if (controlsTabButton != null)
            controlsTabButton.onClick.AddListener(() => ShowTab(2));
    }

    public void ShowTab(int tabIndex)
    {
        // Update panels
        if (audioPanel != null) audioPanel.SetActive(tabIndex == 0);
        if (graphicsPanel != null) graphicsPanel.SetActive(tabIndex == 1);
        if (controlsPanel != null) controlsPanel.SetActive(tabIndex == 2);

        // Update tab button colors
        UpdateTabButtonColor(audioTabButton, tabIndex == 0);
        UpdateTabButtonColor(graphicsTabButton, tabIndex == 1);
        UpdateTabButtonColor(controlsTabButton, tabIndex == 2);
    }

    void UpdateTabButtonColor(Button button, bool isActive)
    {
        if (button == null) return;

        var colors = button.colors;
        colors.normalColor = isActive ? activeTabColor : inactiveTabColor;
        colors.highlightedColor = isActive ? activeTabColor : new Color(0.3f, 0.3f, 0.3f, 1f);
        button.colors = colors;

        // Also update text color if present
        var text = button.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
        {
            text.color = isActive ? Color.white : new Color(0.7f, 0.7f, 0.7f, 1f);
        }
    }

    // ========== AUDIO CONTROLS ==========

    void SetupAudioControls()
    {
        // Master Volume
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.minValue = 0f;
            masterVolumeSlider.maxValue = 1f;
            masterVolumeSlider.value = MainMenuSettings.GetMasterVolume();
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            UpdateMasterVolumeText(masterVolumeSlider.value);
        }

        // Voice Volume
        if (voiceVolumeSlider != null)
        {
            voiceVolumeSlider.minValue = 0f;
            voiceVolumeSlider.maxValue = 1f;
            voiceVolumeSlider.value = MainMenuSettings.GetVoiceVolume();
            voiceVolumeSlider.onValueChanged.AddListener(OnVoiceVolumeChanged);
            UpdateVoiceVolumeText(voiceVolumeSlider.value);
        }

        // Microphone
        if (microphoneDropdown != null)
        {
            PopulateMicrophoneDropdown();
        }
    }

    void PopulateMicrophoneDropdown()
    {
        microphoneDropdown.ClearOptions();

        var options = new List<string> { "Default" };
        foreach (var device in Microphone.devices)
        {
            options.Add(device);
        }

        microphoneDropdown.AddOptions(options);

        // Set current selection
        string currentMic = MainMenuSettings.GetMicrophoneDevice();
        int index = string.IsNullOrEmpty(currentMic) ? 0 : options.IndexOf(currentMic);
        if (index < 0) index = 0;
        microphoneDropdown.value = index;

        microphoneDropdown.onValueChanged.AddListener(OnMicrophoneChanged);
    }

    void OnMasterVolumeChanged(float value)
    {
        if (_isInitializing) return;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.MasterVolume = value;
        UpdateMasterVolumeText(value);
    }

    void OnVoiceVolumeChanged(float value)
    {
        if (_isInitializing) return;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.VoiceVolume = value;
        UpdateVoiceVolumeText(value);
    }

    void OnMicrophoneChanged(int index)
    {
        if (_isInitializing) return;
        if (MainMenuSettings.Instance != null)
        {
            string device = index == 0 ? "" : microphoneDropdown.options[index].text;
            MainMenuSettings.Instance.MicrophoneDevice = device;
        }
    }

    void UpdateMasterVolumeText(float value)
    {
        if (masterVolumeText != null)
            masterVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    void UpdateVoiceVolumeText(float value)
    {
        if (voiceVolumeText != null)
            voiceVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
    }

    // ========== GRAPHICS CONTROLS ==========

    void SetupGraphicsControls()
    {
        // Hide desktop-only settings in VR mode
        if (graphicsDesktopOnly != null)
            graphicsDesktopOnly.SetActive(!_isVRMode);

        // Quality
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
            qualityDropdown.value = MainMenuSettings.GetQualityLevel();
            qualityDropdown.onValueChanged.AddListener(OnQualityChanged);
        }

        // Resolution (desktop only)
        if (resolutionDropdown != null && !_isVRMode)
        {
            PopulateResolutionDropdown();
        }

        // Fullscreen (desktop only)
        if (fullscreenToggle != null && !_isVRMode)
        {
            fullscreenToggle.isOn = Screen.fullScreen;
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        }
    }

    void PopulateResolutionDropdown()
    {
        resolutionDropdown.ClearOptions();
        _availableResolutions.Clear();

        var options = new List<string>();
        Resolution currentRes = Screen.currentResolution;
        int currentIndex = 0;

        // Filter to unique resolutions (ignore refresh rate variations)
        HashSet<string> seen = new HashSet<string>();
        foreach (var res in Screen.resolutions)
        {
            string key = $"{res.width}x{res.height}";
            if (!seen.Contains(key))
            {
                seen.Add(key);
                _availableResolutions.Add(res);
                options.Add(key);

                if (res.width == currentRes.width && res.height == currentRes.height)
                {
                    currentIndex = _availableResolutions.Count - 1;
                }
            }
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentIndex;
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
    }

    void OnQualityChanged(int index)
    {
        if (_isInitializing) return;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.QualityLevel = index;
    }

    void OnResolutionChanged(int index)
    {
        if (_isInitializing || index < 0 || index >= _availableResolutions.Count) return;

        var res = _availableResolutions[index];
        Screen.SetResolution(res.width, res.height, Screen.fullScreen);

        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.ResolutionIndex = index;
    }

    void OnFullscreenChanged(bool isFullscreen)
    {
        if (_isInitializing) return;
        Screen.fullScreen = isFullscreen;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.IsFullscreen = isFullscreen;
    }

    // ========== CONTROLS SECTION ==========

    void SetupControlsSection()
    {
        // Show appropriate panel based on mode
        if (vrControlsPanel != null)
            vrControlsPanel.SetActive(_isVRMode);
        if (desktopControlsPanel != null)
            desktopControlsPanel.SetActive(!_isVRMode);

        if (_isVRMode)
        {
            SetupVRControls();
        }
        else
        {
            SetupDesktopControls();
        }
    }

    void SetupVRControls()
    {
        // Turn Mode
        if (turnModeDropdown != null)
        {
            turnModeDropdown.ClearOptions();
            turnModeDropdown.AddOptions(new List<string> { "Snap Turn", "Smooth Turn" });
            turnModeDropdown.value = MainMenuSettings.GetTurnMode();
            turnModeDropdown.onValueChanged.AddListener(OnTurnModeChanged);
            UpdateTurnModeVisibility(turnModeDropdown.value);
        }

        // Snap Angle
        if (snapAngleSlider != null)
        {
            snapAngleSlider.minValue = 15f;
            snapAngleSlider.maxValue = 90f;
            snapAngleSlider.value = MainMenuSettings.GetSnapAngle();
            snapAngleSlider.onValueChanged.AddListener(OnSnapAngleChanged);
            UpdateSnapAngleText(snapAngleSlider.value);
        }

        // Smooth Turn Speed
        if (smoothTurnSpeedSlider != null)
        {
            smoothTurnSpeedSlider.minValue = 30f;
            smoothTurnSpeedSlider.maxValue = 180f;
            smoothTurnSpeedSlider.value = MainMenuSettings.GetSmoothTurnSpeed();
            smoothTurnSpeedSlider.onValueChanged.AddListener(OnSmoothTurnSpeedChanged);
            UpdateSmoothTurnSpeedText(smoothTurnSpeedSlider.value);
        }
    }

    void SetupDesktopControls()
    {
        // Mouse Sensitivity
        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.minValue = 0.1f;
            mouseSensitivitySlider.maxValue = 10f;
            mouseSensitivitySlider.value = MainMenuSettings.GetMouseSensitivity();
            mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);
            UpdateMouseSensitivityText(mouseSensitivitySlider.value);
        }

        // Invert Y
        if (invertYToggle != null)
        {
            invertYToggle.isOn = MainMenuSettings.GetInvertY();
            invertYToggle.onValueChanged.AddListener(OnInvertYChanged);
        }
    }

    void OnTurnModeChanged(int value)
    {
        if (_isInitializing) return;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.TurnMode = value;
        UpdateTurnModeVisibility(value);
    }

    void UpdateTurnModeVisibility(int turnMode)
    {
        // Show snap angle controls for snap turn (0), smooth speed for smooth turn (1)
        if (snapAngleContainer != null)
            snapAngleContainer.SetActive(turnMode == 0);
        if (smoothTurnContainer != null)
            smoothTurnContainer.SetActive(turnMode == 1);
    }

    void OnSnapAngleChanged(float value)
    {
        if (_isInitializing) return;
        // Round to nearest 5 degrees
        float rounded = Mathf.Round(value / 5f) * 5f;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.SnapAngle = rounded;
        UpdateSnapAngleText(rounded);
    }

    void OnSmoothTurnSpeedChanged(float value)
    {
        if (_isInitializing) return;
        // Round to nearest 10
        float rounded = Mathf.Round(value / 10f) * 10f;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.SmoothTurnSpeed = rounded;
        UpdateSmoothTurnSpeedText(rounded);
    }

    void OnMouseSensitivityChanged(float value)
    {
        if (_isInitializing) return;
        // Round to 1 decimal
        float rounded = Mathf.Round(value * 10f) / 10f;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.MouseSensitivity = rounded;
        UpdateMouseSensitivityText(rounded);
    }

    void OnInvertYChanged(bool value)
    {
        if (_isInitializing) return;
        if (MainMenuSettings.Instance != null)
            MainMenuSettings.Instance.InvertY = value;
    }

    void UpdateSnapAngleText(float value)
    {
        if (snapAngleText != null)
            snapAngleText.text = $"{Mathf.RoundToInt(value)}°";
    }

    void UpdateSmoothTurnSpeedText(float value)
    {
        if (smoothTurnSpeedText != null)
            smoothTurnSpeedText.text = $"{Mathf.RoundToInt(value)}°/s";
    }

    void UpdateMouseSensitivityText(float value)
    {
        if (mouseSensitivityText != null)
            mouseSensitivityText.text = $"{value:F1}";
    }

    // ========== BUTTONS ==========

    void SetupButtons()
    {
        if (resetButton != null)
            resetButton.onClick.AddListener(OnResetClicked);

        if (applyButton != null)
            applyButton.onClick.AddListener(OnApplyClicked);

        // Note: backButton is handled by MainMenuManager
    }

    void OnResetClicked()
    {
        if (MainMenuSettings.Instance != null)
        {
            MainMenuSettings.Instance.ResetToDefaults();
            RefreshAllControls();
        }
        Debug.Log("[OptionsUI] Settings reset to defaults");
    }

    void OnApplyClicked()
    {
        // Settings are already applied in real-time, this is just for user feedback
        Debug.Log("[OptionsUI] Settings applied");
    }

    void RefreshAllControls()
    {
        _isInitializing = true;

        // Audio
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = MainMenuSettings.GetMasterVolume();
            UpdateMasterVolumeText(masterVolumeSlider.value);
        }
        if (voiceVolumeSlider != null)
        {
            voiceVolumeSlider.value = MainMenuSettings.GetVoiceVolume();
            UpdateVoiceVolumeText(voiceVolumeSlider.value);
        }

        // Graphics
        if (qualityDropdown != null)
            qualityDropdown.value = MainMenuSettings.GetQualityLevel();
        if (fullscreenToggle != null)
            fullscreenToggle.isOn = Screen.fullScreen;

        // VR Controls
        if (turnModeDropdown != null)
        {
            turnModeDropdown.value = MainMenuSettings.GetTurnMode();
            UpdateTurnModeVisibility(turnModeDropdown.value);
        }
        if (snapAngleSlider != null)
        {
            snapAngleSlider.value = MainMenuSettings.GetSnapAngle();
            UpdateSnapAngleText(snapAngleSlider.value);
        }
        if (smoothTurnSpeedSlider != null)
        {
            smoothTurnSpeedSlider.value = MainMenuSettings.GetSmoothTurnSpeed();
            UpdateSmoothTurnSpeedText(smoothTurnSpeedSlider.value);
        }

        // Desktop Controls
        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.value = MainMenuSettings.GetMouseSensitivity();
            UpdateMouseSensitivityText(mouseSensitivitySlider.value);
        }
        if (invertYToggle != null)
            invertYToggle.isOn = MainMenuSettings.GetInvertY();

        _isInitializing = false;
    }
}
