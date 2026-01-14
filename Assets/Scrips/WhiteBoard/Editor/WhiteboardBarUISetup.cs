#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>
/// Crée l'UI en barre dans la scène (mode Edit).
/// Tu peux ensuite déplacer l'UI où tu veux.
/// </summary>
public class WhiteboardBarUISetup : Editor
{
    static Color backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
    static Color buttonColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    static Color accentColor = new Color(0.3f, 0.6f, 1f, 1f);

    static Color[] defaultColors = new Color[]
    {
        Color.blue, Color.red, Color.green,
        Color.yellow, Color.black, Color.white
    };

    [MenuItem("GameObject/Whiteboard/Create Bar UI", false, 10)]
    static void CreateBarUI()
    {
        // Créer le Canvas
        GameObject canvasGO = new GameObject("WhiteboardBarUI");
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Whiteboard Bar UI");

        // Position à 0,0,0
        canvasGO.transform.position = Vector3.zero;
        canvasGO.transform.rotation = Quaternion.identity;

        // Canvas WorldSpace
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(500, 70);
        canvasRect.localScale = new Vector3(0.001f, 0.001f, 0.001f);

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // VR Raycaster
        try
        {
            canvasGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        }
        catch { }

        // Background
        GameObject bg = CreateImage("Background", canvasRect, Vector2.zero, canvasRect.sizeDelta, backgroundColor);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        bgRect.anchoredPosition = Vector2.zero;

        // Burger Button
        GameObject burgerBtn = CreateButton("BurgerButton", bgRect, new Vector2(35, 60), "≡", 28);
        SetAnchorLeft(burgerBtn, 30);

        // Menu Content
        GameObject menuContent = new GameObject("MenuContent");
        menuContent.transform.SetParent(bgRect);
        RectTransform menuRect = menuContent.AddComponent<RectTransform>();
        menuRect.anchorMin = Vector2.zero;
        menuRect.anchorMax = Vector2.one;
        menuRect.offsetMin = new Vector2(70, 5);
        menuRect.offsetMax = new Vector2(-5, -5);
        menuRect.localScale = Vector3.one;

        HorizontalLayoutGroup hlg = menuContent.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // Color buttons
        string[] colorNames = { "Blue", "Red", "Green", "Yellow", "Black", "White" };
        for (int i = 0; i < defaultColors.Length; i++)
        {
            GameObject colorBtn = CreateButton($"Btn_{colorNames[i]}", menuRect, new Vector2(40, 40), "", 14);
            colorBtn.GetComponent<Image>().color = defaultColors[i];
            LayoutElement le = colorBtn.AddComponent<LayoutElement>();
            le.preferredWidth = 40;
            le.preferredHeight = 40;
        }

        // Separator
        CreateSeparator(menuRect);

        // Clear Button
        GameObject clearBtn = CreateButton("Btn_Clear", menuRect, new Vector2(70, 50), "Effacer", 14);
        clearBtn.AddComponent<LayoutElement>().preferredWidth = 70;

        // Separator
        CreateSeparator(menuRect);

        // Share Button
        GameObject shareBtn = CreateButton("Btn_Share", menuRect, new Vector2(80, 50), "Partager", 14);
        shareBtn.GetComponent<Image>().color = accentColor;
        shareBtn.AddComponent<LayoutElement>().preferredWidth = 80;

        // Share Panel (popup)
        GameObject sharePanel = CreateSharePanel(bgRect);

        // Add WhiteboardBarUI component
        WhiteboardBarUI barUI = canvasGO.AddComponent<WhiteboardBarUI>();
        barUI.menuContent = menuContent;
        barUI.sharePanel = sharePanel;
        barUI.burgerText = burgerBtn.GetComponentInChildren<Text>();
        barUI.screenDropdown = sharePanel.GetComponentInChildren<Dropdown>();

        // Connect buttons
        burgerBtn.GetComponent<Button>().onClick.AddListener(barUI.ToggleMenu);

        for (int i = 0; i < defaultColors.Length; i++)
        {
            int index = i;
            GameObject btn = menuContent.transform.Find($"Btn_{colorNames[i]}")?.gameObject;
            if (btn != null)
            {
                btn.GetComponent<Button>().onClick.AddListener(() => barUI.SetColorByIndex(index));
            }
        }

        clearBtn.GetComponent<Button>().onClick.AddListener(barUI.Clear);
        shareBtn.GetComponent<Button>().onClick.AddListener(barUI.ToggleSharePanel);

        // Share panel buttons
        Transform startBtn = sharePanel.transform.Find("Btn_StartShare");
        Transform stopBtn = sharePanel.transform.Find("Btn_StopShare");
        Transform refreshBtn = sharePanel.transform.Find("Btn_Refresh");

        if (startBtn != null)
            startBtn.GetComponent<Button>().onClick.AddListener(barUI.StartScreenShare);
        if (stopBtn != null)
            stopBtn.GetComponent<Button>().onClick.AddListener(barUI.StopScreenShare);
        if (refreshBtn != null)
            refreshBtn.GetComponent<Button>().onClick.AddListener(barUI.RefreshWindowList);

        Dropdown dropdown = sharePanel.GetComponentInChildren<Dropdown>();
        if (dropdown != null)
            dropdown.onValueChanged.AddListener(barUI.OnScreenSelected);

        // Initial state
        sharePanel.SetActive(false);

        Selection.activeGameObject = canvasGO;

        Debug.Log("[Setup] WhiteboardBarUI créé à (0,0,0). Déplace-le où tu veux!");
        EditorUtility.DisplayDialog("Succès",
            "WhiteboardBarUI créé!\n\n" +
            "Position: (0, 0, 0)\n" +
            "Déplace-le dans la Scene View.\n\n" +
            "Les boutons sont déjà connectés.", "OK");
    }

