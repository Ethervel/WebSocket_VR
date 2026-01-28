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
    private static readonly Color TabActiveColor = new Color(0.3f, 0.6f, 1f, 1f);
    private static readonly Color TabInactiveColor = new Color(0.2f, 0.2f, 0.25f, 1f);
    private static readonly Color TextColor = Color.white;
    private static readonly Color LabelColor = new Color(0.7f, 0.7f, 0.8f, 1f);
    private static readonly Color HeaderColor = new Color(0.6f, 0.8f, 1f, 1f);

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
            "- Options Panel with Tabs (Audio, Graphics, Controls)\n" +
            "- Quit Dialog (Yes/No)\n" +
            "- MainMenuSettings and MainMenuOptionsUI components",
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

        // Create Options Panel with full UI
        GameObject optionsPanel = CreateFullOptionsPanel(bgObj.transform);
        optionsPanel.SetActive(false);

        // Create Quit Dialog
        GameObject quitDialog = CreateQuitDialog(bgObj.transform);
        quitDialog.SetActive(false);

        // Add MainMenuSettings if not exists
        MainMenuSettings settings = FindFirstObjectByType<MainMenuSettings>();
        if (settings == null)
        {
            GameObject settingsObj = new GameObject("MainMenuSettings");
            settings = settingsObj.AddComponent<MainMenuSettings>();
            Debug.Log("[MainMenuUISetup] Created MainMenuSettings object");
        }

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
            manager.backButton = FindDeep(optionsPanel.transform, "BackButton")?.GetComponent<Button>();
            manager.quitYesButton = quitDialog.transform.Find("DialogBox/ButtonsContainer/YesButton")?.GetComponent<Button>();
            manager.quitNoButton = quitDialog.transform.Find("DialogBox/ButtonsContainer/NoButton")?.GetComponent<Button>();

            EditorUtility.SetDirty(manager);
            Debug.Log("[MainMenuUISetup] Linked all references to MainMenuManager");
        }

        // Link MainMenuOptionsUI
        MainMenuOptionsUI optionsUI = optionsPanel.GetComponent<MainMenuOptionsUI>();
        if (optionsUI != null)
        {
            LinkOptionsUIReferences(optionsUI, optionsPanel);
            EditorUtility.SetDirty(optionsUI);
            Debug.Log("[MainMenuUISetup] Linked all references to MainMenuOptionsUI");
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

    static GameObject CreateFullOptionsPanel(Transform parent)
    {
        GameObject panel = CreatePanel(parent, "OptionsPanel", Vector2.zero, new Vector2(700, 550));
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

        // Add MainMenuOptionsUI component
        MainMenuOptionsUI optionsUI = panel.AddComponent<MainMenuOptionsUI>();

        // Title
        GameObject titleObj = CreateText(panel.transform, "Title", "Options", 36, FontStyles.Bold);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchoredPosition = new Vector2(0, 240);

        // Tab Buttons Container
        GameObject tabContainer = new GameObject("TabContainer");
        tabContainer.transform.SetParent(panel.transform, false);
        RectTransform tabContainerRect = tabContainer.AddComponent<RectTransform>();
        tabContainerRect.anchoredPosition = new Vector2(0, 190);
        tabContainerRect.sizeDelta = new Vector2(600, 45);

        HorizontalLayoutGroup tabLayout = tabContainer.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 10;
        tabLayout.childAlignment = TextAnchor.MiddleCenter;
        tabLayout.childForceExpandWidth = false;
        tabLayout.childForceExpandHeight = false;

        // Tab Buttons
        GameObject audioTab = CreateTabButton(tabContainer.transform, "AudioTab", "Audio");
        GameObject graphicsTab = CreateTabButton(tabContainer.transform, "GraphicsTab", "Graphics");
        GameObject controlsTab = CreateTabButton(tabContainer.transform, "ControlsTab", "Controls");

        // Content Area
        GameObject contentArea = CreatePanel(panel.transform, "ContentArea", new Vector2(0, -20), new Vector2(650, 350));
        contentArea.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.17f, 0.9f);

        // Audio Panel
        GameObject audioPanel = CreateAudioPanel(contentArea.transform);

        // Graphics Panel
        GameObject graphicsPanel = CreateGraphicsPanel(contentArea.transform);
        graphicsPanel.SetActive(false);

        // Controls Panel
        GameObject controlsPanel = CreateControlsPanel(contentArea.transform);
        controlsPanel.SetActive(false);

        // Bottom Buttons
        GameObject bottomButtons = new GameObject("BottomButtons");
        bottomButtons.transform.SetParent(panel.transform, false);
        RectTransform bottomRect = bottomButtons.AddComponent<RectTransform>();
        bottomRect.anchoredPosition = new Vector2(0, -230);
        bottomRect.sizeDelta = new Vector2(600, 50);

        HorizontalLayoutGroup bottomLayout = bottomButtons.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.spacing = 20;
        bottomLayout.childAlignment = TextAnchor.MiddleCenter;
        bottomLayout.childForceExpandWidth = false;
        bottomLayout.childForceExpandHeight = false;

        // Reset Button
        GameObject resetBtn = CreateButton(bottomButtons.transform, "ResetButton", "Reset Defaults", new Color(0.5f, 0.5f, 0.5f, 1f), Vector2.zero);
        resetBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 45);
        resetBtn.AddComponent<LayoutElement>().preferredWidth = 180;

        // Back Button
        GameObject backBtn = CreateButton(bottomButtons.transform, "BackButton", "Back", ButtonColor, Vector2.zero);
        backBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 45);
        backBtn.AddComponent<LayoutElement>().preferredWidth = 180;

        return panel;
    }

    static GameObject CreateTabButton(Transform parent, string name, string text)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(150, 40);

        Image image = btnObj.AddComponent<Image>();
        image.color = TabInactiveColor;

        Button button = btnObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = TabInactiveColor;
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.35f, 1f);
        colors.pressedColor = TabActiveColor;
        colors.selectedColor = TabActiveColor;
        button.colors = colors;

        LayoutElement layout = btnObj.AddComponent<LayoutElement>();
        layout.preferredWidth = 150;
        layout.preferredHeight = 40;

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
        tmp.fontSize = 20;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = LabelColor;

        return btnObj;
    }

    static GameObject CreateAudioPanel(Transform parent)
    {
        GameObject panel = new GameObject("AudioPanel");
        panel.transform.SetParent(parent, false);

        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panelRect.offsetMin = new Vector2(20, 20);
        panelRect.offsetMax = new Vector2(-20, -20);

        // Master Volume
        CreateSliderWithLabel(panel.transform, "MasterVolume", "Master Volume", new Vector2(0, 100), true);

        // Voice Volume
        CreateSliderWithLabel(panel.transform, "VoiceVolume", "Voice Volume", new Vector2(0, 40), true);

        // Microphone Dropdown
        CreateDropdownWithLabel(panel.transform, "Microphone", "Microphone", new Vector2(0, -30));

        return panel;
    }

    static GameObject CreateGraphicsPanel(Transform parent)
    {
        GameObject panel = new GameObject("GraphicsPanel");
        panel.transform.SetParent(parent, false);

        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panelRect.offsetMin = new Vector2(20, 20);
        panelRect.offsetMax = new Vector2(-20, -20);

        // Quality Dropdown
        CreateDropdownWithLabel(panel.transform, "Quality", "Quality", new Vector2(0, 100));

        // Desktop Only Container
        GameObject desktopOnly = new GameObject("DesktopOnly");
        desktopOnly.transform.SetParent(panel.transform, false);
        RectTransform desktopRect = desktopOnly.AddComponent<RectTransform>();
        desktopRect.anchorMin = Vector2.zero;
        desktopRect.anchorMax = Vector2.one;
        desktopRect.sizeDelta = Vector2.zero;

        // Resolution Dropdown
        CreateDropdownWithLabel(desktopOnly.transform, "Resolution", "Resolution", new Vector2(0, 40));

        // Fullscreen Toggle
        CreateToggleWithLabel(desktopOnly.transform, "Fullscreen", "Fullscreen", new Vector2(0, -20));

        return panel;
    }

    static GameObject CreateControlsPanel(Transform parent)
    {
        GameObject panel = new GameObject("ControlsPanel");
        panel.transform.SetParent(parent, false);

        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.sizeDelta = Vector2.zero;
        panelRect.offsetMin = new Vector2(20, 20);
        panelRect.offsetMax = new Vector2(-20, -20);

        // VR Controls Container
        GameObject vrControls = new GameObject("VRControls");
        vrControls.transform.SetParent(panel.transform, false);
        RectTransform vrRect = vrControls.AddComponent<RectTransform>();
        vrRect.anchorMin = Vector2.zero;
        vrRect.anchorMax = Vector2.one;
        vrRect.sizeDelta = Vector2.zero;

        // VR Header
        GameObject vrHeader = CreateText(vrControls.transform, "VRHeader", "VR Controls", 18, FontStyles.Bold);
        vrHeader.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 120);
        vrHeader.GetComponent<TextMeshProUGUI>().color = HeaderColor;

        // Turn Mode Dropdown
        CreateDropdownWithLabel(vrControls.transform, "TurnMode", "Turn Mode", new Vector2(0, 80));

        // Snap Angle Container
        GameObject snapContainer = new GameObject("SnapAngleContainer");
        snapContainer.transform.SetParent(vrControls.transform, false);
        RectTransform snapContainerRect = snapContainer.AddComponent<RectTransform>();
        snapContainerRect.anchoredPosition = new Vector2(0, 30);
        snapContainerRect.sizeDelta = new Vector2(600, 50);

        CreateSliderWithLabel(snapContainer.transform, "SnapAngle", "Snap Angle", Vector2.zero, true);

        // Smooth Turn Container
        GameObject smoothContainer = new GameObject("SmoothTurnContainer");
        smoothContainer.transform.SetParent(vrControls.transform, false);
        RectTransform smoothContainerRect = smoothContainer.AddComponent<RectTransform>();
        smoothContainerRect.anchoredPosition = new Vector2(0, -20);
        smoothContainerRect.sizeDelta = new Vector2(600, 50);
        smoothContainer.SetActive(false);

        CreateSliderWithLabel(smoothContainer.transform, "SmoothTurnSpeed", "Turn Speed", Vector2.zero, true);

        // Desktop Controls Container
        GameObject desktopControls = new GameObject("DesktopControls");
        desktopControls.transform.SetParent(panel.transform, false);
        RectTransform desktopRect = desktopControls.AddComponent<RectTransform>();
        desktopRect.anchorMin = Vector2.zero;
        desktopRect.anchorMax = Vector2.one;
        desktopRect.sizeDelta = Vector2.zero;
        desktopControls.SetActive(false);

        // Desktop Header
        GameObject desktopHeader = CreateText(desktopControls.transform, "DesktopHeader", "Desktop Controls", 18, FontStyles.Bold);
        desktopHeader.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 120);
        desktopHeader.GetComponent<TextMeshProUGUI>().color = HeaderColor;

        // Mouse Sensitivity
        CreateSliderWithLabel(desktopControls.transform, "MouseSensitivity", "Mouse Sensitivity", new Vector2(0, 70), true);

        // Invert Y
        CreateToggleWithLabel(desktopControls.transform, "InvertY", "Invert Y Axis", new Vector2(0, 20));

        return panel;
    }

    static void CreateSliderWithLabel(Transform parent, string name, string label, Vector2 position, bool showValue)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);

        RectTransform containerRect = container.AddComponent<RectTransform>();
        containerRect.anchoredPosition = position;
        containerRect.sizeDelta = new Vector2(580, 40);
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Label
        GameObject labelObj = CreateText(container.transform, "Label", label, 18, FontStyles.Normal);
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(0, 0.5f);
        labelRect.pivot = new Vector2(0, 0.5f);
        labelRect.anchoredPosition = new Vector2(0, 0);
        labelRect.sizeDelta = new Vector2(200, 30);
        labelObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
        labelObj.GetComponent<TextMeshProUGUI>().color = LabelColor;

        // Slider
        GameObject sliderObj = CreateSlider(container.transform, "Slider");
        RectTransform sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRect.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRect.anchoredPosition = new Vector2(50, 0);
        sliderRect.sizeDelta = new Vector2(250, 25);

        // Value Text
        if (showValue)
        {
            GameObject valueObj = CreateText(container.transform, "Value", "100%", 16, FontStyles.Normal);
            RectTransform valueRect = valueObj.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1, 0.5f);
            valueRect.anchorMax = new Vector2(1, 0.5f);
            valueRect.pivot = new Vector2(1, 0.5f);
            valueRect.anchoredPosition = new Vector2(0, 0);
            valueRect.sizeDelta = new Vector2(80, 30);
            valueObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;
            valueObj.GetComponent<TextMeshProUGUI>().color = TextColor;
        }
    }

    static GameObject CreateSlider(Transform parent, string name)
    {
        // Create slider with proper structure for Unity UI Slider component
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);

        RectTransform sliderRect = sliderObj.AddComponent<RectTransform>();
        sliderRect.sizeDelta = new Vector2(250, 25);

        // Background
        GameObject background = new GameObject("Background");
        background.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRect = background.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.25f);
        bgRect.anchorMax = new Vector2(1, 0.75f);
        bgRect.sizeDelta = Vector2.zero;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.25f, 0.25f, 0.3f, 1f);

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1, 0.75f);
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-5, 0);

        // Fill
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = ButtonColor;

        // Handle Slide Area
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10, 0);
        handleAreaRect.offsetMax = new Vector2(-10, 0);

        // Handle
        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20, 0);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = Color.white;

        // Add Slider component
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 1;

        return sliderObj;
    }

    static void CreateDropdownWithLabel(Transform parent, string name, string label, Vector2 position)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);

        RectTransform containerRect = container.AddComponent<RectTransform>();
        containerRect.anchoredPosition = position;
        containerRect.sizeDelta = new Vector2(580, 40);
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Label
        GameObject labelObj = CreateText(container.transform, "Label", label, 18, FontStyles.Normal);
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(0, 0.5f);
        labelRect.pivot = new Vector2(0, 0.5f);
        labelRect.anchoredPosition = new Vector2(0, 0);
        labelRect.sizeDelta = new Vector2(200, 30);
        labelObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
        labelObj.GetComponent<TextMeshProUGUI>().color = LabelColor;

        // Dropdown
        GameObject dropdownObj = new GameObject("Dropdown");
        dropdownObj.transform.SetParent(container.transform, false);

        RectTransform dropdownRect = dropdownObj.AddComponent<RectTransform>();
        dropdownRect.anchorMin = new Vector2(1, 0.5f);
        dropdownRect.anchorMax = new Vector2(1, 0.5f);
        dropdownRect.pivot = new Vector2(1, 0.5f);
        dropdownRect.anchoredPosition = new Vector2(0, 0);
        dropdownRect.sizeDelta = new Vector2(280, 35);

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
        captionText.text = "Select...";
        captionText.fontSize = 16;
        captionText.alignment = TextAlignmentOptions.Left;
        captionText.color = TextColor;

        dropdown.captionText = captionText;

        // Arrow
        GameObject arrowObj = new GameObject("Arrow");
        arrowObj.transform.SetParent(dropdownObj.transform, false);
        RectTransform arrowRect = arrowObj.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1, 0.5f);
        arrowRect.anchorMax = new Vector2(1, 0.5f);
        arrowRect.pivot = new Vector2(1, 0.5f);
        arrowRect.anchoredPosition = new Vector2(-10, 0);
        arrowRect.sizeDelta = new Vector2(15, 15);

        TextMeshProUGUI arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
        arrowText.text = "v";
        arrowText.fontSize = 14;
        arrowText.alignment = TextAlignmentOptions.Center;
        arrowText.color = TextColor;

        // Template (needed for dropdown to work)
        GameObject template = new GameObject("Template");
        template.transform.SetParent(dropdownObj.transform, false);
        RectTransform templateRect = template.AddComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1);
        templateRect.anchoredPosition = new Vector2(0, 0);
        templateRect.sizeDelta = new Vector2(0, 150);

        Image templateImage = template.AddComponent<Image>();
        templateImage.color = new Color(0.2f, 0.2f, 0.25f, 1f);

        ScrollRect scrollRect = template.AddComponent<ScrollRect>();

        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(template.transform, false);
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportRect.pivot = new Vector2(0, 1);

        viewport.AddComponent<Mask>().showMaskGraphic = false;
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = Color.white;

        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 30);

        // Item
        GameObject item = new GameObject("Item");
        item.transform.SetParent(content.transform, false);
        RectTransform itemRect = item.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.sizeDelta = new Vector2(0, 30);

        Toggle itemToggle = item.AddComponent<Toggle>();

        // Item Background
        GameObject itemBg = new GameObject("Item Background");
        itemBg.transform.SetParent(item.transform, false);
        RectTransform itemBgRect = itemBg.AddComponent<RectTransform>();
        itemBgRect.anchorMin = Vector2.zero;
        itemBgRect.anchorMax = Vector2.one;
        itemBgRect.sizeDelta = Vector2.zero;
        Image itemBgImage = itemBg.AddComponent<Image>();
        itemBgImage.color = new Color(0.3f, 0.3f, 0.35f, 1f);

        // Item Checkmark
        GameObject checkmark = new GameObject("Item Checkmark");
        checkmark.transform.SetParent(item.transform, false);
        RectTransform checkmarkRect = checkmark.AddComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0, 0.5f);
        checkmarkRect.anchoredPosition = new Vector2(10, 0);
        checkmarkRect.sizeDelta = new Vector2(20, 20);
        Image checkmarkImage = checkmark.AddComponent<Image>();
        checkmarkImage.color = ButtonColor;

        // Item Label
        GameObject itemLabel = new GameObject("Item Label");
        itemLabel.transform.SetParent(item.transform, false);
        RectTransform itemLabelRect = itemLabel.AddComponent<RectTransform>();
        itemLabelRect.anchorMin = Vector2.zero;
        itemLabelRect.anchorMax = Vector2.one;
        itemLabelRect.offsetMin = new Vector2(35, 0);
        itemLabelRect.offsetMax = new Vector2(-10, 0);

        TextMeshProUGUI itemLabelText = itemLabel.AddComponent<TextMeshProUGUI>();
        itemLabelText.text = "Option";
        itemLabelText.fontSize = 16;
        itemLabelText.alignment = TextAlignmentOptions.Left;
        itemLabelText.color = TextColor;

        // Configure toggle
        itemToggle.targetGraphic = itemBgImage;
        itemToggle.graphic = checkmarkImage;

        // Configure scroll rect
        scrollRect.content = contentRect;
        scrollRect.viewport = viewportRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        // Configure dropdown
        dropdown.template = templateRect;
        dropdown.itemText = itemLabelText;

        template.SetActive(false);
    }

    static void CreateToggleWithLabel(Transform parent, string name, string label, Vector2 position)
    {
        GameObject container = new GameObject(name);
        container.transform.SetParent(parent, false);

        RectTransform containerRect = container.AddComponent<RectTransform>();
        containerRect.anchoredPosition = position;
        containerRect.sizeDelta = new Vector2(580, 40);
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);

        // Label
        GameObject labelObj = CreateText(container.transform, "Label", label, 18, FontStyles.Normal);
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(0, 0.5f);
        labelRect.pivot = new Vector2(0, 0.5f);
        labelRect.anchoredPosition = new Vector2(0, 0);
        labelRect.sizeDelta = new Vector2(200, 30);
        labelObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
        labelObj.GetComponent<TextMeshProUGUI>().color = LabelColor;

        // Toggle
        GameObject toggleObj = new GameObject("Toggle");
        toggleObj.transform.SetParent(container.transform, false);

        RectTransform toggleRect = toggleObj.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(1, 0.5f);
        toggleRect.anchorMax = new Vector2(1, 0.5f);
        toggleRect.pivot = new Vector2(1, 0.5f);
        toggleRect.anchoredPosition = new Vector2(0, 0);
        toggleRect.sizeDelta = new Vector2(50, 30);

        // Background
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(toggleObj.transform, false);
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0.25f, 0.25f, 0.3f, 1f);

        // Checkmark
        GameObject checkObj = new GameObject("Checkmark");
        checkObj.transform.SetParent(bgObj.transform, false);
        RectTransform checkRect = checkObj.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.1f, 0.1f);
        checkRect.anchorMax = new Vector2(0.9f, 0.9f);
        checkRect.sizeDelta = Vector2.zero;
        Image checkImage = checkObj.AddComponent<Image>();
        checkImage.color = ButtonColor;

        // Toggle component
        Toggle toggle = toggleObj.AddComponent<Toggle>();
        toggle.targetGraphic = bgImage;
        toggle.graphic = checkImage;
        toggle.isOn = false;
    }

    static void LinkOptionsUIReferences(MainMenuOptionsUI optionsUI, GameObject optionsPanel)
    {
        Transform root = optionsPanel.transform;

        // Tab buttons
        optionsUI.audioTabButton = FindDeep(root, "AudioTab")?.GetComponent<Button>();
        optionsUI.graphicsTabButton = FindDeep(root, "GraphicsTab")?.GetComponent<Button>();
        optionsUI.controlsTabButton = FindDeep(root, "ControlsTab")?.GetComponent<Button>();

        // Panels
        optionsUI.audioPanel = FindDeep(root, "AudioPanel")?.gameObject;
        optionsUI.graphicsPanel = FindDeep(root, "GraphicsPanel")?.gameObject;
        optionsUI.controlsPanel = FindDeep(root, "ControlsPanel")?.gameObject;

        // Audio controls
        Transform audioRoot = optionsUI.audioPanel?.transform;
        if (audioRoot != null)
        {
            optionsUI.masterVolumeSlider = FindDeep(audioRoot, "MasterVolume")?.GetComponentInChildren<Slider>();
            optionsUI.masterVolumeText = FindDeep(audioRoot, "MasterVolume/Value")?.GetComponent<TextMeshProUGUI>();
            optionsUI.voiceVolumeSlider = FindDeep(audioRoot, "VoiceVolume")?.GetComponentInChildren<Slider>();
            optionsUI.voiceVolumeText = FindDeep(audioRoot, "VoiceVolume/Value")?.GetComponent<TextMeshProUGUI>();
            optionsUI.microphoneDropdown = FindDeep(audioRoot, "Microphone")?.GetComponentInChildren<TMP_Dropdown>();
        }

        // Graphics controls
        Transform graphicsRoot = optionsUI.graphicsPanel?.transform;
        if (graphicsRoot != null)
        {
            optionsUI.qualityDropdown = FindDeep(graphicsRoot, "Quality")?.GetComponentInChildren<TMP_Dropdown>();
            optionsUI.graphicsDesktopOnly = FindDeep(graphicsRoot, "DesktopOnly")?.gameObject;
            if (optionsUI.graphicsDesktopOnly != null)
            {
                optionsUI.resolutionDropdown = FindDeep(optionsUI.graphicsDesktopOnly.transform, "Resolution")?.GetComponentInChildren<TMP_Dropdown>();
                optionsUI.fullscreenToggle = FindDeep(optionsUI.graphicsDesktopOnly.transform, "Fullscreen")?.GetComponentInChildren<Toggle>();
            }
        }

        // Controls
        Transform controlsRoot = optionsUI.controlsPanel?.transform;
        if (controlsRoot != null)
        {
            optionsUI.vrControlsPanel = FindDeep(controlsRoot, "VRControls")?.gameObject;
            optionsUI.desktopControlsPanel = FindDeep(controlsRoot, "DesktopControls")?.gameObject;

            if (optionsUI.vrControlsPanel != null)
            {
                optionsUI.turnModeDropdown = FindDeep(optionsUI.vrControlsPanel.transform, "TurnMode")?.GetComponentInChildren<TMP_Dropdown>();
                optionsUI.snapAngleContainer = FindDeep(optionsUI.vrControlsPanel.transform, "SnapAngleContainer")?.gameObject;
                optionsUI.smoothTurnContainer = FindDeep(optionsUI.vrControlsPanel.transform, "SmoothTurnContainer")?.gameObject;

                if (optionsUI.snapAngleContainer != null)
                {
                    optionsUI.snapAngleSlider = optionsUI.snapAngleContainer.GetComponentInChildren<Slider>();
                    optionsUI.snapAngleText = FindDeep(optionsUI.snapAngleContainer.transform, "Value")?.GetComponent<TextMeshProUGUI>();
                }
                if (optionsUI.smoothTurnContainer != null)
                {
                    optionsUI.smoothTurnSpeedSlider = optionsUI.smoothTurnContainer.GetComponentInChildren<Slider>();
                    optionsUI.smoothTurnSpeedText = FindDeep(optionsUI.smoothTurnContainer.transform, "Value")?.GetComponent<TextMeshProUGUI>();
                }
            }

            if (optionsUI.desktopControlsPanel != null)
            {
                optionsUI.mouseSensitivitySlider = FindDeep(optionsUI.desktopControlsPanel.transform, "MouseSensitivity")?.GetComponentInChildren<Slider>();
                optionsUI.mouseSensitivityText = FindDeep(optionsUI.desktopControlsPanel.transform, "MouseSensitivity/Value")?.GetComponent<TextMeshProUGUI>();
                optionsUI.invertYToggle = FindDeep(optionsUI.desktopControlsPanel.transform, "InvertY")?.GetComponentInChildren<Toggle>();
            }
        }

        // Buttons
        optionsUI.resetButton = FindDeep(root, "ResetButton")?.GetComponent<Button>();
        optionsUI.backButton = FindDeep(root, "BackButton")?.GetComponent<Button>();
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

            // Also link MainMenuOptionsUI
            MainMenuOptionsUI optionsUI = manager.optionsPanel.GetComponent<MainMenuOptionsUI>();
            if (optionsUI != null)
            {
                LinkOptionsUIReferences(optionsUI, manager.optionsPanel);
                EditorUtility.SetDirty(optionsUI);
            }
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
        // Support path-based search (e.g., "Parent/Child")
        if (name.Contains("/"))
        {
            string[] parts = name.Split('/');
            Transform current = parent;
            foreach (string part in parts)
            {
                current = FindDeep(current, part);
                if (current == null) return null;
            }
            return current;
        }

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
