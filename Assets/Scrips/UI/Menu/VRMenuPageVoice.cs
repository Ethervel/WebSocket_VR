using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Voice page - Microphone settings, device selection, volume controls.
/// </summary>
public class VRMenuPageVoice : MonoBehaviour
{
    [Header("Microphone Toggle")]
    public Toggle microphoneToggle;
    public TextMeshProUGUI micStatusText;
    public Image micStatusIcon;
    public Color micOnColor = new Color(0.2f, 0.8f, 0.3f, 1f);
    public Color micOffColor = new Color(0.8f, 0.2f, 0.2f, 1f);

    [Header("Device Selection")]
    public TMP_Dropdown inputDeviceDropdown;
    public TMP_Dropdown outputDeviceDropdown;
    public Button refreshDevicesButton;

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

    private string[] _microphoneDevices;
    private bool _isInitialized = false;

    void Start()
    {
        AutoFindReferences();

        // Fix dropdown layouts for existing UI
        FixDropdownLayout(inputDeviceDropdown);
        FixDropdownLayout(outputDeviceDropdown);

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

        // Find dropdowns
        TMP_Dropdown[] dropdowns = GetComponentsInChildren<TMP_Dropdown>(true);
        foreach (var dd in dropdowns)
        {
            string n = dd.name.ToLower();
            if (inputDeviceDropdown == null && n.Contains("input"))
                inputDeviceDropdown = dd;
            else if (outputDeviceDropdown == null && n.Contains("output"))
                outputDeviceDropdown = dd;
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

        Debug.Log($"[VRMenuPageVoice] AutoFind: mic={microphoneToggle != null}, input={inputDeviceDropdown != null}, micVol={micVolumeSlider != null}");
    }

    /// <summary>
    /// Fix dropdown template layout at runtime (for existing UI that wasn't created with proper layout)
    /// </summary>
    void FixDropdownLayout(TMP_Dropdown dropdown)
    {
        if (dropdown == null)
        {
            Debug.LogWarning("[VRMenuPageVoice] FixDropdownLayout: dropdown is null");
            return;
        }

        Debug.Log($"[VRMenuPageVoice] Fixing dropdown: {dropdown.name}");

        // Fix caption text font (the selected option display)
        if (dropdown.captionText != null && dropdown.captionText.font == null && TMP_Settings.defaultFontAsset != null)
        {
            dropdown.captionText.font = TMP_Settings.defaultFontAsset;
            Debug.Log($"[VRMenuPageVoice] Assigned default font to captionText in {dropdown.name}");
        }

        // Fix itemText font (template for dropdown items)
        if (dropdown.itemText != null && dropdown.itemText.font == null && TMP_Settings.defaultFontAsset != null)
        {
            dropdown.itemText.font = TMP_Settings.defaultFontAsset;
            Debug.Log($"[VRMenuPageVoice] Assigned default font to itemText in {dropdown.name}");
        }

        // Find template
        RectTransform template = dropdown.template;
        if (template == null)
        {
            Debug.LogError($"[VRMenuPageVoice] {dropdown.name} has NO template assigned!");
            // Try to find template child
            Transform templateChild = dropdown.transform.Find("Template");
            if (templateChild != null)
            {
                dropdown.template = templateChild.GetComponent<RectTransform>();
                template = dropdown.template;
                Debug.Log($"[VRMenuPageVoice] Found and assigned Template for {dropdown.name}");
            }
            else
            {
                Debug.LogError($"[VRMenuPageVoice] Cannot find Template child in {dropdown.name}");
                LogDropdownHierarchy(dropdown);
                return;
            }
        }

        // Fix Template height - must be big enough to show items
        RectTransform templateRect = template.GetComponent<RectTransform>();
        if (templateRect != null && templateRect.sizeDelta.y < 150)
        {
            templateRect.sizeDelta = new Vector2(templateRect.sizeDelta.x, 150);
            Debug.Log($"[VRMenuPageVoice] Fixed Template height to 150 for {dropdown.name}");
        }

        // Find Content inside Viewport
        Transform viewport = template.Find("Viewport");
        if (viewport == null)
        {
            Debug.LogError($"[VRMenuPageVoice] {dropdown.name} Template has no Viewport!");
            LogDropdownHierarchy(dropdown);
            return;
        }

        // Ensure Viewport stretches to fill Template
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        if (viewportRect != null)
        {
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
        }

        Transform content = viewport.Find("Content");
        if (content == null)
        {
            Debug.LogError($"[VRMenuPageVoice] {dropdown.name} Viewport has no Content!");
            LogDropdownHierarchy(dropdown);
            return;
        }

        // Add VerticalLayoutGroup if missing
        VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 0;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            Debug.Log($"[VRMenuPageVoice] Added VerticalLayoutGroup to {dropdown.name}");
        }

        // Add or fix ContentSizeFitter
        ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
        if (csf == null)
        {
            csf = content.gameObject.AddComponent<ContentSizeFitter>();
            Debug.Log($"[VRMenuPageVoice] Added ContentSizeFitter to {dropdown.name}");
        }
        // Always ensure correct settings
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        Debug.Log($"[VRMenuPageVoice] ContentSizeFitter configured on {dropdown.name}");

        // Find Item template and add LayoutElement if missing
        Transform item = content.Find("Item");
        if (item != null)
        {
            LayoutElement le = item.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = item.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 35;
                le.preferredHeight = 35;
                le.flexibleWidth = 1;
                Debug.Log($"[VRMenuPageVoice] Added LayoutElement to Item in {dropdown.name}");
            }

            // CRITICAL: Assign font to Item Label template so copied items have font
            TextMeshProUGUI itemLabel = item.GetComponentInChildren<TextMeshProUGUI>();
            if (itemLabel != null && itemLabel.font == null && TMP_Settings.defaultFontAsset != null)
            {
                itemLabel.font = TMP_Settings.defaultFontAsset;
                Debug.Log($"[VRMenuPageVoice] Assigned default font to Item Label template in {dropdown.name}");
            }
        }
        else
        {
            Debug.LogError($"[VRMenuPageVoice] {dropdown.name} Content has no Item!");
            LogDropdownHierarchy(dropdown);
        }

