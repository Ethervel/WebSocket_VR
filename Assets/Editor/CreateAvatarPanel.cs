using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using UnityEditor.SceneManagement;

/// <summary>
/// Script Editor pour créer le panel Avatar en dupliquant le MainPanel.
/// Menu: Tools > Create Avatar Panel
/// </summary>
public class CreateAvatarPanel : EditorWindow
{
    [MenuItem("Tools/Create Avatar Panel")]
    public static void CreatePanel()
    {
        // Trouver VRMenuUI
        VRMenuUI menuUI = Object.FindFirstObjectByType<VRMenuUI>();
        if (menuUI == null)
        {
            Debug.LogError("[CreateAvatarPanel] VRMenuUI non trouvé!");
            return;
        }

        if (menuUI.mainPanel == null)
        {
            Debug.LogError("[CreateAvatarPanel] MainPanel non assigné dans VRMenuUI!");
            return;
        }

        // Setup undo
        Undo.SetCurrentGroupName("Create Avatar Panel");
        int undoGroup = Undo.GetCurrentGroup();

        // Dupliquer le MainPanel
        GameObject avatarPanel = Object.Instantiate(menuUI.mainPanel, menuUI.mainPanel.transform.parent);
        avatarPanel.name = "AvatarPanel";
        Undo.RegisterCreatedObjectUndo(avatarPanel, "Create Avatar Panel");

        // Supprimer tous les enfants existants
        for (int i = avatarPanel.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(avatarPanel.transform.GetChild(i).gameObject);
        }

        // Récupérer le RectTransform et LayoutGroup si présent
        RectTransform panelRect = avatarPanel.GetComponent<RectTransform>();

        // Ajouter VerticalLayoutGroup si pas présent
        VerticalLayoutGroup vlg = avatarPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = Undo.AddComponent<VerticalLayoutGroup>(avatarPanel);
        }
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 15;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Ajouter ContentSizeFitter si nécessaire
        ContentSizeFitter csf = avatarPanel.GetComponent<ContentSizeFitter>();
        if (csf != null)
        {
            Object.DestroyImmediate(csf);
        }

        // === TITRE ===
        GameObject titleObj = CreateText(avatarPanel.transform, "Title", "Configuration Avatar", 24, FontStyles.Bold);
        SetHeight(titleObj, 40);

        // === SPACER ===
        CreateSpacer(avatarPanel.transform, 10);

        // === LABEL PSEUDO ===
        GameObject usernameLabel = CreateText(avatarPanel.transform, "UsernameLabel", "Votre pseudo :", 16, FontStyles.Normal);
        SetHeight(usernameLabel, 25);

        // === INPUT PSEUDO ===
        GameObject usernameInput = CreateInputField(avatarPanel.transform, "UsernameInput", "Entrez votre pseudo...");
        SetHeight(usernameInput, 45);

        // === SPACER ===
        CreateSpacer(avatarPanel.transform, 15);

        // === LABEL COULEUR ===
        GameObject colorLabel = CreateText(avatarPanel.transform, "ColorLabel", "Couleur de l'avatar :", 16, FontStyles.Normal);
        SetHeight(colorLabel, 25);

        // === GRILLE COULEURS ===
        GameObject colorGrid = CreateColorGrid(avatarPanel.transform);
        SetHeight(colorGrid, 100);

        // === SPACER ===
        CreateSpacer(avatarPanel.transform, 15);

        // === PREVIEW ===
        GameObject previewSection = CreatePreviewSection(avatarPanel.transform);
        SetHeight(previewSection, 120);

        // === SPACER FLEXIBLE ===
        CreateSpacer(avatarPanel.transform, 20);

        // === BOUTON CONFIRMER ===
        GameObject confirmBtn = CreateButton(avatarPanel.transform, "ConfirmButton", "Confirmer", new Color(0.2f, 0.6f, 0.3f, 1f));
        SetHeight(confirmBtn, 50);

