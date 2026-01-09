using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

/// <summary>
/// Génère automatiquement l'UI de personnalisation d'avatar si elle n'existe pas.
/// Crée une UI World Space compatible VR avec TrackedDeviceGraphicRaycaster.
/// </summary>
public class AvatarCustomizationUIBuilder : MonoBehaviour
{
    [Header("Auto-Generate Settings")]
    [SerializeField] private bool autoGenerateIfMissing = true;
    [SerializeField] private Font defaultFont;

    [Header("VR Settings")]
    [SerializeField] private bool useWorldSpaceForVR = true;
    [SerializeField] private float canvasDistance = 2f;
    [SerializeField] private float canvasScale = 0.002f;
    [SerializeField] private Vector3 canvasOffset = new Vector3(0, 1.5f, 0);

    [Header("Colors")]
    [SerializeField] private Color panelColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
    [SerializeField] private Color buttonColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color inputFieldColor = new Color(0.15f, 0.15f, 0.2f, 1f);
    [SerializeField] private Color textColor = Color.white;

    private GameObject _generatedCanvas;
    private GameObject _customizationPanel;

    void Awake()
    {
        // Ne génère pas automatiquement dans Awake - laisse le BootstrapManager contrôler
    }

    /// <summary>
    /// Génère l'UI si elle n'existe pas encore et retourne le panel
    /// </summary>
    public GameObject GenerateIfNeeded()
    {
        if (_customizationPanel == null)
        {
            GenerateUI();
        }
        return _customizationPanel;
    }

    public void GenerateUI()
    {
        Debug.Log("[AvatarUIBuilder] Generating customization UI...");

        // Create Canvas
        _generatedCanvas = new GameObject("CustomizationCanvas");
        _generatedCanvas.transform.SetParent(transform);

        var canvas = _generatedCanvas.AddComponent<Canvas>();
        
        if (useWorldSpaceForVR)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            _generatedCanvas.transform.position = new Vector3(0, 1.5f, 2f); // Default fixed pos
            // Try to place in front of camera if possible
            if (Camera.main != null)
            {
                var camTf = Camera.main.transform;
                _generatedCanvas.transform.position = camTf.position + camTf.forward * canvasDistance + canvasOffset;
                _generatedCanvas.transform.rotation = Quaternion.LookRotation(_generatedCanvas.transform.position - camTf.position);
                // Ensure upright
                var euler = _generatedCanvas.transform.rotation.eulerAngles;
                _generatedCanvas.transform.rotation = Quaternion.Euler(0, euler.y, 0);
            }
            _generatedCanvas.transform.localScale = Vector3.one * canvasScale;
            
            // Add VR Interaction Raycaster
            _generatedCanvas.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            _generatedCanvas.AddComponent<GraphicRaycaster>();
        }

        var canvasScaler = _generatedCanvas.AddComponent<CanvasScaler>();
        if (useWorldSpaceForVR)
        {
            canvasScaler.dynamicPixelsPerUnit = 10;
        }
        else
        {
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.matchWidthOrHeight = 0.5f;
        }

        // Create EventSystem if needed
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.transform.SetParent(_generatedCanvas.transform);
            eventSystemObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // Create main panel
        _customizationPanel = CreatePanel(_generatedCanvas.transform, "CustomizationPanel", panelColor);
        var panelRect = _customizationPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Create content container (centered)
        var contentPanel = CreatePanel(_customizationPanel.transform, "ContentPanel", new Color(0.12f, 0.12f, 0.18f, 1f));
        var contentRect = contentPanel.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(500, 400);
        contentRect.anchoredPosition = Vector2.zero;

        // Add rounded corners effect (via child image)
        var outline = contentPanel.AddComponent<Outline>();
        outline.effectColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
        outline.effectDistance = new Vector2(2, 2);

        // Create Title
        var title = CreateText(contentPanel.transform, "Title", "Bienvenue !", 36, TextAlignmentOptions.Center);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.anchoredPosition = new Vector2(0, -30);
        titleRect.sizeDelta = new Vector2(0, 50);

