#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Editor tool to create the complete Main Menu UI structure.
/// Tools > VR Meeting > Setup Main Menu UI
/// </summary>
public class MainMenuUISetup : EditorWindow
{
    // Colors
    private static readonly Color PanelColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
    private static readonly Color ButtonColor = new Color(0.2f, 0.4f, 0.8f, 1f);
    private static readonly Color ButtonHoverColor = new Color(0.3f, 0.5f, 0.9f, 1f);
    private static readonly Color ButtonQuitColor = new Color(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Color TextColor = Color.white;

    [MenuItem("Tools/VR Meeting/Setup Main Menu UI")]
    public static void ShowWindow()
    {
        GetWindow<MainMenuUISetup>("Main Menu UI Setup");
    }

    [MenuItem("Tools/VR Meeting/Create Main Menu UI Now")]
    public static void CreateMainMenuUIDirect()
    {
        CreateMainMenuUI();
    }

    [MenuItem("Tools/VR Meeting/Link Main Menu References")]
    public static void LinkReferencesDirect()
    {
        LinkToManager();
    }

    void OnGUI()
    {
        GUILayout.Label("Main Menu UI Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This will create a complete Main Menu UI structure with:\n" +
            "- World Space Canvas\n" +
            "- Main Panel (Start, Options, Quit)\n" +
            "- Options Panel (Audio, Graphics, Controls)\n" +
            "- Quit Dialog (Yes/No)",
            MessageType.Info);

        GUILayout.Space(10);

        if (GUILayout.Button("Create Main Menu UI", GUILayout.Height(40)))
        {
            CreateMainMenuUI();
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Link to Existing MainMenuManager", GUILayout.Height(30)))
        {
            LinkToManager();
        }
    }

    static void CreateMainMenuUI()
    {
        // Find or create MainMenuManager
        MainMenuManager manager = FindFirstObjectByType<MainMenuManager>();

        // Create Canvas
        GameObject canvasObj = new GameObject("MainMenuUI");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;

        // Add XR raycaster for VR interaction
        canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();

        // Configure RectTransform
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(800, 600);
        canvasRect.localScale = new Vector3(0.002f, 0.002f, 0.002f);
        canvasRect.position = new Vector3(0, 1.5f, 2f);

        // Create Background
        GameObject bgObj = CreatePanel(canvasObj.transform, "Background", Vector2.zero, new Vector2(800, 600));
        Image bgImage = bgObj.GetComponent<Image>();
        bgImage.color = PanelColor;

        // Create Main Panel
        GameObject mainPanel = CreateMainPanel(bgObj.transform);

        // Create Options Panel
        GameObject optionsPanel = CreateOptionsPanel(bgObj.transform);
        optionsPanel.SetActive(false);

        // Create Quit Dialog
        GameObject quitDialog = CreateQuitDialog(bgObj.transform);
        quitDialog.SetActive(false);

        // Link to manager if exists
        if (manager != null)
        {
            manager.mainPanel = mainPanel;
            manager.optionsPanel = optionsPanel;
            manager.quitDialog = quitDialog;

            // Find buttons
            manager.startButton = mainPanel.transform.Find("StartButton")?.GetComponent<Button>();
            manager.optionsButton = mainPanel.transform.Find("OptionsButton")?.GetComponent<Button>();
            manager.quitButton = mainPanel.transform.Find("QuitButton")?.GetComponent<Button>();
            manager.backButton = optionsPanel.transform.Find("BackButton")?.GetComponent<Button>();
            manager.quitYesButton = quitDialog.transform.Find("DialogBox/ButtonsContainer/YesButton")?.GetComponent<Button>();
            manager.quitNoButton = quitDialog.transform.Find("DialogBox/ButtonsContainer/NoButton")?.GetComponent<Button>();

            EditorUtility.SetDirty(manager);
            Debug.Log("[MainMenuUISetup] Linked all references to MainMenuManager");
        }

        Selection.activeGameObject = canvasObj;
        Undo.RegisterCreatedObjectUndo(canvasObj, "Create Main Menu UI");

        Debug.Log("[MainMenuUISetup] Main Menu UI created successfully!");
    }

    static GameObject CreateMainPanel(Transform parent)
    {
        GameObject panel = CreatePanel(parent, "MainPanel", Vector2.zero, new Vector2(400, 500));
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

        // Title
        GameObject titleObj = CreateText(panel.transform, "Title", "VR Meeting", 48, FontStyles.Bold);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, 180);

        // Subtitle
        GameObject subtitleObj = CreateText(panel.transform, "Subtitle", "Collaborative VR Space", 20, FontStyles.Normal);
        RectTransform subtitleRect = subtitleObj.GetComponent<RectTransform>();
        subtitleRect.anchoredPosition = new Vector2(0, 130);
        subtitleObj.GetComponent<TextMeshProUGUI>().color = new Color(0.7f, 0.7f, 0.8f, 1f);

        // Start Button
        GameObject startBtn = CreateButton(panel.transform, "StartButton", "Start", ButtonColor, new Vector2(0, 30));
        RectTransform startRect = startBtn.GetComponent<RectTransform>();
        startRect.sizeDelta = new Vector2(250, 60);

        // Options Button
        GameObject optionsBtn = CreateButton(panel.transform, "OptionsButton", "Options", ButtonColor, new Vector2(0, -50));
        RectTransform optionsRect = optionsBtn.GetComponent<RectTransform>();
        optionsRect.sizeDelta = new Vector2(250, 60);

        // Quit Button
        GameObject quitBtn = CreateButton(panel.transform, "QuitButton", "Quit", ButtonQuitColor, new Vector2(0, -130));
        RectTransform quitRect = quitBtn.GetComponent<RectTransform>();
        quitRect.sizeDelta = new Vector2(250, 60);

        // Version text
        GameObject versionObj = CreateText(panel.transform, "Version", "v1.0.0", 14, FontStyles.Normal);
        RectTransform versionRect = versionObj.GetComponent<RectTransform>();
        versionRect.anchoredPosition = new Vector2(0, -220);
        versionObj.GetComponent<TextMeshProUGUI>().color = new Color(0.5f, 0.5f, 0.5f, 1f);

        return panel;
    }

    static GameObject CreateOptionsPanel(Transform parent)
    {
        GameObject panel = CreatePanel(parent, "OptionsPanel", Vector2.zero, new Vector2(600, 500));
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

        // Title
        GameObject titleObj = CreateText(panel.transform, "Title", "Options", 36, FontStyles.Bold);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, 210);

        // Audio Section
        CreateSectionHeader(panel.transform, "AudioHeader", "Audio", new Vector2(0, 150));
        CreateSliderOption(panel.transform, "MasterVolume", "Master Volume", new Vector2(0, 110));
        CreateSliderOption(panel.transform, "VoiceVolume", "Voice Volume", new Vector2(0, 60));

        // Graphics Section
        CreateSectionHeader(panel.transform, "GraphicsHeader", "Graphics", new Vector2(0, 0));
        CreateDropdownOption(panel.transform, "QualityLevel", "Quality", new Vector2(0, -40));

        // Controls Section
        CreateSectionHeader(panel.transform, "ControlsHeader", "Controls", new Vector2(0, -100));
        CreateDropdownOption(panel.transform, "TurnMode", "Turn Mode", new Vector2(0, -140));

        // Back Button
        GameObject backBtn = CreateButton(panel.transform, "BackButton", "Back", ButtonColor, new Vector2(0, -210));
        RectTransform backRect = backBtn.GetComponent<RectTransform>();
        backRect.sizeDelta = new Vector2(200, 50);

        return panel;
    }