        // === AJOUTER COMPOSANT AvatarCustomization ===
        AvatarCustomization avatarCustomization = avatarPanel.GetComponent<AvatarCustomization>();
        if (avatarCustomization == null)
        {
            avatarCustomization = Undo.AddComponent<AvatarCustomization>(avatarPanel);
        }

        // Assigner les références
        avatarCustomization.avatarPanel = avatarPanel;
        avatarCustomization.usernameInput = usernameInput.GetComponentInChildren<TMP_InputField>();
        avatarCustomization.confirmButton = confirmBtn.GetComponent<Button>();

        // Trouver les boutons de couleur
        Button[] colorButtons = colorGrid.GetComponentsInChildren<Button>();
        avatarCustomization.colorButtons = colorButtons;

        // Trouver preview
        Transform previewImage = previewSection.transform.Find("AvatarPreviewImage");
        if (previewImage != null)
        {
            avatarCustomization.avatarPreviewImage = previewImage.GetComponent<Image>();
        }

        Transform previewName = previewSection.transform.Find("PreviewName");
        if (previewName != null)
        {
            avatarCustomization.usernamePreview = previewName.GetComponent<TextMeshProUGUI>();
        }

        // Assigner dans VRMenuUI
        menuUI.avatarPanel = avatarPanel;
        EditorUtility.SetDirty(menuUI);

        // Activer le panel pour le voir
        avatarPanel.SetActive(true);