    static GameObject CreateSharePanel(RectTransform parent)
    {
        GameObject panel = CreateImage("SharePanel", parent, Vector2.zero, new Vector2(220, 180), backgroundColor);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(1, 0);
        panelRect.anchoredPosition = new Vector2(-10, 10);

        VerticalLayoutGroup vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5;
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        // Title
        GameObject title = CreateText("Title", panelRect, "Partage d'écran", 16, FontStyle.Bold);
        title.AddComponent<LayoutElement>().preferredHeight = 25;

        // Dropdown
        GameObject dropdownGO = CreateDropdown(panelRect);
        dropdownGO.AddComponent<LayoutElement>().preferredHeight = 35;

        // Refresh button
        GameObject refreshBtn = CreateButton("Btn_Refresh", panelRect, new Vector2(200, 30), "Rafraîchir", 12);
        refreshBtn.AddComponent<LayoutElement>().preferredHeight = 30;

        // Start button
        GameObject startBtn = CreateButton("Btn_StartShare", panelRect, new Vector2(200, 40), "Démarrer", 14);
        startBtn.GetComponent<Image>().color = accentColor;
        startBtn.AddComponent<LayoutElement>().preferredHeight = 40;

        // Stop button
        GameObject stopBtn = CreateButton("Btn_StopShare", panelRect, new Vector2(200, 35), "Arrêter", 13);
        stopBtn.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f, 1f);
        stopBtn.AddComponent<LayoutElement>().preferredHeight = 35;

