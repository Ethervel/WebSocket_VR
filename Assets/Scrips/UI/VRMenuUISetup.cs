using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Helper to connect close button to menu toggle.
/// </summary>
public class VRMenuCloseHelper : MonoBehaviour
{
    [Tooltip("Close button - if null, will auto-find 'CloseButton' child")]
    public Button closeButton;

    private VRMenuToggle _cachedToggle;
    private Canvas _parentCanvas;

    void Awake()
    {
        // Auto-find close button if not assigned
        if (closeButton == null)
        {
            // Try to find by name in children
            Transform closeBtnTransform = transform.Find("CloseButton");
            if (closeBtnTransform == null)
            {
                // Search recursively
                closeBtnTransform = FindChildRecursive(transform, "CloseButton");
            }

            if (closeBtnTransform != null)
            {
                closeButton = closeBtnTransform.GetComponent<Button>();
            }
        }
    }

    void Start()
    {
        // Cache the parent canvas
        _parentCanvas = GetComponentInParent<Canvas>();
        if (_parentCanvas == null)
        {
            _parentCanvas = GetComponent<Canvas>();
        }

        // Pre-cache VRMenuToggle
        _cachedToggle = FindFirstObjectByType<VRMenuToggle>(FindObjectsInactive.Include);

        if (closeButton != null)
        {
            // Remove any existing listeners to avoid duplicates
            closeButton.onClick.RemoveListener(CloseMenu);
            closeButton.onClick.AddListener(CloseMenu);
            Debug.Log("[VRMenuCloseHelper] Close button listener registered");
        }
        else
        {
            Debug.LogWarning("[VRMenuCloseHelper] No close button found! Make sure there's a Button named 'CloseButton'");
        }
    }

    Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            Transform found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    void CloseMenu()
    {
        Debug.Log("[VRMenuCloseHelper] Close button clicked");

        // Try to find VRMenuToggle if not cached
        if (_cachedToggle == null)
        {
            _cachedToggle = FindFirstObjectByType<VRMenuToggle>(FindObjectsInactive.Include);
        }

        if (_cachedToggle != null)
        {
            _cachedToggle.HideMenu();
            Debug.Log("[VRMenuCloseHelper] Menu hidden via VRMenuToggle");
        }
        else
        {
            // Fallback: hide the canvas or this gameObject
            if (_parentCanvas != null)
            {
                _parentCanvas.gameObject.SetActive(false);
                Debug.Log("[VRMenuCloseHelper] Canvas hidden directly (no VRMenuToggle found)");
            }
            else
            {
                gameObject.SetActive(false);
                Debug.Log("[VRMenuCloseHelper] GameObject hidden directly (no VRMenuToggle found)");
            }
        }
    }

    void OnDestroy()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseMenu);
        }
    }
}

/// <summary>
/// Generates complete VR Menu with Room, Avatar, Voice, Settings pages + Exit button.
/// Use via menu: GameObject > UI > Create VR Menu
/// </summary>
public class VRMenuUISetup : MonoBehaviour
{
    [Header("Appearance")]
    public Color backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.98f);
    public Color sidebarColor = new Color(0.12f, 0.12f, 0.18f, 1f);
    public Color contentBgColor = new Color(0.1f, 0.1f, 0.14f, 1f);
    public Color buttonNormalColor = new Color(0.18f, 0.18f, 0.24f, 1f);
    public Color buttonSelectedColor = new Color(0.25f, 0.45f, 0.75f, 1f);
    public Color buttonHoverColor = new Color(0.25f, 0.25f, 0.32f, 1f);
    public Color exitButtonColor = new Color(0.7f, 0.2f, 0.2f, 1f);
    public Color textColor = Color.white;
    public Color textSecondaryColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Header("Dimensions")]
    public float canvasWidth = 900f;
    public float canvasHeight = 650f;
    public float canvasScale = 0.001f;
    public float sidebarWidth = 90f;
    public float buttonSize = 60f;
    public float buttonSpacing = 12f;
    public float padding = 20f;

