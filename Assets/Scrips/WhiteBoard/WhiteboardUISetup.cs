using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

/// <summary>
/// Script d'aide pour créer rapidement un panel UI whiteboard WorldSpace
/// Attacher à un GameObject vide, puis cliquer sur "Setup UI" dans l'inspecteur (Context Menu)
/// Le whiteboard cible est auto-détecté (le plus proche) si non assigné
/// </summary>
public class WhiteboardUISetup : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Laisser vide pour auto-détecter la surface de dessin la plus proche")]
    public WhiteboardDrawingSurface targetDrawingSurface;

    [Tooltip("Distance max pour auto-détection de la surface")]
    public float autoDetectRadius = 5f;

    [Header("Panel Settings")]
    public float panelWidth = 0.4f;
    public float panelHeight = 0.15f;
    public float buttonSize = 0.08f;
    public float spacing = 0.02f;

    [Header("Colors")]
    public Color redColor = Color.red;
    public Color blueColor = Color.blue;
    public Color greenColor = Color.green;
    public Color clearButtonColor = new Color(0.8f, 0.8f, 0.8f, 1f);

    [ContextMenu("Setup UI")]
    public void SetupUI()
    {
        // Clean existing children
        foreach (Transform child in transform)
        {
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }

        // Add Canvas component if not present
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }
        canvas.renderMode = RenderMode.WorldSpace;

        // Add CanvasScaler
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = gameObject.AddComponent<CanvasScaler>();
        }
        scaler.dynamicPixelsPerUnit = 100;

        // Add GraphicRaycaster (for mouse)
        GraphicRaycaster graphicRaycaster = GetComponent<GraphicRaycaster>();
        if (graphicRaycaster == null)
        {
            graphicRaycaster = gameObject.AddComponent<GraphicRaycaster>();
        }

        // Add TrackedDeviceGraphicRaycaster (for VR)
        TrackedDeviceGraphicRaycaster trackedRaycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
        if (trackedRaycaster == null)
        {
            trackedRaycaster = gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        // Set RectTransform size
        RectTransform rectTransform = GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(panelWidth * 1000, panelHeight * 1000); // Convert to pixels
        rectTransform.localScale = new Vector3(0.001f, 0.001f, 0.001f); // Scale down for world space

        // Create background panel
        GameObject panelBg = CreateUIElement("Background", transform);
        Image bgImage = panelBg.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        RectTransform bgRect = panelBg.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Create horizontal layout
        GameObject buttonContainer = CreateUIElement("Buttons", transform);
        RectTransform containerRect = buttonContainer.GetComponent<RectTransform>();
        containerRect.anchorMin = Vector2.zero;
        containerRect.anchorMax = Vector2.one;
        containerRect.offsetMin = new Vector2(10, 10);
        containerRect.offsetMax = new Vector2(-10, -10);

        HorizontalLayoutGroup layout = buttonContainer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing * 1000;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        // Create color buttons
        Button redButton = CreateColorButton("RedButton", buttonContainer.transform, redColor, buttonSize * 1000);
        Button blueButton = CreateColorButton("BlueButton", buttonContainer.transform, blueColor, buttonSize * 1000);
        Button greenButton = CreateColorButton("GreenButton", buttonContainer.transform, greenColor, buttonSize * 1000);

        // Create clear button
        Button clearButton = CreateClearButton("ClearButton", buttonContainer.transform, clearButtonColor, buttonSize * 1000);

        // Add WhiteboardUIManager
        WhiteboardUIManager uiManager = GetComponent<WhiteboardUIManager>();
        if (uiManager == null)
        {
            uiManager = gameObject.AddComponent<WhiteboardUIManager>();
        }

        uiManager.targetDrawingSurface = targetDrawingSurface;
        uiManager.clearButton = clearButton;
        uiManager.colorButtons = new Button[] { redButton, blueButton, greenButton };
        uiManager.availableColors = new Color[] { redColor, blueColor, greenColor };

        Debug.Log("[WhiteboardUISetup] UI configurée avec succès!");
    }

    GameObject CreateUIElement(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    Button CreateColorButton(string name, Transform parent, Color color, float size)
    {
        GameObject btnGo = CreateUIElement(name, parent);

        Image img = btnGo.AddComponent<Image>();
        img.color = color;

        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;

        // Setup button colors
        ColorBlock colors = btn.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.2f;
        colors.pressedColor = color * 0.8f;
        colors.selectedColor = color;
        btn.colors = colors;

        RectTransform rect = btnGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(size, size);

        // Add border effect (outline)
        Outline outline = btnGo.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2, 2);

        return btn;
    }

    Button CreateClearButton(string name, Transform parent, Color bgColor, float size)
    {
        GameObject btnGo = CreateUIElement(name, parent);

        Image img = btnGo.AddComponent<Image>();
        img.color = bgColor;

        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;

        RectTransform rect = btnGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(size * 1.5f, size); // Wider for text

        // Add text
        GameObject textGo = CreateUIElement("Text", btnGo.transform);
        TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = "Clear";
        text.fontSize = 24;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.Center;

        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return btn;
    }

    void OnDrawGizmosSelected()
    {
        // Draw panel preview
        Gizmos.color = new Color(0.2f, 0.2f, 0.8f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, new Vector3(panelWidth, panelHeight, 0.01f));
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(panelWidth, panelHeight, 0.01f));
    }
}