        // Sélectionner
        Selection.activeGameObject = avatarPanel;
        EditorUtility.SetDirty(avatarPanel);

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log("[CreateAvatarPanel] Panel Avatar créé! Sauvegarde la scène (Ctrl+S)");
    }

    static void SetHeight(GameObject obj, float height)
    {
        LayoutElement le = obj.GetComponent<LayoutElement>();
        if (le == null) le = Undo.AddComponent<LayoutElement>(obj);
        le.minHeight = height;
        le.preferredHeight = height;
    }

    static GameObject CreateSpacer(Transform parent, float height)
    {
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(spacer, "Create Spacer");
        RectTransform rect = spacer.AddComponent<RectTransform>();
        LayoutElement le = Undo.AddComponent<LayoutElement>(spacer);
        le.minHeight = height;
        le.preferredHeight = height;
        return spacer;
    }

    static GameObject CreateText(Transform parent, string name, string text, int fontSize, FontStyles style)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(obj, "Create Text");
        obj.AddComponent<RectTransform>();

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        return obj;
    }

    static GameObject CreateInputField(Transform parent, string name, string placeholder)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(obj, "Create Input Field");
        RectTransform rect = obj.AddComponent<RectTransform>();

        Image bg = obj.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

        // TextArea
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(obj.transform, false);
        RectTransform taRect = textArea.AddComponent<RectTransform>();
        taRect.anchorMin = Vector2.zero;
        taRect.anchorMax = Vector2.one;
        taRect.offsetMin = new Vector2(10, 0);
        taRect.offsetMax = new Vector2(-10, 0);
        textArea.AddComponent<RectMask2D>();

        // Placeholder
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        RectTransform phRect = placeholderObj.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = Vector2.zero;
        phRect.offsetMax = Vector2.zero;
        TextMeshProUGUI phTmp = placeholderObj.AddComponent<TextMeshProUGUI>();
        phTmp.text = placeholder;
        phTmp.fontSize = 16;
        phTmp.fontStyle = FontStyles.Italic;
        phTmp.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
        phTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform txtRect = textObj.AddComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;
        TextMeshProUGUI txtTmp = textObj.AddComponent<TextMeshProUGUI>();
        txtTmp.fontSize = 18;
        txtTmp.color = Color.white;
        txtTmp.alignment = TextAlignmentOptions.MidlineLeft;

        // InputField
        TMP_InputField input = obj.AddComponent<TMP_InputField>();
        input.textViewport = taRect;
        input.textComponent = txtTmp;
        input.placeholder = phTmp;
        input.fontAsset = txtTmp.font;

        return obj;
    }

    static GameObject CreateColorGrid(Transform parent)
    {
        GameObject grid = new GameObject("ColorGrid");
        grid.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(grid, "Create Color Grid");
        grid.AddComponent<RectTransform>();

        GridLayoutGroup glg = Undo.AddComponent<GridLayoutGroup>(grid);
        glg.cellSize = new Vector2(45, 45);
        glg.spacing = new Vector2(12, 12);
        glg.childAlignment = TextAnchor.MiddleCenter;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 4;

        Color[] colors = new Color[]
        {
            new Color(0.2f, 0.6f, 1f, 1f),    // Bleu
            new Color(1f, 0.3f, 0.3f, 1f),    // Rouge
            new Color(0.3f, 0.9f, 0.3f, 1f),  // Vert
            new Color(1f, 0.8f, 0.2f, 1f),    // Jaune
            new Color(0.8f, 0.4f, 1f, 1f),    // Violet
            new Color(1f, 0.5f, 0.2f, 1f),    // Orange
            new Color(0.2f, 0.9f, 0.9f, 1f),  // Cyan
            new Color(1f, 0.4f, 0.7f, 1f),    // Rose
        };

        for (int i = 0; i < colors.Length; i++)
        {
            GameObject btn = new GameObject($"Color_{i}");
            btn.transform.SetParent(grid.transform, false);
            Undo.RegisterCreatedObjectUndo(btn, "Create Color Button");

            Image img = btn.AddComponent<Image>();
            img.color = colors[i];

            Button button = btn.AddComponent<Button>();
            ColorBlock cb = button.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = cb;

            // Outline pour sélection
            Outline outline = btn.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(2, 2);
            outline.enabled = false;
        }

        return grid;
    }

    static GameObject CreatePreviewSection(Transform parent)
    {
        GameObject section = new GameObject("PreviewSection");
        section.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(section, "Create Preview Section");
        RectTransform rect = section.AddComponent<RectTransform>();

        // Preview Image (cercle avatar)
        GameObject previewImg = new GameObject("AvatarPreviewImage");
        previewImg.transform.SetParent(section.transform, false);
        Undo.RegisterCreatedObjectUndo(previewImg, "Create Preview Image");
        RectTransform imgRect = previewImg.AddComponent<RectTransform>();
        imgRect.anchorMin = new Vector2(0.5f, 0.6f);
        imgRect.anchorMax = new Vector2(0.5f, 0.6f);
        imgRect.sizeDelta = new Vector2(70, 70);
        Image img = previewImg.AddComponent<Image>();
        img.color = new Color(0.2f, 0.6f, 1f, 1f); // Bleu par défaut

        // Preview Name
        GameObject previewName = new GameObject("PreviewName");
        previewName.transform.SetParent(section.transform, false);
        Undo.RegisterCreatedObjectUndo(previewName, "Create Preview Name");
        RectTransform nameRect = previewName.AddComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0);
        nameRect.anchorMax = new Vector2(1, 0.3f);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;
        TextMeshProUGUI nameTmp = previewName.AddComponent<TextMeshProUGUI>();
        nameTmp.text = "Player";
        nameTmp.fontSize = 18;
        nameTmp.color = Color.white;
        nameTmp.alignment = TextAlignmentOptions.Center;

        return section;
    }

    static GameObject CreateButton(Transform parent, string name, string text, Color bgColor)
    {
        GameObject btn = new GameObject(name);
        btn.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(btn, "Create Button");
        btn.AddComponent<RectTransform>();

        Image bg = btn.AddComponent<Image>();
        bg.color = bgColor;

        Button button = btn.AddComponent<Button>();
        ColorBlock cb = button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        cb.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        button.colors = cb;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btn.transform, false);
        Undo.RegisterCreatedObjectUndo(textObj, "Create Button Text");
        RectTransform txtRect = textObj.AddComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 20;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        return btn;
    }
}