        // Create Subtitle
        var subtitle = CreateText(contentPanel.transform, "Subtitle", "Entrez votre pseudo pour continuer", 18, TextAlignmentOptions.Center);
        subtitle.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        var subtitleRect = subtitle.GetComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0, 1);
        subtitleRect.anchorMax = new Vector2(1, 1);
        subtitleRect.pivot = new Vector2(0.5f, 1);
        subtitleRect.anchoredPosition = new Vector2(0, -85);
        subtitleRect.sizeDelta = new Vector2(0, 30);

        // Create Input Field
        var inputFieldObj = CreateInputField(contentPanel.transform, "PseudoInputField");
        var inputRect = inputFieldObj.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputRect.anchoredPosition = new Vector2(0, 20);
        inputRect.sizeDelta = new Vector2(350, 50);

        // Create Character Count
        var charCount = CreateText(contentPanel.transform, "CharacterCount", "0/20", 14, TextAlignmentOptions.Right);
        charCount.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        var charCountRect = charCount.GetComponent<RectTransform>();
        charCountRect.anchorMin = new Vector2(0.5f, 0.5f);
        charCountRect.anchorMax = new Vector2(0.5f, 0.5f);
        charCountRect.anchoredPosition = new Vector2(150, -15);
        charCountRect.sizeDelta = new Vector2(100, 25);

        // Create Error Text
        var errorText = CreateText(contentPanel.transform, "ErrorText", "", 14, TextAlignmentOptions.Center);
        errorText.color = new Color(1f, 0.4f, 0.4f, 1f);
        var errorRect = errorText.GetComponent<RectTransform>();
        errorRect.anchorMin = new Vector2(0.5f, 0.5f);
        errorRect.anchorMax = new Vector2(0.5f, 0.5f);
        errorRect.anchoredPosition = new Vector2(0, -45);
        errorRect.sizeDelta = new Vector2(350, 25);
        errorText.gameObject.SetActive(false);

        // Create Continue Button
        var buttonObj = CreateButton(contentPanel.transform, "ContinueButton", "Continuer");
        var buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0);
        buttonRect.anchorMax = new Vector2(0.5f, 0);
        buttonRect.pivot = new Vector2(0.5f, 0);
        buttonRect.anchoredPosition = new Vector2(0, 40);
        buttonRect.sizeDelta = new Vector2(200, 50);

        // Add AvatarCustomizationUI component
        var customizationUI = _customizationPanel.AddComponent<AvatarCustomizationUI>();

        // Use reflection to set private serialized fields
        SetPrivateField(customizationUI, "customizationPanel", _customizationPanel);
        SetPrivateField(customizationUI, "pseudoInputField", inputFieldObj.GetComponent<TMP_InputField>());
        SetPrivateField(customizationUI, "continueButton", buttonObj.GetComponent<Button>());
        SetPrivateField(customizationUI, "errorText", errorText);
        SetPrivateField(customizationUI, "characterCountText", charCount);

        // Start hidden - BootstrapManager will show it when needed
        _customizationPanel.SetActive(false);

        Debug.Log("[AvatarUIBuilder] UI generated successfully!");
    }

    GameObject CreatePanel(Transform parent, string name, Color color)
    {
        var panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        var image = panel.AddComponent<Image>();
        image.color = color;

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;

        return panel;
    }

    TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize, TextAlignmentOptions alignment)
    {
        var textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = false;

        return tmp;
    }

    GameObject CreateInputField(Transform parent, string name)
    {
        var inputObj = new GameObject(name);
        inputObj.transform.SetParent(parent, false);

        var image = inputObj.AddComponent<Image>();
        image.color = inputFieldColor;

        var inputField = inputObj.AddComponent<TMP_InputField>();
        inputField.characterLimit = 20;

        // Text Area
        var textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        var textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10, 5);
        textAreaRect.offsetMax = new Vector2(-10, -5);

        // Placeholder
        var placeholder = new GameObject("Placeholder");
        placeholder.transform.SetParent(textArea.transform, false);
        var placeholderText = placeholder.AddComponent<TextMeshProUGUI>();
        placeholderText.text = "Votre pseudo...";
        placeholderText.fontSize = 20;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
        placeholderText.alignment = TextAlignmentOptions.Left;
        placeholderText.enableWordWrapping = false;
        var placeholderRect = placeholder.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        // Text
        var textComponent = new GameObject("Text");
        textComponent.transform.SetParent(textArea.transform, false);
        var textTMP = textComponent.AddComponent<TextMeshProUGUI>();
        textTMP.fontSize = 20;
        textTMP.color = textColor;
        textTMP.alignment = TextAlignmentOptions.Left;
        textTMP.enableWordWrapping = false;
        var textRect = textComponent.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // Assign to input field
        inputField.textViewport = textAreaRect;
        inputField.textComponent = textTMP;
        inputField.placeholder = placeholderText;

        return inputObj;
    }

    GameObject CreateButton(Transform parent, string name, string text)
    {
        var buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);

        var image = buttonObj.AddComponent<Image>();
        image.color = buttonColor;

        var button = buttonObj.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = buttonColor;
        colors.highlightedColor = new Color(buttonColor.r * 1.2f, buttonColor.g * 1.2f, buttonColor.b * 1.2f, 1f);
        colors.pressedColor = new Color(buttonColor.r * 0.8f, buttonColor.g * 0.8f, buttonColor.b * 0.8f, 1f);
        button.colors = colors;

        // Button text
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 22;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;

        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return buttonObj;
    }

    void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);

        if (field != null)
        {
            field.SetValue(obj, value);
        }
        else
        {
            Debug.LogWarning($"[AvatarUIBuilder] Field '{fieldName}' not found");
        }
    }

    public GameObject GetGeneratedPanel()
    {
        return _customizationPanel;
    }
}