    static GameObject CreateQuitDialog(Transform parent)
    {
        // Overlay
        GameObject overlay = CreatePanel(parent, "QuitDialog", Vector2.zero, new Vector2(800, 600));
        Image overlayImage = overlay.GetComponent<Image>();
        overlayImage.color = new Color(0, 0, 0, 0.7f);

        // Dialog box
        GameObject dialogBox = CreatePanel(overlay.transform, "DialogBox", Vector2.zero, new Vector2(350, 200));
        Image dialogImage = dialogBox.GetComponent<Image>();
        dialogImage.color = new Color(0.2f, 0.2f, 0.25f, 1f);

        // Message
        GameObject msgObj = CreateText(dialogBox.transform, "Message", "Are you sure you want to quit?", 22, FontStyles.Normal);
        RectTransform msgRect = msgObj.GetComponent<RectTransform>();
        msgRect.anchoredPosition = new Vector2(0, 40);

        // Buttons container
        GameObject buttonsContainer = new GameObject("ButtonsContainer");
        buttonsContainer.transform.SetParent(dialogBox.transform, false);
        RectTransform containerRect = buttonsContainer.AddComponent<RectTransform>();
        containerRect.anchoredPosition = new Vector2(0, -40);
        containerRect.sizeDelta = new Vector2(300, 60);

        HorizontalLayoutGroup layout = buttonsContainer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        // Yes Button
        GameObject yesBtn = CreateButton(buttonsContainer.transform, "YesButton", "Yes", ButtonQuitColor, Vector2.zero);
        RectTransform yesRect = yesBtn.GetComponent<RectTransform>();
        yesRect.sizeDelta = new Vector2(120, 50);
        LayoutElement yesLayout = yesBtn.AddComponent<LayoutElement>();
        yesLayout.preferredWidth = 120;
        yesLayout.preferredHeight = 50;

        // No Button
        GameObject noBtn = CreateButton(buttonsContainer.transform, "NoButton", "No", ButtonColor, Vector2.zero);
        RectTransform noRect = noBtn.GetComponent<RectTransform>();
        noRect.sizeDelta = new Vector2(120, 50);
        LayoutElement noLayout = noBtn.AddComponent<LayoutElement>();
        noLayout.preferredWidth = 120;
        noLayout.preferredHeight = 50;

        return overlay;
    }