#if UNITY_EDITOR
    [MenuItem("GameObject/UI/Create VR Menu", false, 10)]
    static void CreateVRMenu()
    {
        GameObject setupObj = new GameObject("_VRMenuSetup_Temp");
        VRMenuUISetup setup = setupObj.AddComponent<VRMenuUISetup>();
        GameObject menu = setup.GenerateMenu();
        DestroyImmediate(setupObj);
        Selection.activeGameObject = menu;
        Debug.Log("[VRMenuUISetup] VR Menu created successfully!");
    }

    [ContextMenu("Generate Menu")]
    public GameObject GenerateMenu()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("VRMenu");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 100; // Ensure menu renders on top of whiteboard

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        TrackedDeviceGraphicRaycaster raycaster = canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(canvasWidth, canvasHeight);
        canvasRect.localScale = Vector3.one * canvasScale;

        // Add VRFollowMenu
        VRFollowMenu followMenu = canvasObj.AddComponent<VRFollowMenu>();
        followMenu.distanceFromPlayer = 1.5f;
        followMenu.heightOffset = -0.1f;
        followMenu.followSpeed = 8f;

        // Background
        GameObject bgPanel = CreatePanel(canvasObj.transform, "Background", backgroundColor);
        SetRectTransformStretch(bgPanel.GetComponent<RectTransform>());

        // Sidebar
        GameObject sidebar = CreateSidebar(bgPanel.transform);

        // Content area
        GameObject content = CreateContentArea(bgPanel.transform);

        // Create pages
        GameObject pageRoom = CreatePageRoom(content.transform);
        GameObject pageRecording = CreatePageRecording(content.transform);
        GameObject pageAvatar = CreatePageAvatar(content.transform);
        GameObject pageVoice = CreatePageVoice(content.transform);
        GameObject pageSettings = CreatePageSettings(content.transform);

        // Only first page active
        pageRecording.SetActive(false);
        pageAvatar.SetActive(false);
        pageVoice.SetActive(false);
        pageSettings.SetActive(false);

        // Exit dialog
        GameObject exitDialog = CreateExitDialog(bgPanel.transform);
        exitDialog.SetActive(false);

        // Sidebar buttons container
        GameObject buttonsContainer = CreatePanel(sidebar.transform, "ButtonsContainer", Color.clear);
        RectTransform btnContainerRect = buttonsContainer.GetComponent<RectTransform>();
        btnContainerRect.anchorMin = new Vector2(0, 0);
        btnContainerRect.anchorMax = new Vector2(1, 1);
        btnContainerRect.offsetMin = new Vector2(0, 80); // Leave space for exit button
        btnContainerRect.offsetMax = Vector2.zero;

        VerticalLayoutGroup btnLayout = buttonsContainer.AddComponent<VerticalLayoutGroup>();
        btnLayout.padding = new RectOffset(15, 15, 15, 15);
        btnLayout.spacing = buttonSpacing;
        btnLayout.childAlignment = TextAnchor.UpperCenter;
        btnLayout.childControlWidth = false;
        btnLayout.childControlHeight = false;

        // Create sidebar buttons
        GameObject btnRoom = CreateSidebarButton(buttonsContainer.transform, "Room", null);
        GameObject btnRecording = CreateSidebarButton(buttonsContainer.transform, "Recording", null);
        GameObject btnAvatar = CreateSidebarButton(buttonsContainer.transform, "Avatar", null);
        GameObject btnVoice = CreateSidebarButton(buttonsContainer.transform, "Voice", null);
        GameObject btnSettings = CreateSidebarButton(buttonsContainer.transform, "Settings", null);

        // Exit button at bottom
        GameObject exitBtnContainer = CreatePanel(sidebar.transform, "ExitContainer", Color.clear);
        RectTransform exitContainerRect = exitBtnContainer.GetComponent<RectTransform>();
        exitContainerRect.anchorMin = new Vector2(0, 0);
        exitContainerRect.anchorMax = new Vector2(1, 0);
        exitContainerRect.pivot = new Vector2(0.5f, 0);
        exitContainerRect.anchoredPosition = Vector2.zero;
        exitContainerRect.sizeDelta = new Vector2(0, 80);

        GameObject exitBtn = CreateExitButton(exitBtnContainer.transform);

        // Add VRMenuSidebar component
        VRMenuSidebar menuSidebar = canvasObj.AddComponent<VRMenuSidebar>();
        menuSidebar.sidebarContainer = buttonsContainer.transform;
        menuSidebar.normalColor = buttonNormalColor;
        menuSidebar.selectedColor = buttonSelectedColor;
        menuSidebar.hoverColor = buttonHoverColor;

        // Setup pages
        menuSidebar.pages = new List<VRMenuSidebar.MenuPage>
        {
            new VRMenuSidebar.MenuPage { pageName = "Room", panel = pageRoom, icon = null },
            new VRMenuSidebar.MenuPage { pageName = "Recording", panel = pageRecording, icon = null },
            new VRMenuSidebar.MenuPage { pageName = "Avatar", panel = pageAvatar, icon = null },
            new VRMenuSidebar.MenuPage { pageName = "Voice", panel = pageVoice, icon = null },
            new VRMenuSidebar.MenuPage { pageName = "Settings", panel = pageSettings, icon = null }
        };

        // Create icon button prefab (hidden template)
        GameObject iconBtnPrefab = CreateIconButtonPrefab(buttonsContainer.transform);
        menuSidebar.iconButtonPrefab = iconBtnPrefab;
        iconBtnPrefab.SetActive(false);

        // Close button
        GameObject closeBtn = CreateCloseButton(bgPanel.transform);
        VRMenuCloseHelper closeHelper = canvasObj.AddComponent<VRMenuCloseHelper>();
        closeHelper.closeButton = closeBtn.GetComponent<Button>();

        // Menu title
        CreateTitle(bgPanel.transform);

        // Connect exit dialog
        VRMenuExitDialog exitDialogScript = canvasObj.AddComponent<VRMenuExitDialog>();
        exitDialogScript.exitButton = exitBtn.GetComponent<Button>();
        exitDialogScript.dialogPanel = exitDialog;

        // Find dialog buttons (they are inside DialogBox)
        Transform dialogBox = exitDialog.transform.Find("DialogBox");
        if (dialogBox != null)
        {
            exitDialogScript.leaveRoomButton = dialogBox.Find("LeaveRoomBtn")?.GetComponent<Button>();
            exitDialogScript.quitGameButton = dialogBox.Find("QuitGameBtn")?.GetComponent<Button>();
            exitDialogScript.cancelButton = dialogBox.Find("CancelBtn")?.GetComponent<Button>();
            exitDialogScript.titleText = dialogBox.Find("Title")?.GetComponent<TextMeshProUGUI>();
            exitDialogScript.messageText = dialogBox.Find("Message")?.GetComponent<TextMeshProUGUI>();
        }

        return canvasObj;
    }

    #region Sidebar

    GameObject CreateSidebar(Transform parent)
    {
        GameObject sidebar = CreatePanel(parent, "Sidebar", sidebarColor);
        RectTransform rect = sidebar.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(sidebarWidth, 0);

        return sidebar;
    }

    GameObject CreateSidebarButton(Transform parent, string name, Sprite icon)
    {
        GameObject btnObj = new GameObject($"Btn_{name}");
        btnObj.transform.SetParent(parent, false);

        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.color = buttonNormalColor;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = buttonNormalColor;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor = buttonSelectedColor;
        colors.selectedColor = buttonSelectedColor;
        btn.colors = colors;

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(buttonSize, buttonSize);

        // Icon placeholder
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(btnObj.transform, false);

        Image iconImage = iconObj.AddComponent<Image>();
        iconImage.color = textColor;
        if (icon != null) iconImage.sprite = icon;

        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(12, 12);
        iconRect.offsetMax = new Vector2(-12, -12);

        return btnObj;
    }

    GameObject CreateExitButton(Transform parent)
    {
        GameObject btnObj = new GameObject("ExitButton");
        btnObj.transform.SetParent(parent, false);

        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.color = exitButtonColor;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = exitButtonColor;
        colors.highlightedColor = new Color(0.85f, 0.25f, 0.25f, 1f);
        colors.pressedColor = new Color(0.5f, 0.15f, 0.15f, 1f);
        btn.colors = colors;

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(buttonSize, buttonSize);

        // Exit icon (X or door icon placeholder)
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(btnObj.transform, false);

        TextMeshProUGUI iconText = iconObj.AddComponent<TextMeshProUGUI>();
        iconText.text = "⏻"; // Power icon
        iconText.fontSize = 28;
        iconText.color = Color.white;
        iconText.alignment = TextAlignmentOptions.Center;

        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        SetRectTransformStretch(iconRect);

        return btnObj;
    }

    GameObject CreateIconButtonPrefab(Transform parent)
    {
        GameObject btnObj = new GameObject("IconButton_Template");
        btnObj.transform.SetParent(parent, false);

        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.color = buttonNormalColor;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = buttonNormalColor;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor = buttonSelectedColor;
        colors.selectedColor = buttonSelectedColor;
        btn.colors = colors;

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(buttonSize, buttonSize);

        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(btnObj.transform, false);

        Image iconImage = iconObj.AddComponent<Image>();
        iconImage.color = textColor;

        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(12, 12);
        iconRect.offsetMax = new Vector2(-12, -12);

        return btnObj;
    }

    #endregion

    #region Content Area

    GameObject CreateContentArea(Transform parent)
    {
        GameObject content = CreatePanel(parent, "Content", contentBgColor);
        RectTransform rect = content.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(sidebarWidth + 10, 10);
        rect.offsetMax = new Vector2(-10, -50);

        return content;
    }

    #endregion

    #region Page: Room

    GameObject CreatePageRoom(Transform parent)
    {
        GameObject page = CreatePanel(parent, "Page_Room", Color.clear);
        SetRectTransformStretch(page.GetComponent<RectTransform>());

        // Add page script
        page.AddComponent<VRMenuPageRoom>();

        // Title
        CreatePageTitle(page.transform, "Room");

        // Room Info section
        GameObject infoSection = CreatePanel(page.transform, "RoomInfo", new Color(0, 0, 0, 0.2f));
        RectTransform infoRect = infoSection.GetComponent<RectTransform>();
        infoRect.anchorMin = new Vector2(0, 1);
        infoRect.anchorMax = new Vector2(1, 1);
        infoRect.pivot = new Vector2(0.5f, 1);
        infoRect.anchoredPosition = new Vector2(0, -70);
        infoRect.sizeDelta = new Vector2(-padding * 2, 120);

        // Room name
        GameObject roomNameObj = CreateLabel(infoSection.transform, "RoomName", "Room: ---", 24);
        RectTransform roomNameRect = roomNameObj.GetComponent<RectTransform>();
        roomNameRect.anchorMin = new Vector2(0, 1);
        roomNameRect.anchorMax = new Vector2(1, 1);
        roomNameRect.pivot = new Vector2(0, 1);
        roomNameRect.anchoredPosition = new Vector2(15, -15);
        roomNameRect.sizeDelta = new Vector2(-30, 35);

        // Room code
        GameObject roomCodeObj = CreateLabel(infoSection.transform, "RoomCode", "Code: ---", 20);
        RectTransform roomCodeRect = roomCodeObj.GetComponent<RectTransform>();
        roomCodeRect.anchorMin = new Vector2(0, 1);
        roomCodeRect.anchorMax = new Vector2(0.5f, 1);
        roomCodeRect.pivot = new Vector2(0, 1);
        roomCodeRect.anchoredPosition = new Vector2(15, -50);
        roomCodeRect.sizeDelta = new Vector2(-15, 30);

        // Player count
        GameObject playerCountObj = CreateLabel(infoSection.transform, "PlayerCount", "Players: 0", 20);
        RectTransform playerCountRect = playerCountObj.GetComponent<RectTransform>();
        playerCountRect.anchorMin = new Vector2(0.5f, 1);
        playerCountRect.anchorMax = new Vector2(1, 1);
        playerCountRect.pivot = new Vector2(0, 1);
        playerCountRect.anchoredPosition = new Vector2(0, -50);
        playerCountRect.sizeDelta = new Vector2(-15, 30);

        // Copy code button
        GameObject copyBtn = CreateButton(infoSection.transform, "CopyCodeBtn", "Copy Code", buttonNormalColor);
        RectTransform copyBtnRect = copyBtn.GetComponent<RectTransform>();
        copyBtnRect.anchorMin = new Vector2(1, 0);
        copyBtnRect.anchorMax = new Vector2(1, 0);
        copyBtnRect.pivot = new Vector2(1, 0);
        copyBtnRect.anchoredPosition = new Vector2(-15, 15);
        copyBtnRect.sizeDelta = new Vector2(120, 35);

        // Player list section
        GameObject playerListLabel = CreateLabel(page.transform, "PlayerListLabel", "Players in Room:", 22);
        RectTransform listLabelRect = playerListLabel.GetComponent<RectTransform>();
        listLabelRect.anchorMin = new Vector2(0, 1);
        listLabelRect.anchorMax = new Vector2(1, 1);
        listLabelRect.pivot = new Vector2(0, 1);
        listLabelRect.anchoredPosition = new Vector2(padding, -200);
        listLabelRect.sizeDelta = new Vector2(-padding * 2, 30);

        // Player list container
        GameObject playerList = CreatePanel(page.transform, "PlayerList", new Color(0, 0, 0, 0.15f));
        RectTransform playerListRect = playerList.GetComponent<RectTransform>();
        playerListRect.anchorMin = new Vector2(0, 0);
        playerListRect.anchorMax = new Vector2(1, 1);
        playerListRect.offsetMin = new Vector2(padding, 60);
        playerListRect.offsetMax = new Vector2(-padding, -240);

        // Add scroll rect
        ScrollRect scrollRect = playerList.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        // Viewport
        GameObject viewport = CreatePanel(playerList.transform, "Viewport", Color.clear);
        SetRectTransformStretch(viewport.GetComponent<RectTransform>());
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = viewport.GetComponent<RectTransform>();

        // Content for scroll
        GameObject scrollContent = CreatePanel(viewport.transform, "Content", Color.clear);
        RectTransform scrollContentRect = scrollContent.GetComponent<RectTransform>();
        scrollContentRect.anchorMin = new Vector2(0, 1);
        scrollContentRect.anchorMax = new Vector2(1, 1);
        scrollContentRect.pivot = new Vector2(0.5f, 1);
        scrollContentRect.sizeDelta = new Vector2(0, 0);
        scrollRect.content = scrollContentRect;

        VerticalLayoutGroup scrollLayout = scrollContent.AddComponent<VerticalLayoutGroup>();
        scrollLayout.padding = new RectOffset(10, 10, 10, 10);
        scrollLayout.spacing = 5;
        scrollLayout.childControlHeight = false;
        scrollLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = scrollContent.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Player item prefab
        GameObject playerItemPrefab = CreatePlayerItemPrefab(scrollContent.transform);
        playerItemPrefab.SetActive(false);

        // Leave room button
        GameObject leaveBtn = CreateButton(page.transform, "LeaveRoomBtn", "Leave Room", new Color(0.8f, 0.4f, 0.1f, 1f));
        RectTransform leaveBtnRect = leaveBtn.GetComponent<RectTransform>();
        leaveBtnRect.anchorMin = new Vector2(0.5f, 0);
        leaveBtnRect.anchorMax = new Vector2(0.5f, 0);
        leaveBtnRect.pivot = new Vector2(0.5f, 0);
        leaveBtnRect.anchoredPosition = new Vector2(0, 15);
        leaveBtnRect.sizeDelta = new Vector2(180, 45);

        // Connect references to script
        var pageScript = page.GetComponent<VRMenuPageRoom>();
        pageScript.roomNameText = roomNameObj.GetComponent<TextMeshProUGUI>();
        pageScript.roomCodeText = roomCodeObj.GetComponent<TextMeshProUGUI>();
        pageScript.playerCountText = playerCountObj.GetComponent<TextMeshProUGUI>();
        pageScript.playerListContainer = scrollContent.transform;
        pageScript.playerItemPrefab = playerItemPrefab;
        pageScript.leaveRoomButton = leaveBtn.GetComponent<Button>();
        pageScript.copyCodeButton = copyBtn.GetComponent<Button>();

        return page;
    }

    GameObject CreatePlayerItemPrefab(Transform parent)
    {
        GameObject item = CreatePanel(parent, "PlayerItem_Template", new Color(0.2f, 0.2f, 0.25f, 0.8f));
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 40);

        LayoutElement layout = item.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        // Player name text
        GameObject nameText = CreateLabel(item.transform, "PlayerName", "Player Name", 18);
        RectTransform nameRect = nameText.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0);
        nameRect.anchorMax = new Vector2(1, 1);
        nameRect.offsetMin = new Vector2(15, 5);
        nameRect.offsetMax = new Vector2(-15, -5);
        nameText.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;

        return item;
    }

    #endregion

    #region Page: Recording

    GameObject CreatePageRecording(Transform parent)
    {
        GameObject page = CreatePanel(parent, "Page_Recording", Color.clear);
        SetRectTransformStretch(page.GetComponent<RectTransform>());

        // Add page script
        page.AddComponent<VRMenuPageRecording>();

        // Note: VRMenuPageRecording creates its own UI dynamically
        // This is just the container

        return page;
    }

    #endregion

    #region Page: Avatar

    GameObject CreatePageAvatar(Transform parent)
    {
        GameObject page = CreatePanel(parent, "Page_Avatar", Color.clear);
        SetRectTransformStretch(page.GetComponent<RectTransform>());

        page.AddComponent<VRMenuPageAvatar>();

        CreatePageTitle(page.transform, "Avatar");

        // Username section
        GameObject usernameLabel = CreateLabel(page.transform, "UsernameLabel", "Username:", 22);
        RectTransform usernameLabelRect = usernameLabel.GetComponent<RectTransform>();
        usernameLabelRect.anchorMin = new Vector2(0, 1);
        usernameLabelRect.anchorMax = new Vector2(1, 1);
        usernameLabelRect.pivot = new Vector2(0, 1);
        usernameLabelRect.anchoredPosition = new Vector2(padding, -80);
        usernameLabelRect.sizeDelta = new Vector2(-padding * 2, 30);

        GameObject usernameInput = CreateInputField(page.transform, "UsernameInput", "Enter your name...");
        RectTransform usernameInputRect = usernameInput.GetComponent<RectTransform>();
        usernameInputRect.anchorMin = new Vector2(0, 1);
        usernameInputRect.anchorMax = new Vector2(1, 1);
        usernameInputRect.pivot = new Vector2(0, 1);
        usernameInputRect.anchoredPosition = new Vector2(padding, -115);
        usernameInputRect.sizeDelta = new Vector2(-padding * 2 - 120, 45);

        // Apply button
        GameObject applyBtn = CreateButton(page.transform, "ApplyNameBtn", "Apply", buttonSelectedColor);
        RectTransform applyBtnRect = applyBtn.GetComponent<RectTransform>();
        applyBtnRect.anchorMin = new Vector2(1, 1);
        applyBtnRect.anchorMax = new Vector2(1, 1);
        applyBtnRect.pivot = new Vector2(1, 1);
        applyBtnRect.anchoredPosition = new Vector2(-padding, -115);
        applyBtnRect.sizeDelta = new Vector2(100, 45);

        // Color section
        GameObject colorLabel = CreateLabel(page.transform, "ColorLabel", "Avatar Color:", 22);
        RectTransform colorLabelRect = colorLabel.GetComponent<RectTransform>();
        colorLabelRect.anchorMin = new Vector2(0, 1);
        colorLabelRect.anchorMax = new Vector2(1, 1);
        colorLabelRect.pivot = new Vector2(0, 1);
        colorLabelRect.anchoredPosition = new Vector2(padding, -180);
        colorLabelRect.sizeDelta = new Vector2(-padding * 2, 30);

        // Color buttons container
        GameObject colorContainer = CreatePanel(page.transform, "ColorButtons", Color.clear);
        RectTransform colorContainerRect = colorContainer.GetComponent<RectTransform>();
        colorContainerRect.anchorMin = new Vector2(0, 1);
        colorContainerRect.anchorMax = new Vector2(1, 1);
        colorContainerRect.pivot = new Vector2(0, 1);
        colorContainerRect.anchoredPosition = new Vector2(padding, -220);
        colorContainerRect.sizeDelta = new Vector2(-padding * 2, 70);

        HorizontalLayoutGroup colorLayout = colorContainer.AddComponent<HorizontalLayoutGroup>();
        colorLayout.spacing = 15;
        colorLayout.childAlignment = TextAnchor.MiddleLeft;
        colorLayout.childControlWidth = false;
        colorLayout.childControlHeight = false;

        // Preview section
        GameObject previewLabel = CreateLabel(page.transform, "PreviewLabel", "Preview:", 22);
        RectTransform previewLabelRect = previewLabel.GetComponent<RectTransform>();
        previewLabelRect.anchorMin = new Vector2(0, 1);
        previewLabelRect.anchorMax = new Vector2(1, 1);
        previewLabelRect.pivot = new Vector2(0, 1);
        previewLabelRect.anchoredPosition = new Vector2(padding, -310);
        previewLabelRect.sizeDelta = new Vector2(-padding * 2, 30);

        // Avatar preview
        GameObject previewPanel = CreatePanel(page.transform, "Preview", new Color(0, 0, 0, 0.2f));
        RectTransform previewRect = previewPanel.GetComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0, 1);
        previewRect.anchorMax = new Vector2(0, 1);
        previewRect.pivot = new Vector2(0, 1);
        previewRect.anchoredPosition = new Vector2(padding, -350);
        previewRect.sizeDelta = new Vector2(120, 120);

        GameObject previewImage = CreatePanel(previewPanel.transform, "AvatarPreview", Color.blue);
        RectTransform previewImageRect = previewImage.GetComponent<RectTransform>();
        previewImageRect.anchorMin = new Vector2(0.5f, 0.5f);
        previewImageRect.anchorMax = new Vector2(0.5f, 0.5f);
        previewImageRect.sizeDelta = new Vector2(80, 80);

        GameObject previewName = CreateLabel(page.transform, "PreviewName", "PlayerName", 20);
        RectTransform previewNameRect = previewName.GetComponent<RectTransform>();
        previewNameRect.anchorMin = new Vector2(0, 1);
        previewNameRect.anchorMax = new Vector2(1, 1);
        previewNameRect.pivot = new Vector2(0, 1);
        previewNameRect.anchoredPosition = new Vector2(padding + 140, -400);
        previewNameRect.sizeDelta = new Vector2(-padding * 2 - 140, 30);

        // Connect references
        var pageScript = page.GetComponent<VRMenuPageAvatar>();
        pageScript.usernameInput = usernameInput.GetComponent<TMP_InputField>();
        pageScript.applyNameButton = applyBtn.GetComponent<Button>();
        pageScript.colorButtonsContainer = colorContainer.transform;
        pageScript.avatarPreviewImage = previewImage.GetComponent<Image>();
        pageScript.previewNameText = previewName.GetComponent<TextMeshProUGUI>();

        return page;
    }

    #endregion

    #region Page: Voice

    GameObject CreatePageVoice(Transform parent)
    {
        GameObject page = CreatePanel(parent, "Page_Voice", Color.clear);
        SetRectTransformStretch(page.GetComponent<RectTransform>());

        page.AddComponent<VRMenuPageVoice>();

        CreatePageTitle(page.transform, "Voice");

        float yPos = -80;

        // Microphone toggle section
        GameObject micSection = CreatePanel(page.transform, "MicSection", new Color(0, 0, 0, 0.2f));
        RectTransform micSectionRect = micSection.GetComponent<RectTransform>();
        micSectionRect.anchorMin = new Vector2(0, 1);
        micSectionRect.anchorMax = new Vector2(1, 1);
        micSectionRect.pivot = new Vector2(0.5f, 1);
        micSectionRect.anchoredPosition = new Vector2(0, yPos);
        micSectionRect.sizeDelta = new Vector2(-padding * 2, 60);

        GameObject micLabel = CreateLabel(micSection.transform, "MicLabel", "Microphone", 22);
        RectTransform micLabelRect = micLabel.GetComponent<RectTransform>();
        micLabelRect.anchorMin = new Vector2(0, 0);
        micLabelRect.anchorMax = new Vector2(0.5f, 1);
        micLabelRect.offsetMin = new Vector2(15, 10);
        micLabelRect.offsetMax = new Vector2(0, -10);

        GameObject micToggle = CreateToggle(micSection.transform, "MicToggle", true);
        RectTransform micToggleRect = micToggle.GetComponent<RectTransform>();
        micToggleRect.anchorMin = new Vector2(1, 0.5f);
        micToggleRect.anchorMax = new Vector2(1, 0.5f);
        micToggleRect.pivot = new Vector2(1, 0.5f);
        micToggleRect.anchoredPosition = new Vector2(-15, 0);
        micToggleRect.sizeDelta = new Vector2(60, 30);

        yPos -= 80;

        // Input device dropdown
        GameObject inputLabel = CreateLabel(page.transform, "InputLabel", "Input Device:", 20);
        RectTransform inputLabelRect = inputLabel.GetComponent<RectTransform>();
        inputLabelRect.anchorMin = new Vector2(0, 1);
        inputLabelRect.anchorMax = new Vector2(1, 1);
        inputLabelRect.pivot = new Vector2(0, 1);
        inputLabelRect.anchoredPosition = new Vector2(padding, yPos);
        inputLabelRect.sizeDelta = new Vector2(-padding * 2, 25);

        yPos -= 35;

        GameObject inputDropdown = CreateDropdown(page.transform, "InputDeviceDropdown");
        RectTransform inputDropdownRect = inputDropdown.GetComponent<RectTransform>();
        inputDropdownRect.anchorMin = new Vector2(0, 1);
        inputDropdownRect.anchorMax = new Vector2(1, 1);
        inputDropdownRect.pivot = new Vector2(0, 1);
        inputDropdownRect.anchoredPosition = new Vector2(padding, yPos);
        inputDropdownRect.sizeDelta = new Vector2(-padding * 2, 40);

        yPos -= 60;

        // Volume sliders
        GameObject micVolLabel = CreateLabel(page.transform, "MicVolLabel", "Mic Volume:", 20);
        RectTransform micVolLabelRect = micVolLabel.GetComponent<RectTransform>();
        micVolLabelRect.anchorMin = new Vector2(0, 1);
        micVolLabelRect.anchorMax = new Vector2(0.4f, 1);
        micVolLabelRect.pivot = new Vector2(0, 1);
        micVolLabelRect.anchoredPosition = new Vector2(padding, yPos);
        micVolLabelRect.sizeDelta = new Vector2(0, 25);

        GameObject micVolSlider = CreateSlider(page.transform, "MicVolumeSlider");
        RectTransform micVolSliderRect = micVolSlider.GetComponent<RectTransform>();
        micVolSliderRect.anchorMin = new Vector2(0, 1);
        micVolSliderRect.anchorMax = new Vector2(0.75f, 1);
        micVolSliderRect.pivot = new Vector2(0, 1);
        micVolSliderRect.anchoredPosition = new Vector2(padding, yPos - 30);
        micVolSliderRect.sizeDelta = new Vector2(-padding, 25);

        GameObject micVolText = CreateLabel(page.transform, "MicVolumeText", "100%", 18);
        RectTransform micVolTextRect = micVolText.GetComponent<RectTransform>();
        micVolTextRect.anchorMin = new Vector2(0.75f, 1);
        micVolTextRect.anchorMax = new Vector2(1, 1);
        micVolTextRect.pivot = new Vector2(0, 1);
        micVolTextRect.anchoredPosition = new Vector2(10, yPos - 30);
        micVolTextRect.sizeDelta = new Vector2(-padding - 10, 25);

        yPos -= 70;

        // Others volume
        GameObject othersVolLabel = CreateLabel(page.transform, "OthersVolLabel", "Others Volume:", 20);
        RectTransform othersVolLabelRect = othersVolLabel.GetComponent<RectTransform>();
        othersVolLabelRect.anchorMin = new Vector2(0, 1);
        othersVolLabelRect.anchorMax = new Vector2(0.4f, 1);
        othersVolLabelRect.pivot = new Vector2(0, 1);
        othersVolLabelRect.anchoredPosition = new Vector2(padding, yPos);
        othersVolLabelRect.sizeDelta = new Vector2(0, 25);

        GameObject othersVolSlider = CreateSlider(page.transform, "OthersVolumeSlider");
        RectTransform othersVolSliderRect = othersVolSlider.GetComponent<RectTransform>();
        othersVolSliderRect.anchorMin = new Vector2(0, 1);
        othersVolSliderRect.anchorMax = new Vector2(0.75f, 1);
        othersVolSliderRect.pivot = new Vector2(0, 1);
        othersVolSliderRect.anchoredPosition = new Vector2(padding, yPos - 30);
        othersVolSliderRect.sizeDelta = new Vector2(-padding, 25);

        GameObject othersVolText = CreateLabel(page.transform, "OthersVolumeText", "100%", 18);
        RectTransform othersVolTextRect = othersVolText.GetComponent<RectTransform>();
        othersVolTextRect.anchorMin = new Vector2(0.75f, 1);
        othersVolTextRect.anchorMax = new Vector2(1, 1);
        othersVolTextRect.pivot = new Vector2(0, 1);
        othersVolTextRect.anchoredPosition = new Vector2(10, yPos - 30);
        othersVolTextRect.sizeDelta = new Vector2(-padding - 10, 25);

        yPos -= 70;

        // Master volume
        GameObject masterVolLabel = CreateLabel(page.transform, "MasterVolLabel", "Master Volume:", 20);
        RectTransform masterVolLabelRect = masterVolLabel.GetComponent<RectTransform>();
        masterVolLabelRect.anchorMin = new Vector2(0, 1);
        masterVolLabelRect.anchorMax = new Vector2(0.4f, 1);
        masterVolLabelRect.pivot = new Vector2(0, 1);
        masterVolLabelRect.anchoredPosition = new Vector2(padding, yPos);
        masterVolLabelRect.sizeDelta = new Vector2(0, 25);

        GameObject masterVolSlider = CreateSlider(page.transform, "MasterVolumeSlider");
        RectTransform masterVolSliderRect = masterVolSlider.GetComponent<RectTransform>();
        masterVolSliderRect.anchorMin = new Vector2(0, 1);
        masterVolSliderRect.anchorMax = new Vector2(0.75f, 1);
        masterVolSliderRect.pivot = new Vector2(0, 1);
        masterVolSliderRect.anchoredPosition = new Vector2(padding, yPos - 30);
        masterVolSliderRect.sizeDelta = new Vector2(-padding, 25);

        GameObject masterVolText = CreateLabel(page.transform, "MasterVolumeText", "100%", 18);
        RectTransform masterVolTextRect = masterVolText.GetComponent<RectTransform>();
        masterVolTextRect.anchorMin = new Vector2(0.75f, 1);
        masterVolTextRect.anchorMax = new Vector2(1, 1);
        masterVolTextRect.pivot = new Vector2(0, 1);
        masterVolTextRect.anchoredPosition = new Vector2(10, yPos - 30);
        masterVolTextRect.sizeDelta = new Vector2(-padding - 10, 25);

        // Connect references
        var pageScript = page.GetComponent<VRMenuPageVoice>();
        pageScript.microphoneToggle = micToggle.GetComponent<Toggle>();
        pageScript.inputDeviceDropdown = inputDropdown.GetComponent<TMP_Dropdown>();
        pageScript.micVolumeSlider = micVolSlider.GetComponent<Slider>();
        pageScript.micVolumeText = micVolText.GetComponent<TextMeshProUGUI>();
        pageScript.othersVolumeSlider = othersVolSlider.GetComponent<Slider>();
        pageScript.othersVolumeText = othersVolText.GetComponent<TextMeshProUGUI>();
        pageScript.masterVolumeSlider = masterVolSlider.GetComponent<Slider>();
        pageScript.masterVolumeText = masterVolText.GetComponent<TextMeshProUGUI>();

        return page;
    }

    #endregion

    #region Page: Settings

    GameObject CreatePageSettings(Transform parent)
    {
        GameObject page = CreatePanel(parent, "Page_Settings", Color.clear);
        SetRectTransformStretch(page.GetComponent<RectTransform>());

        page.AddComponent<VRMenuPageSettings>();

        CreatePageTitle(page.transform, "Settings");

        float yPos = -80;

        // Quality dropdown
        GameObject qualityLabel = CreateLabel(page.transform, "QualityLabel", "Graphics Quality:", 20);
        RectTransform qualityLabelRect = qualityLabel.GetComponent<RectTransform>();
        qualityLabelRect.anchorMin = new Vector2(0, 1);
        qualityLabelRect.anchorMax = new Vector2(1, 1);
        qualityLabelRect.pivot = new Vector2(0, 1);
        qualityLabelRect.anchoredPosition = new Vector2(padding, yPos);
        qualityLabelRect.sizeDelta = new Vector2(-padding * 2, 25);

        yPos -= 35;

        GameObject qualityDropdown = CreateDropdown(page.transform, "QualityDropdown");
        RectTransform qualityDropdownRect = qualityDropdown.GetComponent<RectTransform>();
        qualityDropdownRect.anchorMin = new Vector2(0, 1);
        qualityDropdownRect.anchorMax = new Vector2(0.5f, 1);
        qualityDropdownRect.pivot = new Vector2(0, 1);
        qualityDropdownRect.anchoredPosition = new Vector2(padding, yPos);
        qualityDropdownRect.sizeDelta = new Vector2(-padding, 40);

        yPos -= 60;

        // Snap turn toggle
        GameObject snapSection = CreatePanel(page.transform, "SnapTurnSection", new Color(0, 0, 0, 0.2f));
        RectTransform snapSectionRect = snapSection.GetComponent<RectTransform>();
        snapSectionRect.anchorMin = new Vector2(0, 1);
        snapSectionRect.anchorMax = new Vector2(1, 1);
        snapSectionRect.pivot = new Vector2(0.5f, 1);
        snapSectionRect.anchoredPosition = new Vector2(0, yPos);
        snapSectionRect.sizeDelta = new Vector2(-padding * 2, 50);

        GameObject snapLabel = CreateLabel(snapSection.transform, "SnapLabel", "Snap Turn", 20);
        RectTransform snapLabelRect = snapLabel.GetComponent<RectTransform>();
        snapLabelRect.anchorMin = new Vector2(0, 0);
        snapLabelRect.anchorMax = new Vector2(0.7f, 1);
        snapLabelRect.offsetMin = new Vector2(15, 10);
        snapLabelRect.offsetMax = new Vector2(0, -10);

        GameObject snapToggle = CreateToggle(snapSection.transform, "SnapTurnToggle", true);
        RectTransform snapToggleRect = snapToggle.GetComponent<RectTransform>();
        snapToggleRect.anchorMin = new Vector2(1, 0.5f);
        snapToggleRect.anchorMax = new Vector2(1, 0.5f);
        snapToggleRect.pivot = new Vector2(1, 0.5f);
        snapToggleRect.anchoredPosition = new Vector2(-15, 0);
        snapToggleRect.sizeDelta = new Vector2(60, 30);

        yPos -= 65;

        // Vignette toggle
        GameObject vignetteSection = CreatePanel(page.transform, "VignetteSection", new Color(0, 0, 0, 0.2f));
        RectTransform vignetteSectionRect = vignetteSection.GetComponent<RectTransform>();
        vignetteSectionRect.anchorMin = new Vector2(0, 1);
        vignetteSectionRect.anchorMax = new Vector2(1, 1);
        vignetteSectionRect.pivot = new Vector2(0.5f, 1);
        vignetteSectionRect.anchoredPosition = new Vector2(0, yPos);
        vignetteSectionRect.sizeDelta = new Vector2(-padding * 2, 50);

        GameObject vignetteLabel = CreateLabel(vignetteSection.transform, "VignetteLabel", "Comfort Vignette", 20);
        RectTransform vignetteLabelRect = vignetteLabel.GetComponent<RectTransform>();
        vignetteLabelRect.anchorMin = new Vector2(0, 0);
        vignetteLabelRect.anchorMax = new Vector2(0.7f, 1);
        vignetteLabelRect.offsetMin = new Vector2(15, 10);
        vignetteLabelRect.offsetMax = new Vector2(0, -10);

        GameObject vignetteToggle = CreateToggle(vignetteSection.transform, "VignetteToggle", true);
        RectTransform vignetteToggleRect = vignetteToggle.GetComponent<RectTransform>();
        vignetteToggleRect.anchorMin = new Vector2(1, 0.5f);
        vignetteToggleRect.anchorMax = new Vector2(1, 0.5f);
        vignetteToggleRect.pivot = new Vector2(1, 0.5f);
        vignetteToggleRect.anchoredPosition = new Vector2(-15, 0);
        vignetteToggleRect.sizeDelta = new Vector2(60, 30);

        yPos -= 65;

        // Show FPS toggle
        GameObject fpsSection = CreatePanel(page.transform, "FPSSection", new Color(0, 0, 0, 0.2f));
        RectTransform fpsSectionRect = fpsSection.GetComponent<RectTransform>();
        fpsSectionRect.anchorMin = new Vector2(0, 1);
        fpsSectionRect.anchorMax = new Vector2(1, 1);
        fpsSectionRect.pivot = new Vector2(0.5f, 1);
        fpsSectionRect.anchoredPosition = new Vector2(0, yPos);
        fpsSectionRect.sizeDelta = new Vector2(-padding * 2, 50);

        GameObject fpsLabel = CreateLabel(fpsSection.transform, "FPSLabel", "Show FPS", 20);
        RectTransform fpsLabelRect = fpsLabel.GetComponent<RectTransform>();
        fpsLabelRect.anchorMin = new Vector2(0, 0);
        fpsLabelRect.anchorMax = new Vector2(0.7f, 1);
        fpsLabelRect.offsetMin = new Vector2(15, 10);
        fpsLabelRect.offsetMax = new Vector2(0, -10);

        GameObject fpsToggle = CreateToggle(fpsSection.transform, "ShowFPSToggle", false);
        RectTransform fpsToggleRect = fpsToggle.GetComponent<RectTransform>();
        fpsToggleRect.anchorMin = new Vector2(1, 0.5f);
        fpsToggleRect.anchorMax = new Vector2(1, 0.5f);
        fpsToggleRect.pivot = new Vector2(1, 0.5f);
        fpsToggleRect.anchoredPosition = new Vector2(-15, 0);
        fpsToggleRect.sizeDelta = new Vector2(60, 30);

        // Connect references
        var pageScript = page.GetComponent<VRMenuPageSettings>();
        pageScript.qualityDropdown = qualityDropdown.GetComponent<TMP_Dropdown>();
        pageScript.snapTurnToggle = snapToggle.GetComponent<Toggle>();
        pageScript.vignetteToggle = vignetteToggle.GetComponent<Toggle>();
        pageScript.showFPSToggle = fpsToggle.GetComponent<Toggle>();

        return page;
    }

    #endregion

    #region Exit Dialog

    GameObject CreateExitDialog(Transform parent)
    {
        // Overlay
        GameObject overlay = CreatePanel(parent, "ExitDialog", new Color(0, 0, 0, 0.7f));
        SetRectTransformStretch(overlay.GetComponent<RectTransform>());

        // Dialog box
        GameObject dialog = CreatePanel(overlay.transform, "DialogBox", backgroundColor);
        RectTransform dialogRect = dialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.sizeDelta = new Vector2(400, 280);

        // Title
        GameObject title = CreateLabel(dialog.transform, "Title", "Exit", 28);
        title.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Bold;
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -20);
        titleRect.sizeDelta = new Vector2(-40, 40);

        // Message
        GameObject message = CreateLabel(dialog.transform, "Message", "What would you like to do?", 20);
        RectTransform messageRect = message.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0, 1);
        messageRect.anchorMax = new Vector2(1, 1);
        messageRect.pivot = new Vector2(0.5f, 1);
        messageRect.anchoredPosition = new Vector2(0, -70);
        messageRect.sizeDelta = new Vector2(-40, 30);

        // Leave Room button
        GameObject leaveBtn = CreateButton(dialog.transform, "LeaveRoomBtn", "Leave Room", new Color(0.9f, 0.6f, 0.1f, 1f));
        RectTransform leaveBtnRect = leaveBtn.GetComponent<RectTransform>();
        leaveBtnRect.anchorMin = new Vector2(0.5f, 0.5f);
        leaveBtnRect.anchorMax = new Vector2(0.5f, 0.5f);
        leaveBtnRect.anchoredPosition = new Vector2(0, 20);
        leaveBtnRect.sizeDelta = new Vector2(250, 50);

        // Quit Game button
        GameObject quitBtn = CreateButton(dialog.transform, "QuitGameBtn", "Quit Game", exitButtonColor);
        RectTransform quitBtnRect = quitBtn.GetComponent<RectTransform>();
        quitBtnRect.anchorMin = new Vector2(0.5f, 0.5f);
        quitBtnRect.anchorMax = new Vector2(0.5f, 0.5f);
        quitBtnRect.anchoredPosition = new Vector2(0, -40);
        quitBtnRect.sizeDelta = new Vector2(250, 50);

        // Cancel button
        GameObject cancelBtn = CreateButton(dialog.transform, "CancelBtn", "Cancel", buttonNormalColor);
        RectTransform cancelBtnRect = cancelBtn.GetComponent<RectTransform>();
        cancelBtnRect.anchorMin = new Vector2(0.5f, 0);
        cancelBtnRect.anchorMax = new Vector2(0.5f, 0);
        cancelBtnRect.pivot = new Vector2(0.5f, 0);
        cancelBtnRect.anchoredPosition = new Vector2(0, 20);
        cancelBtnRect.sizeDelta = new Vector2(150, 40);

        return overlay;
    }

    #endregion

    #region UI Helpers

    GameObject CreateCloseButton(Transform parent)
    {
        GameObject btnObj = new GameObject("CloseButton");
        btnObj.transform.SetParent(parent, false);

        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.color = new Color(0.6f, 0.15f, 0.15f, 1f);
        bgImage.raycastTarget = true; // Ensure clicks are received

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = bgImage; // Explicitly set target graphic
        ColorBlock colors = btn.colors;
        colors.normalColor = new Color(0.6f, 0.15f, 0.15f, 1f);
        colors.highlightedColor = new Color(0.8f, 0.2f, 0.2f, 1f);
        colors.pressedColor = new Color(0.4f, 0.1f, 0.1f, 1f);
        btn.colors = colors;

        RectTransform rect = btnObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-8, -8);
        rect.sizeDelta = new Vector2(35, 35);

        GameObject textObj = new GameObject("X");
        textObj.transform.SetParent(btnObj.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "✕";
        text.fontSize = 22;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false; // Don't block button clicks
        SetRectTransformStretch(textObj.GetComponent<RectTransform>());

        return btnObj;
    }

    GameObject CreateTitle(Transform parent)
    {
        GameObject titleObj = new GameObject("MenuTitle");
        titleObj.transform.SetParent(parent, false);

        TextMeshProUGUI text = titleObj.AddComponent<TextMeshProUGUI>();
        text.text = "MENU";
        text.fontSize = 20;
        text.fontStyle = FontStyles.Bold;
        text.color = textColor;
        text.alignment = TextAlignmentOptions.Center;

        RectTransform rect = titleObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(sidebarWidth / 2, -12);
        rect.sizeDelta = new Vector2(sidebarWidth, 25);

        return titleObj;
    }

    void CreatePageTitle(Transform parent, string title)
    {
        GameObject titleObj = CreateLabel(parent, "PageTitle", title, 32);
        titleObj.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Bold;

        RectTransform rect = titleObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(padding, -15);
        rect.sizeDelta = new Vector2(-padding * 2, 45);
    }

    GameObject CreateLabel(Transform parent, string name, string text, int fontSize)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Left;

        return obj;
    }

    GameObject CreateButton(Transform parent, string name, string text, Color color)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        Image bgImage = btnObj.AddComponent<Image>();
        bgImage.color = color;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.15f;
        colors.pressedColor = color * 0.85f;
        btn.colors = colors;

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        SetRectTransformStretch(textObj.GetComponent<RectTransform>());

        return btnObj;
    }

    GameObject CreateInputField(Transform parent, string name, string placeholder)
    {
        GameObject inputObj = new GameObject(name);
        inputObj.transform.SetParent(parent, false);

        Image bgImage = inputObj.AddComponent<Image>();
        bgImage.color = new Color(0.15f, 0.15f, 0.2f, 1f);

        TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();

        // Text area
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10, 5);
        textAreaRect.offsetMax = new Vector2(-10, -5);
        textArea.AddComponent<RectMask2D>();

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 18;
        text.color = textColor;
        SetRectTransformStretch(textObj.GetComponent<RectTransform>());

        // Placeholder
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 18;
        placeholderText.color = textSecondaryColor;
        placeholderText.fontStyle = FontStyles.Italic;
        SetRectTransformStretch(placeholderObj.GetComponent<RectTransform>());

        inputField.textViewport = textAreaRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholderText;

        return inputObj;
    }

    GameObject CreateToggle(Transform parent, string name, bool isOn)
    {
        GameObject toggleObj = new GameObject(name);
        toggleObj.transform.SetParent(parent, false);

        Toggle toggle = toggleObj.AddComponent<Toggle>();
        toggle.isOn = isOn;

        // Background
        GameObject bg = CreatePanel(toggleObj.transform, "Background", buttonNormalColor);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        // Checkmark
        GameObject checkmark = CreatePanel(toggleObj.transform, "Checkmark", buttonSelectedColor);
        RectTransform checkmarkRect = checkmark.GetComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0.1f, 0.1f);
        checkmarkRect.anchorMax = new Vector2(0.9f, 0.9f);
        checkmarkRect.sizeDelta = Vector2.zero;

        toggle.graphic = checkmark.GetComponent<Image>();
        toggle.targetGraphic = bg.GetComponent<Image>();

        return toggleObj;
    }

    GameObject CreateSlider(Transform parent, string name)
    {
        GameObject sliderObj = new GameObject(name);
        sliderObj.transform.SetParent(parent, false);

        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 1;

        // Background
        GameObject bg = CreatePanel(sliderObj.transform, "Background", new Color(0.15f, 0.15f, 0.2f, 1f));
        SetRectTransformStretch(bg.GetComponent<RectTransform>());

        // Fill area
        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.25f);
        fillAreaRect.anchorMax = new Vector2(1, 0.75f);
        fillAreaRect.offsetMin = new Vector2(5, 0);
        fillAreaRect.offsetMax = new Vector2(-5, 0);

        GameObject fill = CreatePanel(fillArea.transform, "Fill", buttonSelectedColor);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.sizeDelta = Vector2.zero;

        slider.fillRect = fillRect;

        // Handle
        GameObject handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform handleAreaRect = handleArea.AddComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10, 0);
        handleAreaRect.offsetMax = new Vector2(-10, 0);

        GameObject handle = CreatePanel(handleArea.transform, "Handle", Color.white);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(20, 0);

        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();

        return sliderObj;
    }

    GameObject CreateDropdown(Transform parent, string name)
    {
        GameObject dropdownObj = new GameObject(name);
        dropdownObj.transform.SetParent(parent, false);

        Image bgImage = dropdownObj.AddComponent<Image>();
        bgImage.color = new Color(0.15f, 0.15f, 0.2f, 1f);

        TMP_Dropdown dropdown = dropdownObj.AddComponent<TMP_Dropdown>();

        // Label
        GameObject label = CreateLabel(dropdownObj.transform, "Label", "Option", 18);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(10, 5);
        labelRect.offsetMax = new Vector2(-35, -5);

        // Arrow
        GameObject arrow = CreateLabel(dropdownObj.transform, "Arrow", "▼", 14);
        RectTransform arrowRect = arrow.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1, 0);
        arrowRect.anchorMax = new Vector2(1, 1);
        arrowRect.pivot = new Vector2(1, 0.5f);
        arrowRect.anchoredPosition = new Vector2(-10, 0);
        arrowRect.sizeDelta = new Vector2(20, 0);

        // Template (simplified)
        GameObject template = CreatePanel(dropdownObj.transform, "Template", new Color(0.12f, 0.12f, 0.18f, 1f));
        template.SetActive(false);
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0, 0);
        templateRect.anchorMax = new Vector2(1, 0);
        templateRect.pivot = new Vector2(0.5f, 1);
        templateRect.anchoredPosition = Vector2.zero;
        templateRect.sizeDelta = new Vector2(0, 150);

        ScrollRect scrollRect = template.AddComponent<ScrollRect>();

        GameObject viewport = CreatePanel(template.transform, "Viewport", Color.clear);
        SetRectTransformStretch(viewport.GetComponent<RectTransform>());
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        GameObject content = CreatePanel(viewport.transform, "Content", Color.clear);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 0);

        // Item template
        GameObject item = CreatePanel(content.transform, "Item", Color.clear);
        RectTransform itemRect = item.GetComponent<RectTransform>();
        itemRect.sizeDelta = new Vector2(0, 35);

        Toggle itemToggle = item.AddComponent<Toggle>();

        GameObject itemBg = CreatePanel(item.transform, "Item Background", buttonNormalColor);
        SetRectTransformStretch(itemBg.GetComponent<RectTransform>());
        itemToggle.targetGraphic = itemBg.GetComponent<Image>();

        GameObject itemCheck = CreatePanel(item.transform, "Item Checkmark", buttonSelectedColor);
        RectTransform itemCheckRect = itemCheck.GetComponent<RectTransform>();
        itemCheckRect.anchorMin = new Vector2(0, 0.5f);
        itemCheckRect.anchorMax = new Vector2(0, 0.5f);
        itemCheckRect.anchoredPosition = new Vector2(15, 0);
        itemCheckRect.sizeDelta = new Vector2(15, 15);
        itemToggle.graphic = itemCheck.GetComponent<Image>();

        GameObject itemLabel = CreateLabel(item.transform, "Item Label", "Option", 16);
        RectTransform itemLabelRect = itemLabel.GetComponent<RectTransform>();
        itemLabelRect.anchorMin = Vector2.zero;
        itemLabelRect.anchorMax = Vector2.one;
        itemLabelRect.offsetMin = new Vector2(35, 5);
        itemLabelRect.offsetMax = new Vector2(-10, -5);

        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = contentRect;

        dropdown.captionText = label.GetComponent<TextMeshProUGUI>();
        dropdown.template = templateRect;
        dropdown.itemText = itemLabel.GetComponent<TextMeshProUGUI>();

        return dropdownObj;
    }

    GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        Image image = panel.AddComponent<Image>();
        image.color = color;

        return panel;
    }

    void SetRectTransformStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    #endregion
#endif
}
