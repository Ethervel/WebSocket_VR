using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Script d'aide pour configurer le système de partage dans la scène.
/// Utiliser le menu contextuel ou le menu Unity pour setup.
/// </summary>
public class SharingSystemSetup : MonoBehaviour
{
    [Header("References (Auto-filled on Setup)")]
    public FileShareManager fileShareManager;
    public ScreenShareManager screenShareManager;
    public FileViewer fileViewer;
    public SharedFileUI sharedFileUI;

    [Header("Virtual Screen Settings")]
    public Vector3 virtualScreenPosition = new Vector3(0, 2f, 5f);
    public Vector2 virtualScreenSize = new Vector2(4f, 2.25f);

    [Header("File UI Settings")]
    public Vector3 fileUIPanelPosition = new Vector3(-2f, 1.5f, 3f);

#if UNITY_EDITOR
    [MenuItem("VR Meeting/Setup Sharing System")]
    public static void SetupSharingSystemMenu()
    {
        // Trouver ou créer le GameObject
        var existing = FindFirstObjectByType<SharingSystemSetup>();
        if (existing != null)
        {
            existing.SetupManagers();
            Selection.activeGameObject = existing.gameObject;
        }
        else
        {
            // Chercher dans Bootstrap scene ou créer
            GameObject setupGO = new GameObject("SharingSystemSetup");
            var setup = setupGO.AddComponent<SharingSystemSetup>();
            setup.SetupManagers();
            Selection.activeGameObject = setupGO;
        }

        Debug.Log("[SharingSetup] Setup complete! Check the Inspector for references.");
    }
#endif

    [ContextMenu("Setup Managers")]
    public void SetupManagers()
    {
        // 1. FileShareManager (Singleton)
        fileShareManager = FindFirstObjectByType<FileShareManager>();
        if (fileShareManager == null)
        {
            GameObject fsm = new GameObject("FileShareManager");
            fsm.transform.SetParent(transform);
            fileShareManager = fsm.AddComponent<FileShareManager>();
            Debug.Log("[SharingSetup] Created FileShareManager");
        }

        // 2. ScreenShareManager (Singleton)
        screenShareManager = FindFirstObjectByType<ScreenShareManager>();
        if (screenShareManager == null)
        {
            GameObject ssm = new GameObject("ScreenShareManager");
            ssm.transform.SetParent(transform);
            screenShareManager = ssm.AddComponent<ScreenShareManager>();
            Debug.Log("[SharingSetup] Created ScreenShareManager");
        }

        // 3. FileViewer (Singleton)
        fileViewer = FindFirstObjectByType<FileViewer>();
        if (fileViewer == null)
        {
            GameObject fv = new GameObject("FileViewer");
            fv.transform.SetParent(transform);
            fileViewer = fv.AddComponent<FileViewer>();
            Debug.Log("[SharingSetup] Created FileViewer");
        }

        // 4. SharedFileUI (peut être créé plus tard avec prefab)
        sharedFileUI = FindFirstObjectByType<SharedFileUI>();

        Debug.Log("[SharingSetup] Managers setup complete!");
    }

    [ContextMenu("Create Virtual Screen Prefab")]
    public void CreateVirtualScreenPrefab()
    {
        // Créer un écran virtuel dans la scène
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "VirtualScreen";
        screen.transform.position = virtualScreenPosition;

        // Taille avec ratio 16:9
        float aspectRatio = 16f / 9f;
        screen.transform.localScale = new Vector3(virtualScreenSize.x, virtualScreenSize.x / aspectRatio, 1f);

        // Ajouter le composant
        var virtualScreen = screen.AddComponent<VirtualScreen>();

        // Créer un material unlit
        var renderer = screen.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (mat.shader == null)
        {
            mat = new Material(Shader.Find("Unlit/Texture"));
        }
        mat.color = Color.black;
        renderer.material = mat;

        // Assigner au ScreenShareManager
        if (screenShareManager != null)
        {
            // Le prefab sera instancié dynamiquement
        }

        Debug.Log("[SharingSetup] Created VirtualScreen at " + virtualScreenPosition);

#if UNITY_EDITOR
        Selection.activeGameObject = screen;
#endif
    }

