using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// Editor script to create the Presentation Controls prefab.
/// Run via menu: Tools > Create Presentation Controls Prefab
/// </summary>
public class PresentationControlsPrefabCreator : EditorWindow
{
    [MenuItem("Tools/Create Presentation Controls Prefab")]
    public static void CreatePrefab()
    {
        // Create root panel
        GameObject panel = new GameObject("PresentationControlsPanel");
        RectTransform panelRt = panel.AddComponent<RectTransform>();
        panelRt.sizeDelta = new Vector2(600, 60);

        // Add background image
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

        // Add horizontal layout
        HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.padding = new RectOffset(15, 15, 8, 8);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        // Add Canvas Group for fading
        panel.AddComponent<CanvasGroup>();

        // ===== PAGE NAVIGATION =====
        // Previous button
        CreateButton(panel.transform, "PrevPageButton", "<", 40, new Color(0.2f, 0.5f, 0.8f));

        // Page number text
        CreateText(panel.transform, "PageNumberText", "1 / 1", 70, 16);

        // Next button
        CreateButton(panel.transform, "NextPageButton", ">", 40, new Color(0.2f, 0.5f, 0.8f));

        // Separator
        CreateSeparator(panel.transform);

        // ===== ZOOM CONTROLS =====
        // Zoom out button
        CreateButton(panel.transform, "ZoomOutButton", "-", 35, new Color(0.4f, 0.4f, 0.5f));

        // Zoom level text
        CreateText(panel.transform, "ZoomLevelText", "100%", 55, 14);

        // Zoom in button
        CreateButton(panel.transform, "ZoomInButton", "+", 35, new Color(0.4f, 0.4f, 0.5f));

        // Reset zoom button
        CreateButton(panel.transform, "ResetZoomButton", "1:1", 45, new Color(0.4f, 0.4f, 0.5f));

        // Separator
        CreateSeparator(panel.transform);

        // ===== PAN CONTROLS =====
        // Pan left
        CreateButton(panel.transform, "PanLeftButton", "\u25C0", 32, new Color(0.35f, 0.35f, 0.4f));

        // Pan up
        CreateButton(panel.transform, "PanUpButton", "\u25B2", 32, new Color(0.35f, 0.35f, 0.4f));

        // Pan down
        CreateButton(panel.transform, "PanDownButton", "\u25BC", 32, new Color(0.35f, 0.35f, 0.4f));

        // Pan right
        CreateButton(panel.transform, "PanRightButton", "\u25B6", 32, new Color(0.35f, 0.35f, 0.4f));

        // Separator
        CreateSeparator(panel.transform);

        // ===== STATUS & STOP =====
        // Presentation status text
        CreateText(panel.transform, "PresentationStatusText", "Presenting...", 100, 12);

        // Stop button
        CreateButton(panel.transform, "StopPresentationButton", "Stop", 55, new Color(0.8f, 0.25f, 0.25f));

        // Save as prefab
        string prefabPath = "Assets/Prefabs/UI/PresentationControlsPanel.prefab";
        
        // Ensure the directory exists
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

        // Check if prefab already exists
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existingPrefab != null)
        {
            // Update existing prefab
            PrefabUtility.SaveAsPrefabAsset(panel, prefabPath);
            Debug.Log($"Updated existing prefab: {prefabPath}");
        }
        else
        {
            // Create new prefab
            PrefabUtility.SaveAsPrefabAsset(panel, prefabPath);
            Debug.Log($"Created new prefab: {prefabPath}");
        }

        // Cleanup scene object
        DestroyImmediate(panel);

        // Refresh and select
        AssetDatabase.Refresh();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        EditorUtility.DisplayDialog("Prefab Created", 
            $"Presentation Controls prefab created at:\n{prefabPath}\n\n" +
            "To use:\n" +
            "1. Drag into your scene\n" +
            "2. Position near the whiteboard\n" +
            "3. Assign to FileSharingUI.existingPresentationControlsPanel\n" +
            "4. Assign all button references in FileSharingUI", 
            "OK");
    }

    static void CreateButton(Transform parent, string name, string label, float width, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 40);

        Image img = btnObj.AddComponent<Image>();
        img.color = bgColor;

        // Add rounded corners effect (slight)
        img.type = Image.Type.Sliced;

        Button btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = img;

        // Set button colors
        ColorBlock colors = btn.colors;
        colors.normalColor = bgColor;
        colors.highlightedColor = bgColor * 1.2f;
        colors.pressedColor = bgColor * 0.8f;
        colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        btn.colors = colors;

        // Add text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        RectTransform textRt = textObj.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 18;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
    }

    static void CreateText(Transform parent, string name, string content, float width, float fontSize)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        RectTransform rt = textObj.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 40);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
    }

    static void CreateSeparator(Transform parent)
    {
        GameObject sep = new GameObject("Separator");
        sep.transform.SetParent(parent, false);

        RectTransform rt = sep.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(2, 30);

        Image img = sep.AddComponent<Image>();
        img.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
    }
}