        return panel;
    }

    static GameObject CreateDropdown(RectTransform parent)
    {
        GameObject dropdownGO = new GameObject("Dropdown");
        dropdownGO.transform.SetParent(parent);
        RectTransform rect = dropdownGO.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(200, 35);
        rect.localScale = Vector3.one;

        Image img = dropdownGO.AddComponent<Image>();
        img.color = buttonColor;

        Dropdown dropdown = dropdownGO.AddComponent<Dropdown>();

        // Caption
        GameObject caption = CreateText("Label", rect, "Écran principal", 12, FontStyle.Normal);
        RectTransform capRect = caption.GetComponent<RectTransform>();
        capRect.anchorMin = Vector2.zero;
        capRect.anchorMax = Vector2.one;
        capRect.offsetMin = new Vector2(10, 0);
        capRect.offsetMax = new Vector2(-30, 0);
        dropdown.captionText = caption.GetComponent<Text>();

        // Arrow
        GameObject arrow = CreateText("Arrow", rect, "▼", 10, FontStyle.Normal);
        RectTransform arrowRect = arrow.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(1, 0);
        arrowRect.anchorMax = new Vector2(1, 1);
        arrowRect.sizeDelta = new Vector2(25, 0);
        arrowRect.anchoredPosition = new Vector2(-12, 0);

        // Template
        GameObject template = CreateImage("Template", rect, new Vector2(0, -35), new Vector2(200, 120), backgroundColor);
        RectTransform tempRect = template.GetComponent<RectTransform>();
        tempRect.anchorMin = new Vector2(0, 0);
        tempRect.anchorMax = new Vector2(1, 0);
        tempRect.pivot = new Vector2(0.5f, 1);

        ScrollRect scroll = template.AddComponent<ScrollRect>();

        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(tempRect);
        RectTransform vpRect = viewport.AddComponent<RectTransform>();
        vpRect.anchorMin = Vector2.zero;
        vpRect.anchorMax = Vector2.one;
        vpRect.sizeDelta = Vector2.zero;
        vpRect.localScale = Vector3.one;
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        viewport.AddComponent<Image>();

        scroll.viewport = vpRect;

        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform);
        RectTransform contRect = content.AddComponent<RectTransform>();
        contRect.anchorMin = new Vector2(0, 1);
        contRect.anchorMax = new Vector2(1, 1);
        contRect.pivot = new Vector2(0.5f, 1);
        contRect.sizeDelta = new Vector2(0, 0);
        contRect.anchoredPosition = Vector2.zero;
        contRect.localScale = Vector3.one;

        scroll.content = contRect;

        // Item
        GameObject item = CreateImage("Item", contRect, Vector2.zero, new Vector2(200, 28), buttonColor);
        RectTransform itemRect = item.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 0.5f);
        itemRect.anchorMax = new Vector2(1, 0.5f);
        itemRect.pivot = new Vector2(0.5f, 0.5f);

        Toggle toggle = item.AddComponent<Toggle>();
        toggle.targetGraphic = item.GetComponent<Image>();

        ColorBlock colors = toggle.colors;
        colors.highlightedColor = accentColor;
        toggle.colors = colors;

        GameObject itemLabel = CreateText("Item Label", itemRect, "Option", 12, FontStyle.Normal);
        RectTransform ilRect = itemLabel.GetComponent<RectTransform>();
        ilRect.anchorMin = Vector2.zero;
        ilRect.anchorMax = Vector2.one;
        ilRect.offsetMin = new Vector2(10, 0);
        ilRect.offsetMax = new Vector2(-10, 0);

        dropdown.template = tempRect;
        dropdown.itemText = itemLabel.GetComponent<Text>();

        template.SetActive(false);

        dropdown.ClearOptions();
        dropdown.AddOptions(new System.Collections.Generic.List<string> { "Écran principal (Unity)" });

        return dropdownGO;
    }

    static GameObject CreateImage(string name, RectTransform parent, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        Image img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    static GameObject CreateButton(string name, RectTransform parent, Vector2 size, string text, int fontSize)
    {
        GameObject btn = CreateImage(name, parent, Vector2.zero, size, buttonColor);
        Button button = btn.AddComponent<Button>();

        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        button.colors = colors;

        if (!string.IsNullOrEmpty(text))
        {
            CreateText("Text", btn.GetComponent<RectTransform>(), text, fontSize, FontStyle.Normal);
        }

        return btn;
    }

    static GameObject CreateText(string name, RectTransform parent, string text, int fontSize, FontStyle style)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;

        Text t = go.AddComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;

        return go;
    }

    static void CreateSeparator(RectTransform parent)
    {
        GameObject sep = CreateImage("Separator", parent, Vector2.zero, new Vector2(2, 40), new Color(0.4f, 0.4f, 0.4f, 1f));
        LayoutElement le = sep.AddComponent<LayoutElement>();
        le.preferredWidth = 2;
    }

    static void SetAnchorLeft(GameObject go, float x)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(0, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0);
    }

    [MenuItem("GameObject/Whiteboard/Remove All Bar UIs", false, 11)]
    static void RemoveAllBarUIs()
    {
        WhiteboardBarUI[] uis = FindObjectsByType<WhiteboardBarUI>(FindObjectsSortMode.None);

        if (uis.Length == 0)
        {
            EditorUtility.DisplayDialog("Info", "Aucun WhiteboardBarUI trouvé.", "OK");
            return;
        }

        if (EditorUtility.DisplayDialog("Confirmer",
            $"Supprimer {uis.Length} WhiteboardBarUI(s)?", "Supprimer", "Annuler"))
        {
            foreach (var ui in uis)
            {
                Undo.DestroyObjectImmediate(ui.gameObject);
            }
            Debug.Log($"[Setup] {uis.Length} WhiteboardBarUI(s) supprimé(s)");
        }
    }
}
#endif
