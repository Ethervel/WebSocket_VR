using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Avatar page - Customize username and avatar color.
/// </summary>
public class VRMenuPageAvatar : MonoBehaviour
{
    [Header("Username")]
    public TMP_InputField usernameInput;
    public Button applyNameButton;

    [Header("Color Selection")]
    public Transform colorButtonsContainer;
    public GameObject colorButtonPrefab;

    [Header("Preview")]
    public Image avatarPreviewImage;
    public TextMeshProUGUI previewNameText;

    [Header("Colors Available")]
    public Color[] availableColors = new Color[]
    {
        new Color(0.2f, 0.4f, 0.8f, 1f),  // Blue
        new Color(0.8f, 0.2f, 0.2f, 1f),  // Red
        new Color(0.2f, 0.7f, 0.3f, 1f),  // Green
        new Color(0.9f, 0.7f, 0.1f, 1f),  // Yellow
        new Color(0.6f, 0.2f, 0.8f, 1f),  // Purple
        new Color(0.9f, 0.5f, 0.1f, 1f),  // Orange
        new Color(0.1f, 0.8f, 0.8f, 1f),  // Cyan
        new Color(0.9f, 0.4f, 0.6f, 1f),  // Pink
    };

    private int _selectedColorIndex = 0;
    private string _currentUsername;
    private Button[] _colorButtons;

    void Start()
    {
        AutoFindReferences();
        LoadCurrentSettings();
        CreateColorButtons();
        SetupInputHandlers();
        UpdatePreview();
    }

    void AutoFindReferences()
    {
        // Find input field
        if (usernameInput == null)
        {
            usernameInput = GetComponentInChildren<TMP_InputField>(true);
        }

        // Find buttons
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            string n = btn.name.ToLower();
            if (applyNameButton == null && (n.Contains("apply") || n.Contains("save")))
                applyNameButton = btn;
        }

        // Find color buttons container (HorizontalLayoutGroup)
        if (colorButtonsContainer == null)
        {
            HorizontalLayoutGroup hLayout = GetComponentInChildren<HorizontalLayoutGroup>(true);
            if (hLayout != null)
            {
                colorButtonsContainer = hLayout.transform;
            }
        }