    [ContextMenu("Create File UI Panel")]
    public void CreateFileUIPanel()
    {
        // Créer un Canvas WorldSpace pour l'UI des fichiers
        GameObject canvasGO = new GameObject("FileShareUI_Canvas");
        canvasGO.transform.position = fileUIPanelPosition;

        // Canvas
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        // Canvas Scaler
        var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 100;

        // Graphic Raycaster
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // VR Raycaster
        var trackedRaycaster = canvasGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

        // RectTransform size
        var rect = canvasGO.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(600, 800);
        rect.localScale = new Vector3(0.001f, 0.001f, 0.001f);

        // Background panel
        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = panel.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

        // Title
        GameObject titleGO = new GameObject("Title");
        titleGO.transform.SetParent(panel.transform, false);
        var titleRect = titleGO.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(0, 60);
        titleRect.anchoredPosition = new Vector2(0, -10);

        var titleText = titleGO.AddComponent<TMPro.TextMeshProUGUI>();
        titleText.text = "Shared Files";
        titleText.fontSize = 36;
        titleText.alignment = TMPro.TextAlignmentOptions.Center;
        titleText.color = Color.white;

        // Scroll View pour la liste
        GameObject scrollViewGO = new GameObject("ScrollView");
        scrollViewGO.transform.SetParent(panel.transform, false);
        var scrollRect = scrollViewGO.AddComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0, 0.15f);
        scrollRect.anchorMax = new Vector2(1, 0.85f);
        scrollRect.offsetMin = new Vector2(20, 0);
        scrollRect.offsetMax = new Vector2(-20, 0);

        var scrollView = scrollViewGO.AddComponent<UnityEngine.UI.ScrollRect>();
        var scrollImage = scrollViewGO.AddComponent<UnityEngine.UI.Image>();
        scrollImage.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        scrollViewGO.AddComponent<UnityEngine.UI.Mask>();

        // Content container
        GameObject contentGO = new GameObject("Content");
        contentGO.transform.SetParent(scrollViewGO.transform, false);
        var contentRect = contentGO.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.sizeDelta = new Vector2(0, 0);

        var verticalLayout = contentGO.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        verticalLayout.spacing = 10;
        verticalLayout.padding = new RectOffset(10, 10, 10, 10);
        verticalLayout.childControlWidth = true;
        verticalLayout.childControlHeight = false;
        verticalLayout.childForceExpandWidth = true;
        verticalLayout.childForceExpandHeight = false;

        var contentSizeFitter = contentGO.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        contentSizeFitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

        scrollView.content = contentRect;
        scrollView.viewport = scrollRect;

        // Buttons container
        GameObject buttonsGO = new GameObject("Buttons");
        buttonsGO.transform.SetParent(panel.transform, false);
        var buttonsRect = buttonsGO.AddComponent<RectTransform>();
        buttonsRect.anchorMin = new Vector2(0, 0);
        buttonsRect.anchorMax = new Vector2(1, 0.12f);
        buttonsRect.offsetMin = new Vector2(20, 10);
        buttonsRect.offsetMax = new Vector2(-20, -10);

        var horizLayout = buttonsGO.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        horizLayout.spacing = 20;
        horizLayout.childControlWidth = true;
        horizLayout.childControlHeight = true;
        horizLayout.childForceExpandWidth = true;

        // Share button
        CreateButton(buttonsGO.transform, "ShareButton", "Share File", new Color(0.2f, 0.6f, 0.2f));

        // Open Folder button
        CreateButton(buttonsGO.transform, "OpenFolderButton", "Open Folder", new Color(0.3f, 0.3f, 0.6f));

        // Add SharedFileUI component
        var fileUI = canvasGO.AddComponent<SharedFileUI>();
        fileUI.fileManagerPanel = panel;
        fileUI.fileListContainer = contentRect;
        fileUI.shareFileButton = buttonsGO.transform.Find("ShareButton")?.GetComponent<UnityEngine.UI.Button>();
        fileUI.openFolderButton = buttonsGO.transform.Find("OpenFolderButton")?.GetComponent<UnityEngine.UI.Button>();

        sharedFileUI = fileUI;

