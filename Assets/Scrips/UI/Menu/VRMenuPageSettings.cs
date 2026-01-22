using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Threading.Tasks;

/// <summary>
/// Settings page - General application settings.
/// </summary>
public class VRMenuPageSettings : MonoBehaviour
{
    [Header("Movement Settings")]
    public Toggle smoothTurnToggle;
    public Slider turnSpeedSlider;
    public TextMeshProUGUI turnSpeedText;
    public Toggle vignetteToggle;

    [Header("Graphics Settings")]
    public TMP_Dropdown qualityDropdown;
    public Toggle showFPSToggle;

    [Header("Comfort Settings")]
    public Toggle snapTurnToggle;
    public Slider snapAngleSlider;
    public TextMeshProUGUI snapAngleText;

    [Header("Network Settings")]
    public TMP_InputField serverUrlInput;
    public Button reconnectButton;
    public TextMeshProUGUI connectionStatusText;

    [Header("About")]
    public TextMeshProUGUI versionText;

    void Start()
    {
        AutoFindReferences();
        Initialize();
        LoadSettings();
    }

    void AutoFindReferences()
    {
        // Find toggles
        Toggle[] toggles = GetComponentsInChildren<Toggle>(true);
        foreach (var t in toggles)
        {
            string n = t.name.ToLower();
            if (smoothTurnToggle == null && n.Contains("smooth"))
                smoothTurnToggle = t;
            else if (snapTurnToggle == null && n.Contains("snap"))
                snapTurnToggle = t;
            else if (vignetteToggle == null && n.Contains("vignette"))
                vignetteToggle = t;
            else if (showFPSToggle == null && n.Contains("fps"))
                showFPSToggle = t;
        }

        // Find sliders
        Slider[] sliders = GetComponentsInChildren<Slider>(true);
        foreach (var s in sliders)
        {
            string n = s.name.ToLower();
            if (turnSpeedSlider == null && n.Contains("turnspeed"))
                turnSpeedSlider = s;
            else if (snapAngleSlider == null && n.Contains("snapangle"))
                snapAngleSlider = s;
        }

        // Find dropdown
        if (qualityDropdown == null)
        {
            qualityDropdown = GetComponentInChildren<TMP_Dropdown>(true);
        }

        // Find input field
        if (serverUrlInput == null)
        {
            serverUrlInput = GetComponentInChildren<TMP_InputField>(true);
        }

        // Find buttons
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            string n = btn.name.ToLower();
            if (reconnectButton == null && n.Contains("reconnect"))
                reconnectButton = btn;
        }

