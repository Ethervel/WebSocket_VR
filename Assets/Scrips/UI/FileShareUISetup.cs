using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Helper script to create the File Sharing UI structure.
/// Use via Context Menu: Right-click the component → "Setup File Share UI"
/// </summary>
public class FileShareUISetup : MonoBehaviour
{
    [Header("Colors")]
    public Color panelBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
    public Color buttonColor = new Color(0.2f, 0.5f, 0.8f, 1f);
    public Color buttonTextColor = Color.white;
    public Color listItemColor = new Color(0.15f, 0.15f, 0.15f, 1f);

    [Header("Dimensions")]
    public Vector2 panelSize = new Vector2(400, 500);
    public Vector2 fileButtonSize = new Vector2(80, 40);
    public float itemHeight = 60f;

    [ContextMenu("Setup File Share UI")]
    public void SetupUI()
    {
        // Ensure Canvas
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rt = GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0.5f, 0.6f); // World space size
            rt.localScale = Vector3.one * 0.001f; // Scale down for VR
        }

        // Add required components
        if (GetComponent<CanvasScaler>() == null)
            gameObject.AddComponent<CanvasScaler>();

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // Add TrackedDeviceGraphicRaycaster for VR if available
#if UNITY_XR_INTERACTION_TOOLKIT
        if (GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>() == null)
            gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
#endif

        // Create UI structure
        CreateFileButton();
        CreateMainPanel();
        CreatePreviewPanel();
        CreateFileListItemPrefab();

        // Add FileSharingUI component and wire references
        FileSharingUI ui = GetComponent<FileSharingUI>();
        if (ui == null)
            ui = gameObject.AddComponent<FileSharingUI>();

        WireReferences(ui);

        Debug.Log("[FileShareUISetup] UI structure created. Check child objects and wire any missing references.");

#if UNITY_EDITOR
        EditorUtility.SetDirty(gameObject);