        Debug.Log($"[VRMenuPageVoice] Dropdown {dropdown.name} fix complete");
    }

    void LogDropdownHierarchy(TMP_Dropdown dropdown)
    {
        Debug.Log($"[VRMenuPageVoice] === Hierarchy of {dropdown.name} ===");
        LogTransformChildren(dropdown.transform, 0);
    }

    void LogTransformChildren(Transform t, int depth)
    {
        string indent = new string('-', depth * 2);
        Debug.Log($"[VRMenuPageVoice] {indent} {t.name} (active={t.gameObject.activeSelf})");
        foreach (Transform child in t)
        {
            LogTransformChildren(child, depth + 1);
        }
    }

    void AddDropdownClickDebug(TMP_Dropdown dropdown)
    {
        if (dropdown == null) return;

        // Add EventTrigger to detect pointer events
        EventTrigger trigger = dropdown.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = dropdown.gameObject.AddComponent<EventTrigger>();
        }

        // PointerClick - fix dropdown list after it's created
        EventTrigger.Entry clickEntry = new EventTrigger.Entry();
        clickEntry.eventID = EventTriggerType.PointerClick;
        clickEntry.callback.AddListener((data) => {
            Debug.Log($"[VRMenuPageVoice] Dropdown CLICKED!");
            // Use coroutine to fix after dropdown list is created
            StartCoroutine(FixDropdownListAfterOpen(dropdown));
        });
        trigger.triggers.Add(clickEntry);

        // PointerEnter
        EventTrigger.Entry enterEntry = new EventTrigger.Entry();
        enterEntry.eventID = EventTriggerType.PointerEnter;
        enterEntry.callback.AddListener((data) => {
            Debug.Log("[VRMenuPageVoice] Pointer ENTERED dropdown");
        });
        trigger.triggers.Add(enterEntry);

        Debug.Log("[VRMenuPageVoice] Added click debug to dropdown");
    }

    IEnumerator FixDropdownListAfterOpen(TMP_Dropdown dropdown)
    {
        // Wait one frame for the Dropdown List to be created
        yield return null;

        Transform dropdownList = dropdown.transform.Find("Dropdown List");
        if (dropdownList == null)
        {
            Debug.LogWarning("[VRMenuPageVoice] Dropdown List not found!");
            yield break;
        }

        Debug.Log("[VRMenuPageVoice] Fixing Dropdown List...");

        // Log current position info
        Debug.Log($"[VRMenuPageVoice] Dropdown List localPosition: {dropdownList.localPosition}");
        Debug.Log($"[VRMenuPageVoice] Dropdown List localScale: {dropdownList.localScale}");
        Debug.Log($"[VRMenuPageVoice] Dropdown parent: {dropdownList.parent?.name}");

        // Fix the Dropdown List height
        RectTransform listRect = dropdownList.GetComponent<RectTransform>();
        if (listRect != null)
        {
            // Count active items to calculate needed height
            Transform viewport = dropdownList.Find("Viewport");
            Transform content = viewport?.Find("Content");

            if (content != null)
            {
                int activeItems = 0;
                foreach (Transform child in content)
                {
                    if (child.gameObject.activeSelf) activeItems++;
                }

                float neededHeight = activeItems * 35f + 10f; // 35px per item + padding
                float maxHeight = 200f;
                float finalHeight = Mathf.Min(neededHeight, maxHeight);

                listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, finalHeight);
                Debug.Log($"[VRMenuPageVoice] Set Dropdown List height to {finalHeight} for {activeItems} items");

                // Fix Content layout
                VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
                if (vlg == null)
                {
                    vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
                    vlg.childAlignment = TextAnchor.UpperCenter;
                    vlg.childControlHeight = false;
                    vlg.childControlWidth = true;
                    vlg.childForceExpandHeight = false;
                    vlg.childForceExpandWidth = true;
                    vlg.spacing = 0;
                }

                ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
                if (csf == null)
                {
                    csf = content.gameObject.AddComponent<ContentSizeFitter>();
                }
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                // Ensure viewport fills the list
                RectTransform viewportRect = viewport.GetComponent<RectTransform>();
                if (viewportRect != null)
                {
                    viewportRect.anchorMin = Vector2.zero;
                    viewportRect.anchorMax = Vector2.one;
                    viewportRect.offsetMin = Vector2.zero;
                    viewportRect.offsetMax = Vector2.zero;
                }

                // Fix background color of the dropdown list
                Image listBg = dropdownList.GetComponent<Image>();
                if (listBg != null)
                {
                    if (listBg.color.a < 0.5f) // If too transparent
                    {
                        listBg.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);
                        Debug.Log("[VRMenuPageVoice] Fixed dropdown list background color");
                    }
                }

                // Fix item backgrounds and text colors
                foreach (Transform child in content)
                {
                    if (!child.gameObject.activeSelf) continue;

                    // Ensure item has visible background
                    Image itemBg = child.GetComponent<Image>();
                    if (itemBg == null)
                    {
                        itemBg = child.gameObject.AddComponent<Image>();
                    }
                    if (itemBg.color.a < 0.3f)
                    {
                        itemBg.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
                    }

                    // Ensure text is visible - assign font if missing
                    var texts = child.GetComponentsInChildren<TextMeshProUGUI>();
                    foreach (var txt in texts)
                    {
                        // CRITICAL: Assign default font if missing - text is invisible without font!
                        if (txt.font == null && TMP_Settings.defaultFontAsset != null)
                        {
                            txt.font = TMP_Settings.defaultFontAsset;
                            Debug.Log($"[VRMenuPageVoice] Assigned default font to item text");
                        }

                        // Ensure text color is visible
                        if (txt.color.a < 0.5f)
                        {
                            txt.color = Color.white;
                        }

                        Debug.Log($"[VRMenuPageVoice] Item text: '{txt.text}', font: {(txt.font != null ? txt.font.name : "NULL")}, color: {txt.color}");
                    }
                }

                // Force layout rebuild
                LayoutRebuilder.ForceRebuildLayoutImmediate(content as RectTransform);
                LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
                Canvas.ForceUpdateCanvases();

                // Fix position - ensure list is below dropdown and visible
                // In World Space, the list should be positioned relative to the dropdown
                Vector3 localPos = listRect.localPosition;
                localPos.z = 0; // Ensure same z as parent
                listRect.localPosition = localPos;

                // Ensure the dropdown list renders on top
                Canvas listCanvas = dropdownList.GetComponent<Canvas>();
                if (listCanvas == null)
                {
                    listCanvas = dropdownList.gameObject.AddComponent<Canvas>();
                    listCanvas.overrideSorting = true;
                    listCanvas.sortingOrder = 200; // High value to render on top
                    dropdownList.gameObject.AddComponent<GraphicRaycaster>();
                    Debug.Log("[VRMenuPageVoice] Added Canvas to Dropdown List for proper rendering");
                }

                Debug.Log($"[VRMenuPageVoice] Final listRect position: {listRect.localPosition}, size: {listRect.sizeDelta}");
                Debug.Log("[VRMenuPageVoice] Dropdown List fixed!");
            }
        }
    }

    void OnEnable()
    {
        if (_isInitialized)
        {
            RefreshMicrophoneStatus();
            RefreshDeviceList();
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
        else
        {
            Debug.LogWarning("[VRMenuPageVoice] Microphone toggle is NULL!");
        }

        // Device refresh button
        if (refreshDevicesButton != null)
        {
            refreshDevicesButton.onClick.AddListener(RefreshDeviceList);
            Debug.Log("[VRMenuPageVoice] Refresh devices button connected");
        }

        // Input device dropdown
        if (inputDeviceDropdown != null)
        {
            inputDeviceDropdown.onValueChanged.AddListener(OnInputDeviceChanged);

            // Debug: log when dropdown opens
            var dropdownComponent = inputDeviceDropdown;
            var originalShow = inputDeviceDropdown.template;

            Debug.Log("[VRMenuPageVoice] Input device dropdown connected");
            Debug.Log($"[VRMenuPageVoice] Dropdown template: {(inputDeviceDropdown.template != null ? inputDeviceDropdown.template.name : "NULL")}");
            Debug.Log($"[VRMenuPageVoice] Dropdown options count: {inputDeviceDropdown.options.Count}");

            // Add click debug
            AddDropdownClickDebug(inputDeviceDropdown);
        }
        else
        {
            Debug.LogWarning("[VRMenuPageVoice] Input device dropdown is NULL!");
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

        RefreshDeviceList();
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

    #region Device Selection

    void RefreshDeviceList()
    {
        _microphoneDevices = Microphone.devices;

        // Populate input device dropdown
        if (inputDeviceDropdown != null)
        {
            inputDeviceDropdown.ClearOptions();

            List<string> options = new List<string>();
            options.Add("Default");

            foreach (string device in _microphoneDevices)
            {
                options.Add(device);
            }

            inputDeviceDropdown.AddOptions(options);

            // Select saved device
            string savedDevice = PlayerPrefs.GetString("InputDevice", "Default");
            int savedIndex = options.IndexOf(savedDevice);
            if (savedIndex >= 0)
            {
                inputDeviceDropdown.SetValueWithoutNotify(savedIndex);
            }
        }

        // Output device dropdown (Unity doesn't support output device selection natively)
        // This is a placeholder - would need platform-specific implementation
        if (outputDeviceDropdown != null)
        {
            outputDeviceDropdown.ClearOptions();
            outputDeviceDropdown.AddOptions(new List<string> { "Default System Output" });
            outputDeviceDropdown.interactable = false; // Disabled - Unity limitation
        }

        Debug.Log($"[VRMenuPageVoice] Found {_microphoneDevices.Length} microphone devices");
    }

    void OnInputDeviceChanged(int index)
    {
        string deviceName = index == 0 ? null : _microphoneDevices[index - 1];

        var voiceChat = VoiceChatManager.Instance;
        if (voiceChat != null)
        {
            voiceChat.SetMicrophone(deviceName);
        }

        // Save selection
        string saveName = index == 0 ? "Default" : deviceName;
        PlayerPrefs.SetString("InputDevice", saveName);
        PlayerPrefs.Save();

        Debug.Log($"[VRMenuPageVoice] Input device changed to: {saveName}");
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

        // Voice activity meter not implemented - would need audio analysis
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
