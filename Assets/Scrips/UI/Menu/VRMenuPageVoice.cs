using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Voice page - Microphone toggle and volume controls.
/// Device selection is done in Main Menu only.
/// </summary>
public class VRMenuPageVoice : MonoBehaviour
{
    [Header("Microphone Toggle")]
    public Toggle microphoneToggle;
    public TextMeshProUGUI micStatusText;
    public Image micStatusIcon;
    public Color micOnColor = new Color(0.2f, 0.8f, 0.3f, 1f);
    public Color micOffColor = new Color(0.8f, 0.2f, 0.2f, 1f);

    [Header("Volume Controls")]
    public Slider micVolumeSlider;
    public TextMeshProUGUI micVolumeText;
    public Slider masterVolumeSlider;
    public TextMeshProUGUI masterVolumeText;
    public Slider othersVolumeSlider;
    public TextMeshProUGUI othersVolumeText;

    [Header("Voice Activity")]
    public Image voiceActivityIndicator;
    public Slider voiceActivityMeter;

    private bool _isInitialized = false;

    void Start()
    {
        AutoFindReferences();
        Initialize();
    }

    void AutoFindReferences()
    {
        // Find toggle
        if (microphoneToggle == null)
        {
            Toggle[] toggles = GetComponentsInChildren<Toggle>(true);
            foreach (var t in toggles)
            {
                if (t.name.ToLower().Contains("mic"))
                {
                    microphoneToggle = t;
                    break;
                }
            }
        }

        // Find sliders
        Slider[] sliders = GetComponentsInChildren<Slider>(true);
        foreach (var s in sliders)
        {
            string n = s.name.ToLower();
            if (micVolumeSlider == null && n.Contains("micvolume"))
                micVolumeSlider = s;
            else if (masterVolumeSlider == null && n.Contains("master"))
                masterVolumeSlider = s;
            else if (othersVolumeSlider == null && n.Contains("others"))
                othersVolumeSlider = s;
        }

        // Find texts for volume display
        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in texts)
        {
            string n = txt.name.ToLower();
            if (micVolumeText == null && n.Contains("micvolume") && n.Contains("text"))
                micVolumeText = txt;
            else if (masterVolumeText == null && n.Contains("master") && n.Contains("text"))
                masterVolumeText = txt;
            else if (othersVolumeText == null && n.Contains("others") && n.Contains("text"))
                othersVolumeText = txt;
        }

        Debug.Log($"[VRMenuPageVoice] AutoFind: mic={microphoneToggle != null}, micVol={micVolumeSlider != null}");
    }

    void OnEnable()
    {
        if (_isInitialized)
        {
            RefreshMicrophoneStatus();
        }
    }

    void Initialize()
    {
        // Microphone toggle
        if (microphoneToggle != null)
        {
            microphoneToggle.onValueChanged.AddListener(OnMicrophoneToggle);
            Debug.Log("[VRMenuPageVoice] Microphone toggle connected");
        }

        // Volume sliders
        if (micVolumeSlider != null)
        {
            micVolumeSlider.minValue = 0f;
            micVolumeSlider.maxValue = 1f;
            micVolumeSlider.value = PlayerPrefs.GetFloat("MicVolume", 1f);
            micVolumeSlider.onValueChanged.AddListener(OnMicVolumeChanged);
            UpdateMicVolumeText(micVolumeSlider.value);
        }

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.minValue = 0f;
            masterVolumeSlider.maxValue = 1f;
            masterVolumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", 1f);
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            UpdateMasterVolumeText(masterVolumeSlider.value);
        }

        if (othersVolumeSlider != null)
        {
            othersVolumeSlider.minValue = 0f;
            othersVolumeSlider.maxValue = 1f;
            othersVolumeSlider.value = PlayerPrefs.GetFloat("OthersVolume", 1f);
            othersVolumeSlider.onValueChanged.AddListener(OnOthersVolumeChanged);
            UpdateOthersVolumeText(othersVolumeSlider.value);
        }

        RefreshMicrophoneStatus();
        _isInitialized = true;
    }

    void Update()
    {
        UpdateVoiceActivityMeter();
    }

    #region Microphone Toggle

    void OnMicrophoneToggle(bool isOn)
    {
        var voiceChat = VoiceChatManager.Instance;
        if (voiceChat != null)
        {
            if (isOn)
            {
                voiceChat.StartMicrophone();
            }
            else
            {
                voiceChat.StopMicrophone();
            }
        }

        UpdateMicrophoneUI(isOn);
        Debug.Log($"[VRMenuPageVoice] Microphone {(isOn ? "ON" : "OFF")}");
    }

    void RefreshMicrophoneStatus()
    {
        var voiceChat = VoiceChatManager.Instance;
        bool isMicOn = voiceChat != null && voiceChat.IsMicrophoneActive;

        if (microphoneToggle != null)
        {
            microphoneToggle.SetIsOnWithoutNotify(isMicOn);
        }

        UpdateMicrophoneUI(isMicOn);
    }

    void UpdateMicrophoneUI(bool isOn)
    {
        if (micStatusText != null)
        {
            micStatusText.text = isOn ? "Microphone: ON" : "Microphone: OFF";
        }

        if (micStatusIcon != null)
        {
            micStatusIcon.color = isOn ? micOnColor : micOffColor;
        }
    }

    #endregion

    #region Volume Controls

    void OnMicVolumeChanged(float value)
    {
        PlayerPrefs.SetFloat("MicVolume", value);
        UpdateMicVolumeText(value);

        var voiceChat = VoiceChatManager.Instance;
        if (voiceChat != null)
        {
            voiceChat.SetMicrophoneVolume(value);
        }
    }

    void OnMasterVolumeChanged(float value)
    {
        PlayerPrefs.SetFloat("MasterVolume", value);
        AudioListener.volume = value;
        UpdateMasterVolumeText(value);
    }

    void OnOthersVolumeChanged(float value)
    {
        PlayerPrefs.SetFloat("OthersVolume", value);
        UpdateOthersVolumeText(value);

        var voiceChat = VoiceChatManager.Instance;
        if (voiceChat != null)
        {
            voiceChat.SetPlaybackVolume(value);
        }
    }

    void UpdateMicVolumeText(float value)
    {
        if (micVolumeText != null)
        {
            micVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
        }
    }

    void UpdateMasterVolumeText(float value)
    {
        if (masterVolumeText != null)
        {
            masterVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
        }
    }

    void UpdateOthersVolumeText(float value)
    {
        if (othersVolumeText != null)
        {
            othersVolumeText.text = $"{Mathf.RoundToInt(value * 100)}%";
        }
    }

    #endregion

    #region Voice Activity

    void UpdateVoiceActivityMeter()
    {
        var voiceChat = VoiceChatManager.Instance;
        if (voiceChat == null) return;

        float level = voiceChat.IsMicrophoneActive ? 0.5f : 0f;

        if (voiceActivityMeter != null)
        {
            voiceActivityMeter.value = level;
        }

        if (voiceActivityIndicator != null)
        {
            voiceActivityIndicator.color = voiceChat.IsMicrophoneActive ? micOnColor : Color.gray;
        }
    }

    #endregion
}