        // Find texts
        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in texts)
        {
            string n = txt.name.ToLower();
            if (turnSpeedText == null && n.Contains("turnspeed") && n.Contains("text"))
                turnSpeedText = txt;
            else if (snapAngleText == null && n.Contains("snapangle") && n.Contains("text"))
                snapAngleText = txt;
            else if (versionText == null && n.Contains("version"))
                versionText = txt;
            else if (connectionStatusText == null && n.Contains("connection") && n.Contains("status"))
                connectionStatusText = txt;
        }

        Debug.Log($"[VRMenuPageSettings] AutoFind: snap={snapTurnToggle != null}, vignette={vignetteToggle != null}, quality={qualityDropdown != null}");
    }

    void OnEnable()
    {
        UpdateConnectionStatus();
    }

    void Initialize()
    {
        Debug.Log("[VRMenuPageSettings] Initializing...");

        // Smooth turn toggle
        if (smoothTurnToggle != null)
        {
            smoothTurnToggle.onValueChanged.AddListener(OnSmoothTurnChanged);
            Debug.Log("[VRMenuPageSettings] Smooth turn toggle connected");
        }

        // Turn speed slider
        if (turnSpeedSlider != null)
        {
            turnSpeedSlider.minValue = 30f;
            turnSpeedSlider.maxValue = 180f;
            turnSpeedSlider.onValueChanged.AddListener(OnTurnSpeedChanged);
            Debug.Log("[VRMenuPageSettings] Turn speed slider connected");
        }

        // Snap turn toggle
        if (snapTurnToggle != null)
        {
            snapTurnToggle.onValueChanged.AddListener(OnSnapTurnChanged);
            Debug.Log("[VRMenuPageSettings] Snap turn toggle connected");
        }
        else
        {
            Debug.LogWarning("[VRMenuPageSettings] Snap turn toggle is NULL!");
        }

        // Snap angle slider
        if (snapAngleSlider != null)
        {
            snapAngleSlider.minValue = 15f;
            snapAngleSlider.maxValue = 90f;
            snapAngleSlider.onValueChanged.AddListener(OnSnapAngleChanged);
        }

        // Vignette toggle
        if (vignetteToggle != null)
        {
            vignetteToggle.onValueChanged.AddListener(OnVignetteChanged);
            Debug.Log("[VRMenuPageSettings] Vignette toggle connected");
        }

        // Quality dropdown
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(new System.Collections.Generic.List<string>(QualitySettings.names));
            qualityDropdown.onValueChanged.AddListener(OnQualityChanged);
            Debug.Log("[VRMenuPageSettings] Quality dropdown connected");
        }
        else
        {
            Debug.LogWarning("[VRMenuPageSettings] Quality dropdown is NULL!");
        }

        // Show FPS toggle
        if (showFPSToggle != null)
        {
            showFPSToggle.onValueChanged.AddListener(OnShowFPSChanged);
            Debug.Log("[VRMenuPageSettings] Show FPS toggle connected");
        }

        // Server URL input
        if (serverUrlInput != null)
        {
            serverUrlInput.onEndEdit.AddListener(OnServerUrlChanged);
        }

        // Reconnect button
        if (reconnectButton != null)
        {
            reconnectButton.onClick.AddListener(OnReconnectClicked);
            Debug.Log("[VRMenuPageSettings] Reconnect button connected");
        }

        // Version text
        if (versionText != null)
        {
            versionText.text = $"Version: {Application.version}";
        }

        Debug.Log("[VRMenuPageSettings] Initialization complete");
    }

    void LoadSettings()
    {
        // Movement settings
        if (smoothTurnToggle != null)
        {
            smoothTurnToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt("SmoothTurn", 0) == 1);
        }

        if (turnSpeedSlider != null)
        {
            turnSpeedSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("TurnSpeed", 90f));
            UpdateTurnSpeedText(turnSpeedSlider.value);
        }

        if (snapTurnToggle != null)
        {
            snapTurnToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt("SnapTurn", 1) == 1);
        }

        if (snapAngleSlider != null)
        {
            snapAngleSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("SnapAngle", 45f));
            UpdateSnapAngleText(snapAngleSlider.value);
        }

        if (vignetteToggle != null)
        {
            vignetteToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt("Vignette", 1) == 1);
        }

        // Graphics settings
        if (qualityDropdown != null)
        {
            qualityDropdown.SetValueWithoutNotify(QualitySettings.GetQualityLevel());
        }

        if (showFPSToggle != null)
        {
            showFPSToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt("ShowFPS", 0) == 1);
        }

        // Network settings
        if (serverUrlInput != null)
        {
            var networkManager = VRNetworkManager.Instance;
            if (networkManager != null)
            {
                serverUrlInput.text = networkManager.serverUrl;
            }
        }
    }

    #region Movement Settings

    void OnSmoothTurnChanged(bool value)
    {
        PlayerPrefs.SetInt("SmoothTurn", value ? 1 : 0);
        PlayerPrefs.Save();

        // Apply to VR player controller if available (smooth turn = NOT snap turn)
        var playerController = FindFirstObjectByType<VRPlayerController>();
        if (playerController != null)
        {
            playerController.useSnapTurn = !value; // Invert: smooth turn ON = snap turn OFF
        }

        Debug.Log($"[Settings] Smooth turn: {value}");
    }

    void OnTurnSpeedChanged(float value)
    {
        PlayerPrefs.SetFloat("TurnSpeed", value);
        UpdateTurnSpeedText(value);

        var playerController = FindFirstObjectByType<VRPlayerController>();
        if (playerController != null)
        {
            playerController.smoothTurnSpeed = value;
        }
    }

    void UpdateTurnSpeedText(float value)
    {
        if (turnSpeedText != null)
        {
            turnSpeedText.text = $"{Mathf.RoundToInt(value)}°/s";
        }
    }

    void OnSnapTurnChanged(bool value)
    {
        PlayerPrefs.SetInt("SnapTurn", value ? 1 : 0);
        PlayerPrefs.Save();

        var playerController = FindFirstObjectByType<VRPlayerController>();
        if (playerController != null)
        {
            playerController.useSnapTurn = value;
        }

        Debug.Log($"[Settings] Snap turn: {value}");
    }

    void OnSnapAngleChanged(float value)
    {
        PlayerPrefs.SetFloat("SnapAngle", value);
        UpdateSnapAngleText(value);

        var playerController = FindFirstObjectByType<VRPlayerController>();
        if (playerController != null)
        {
            playerController.snapTurnAngle = value;
        }
    }

    void UpdateSnapAngleText(float value)
    {
        if (snapAngleText != null)
        {
            snapAngleText.text = $"{Mathf.RoundToInt(value)}°";
        }
    }

    void OnVignetteChanged(bool value)
    {
        PlayerPrefs.SetInt("Vignette", value ? 1 : 0);
        PlayerPrefs.Save();

        // Apply vignette effect (would need reference to post-processing)
        Debug.Log($"[Settings] Vignette: {value}");
    }

    #endregion

    #region Graphics Settings

    void OnQualityChanged(int index)
    {
        QualitySettings.SetQualityLevel(index);
        Debug.Log($"[Settings] Quality set to: {QualitySettings.names[index]}");
    }

    void OnShowFPSChanged(bool value)
    {
        PlayerPrefs.SetInt("ShowFPS", value ? 1 : 0);
        PlayerPrefs.Save();

        // Toggle FPS display (would need FPS counter component)
        Debug.Log($"[Settings] Show FPS: {value}");
    }

    #endregion

    #region Network Settings

    void OnServerUrlChanged(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        PlayerPrefs.SetString("ServerUrl", url);
        PlayerPrefs.Save();

        Debug.Log($"[Settings] Server URL set to: {url}");
    }

    async void OnReconnectClicked()
    {
        var networkManager = VRNetworkManager.Instance;
        if (networkManager != null)
        {
            if (serverUrlInput != null && !string.IsNullOrEmpty(serverUrlInput.text))
            {
                networkManager.serverUrl = serverUrlInput.text;
            }
            await networkManager.Connect();
        }
    }

    void UpdateConnectionStatus()
    {
        if (connectionStatusText == null) return;

        if (VRNetworkManager.IsConnected)
        {
            connectionStatusText.text = "Status: Connected";
            connectionStatusText.color = Color.green;
        }
        else
        {
            connectionStatusText.text = "Status: Disconnected";
            connectionStatusText.color = Color.red;
        }
    }

    #endregion

    public void ResetAllSettings()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        LoadSettings();
        Debug.Log("[Settings] All settings reset to default");
    }
}