    // ========== HELPER METHODS ==========

    static GameObject CreatePanel(Transform parent, string name, Vector2 position, Vector2 size)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);

        Image image = panel.AddComponent<Image>();
        image.color = PanelColor;

        return panel;
    }

    static GameObject CreateButton(Transform parent, string name, string text, Color color, Vector2 position)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(200, 50);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);

        Image image = btnObj.AddComponent<Image>();
        image.color = color;

        Button button = btnObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.2f;
        colors.pressedColor = color * 0.8f;
        colors.selectedColor = color;
        button.colors = colors;

        // Button text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 24;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = TextColor;

        return btnObj;
    }

    static GameObject CreateText(Transform parent, string name, string text, int fontSize, FontStyles style)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        RectTransform rect = textObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500, 60);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = TextColor;

        return textObj;
    }

    static void CreateSectionHeader(Transform parent, string name, string text, Vector2 position)
    {
        GameObject headerObj = CreateText(parent, name, text, 20, FontStyles.Bold);
        RectTransform rect = headerObj.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        headerObj.GetComponent<TextMeshProUGUI>().color = new Color(0.6f, 0.8f, 1f, 1f);
    }

    static void CreateSliderOption(Transform parent, string name, string label, Vector2 position)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);

        RectTransform containerRect = container.AddComponent<RectTransform>();
        containerRect.anchoredPosition = position;
        containerRect.sizeDelta = new Vector2(500, 40);
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Label
        GameObject labelObj = CreateText(container.transform, "Label", label, 16, FontStyles.Normal);
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(0, 0.5f);
        labelRect.anchoredPosition = new Vector2(100, 0);
        labelRect.sizeDelta = new Vector2(150, 30);
        labelObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        // Slider background
        GameObject sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(container.transform, false);

        RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(1, 0.5f);
        sliderRect.anchorMax = new Vector2(1, 0.5f);
        sliderRect.anchoredPosition = new Vector2(-100, 0);
        sliderRect.sizeDelta = new Vector2(200, 20);

        Image bgImage = sliderObj.AddComponent<Image>();
        bgImage.color = new Color(0.3f, 0.3f, 0.35f, 1f);

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 1;

        // Fill area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = new Vector2(1, 1);
        fillAreaRect.sizeDelta = Vector2.zero;
        fillAreaRect.offsetMin = new Vector2(5, 5);
        fillAreaRect.offsetMax = new Vector2(-5, -5);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(1, 1);
        fillRect.sizeDelta = Vector2.zero;

        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = ButtonColor;

        slider.fillRect = fillRect;
    }

    static void CreateDropdownOption(Transform parent, string name, string label, Vector2 position)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);

        RectTransform containerRect = container.AddComponent<RectTransform>();
        containerRect.anchoredPosition = position;
        containerRect.sizeDelta = new Vector2(500, 40);
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Label
        GameObject labelObj = CreateText(container.transform, "Label", label, 16, FontStyles.Normal);
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(0, 0.5f);
        labelRect.anchoredPosition = new Vector2(100, 0);
        labelRect.sizeDelta = new Vector2(150, 30);
        labelObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        // Dropdown background
        GameObject dropdownObj = new GameObject("Dropdown");
        dropdownObj.transform.SetParent(container.transform, false);

        RectTransform dropdownRect = dropdownObj.AddComponent<RectTransform>();
        dropdownRect.anchorMin = new Vector2(1, 0.5f);
        dropdownRect.anchorMax = new Vector2(1, 0.5f);
        dropdownRect.anchoredPosition = new Vector2(-100, 0);
        dropdownRect.sizeDelta = new Vector2(200, 35);

        Image ddImage = dropdownObj.AddComponent<Image>();
        ddImage.color = new Color(0.25f, 0.25f, 0.3f, 1f);

        TMP_Dropdown dropdown = dropdownObj.AddComponent<TMP_Dropdown>();

        // Caption text
        GameObject captionObj = new GameObject("Label");
        captionObj.transform.SetParent(dropdownObj.transform, false);
        RectTransform captionRect = captionObj.AddComponent<RectTransform>();
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(10, 0);
        captionRect.offsetMax = new Vector2(-30, 0);

        TextMeshProUGUI captionText = captionObj.AddComponent<TextMeshProUGUI>();
        captionText.text = "Option 1";
        captionText.fontSize = 16;
        captionText.alignment = TextAlignmentOptions.Left;
        captionText.color = TextColor;

        dropdown.captionText = captionText;

        // Add some default options
        dropdown.options.Add(new TMP_Dropdown.OptionData("Option 1"));
        dropdown.options.Add(new TMP_Dropdown.OptionData("Option 2"));
        dropdown.options.Add(new TMP_Dropdown.OptionData("Option 3"));
    }

    static void LinkToManager()
    {
        MainMenuManager manager = FindFirstObjectByType<MainMenuManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Error", "No MainMenuManager found in scene!", "OK");
            return;
        }

        // Find MainMenuUI Canvas
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        Canvas menuCanvas = null;
        foreach (var canvas in canvases)
        {
            if (canvas.gameObject.name.Contains("MainMenu"))
            {
                menuCanvas = canvas;
                break;
            }
        }

        if (menuCanvas == null)
        {
            EditorUtility.DisplayDialog("Error", "No MainMenuUI Canvas found in scene!", "OK");
            return;
        }

        Transform root = menuCanvas.transform;

        // Find panels
        manager.mainPanel = FindDeep(root, "MainPanel")?.gameObject;
        manager.optionsPanel = FindDeep(root, "OptionsPanel")?.gameObject;
        manager.quitDialog = FindDeep(root, "QuitDialog")?.gameObject;

        // Find buttons
        if (manager.mainPanel != null)
        {
            manager.startButton = FindDeep(manager.mainPanel.transform, "StartButton")?.GetComponent<Button>();
            manager.optionsButton = FindDeep(manager.mainPanel.transform, "OptionsButton")?.GetComponent<Button>();
            manager.quitButton = FindDeep(manager.mainPanel.transform, "QuitButton")?.GetComponent<Button>();
        }

        if (manager.optionsPanel != null)
        {
            manager.backButton = FindDeep(manager.optionsPanel.transform, "BackButton")?.GetComponent<Button>();
        }

        if (manager.quitDialog != null)
        {
            manager.quitYesButton = FindDeep(manager.quitDialog.transform, "YesButton")?.GetComponent<Button>();
            manager.quitNoButton = FindDeep(manager.quitDialog.transform, "NoButton")?.GetComponent<Button>();
        }

        EditorUtility.SetDirty(manager);
        Debug.Log("[MainMenuUISetup] Linked references to MainMenuManager");
        EditorUtility.DisplayDialog("Success", "Linked all UI references to MainMenuManager!", "OK");
    }

    static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;

        foreach (Transform child in parent)
        {
            Transform result = FindDeep(child, name);
            if (result != null) return result;
        }

        return null;
    }
}
#endif