        Debug.Log("[SharingSetup] Created File UI Panel at " + fileUIPanelPosition);

#if UNITY_EDITOR
        Selection.activeGameObject = canvasGO;
#endif
    }

    void CreateButton(Transform parent, string name, string text, Color color)
    {
        GameObject btnGO = new GameObject(name);
        btnGO.transform.SetParent(parent, false);

        var image = btnGO.AddComponent<UnityEngine.UI.Image>();
        image.color = color;

        var button = btnGO.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;

        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.2f;
        colors.pressedColor = color * 0.8f;
        button.colors = colors;

        // Text
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(btnGO.transform, false);
        var textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var tmpText = textGO.AddComponent<TMPro.TextMeshProUGUI>();
        tmpText.text = text;
        tmpText.fontSize = 24;
        tmpText.alignment = TMPro.TextAlignmentOptions.Center;
        tmpText.color = Color.white;
    }

    [ContextMenu("Create File Item Prefab")]
    public void CreateFileItemPrefab()
    {
        // Créer un prefab pour les items de fichier dans la liste
        GameObject itemGO = new GameObject("FileItem");

        var rect = itemGO.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0, 80);

        var image = itemGO.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.25f, 0.25f, 0.25f, 1f);

        var button = itemGO.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;

        // Horizontal layout
        var layout = itemGO.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        // File icon placeholder
        GameObject iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(itemGO.transform, false);
        var iconRect = iconGO.AddComponent<RectTransform>();
        iconRect.sizeDelta = new Vector2(60, 60);
        var iconImage = iconGO.AddComponent<UnityEngine.UI.Image>();
        iconImage.color = new Color(0.4f, 0.4f, 0.4f);

        // Info container
        GameObject infoGO = new GameObject("Info");
        infoGO.transform.SetParent(itemGO.transform, false);
        var infoRect = infoGO.AddComponent<RectTransform>();
        infoRect.sizeDelta = new Vector2(350, 70);

        var infoLayout = infoGO.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        infoLayout.childControlWidth = true;
        infoLayout.childControlHeight = false;

        // File name
        GameObject nameGO = new GameObject("FileName");
        nameGO.transform.SetParent(infoGO.transform, false);
        var nameText = nameGO.AddComponent<TMPro.TextMeshProUGUI>();
        nameText.text = "filename.pdf";
        nameText.fontSize = 22;
        nameText.color = Color.white;
        var nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.sizeDelta = new Vector2(0, 30);

        // File size
        GameObject sizeGO = new GameObject("FileSize");
        sizeGO.transform.SetParent(infoGO.transform, false);
        var sizeText = sizeGO.AddComponent<TMPro.TextMeshProUGUI>();
        sizeText.text = "1.5 MB";
        sizeText.fontSize = 16;
        sizeText.color = new Color(0.7f, 0.7f, 0.7f);
        var sizeRect = sizeGO.GetComponent<RectTransform>();
        sizeRect.sizeDelta = new Vector2(0, 20);

        // Sender
        GameObject senderGO = new GameObject("Sender");
        senderGO.transform.SetParent(infoGO.transform, false);
        var senderText = senderGO.AddComponent<TMPro.TextMeshProUGUI>();
        senderText.text = "From: Player1";
        senderText.fontSize = 14;
        senderText.color = new Color(0.5f, 0.5f, 0.5f);
        var senderRect = senderGO.GetComponent<RectTransform>();
        senderRect.sizeDelta = new Vector2(0, 18);

        // Open button
        GameObject openBtnGO = new GameObject("OpenButton");
        openBtnGO.transform.SetParent(itemGO.transform, false);
        var openRect = openBtnGO.AddComponent<RectTransform>();
        openRect.sizeDelta = new Vector2(80, 60);

        var openImage = openBtnGO.AddComponent<UnityEngine.UI.Image>();
        openImage.color = new Color(0.3f, 0.5f, 0.3f);

        var openButton = openBtnGO.AddComponent<UnityEngine.UI.Button>();
        openButton.targetGraphic = openImage;

        GameObject openTextGO = new GameObject("Text");
        openTextGO.transform.SetParent(openBtnGO.transform, false);
        var openTextRect = openTextGO.AddComponent<RectTransform>();
        openTextRect.anchorMin = Vector2.zero;
        openTextRect.anchorMax = Vector2.one;
        openTextRect.offsetMin = Vector2.zero;
        openTextRect.offsetMax = Vector2.zero;

        var openText = openTextGO.AddComponent<TMPro.TextMeshProUGUI>();
        openText.text = "Open";
        openText.fontSize = 18;
        openText.alignment = TMPro.TextAlignmentOptions.Center;
        openText.color = Color.white;

        // Progress bar (hidden by default)
        GameObject progressGO = new GameObject("ProgressBar");
        progressGO.transform.SetParent(itemGO.transform, false);
        var progressRect = progressGO.AddComponent<RectTransform>();
        progressRect.anchorMin = new Vector2(0, 0);
        progressRect.anchorMax = new Vector2(1, 0);
        progressRect.pivot = new Vector2(0, 0);
        progressRect.sizeDelta = new Vector2(0, 5);
        progressRect.anchoredPosition = Vector2.zero;

        var progressImage = progressGO.AddComponent<UnityEngine.UI.Image>();
        progressImage.color = new Color(0.2f, 0.7f, 0.2f);
        progressImage.type = UnityEngine.UI.Image.Type.Filled;
        progressImage.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        progressImage.fillAmount = 0.5f;

        progressGO.SetActive(false);

        Debug.Log("[SharingSetup] Created FileItem template. Save as prefab in Assets/Prefabs/UI/");

#if UNITY_EDITOR
        Selection.activeGameObject = itemGO;
#endif
    }
}