#endif
    }

    void CreateFileButton()
    {
        // Main "File" button
        GameObject buttonObj = CreateUIElement("FileButton", transform);
        RectTransform rt = buttonObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(10, -10);
        rt.sizeDelta = fileButtonSize;

        Image bg = buttonObj.AddComponent<Image>();
        bg.color = buttonColor;

        Button btn = buttonObj.AddComponent<Button>();
        btn.targetGraphic = bg;

        // Button text
        GameObject textObj = CreateUIElement("Text", buttonObj.transform);
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = "File";
        text.alignment = TextAlignmentOptions.Center;
        text.color = buttonTextColor;
        text.fontSize = 18;

        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
    }

    void CreateMainPanel()
    {
        // Main Panel (hidden by default)
        GameObject panel = CreateUIElement("MainPanel", transform);
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(10, 0);
        rt.sizeDelta = panelSize;

        Image bg = panel.AddComponent<Image>();
        bg.color = panelBackgroundColor;

        // Header
        GameObject header = CreateUIElement("Header", panel.transform);
        RectTransform headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0, 1);
        headerRt.anchorMax = new Vector2(1, 1);
        headerRt.pivot = new Vector2(0.5f, 1);
        headerRt.anchoredPosition = Vector2.zero;
        headerRt.sizeDelta = new Vector2(0, 50);

        // Title
        GameObject titleObj = CreateUIElement("Title", header.transform);
        TextMeshProUGUI title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text = "Shared Files";
        title.alignment = TextAlignmentOptions.Left;
        title.color = Color.white;
        title.fontSize = 22;
        title.fontStyle = FontStyles.Bold;

        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(0.8f, 1);
        titleRt.offsetMin = new Vector2(15, 10);
        titleRt.offsetMax = new Vector2(0, -10);

        // Close Button
        GameObject closeBtn = CreateButton("CloseButton", header.transform, "X", 40, 40);
        RectTransform closeBtnRt = closeBtn.GetComponent<RectTransform>();
        closeBtnRt.anchorMin = new Vector2(1, 0.5f);
        closeBtnRt.anchorMax = new Vector2(1, 0.5f);
        closeBtnRt.pivot = new Vector2(1, 0.5f);
        closeBtnRt.anchoredPosition = new Vector2(-10, 0);

        // ScrollView for file list
        GameObject scrollView = CreateUIElement("FileListScrollView", panel.transform);
        RectTransform scrollViewRt = scrollView.GetComponent<RectTransform>();
        scrollViewRt.anchorMin = new Vector2(0, 0.25f);
        scrollViewRt.anchorMax = new Vector2(1, 0.88f);
        scrollViewRt.offsetMin = new Vector2(10, 0);
        scrollViewRt.offsetMax = new Vector2(-10, 0);

        ScrollRect scrollRect = scrollView.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        // Add mask image for clipping
        Image scrollBg = scrollView.AddComponent<Image>();
        scrollBg.color = new Color(0.08f, 0.08f, 0.08f, 0.5f);
        scrollView.AddComponent<Mask>().showMaskGraphic = true;

        // Viewport (same size as scroll view)
        GameObject viewport = CreateUIElement("Viewport", scrollView.transform);
        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;

        // Content container (this is what FileSharingUI.fileListContainer should reference)
        GameObject listArea = CreateUIElement("FileListContainer", viewport.transform);
        RectTransform listAreaRt = listArea.GetComponent<RectTransform>();
        listAreaRt.anchorMin = new Vector2(0, 1);
        listAreaRt.anchorMax = new Vector2(1, 1);
        listAreaRt.pivot = new Vector2(0.5f, 1);
        listAreaRt.offsetMin = Vector2.zero;
        listAreaRt.offsetMax = Vector2.zero;

        // Add VerticalLayoutGroup for file items
        VerticalLayoutGroup vlg = listArea.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 5;
        vlg.padding = new RectOffset(5, 5, 5, 5);

        // Content size fitter - makes content grow with items
        ContentSizeFitter csf = listArea.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Wire scroll rect
        scrollRect.viewport = viewportRt;
        scrollRect.content = listAreaRt;

        // Empty list text
        GameObject emptyText = CreateUIElement("EmptyListText", panel.transform);
        TextMeshProUGUI empty = emptyText.AddComponent<TextMeshProUGUI>();
        empty.text = "No files shared yet";
        empty.alignment = TextAlignmentOptions.Center;
        empty.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        empty.fontSize = 16;

        RectTransform emptyRt = emptyText.GetComponent<RectTransform>();
        emptyRt.anchorMin = new Vector2(0, 0.4f);
        emptyRt.anchorMax = new Vector2(1, 0.6f);
        emptyRt.offsetMin = Vector2.zero;
        emptyRt.offsetMax = Vector2.zero;

        // Bottom area - Download path, Share button and status
        GameObject bottomArea = CreateUIElement("BottomArea", panel.transform);
        RectTransform bottomRt = bottomArea.GetComponent<RectTransform>();
        bottomRt.anchorMin = new Vector2(0, 0);
        bottomRt.anchorMax = new Vector2(1, 0.25f);
        bottomRt.offsetMin = new Vector2(10, 10);
        bottomRt.offsetMax = new Vector2(-10, 0);

        // Download path label
        GameObject pathLabel = CreateUIElement("DownloadPathLabel", bottomArea.transform);
        TextMeshProUGUI pathLabelText = pathLabel.AddComponent<TextMeshProUGUI>();
        pathLabelText.text = "Download to:";
        pathLabelText.fontSize = 12;
        pathLabelText.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        pathLabelText.alignment = TextAlignmentOptions.Left;

        RectTransform pathLabelRt = pathLabel.GetComponent<RectTransform>();
        pathLabelRt.anchorMin = new Vector2(0, 0.6f);
        pathLabelRt.anchorMax = new Vector2(1, 0.85f);
        pathLabelRt.offsetMin = Vector2.zero;
        pathLabelRt.offsetMax = Vector2.zero;

        // Download path input row
        GameObject pathRow = CreateUIElement("DownloadPathRow", bottomArea.transform);
        RectTransform pathRowRt = pathRow.GetComponent<RectTransform>();
        pathRowRt.anchorMin = new Vector2(0, 0.25f);
        pathRowRt.anchorMax = new Vector2(1, 0.6f);
        pathRowRt.offsetMin = Vector2.zero;
        pathRowRt.offsetMax = Vector2.zero;

        // Download path input field
        GameObject pathInputObj = CreateUIElement("DownloadPathInput", pathRow.transform);
        RectTransform pathInputRt = pathInputObj.GetComponent<RectTransform>();
        pathInputRt.anchorMin = new Vector2(0, 0);
        pathInputRt.anchorMax = new Vector2(0.7f, 1);
        pathInputRt.offsetMin = Vector2.zero;
        pathInputRt.offsetMax = new Vector2(-5, 0);

        Image pathInputBg = pathInputObj.AddComponent<Image>();
        pathInputBg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

        // Text area for input
        GameObject pathTextArea = CreateUIElement("Text Area", pathInputObj.transform);
        RectTransform pathTextAreaRt = pathTextArea.GetComponent<RectTransform>();
        pathTextAreaRt.anchorMin = Vector2.zero;
        pathTextAreaRt.anchorMax = Vector2.one;
        pathTextAreaRt.offsetMin = new Vector2(5, 0);
        pathTextAreaRt.offsetMax = new Vector2(-5, 0);

        GameObject pathTextObj = CreateUIElement("Text", pathTextArea.transform);
        RectTransform pathTextObjRt = pathTextObj.GetComponent<RectTransform>();
        pathTextObjRt.anchorMin = Vector2.zero;
        pathTextObjRt.anchorMax = Vector2.one;
        pathTextObjRt.offsetMin = Vector2.zero;
        pathTextObjRt.offsetMax = Vector2.zero;

        TextMeshProUGUI pathText = pathTextObj.AddComponent<TextMeshProUGUI>();
        pathText.fontSize = 11;
        pathText.color = Color.white;
        pathText.alignment = TextAlignmentOptions.Left;
        pathText.overflowMode = TextOverflowModes.Ellipsis;

        TMP_InputField pathInput = pathInputObj.AddComponent<TMP_InputField>();
        pathInput.textViewport = pathTextAreaRt;
        pathInput.textComponent = pathText;

        // Browse button
        GameObject browseBtn = CreateButton("BrowsePathButton", pathRow.transform, "...", 35, 0);
        RectTransform browseBtnRt = browseBtn.GetComponent<RectTransform>();
        browseBtnRt.anchorMin = new Vector2(0.72f, 0);
        browseBtnRt.anchorMax = new Vector2(0.85f, 1);
        browseBtnRt.offsetMin = Vector2.zero;
        browseBtnRt.offsetMax = Vector2.zero;

        // Open folder button
        GameObject openBtn = CreateButton("OpenFolderButton", pathRow.transform, "Open", 50, 0);
        RectTransform openBtnRt = openBtn.GetComponent<RectTransform>();
        openBtnRt.anchorMin = new Vector2(0.87f, 0);
        openBtnRt.anchorMax = new Vector2(1, 1);
        openBtnRt.offsetMin = Vector2.zero;
        openBtnRt.offsetMax = Vector2.zero;

        // Share button row
        GameObject shareRow = CreateUIElement("ShareRow", bottomArea.transform);
        RectTransform shareRowRt = shareRow.GetComponent<RectTransform>();
        shareRowRt.anchorMin = new Vector2(0, 0);
        shareRowRt.anchorMax = new Vector2(1, 0.25f);
        shareRowRt.offsetMin = Vector2.zero;
        shareRowRt.offsetMax = Vector2.zero;

        // Share Button
        GameObject shareBtn = CreateButton("ShareButton", shareRow.transform, "Share File", 120, 0);
        RectTransform shareBtnRt = shareBtn.GetComponent<RectTransform>();
        shareBtnRt.anchorMin = new Vector2(0, 0);
        shareBtnRt.anchorMax = new Vector2(0.35f, 1);
        shareBtnRt.offsetMin = Vector2.zero;
        shareBtnRt.offsetMax = Vector2.zero;

        // Status Text
        GameObject statusObj = CreateUIElement("StatusText", shareRow.transform);
        TextMeshProUGUI status = statusObj.AddComponent<TextMeshProUGUI>();
        status.text = "";
        status.alignment = TextAlignmentOptions.Right;
        status.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        status.fontSize = 11;

        RectTransform statusRt = statusObj.GetComponent<RectTransform>();
        statusRt.anchorMin = new Vector2(0.4f, 0);
        statusRt.anchorMax = new Vector2(1, 1);
        statusRt.offsetMin = Vector2.zero;
        statusRt.offsetMax = Vector2.zero;

        panel.SetActive(false);
    }

    void CreatePreviewPanel()
    {
        // Preview Panel (shown before sharing)
        GameObject panel = CreateUIElement("PreviewPanel", transform);
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0.5f);
        rt.anchorMax = new Vector2(0, 0.5f);
        rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(10, 0);
        rt.sizeDelta = new Vector2(350, 400);

        Image bg = panel.AddComponent<Image>();
        bg.color = panelBackgroundColor;

        // Title
        GameObject titleObj = CreateUIElement("Title", panel.transform);
        TextMeshProUGUI title = titleObj.AddComponent<TextMeshProUGUI>();
        title.text = "Share File?";
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        title.fontSize = 22;
        title.fontStyle = FontStyles.Bold;

        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0, 0.85f);
        titleRt.anchorMax = new Vector2(1, 0.95f);
        titleRt.offsetMin = Vector2.zero;
        titleRt.offsetMax = Vector2.zero;

        // Image preview container
        GameObject imageContainer = CreateUIElement("PreviewImageContainer", panel.transform);
        RectTransform imgContRt = imageContainer.GetComponent<RectTransform>();
        imgContRt.anchorMin = new Vector2(0.1f, 0.4f);
        imgContRt.anchorMax = new Vector2(0.9f, 0.8f);
        imgContRt.offsetMin = Vector2.zero;
        imgContRt.offsetMax = Vector2.zero;

        Image imgBg = imageContainer.AddComponent<Image>();
        imgBg.color = new Color(0.05f, 0.05f, 0.05f, 1f);

        GameObject previewImg = CreateUIElement("PreviewImage", imageContainer.transform);
        Image img = previewImg.AddComponent<Image>();
        img.preserveAspect = true;

        RectTransform previewImgRt = previewImg.GetComponent<RectTransform>();
        previewImgRt.anchorMin = new Vector2(0.05f, 0.05f);
        previewImgRt.anchorMax = new Vector2(0.95f, 0.95f);
        previewImgRt.offsetMin = Vector2.zero;
        previewImgRt.offsetMax = Vector2.zero;

        // File name
        GameObject fileNameObj = CreateUIElement("PreviewFileName", panel.transform);
        TextMeshProUGUI fileName = fileNameObj.AddComponent<TextMeshProUGUI>();
        fileName.text = "filename.ext";
        fileName.alignment = TextAlignmentOptions.Center;
        fileName.color = Color.white;
        fileName.fontSize = 16;

        RectTransform fileNameRt = fileNameObj.GetComponent<RectTransform>();
        fileNameRt.anchorMin = new Vector2(0, 0.28f);
        fileNameRt.anchorMax = new Vector2(1, 0.36f);
        fileNameRt.offsetMin = new Vector2(10, 0);
        fileNameRt.offsetMax = new Vector2(-10, 0);

        // File size
        GameObject fileSizeObj = CreateUIElement("PreviewFileSize", panel.transform);
        TextMeshProUGUI fileSize = fileSizeObj.AddComponent<TextMeshProUGUI>();
        fileSize.text = "0 KB";
        fileSize.alignment = TextAlignmentOptions.Center;
        fileSize.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        fileSize.fontSize = 14;

        RectTransform fileSizeRt = fileSizeObj.GetComponent<RectTransform>();
        fileSizeRt.anchorMin = new Vector2(0, 0.2f);
        fileSizeRt.anchorMax = new Vector2(1, 0.28f);
        fileSizeRt.offsetMin = new Vector2(10, 0);
        fileSizeRt.offsetMax = new Vector2(-10, 0);

        // Buttons area
        GameObject buttonsArea = CreateUIElement("ButtonsArea", panel.transform);
        RectTransform btnAreaRt = buttonsArea.GetComponent<RectTransform>();
        btnAreaRt.anchorMin = new Vector2(0, 0.02f);
        btnAreaRt.anchorMax = new Vector2(1, 0.15f);
        btnAreaRt.offsetMin = new Vector2(20, 0);
        btnAreaRt.offsetMax = new Vector2(-20, 0);

        HorizontalLayoutGroup hlg = buttonsArea.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 20;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;

        // Cancel button
        GameObject cancelBtn = CreateButton("PreviewCancelButton", buttonsArea.transform, "Cancel", 100, 40);
        cancelBtn.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 1f);

        // Share button
        GameObject shareBtn = CreateButton("PreviewShareButton", buttonsArea.transform, "Share", 100, 40);

        panel.SetActive(false);
    }

    void CreateFileListItemPrefab()
    {
        // Create a template item (can be saved as prefab)
        GameObject item = CreateUIElement("FileListItemTemplate", transform);
        RectTransform rt = item.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, itemHeight);

        Image bg = item.AddComponent<Image>();
        bg.color = listItemColor;

        Button btn = item.AddComponent<Button>();
        btn.targetGraphic = bg;

        // File name
        GameObject nameObj = CreateUIElement("FileName", item.transform);
        TextMeshProUGUI nameText = nameObj.AddComponent<TextMeshProUGUI>();
        nameText.text = "filename.ext";
        nameText.alignment = TextAlignmentOptions.Left;
        nameText.color = Color.white;
        nameText.fontSize = 14;

        RectTransform nameRt = nameObj.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0, 0.5f);
        nameRt.anchorMax = new Vector2(0.7f, 1);
        nameRt.offsetMin = new Vector2(10, 5);
        nameRt.offsetMax = new Vector2(0, -5);

        // Shared by
        GameObject byObj = CreateUIElement("SharedBy", item.transform);
        TextMeshProUGUI byText = byObj.AddComponent<TextMeshProUGUI>();
        byText.text = "by Player";
        byText.alignment = TextAlignmentOptions.Left;
        byText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        byText.fontSize = 11;

        RectTransform byRt = byObj.GetComponent<RectTransform>();
        byRt.anchorMin = new Vector2(0, 0);
        byRt.anchorMax = new Vector2(0.7f, 0.5f);
        byRt.offsetMin = new Vector2(10, 5);
        byRt.offsetMax = new Vector2(0, -5);

        // File size (positioned to leave room for delete button on the right)
        GameObject sizeObj = CreateUIElement("FileSize", item.transform);
        TextMeshProUGUI sizeText = sizeObj.AddComponent<TextMeshProUGUI>();
        sizeText.text = "0 KB";
        sizeText.alignment = TextAlignmentOptions.Right;
        sizeText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        sizeText.fontSize = 12;

        RectTransform sizeRt = sizeObj.GetComponent<RectTransform>();
        sizeRt.anchorMin = new Vector2(0.55f, 0);
        sizeRt.anchorMax = new Vector2(0.85f, 1);
        sizeRt.offsetMin = new Vector2(0, 5);
        sizeRt.offsetMax = new Vector2(0, -5);

        // Placeholder for delete button (created dynamically in FileSharingUI)
        // Space reserved: anchor 0.85 to 1.0 (right 15%)

        item.SetActive(false); // Template should be inactive
    }

    void WireReferences(FileSharingUI ui)
    {
        // Find and wire references
        ui.fileButton = transform.Find("FileButton")?.GetComponent<Button>();
        ui.mainPanel = transform.Find("MainPanel")?.gameObject;
        ui.previewPanel = transform.Find("PreviewPanel")?.gameObject;

        if (ui.mainPanel != null)
        {
            Transform header = ui.mainPanel.transform.Find("Header");
            ui.closeButton = header?.Find("CloseButton")?.GetComponent<Button>();

            // FileListContainer is now inside ScrollView/Viewport
            Transform scrollView = ui.mainPanel.transform.Find("FileListScrollView");
            Transform viewport = scrollView?.Find("Viewport");
            ui.fileListContainer = viewport?.Find("FileListContainer");
            ui.emptyListText = ui.mainPanel.transform.Find("EmptyListText")?.GetComponent<TextMeshProUGUI>();

            Transform bottomArea = ui.mainPanel.transform.Find("BottomArea");
            Transform pathRow = bottomArea?.Find("DownloadPathRow");
            Transform shareRow = bottomArea?.Find("ShareRow");

            // Download path UI elements
            ui.downloadPathInput = pathRow?.Find("DownloadPathInput")?.GetComponent<TMP_InputField>();
            ui.browsePathButton = pathRow?.Find("BrowsePathButton")?.GetComponent<Button>();
            ui.openFolderButton = pathRow?.Find("OpenFolderButton")?.GetComponent<Button>();

            // Share button and status
            ui.shareButton = shareRow?.Find("ShareButton")?.GetComponent<Button>();
            ui.statusText = shareRow?.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        }

        if (ui.previewPanel != null)
        {
            ui.previewFileName = ui.previewPanel.transform.Find("PreviewFileName")?.GetComponent<TextMeshProUGUI>();
            ui.previewFileSize = ui.previewPanel.transform.Find("PreviewFileSize")?.GetComponent<TextMeshProUGUI>();

            Transform imgContainer = ui.previewPanel.transform.Find("PreviewImageContainer");
            ui.previewImageContainer = imgContainer?.gameObject;
            ui.previewImage = imgContainer?.Find("PreviewImage")?.GetComponent<Image>();

            Transform buttonsArea = ui.previewPanel.transform.Find("ButtonsArea");
            ui.previewCancelButton = buttonsArea?.Find("PreviewCancelButton")?.GetComponent<Button>();
            ui.previewShareButton = buttonsArea?.Find("PreviewShareButton")?.GetComponent<Button>();
        }

        // File list item prefab
        ui.fileListItemPrefab = transform.Find("FileListItemTemplate")?.gameObject;
    }

    #region UI Creation Helpers

    GameObject CreateUIElement(string name, Transform parent)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        return obj;
    }

    GameObject CreateButton(string name, Transform parent, string text, float width, float height)
    {
        GameObject btn = CreateUIElement(name, parent);
        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        Image bg = btn.AddComponent<Image>();
        bg.color = buttonColor;

        Button button = btn.AddComponent<Button>();
        button.targetGraphic = bg;

        GameObject textObj = CreateUIElement("Text", btn.transform);
        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.color = buttonTextColor;
        tmpText.fontSize = 14;

        return btn;
    }

    #endregion

#if UNITY_EDITOR
    [MenuItem("GameObject/UI/File Share UI", false, 10)]
    static void CreateFileShareUI(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("FileShareUI");
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

        FileShareUISetup setup = go.AddComponent<FileShareUISetup>();
        setup.SetupUI();

        Undo.RegisterCreatedObjectUndo(go, "Create File Share UI");
        Selection.activeObject = go;
    }
#endif
}