        // Find preview elements
        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in texts)
        {
            string n = txt.name.ToLower();
            if (previewNameText == null && n.Contains("preview"))
                previewNameText = txt;
        }

        // Find preview image
        if (avatarPreviewImage == null)
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img.name.ToLower().Contains("avatarpreview") || img.name.ToLower().Contains("preview"))
                {
                    avatarPreviewImage = img;
                    break;
                }
            }
        }

        Debug.Log($"[VRMenuPageAvatar] AutoFind: input={usernameInput != null}, apply={applyNameButton != null}, colors={colorButtonsContainer != null}");
    }

    void OnEnable()
    {
        LoadCurrentSettings();
        UpdatePreview();
        BindXRKeyboard();
    }

    void BindXRKeyboard()
    {
        if (usernameInput == null) return;

        var keyboardBind = FindFirstObjectByType<GlobalKeyboardAutoBind>();
        if (keyboardBind != null)
        {
            keyboardBind.SetupInputField(usernameInput);
        }
    }

    void LoadCurrentSettings()
    {
        // Load from PlayerPrefs
        _currentUsername = PlayerPrefs.GetString("PlayerName", "Player");
        _selectedColorIndex = PlayerPrefs.GetInt("PlayerColorIndex", 0);

        if (usernameInput != null)
        {
            usernameInput.text = _currentUsername;
        }
    }

    void CreateColorButtons()
    {
        if (colorButtonsContainer == null) return;

        // Clear existing
        foreach (Transform child in colorButtonsContainer)
        {
            Destroy(child.gameObject);
        }

        _colorButtons = new Button[availableColors.Length];

        for (int i = 0; i < availableColors.Length; i++)
        {
            int colorIndex = i; // Capture for lambda
            GameObject btnObj;

            if (colorButtonPrefab != null)
            {
                btnObj = Instantiate(colorButtonPrefab, colorButtonsContainer);
            }
            else
            {
                btnObj = new GameObject($"ColorBtn_{i}");
                btnObj.transform.SetParent(colorButtonsContainer, false);
                btnObj.AddComponent<Image>();
                btnObj.AddComponent<Button>();

                // Add layout element for sizing 50x50
                var layoutElem = btnObj.AddComponent<LayoutElement>();
                layoutElem.minWidth = 50;
                layoutElem.minHeight = 50;
                layoutElem.preferredWidth = 50;
                layoutElem.preferredHeight = 50;

                // Set RectTransform size
                RectTransform rt = btnObj.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.sizeDelta = new Vector2(50, 50);
                }
            }

            Image img = btnObj.GetComponent<Image>();
            if (img != null)
            {
                img.color = availableColors[i];
            }

            Button btn = btnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.AddListener(() => SelectColor(colorIndex));
                _colorButtons[i] = btn;
            }
        }

        UpdateColorButtonSelection();
    }

    void SetupInputHandlers()
    {
        if (usernameInput != null)
        {
            usernameInput.onEndEdit.AddListener(OnUsernameChanged);
            usernameInput.onValueChanged.AddListener(OnUsernameTyping);
            Debug.Log("[VRMenuPageAvatar] Username input connected");
        }
        else
        {
            Debug.LogWarning("[VRMenuPageAvatar] Username input is NULL!");
        }

        if (applyNameButton != null)
        {
            applyNameButton.onClick.AddListener(ApplyChanges);
            Debug.Log("[VRMenuPageAvatar] Apply button connected");
        }
        else
        {
            Debug.LogWarning("[VRMenuPageAvatar] Apply button is NULL!");
        }
    }

    void SelectColor(int index)
    {
        if (index < 0 || index >= availableColors.Length) return;

        _selectedColorIndex = index;
        UpdateColorButtonSelection();
        UpdatePreview();
        ApplyColorChange();
    }

    void UpdateColorButtonSelection()
    {
        if (_colorButtons == null) return;

        for (int i = 0; i < _colorButtons.Length; i++)
        {
            if (_colorButtons[i] == null) continue;

            // Add selection indicator (outline or scale)
            var outline = _colorButtons[i].GetComponent<Outline>();
            if (outline == null)
            {
                outline = _colorButtons[i].gameObject.AddComponent<Outline>();
            }

            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(3, 3);
            outline.enabled = (i == _selectedColorIndex);
        }
    }

    void UpdatePreview()
    {
        if (avatarPreviewImage != null && _selectedColorIndex < availableColors.Length)
        {
            avatarPreviewImage.color = availableColors[_selectedColorIndex];
        }

        if (previewNameText != null)
        {
            previewNameText.text = _currentUsername;
        }
    }

    void OnUsernameTyping(string value)
    {
        _currentUsername = value;
        UpdatePreview();
    }

    void OnUsernameChanged(string value)
    {
        _currentUsername = value.Trim();
        if (string.IsNullOrEmpty(_currentUsername))
        {
            _currentUsername = "Player";
            usernameInput.text = _currentUsername;
        }
        UpdatePreview();
    }

    void ApplyColorChange()
    {
        // Save to PlayerPrefs
        PlayerPrefs.SetInt("PlayerColorIndex", _selectedColorIndex);
        PlayerPrefs.Save();

        // Apply to avatar customization if available
        if (AvatarCustomization.Instance != null && _selectedColorIndex < availableColors.Length)
        {
            AvatarCustomization.Instance.SetColor(availableColors[_selectedColorIndex]);
        }

        Debug.Log($"[VRMenuPageAvatar] Color changed to index {_selectedColorIndex}");
    }

    public void ApplyChanges()
    {
        // Save username and color
        PlayerPrefs.SetString("PlayerName", _currentUsername);
        PlayerPrefs.SetInt("PlayerColorIndex", _selectedColorIndex);
        PlayerPrefs.Save();

        // Apply color to avatar customization if available
        if (AvatarCustomization.Instance != null && _selectedColorIndex < availableColors.Length)
        {
            AvatarCustomization.Instance.SetColor(availableColors[_selectedColorIndex]);
        }

        // Broadcast full avatar update (name + color) to all players in room
        if (VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.SetPlayerName(_currentUsername);
            VRRoomManager.Instance.BroadcastAvatarUpdate();
        }

        Debug.Log($"[VRMenuPageAvatar] Applied: Name={_currentUsername}, ColorIndex={_selectedColorIndex}");
    }

    public Color GetSelectedColor()
    {
        if (_selectedColorIndex >= 0 && _selectedColorIndex < availableColors.Length)
        {
            return availableColors[_selectedColorIndex];
        }
        return Color.white;
    }
}
