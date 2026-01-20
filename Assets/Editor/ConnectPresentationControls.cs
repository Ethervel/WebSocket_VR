using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// Editor script to connect PresentationControlsPanel to FileSharingUI.
/// Run via menu: Tools > Connect Presentation Controls
/// </summary>
public class ConnectPresentationControls : EditorWindow
{
    [MenuItem("Tools/Connect Presentation Controls")]
    public static void Connect()
    {
        // Find the FileSharingUI near the Whiteboard
        FileSharingUI[] allFileSharingUIs = GameObject.FindObjectsByType<FileSharingUI>(FindObjectsSortMode.None);
        
        FileSharingUI targetUI = null;
        foreach (var ui in allFileSharingUIs)
        {
            // Find the one under Whiteboard/Quad
            if (ui.transform.parent != null && ui.transform.parent.name == "Quad")
            {
                targetUI = ui;
                break;
            }
        }

        if (targetUI == null)
        {
            // Take first one if none found under Whiteboard
            if (allFileSharingUIs.Length > 0)
                targetUI = allFileSharingUIs[0];
        }

        if (targetUI == null)
        {
            EditorUtility.DisplayDialog("Error", "No FileSharingUI found in scene!", "OK");
            return;
        }

        Debug.Log($"[ConnectPresentationControls] Found FileSharingUI on: {targetUI.gameObject.name}");

        // Find the PresentationControlsPanel
        GameObject panel = GameObject.Find("PresentationControlsPanel");
        if (panel == null)
        {
            // Try to find under Whiteboard/Quad
            var quad = GameObject.Find("Whiteboard/Quad");
            if (quad != null)
            {
                var foundPanelTransform = quad.transform.Find("PresentationControlsPanel");
                if (foundPanelTransform != null)
                    panel = foundPanelTransform.gameObject;
            }
        }

        if (panel == null)
        {
            EditorUtility.DisplayDialog("Error", 
                "PresentationControlsPanel not found!\n\n" +
                "Please drag the prefab from Assets/Prefabs/UI/PresentationControlsPanel.prefab into the scene first.", 
                "OK");
            return;
        }

        Debug.Log($"[ConnectPresentationControls] Found panel: {panel.name}");

        // Record undo
        Undo.RecordObject(targetUI, "Connect Presentation Controls");

        // Assign the panel
        targetUI.existingPresentationControlsPanel = panel;

        // Find and assign all buttons and texts
        Transform panelTransform = panel.transform;

        // Page navigation
        var prevBtn = panelTransform.Find("PrevPageButton");
        if (prevBtn != null)
            targetUI.prevPageButton = prevBtn.GetComponent<Button>();

        var nextBtn = panelTransform.Find("NextPageButton");
        if (nextBtn != null)
            targetUI.nextPageButton = nextBtn.GetComponent<Button>();

        var pageText = panelTransform.Find("PageNumberText");
        if (pageText != null)
            targetUI.pageNumberText = pageText.GetComponent<TextMeshProUGUI>();

        // Zoom controls
        var zoomInBtn = panelTransform.Find("ZoomInButton");
        if (zoomInBtn != null)
            targetUI.zoomInButton = zoomInBtn.GetComponent<Button>();

        var zoomOutBtn = panelTransform.Find("ZoomOutButton");
        if (zoomOutBtn != null)
            targetUI.zoomOutButton = zoomOutBtn.GetComponent<Button>();

        var resetZoomBtn = panelTransform.Find("ResetZoomButton");
        if (resetZoomBtn != null)
            targetUI.resetZoomButton = resetZoomBtn.GetComponent<Button>();

        var zoomText = panelTransform.Find("ZoomLevelText");
        if (zoomText != null)
            targetUI.zoomLevelText = zoomText.GetComponent<TextMeshProUGUI>();

        // Pan controls
        var panLeftBtn = panelTransform.Find("PanLeftButton");
        if (panLeftBtn != null)
            targetUI.panLeftButton = panLeftBtn.GetComponent<Button>();

        var panRightBtn = panelTransform.Find("PanRightButton");
        if (panRightBtn != null)
            targetUI.panRightButton = panRightBtn.GetComponent<Button>();

        var panUpBtn = panelTransform.Find("PanUpButton");
        if (panUpBtn != null)
            targetUI.panUpButton = panUpBtn.GetComponent<Button>();

        var panDownBtn = panelTransform.Find("PanDownButton");
        if (panDownBtn != null)
            targetUI.panDownButton = panDownBtn.GetComponent<Button>();

        // Stop and status
        var stopBtn = panelTransform.Find("StopPresentationButton");
        if (stopBtn != null)
            targetUI.stopPresentationButton = stopBtn.GetComponent<Button>();

        var statusText = panelTransform.Find("PresentationStatusText");
        if (statusText != null)
            targetUI.presentationStatusText = statusText.GetComponent<TextMeshProUGUI>();

        // Mark dirty
        EditorUtility.SetDirty(targetUI);

        // Deactivate the panel (will be activated during presentation)
        panel.SetActive(false);

        Debug.Log("[ConnectPresentationControls] All connections made!");

        // Count what was connected
        int connected = 0;
        if (targetUI.existingPresentationControlsPanel != null) connected++;
        if (targetUI.prevPageButton != null) connected++;
        if (targetUI.nextPageButton != null) connected++;
        if (targetUI.pageNumberText != null) connected++;
        if (targetUI.zoomInButton != null) connected++;
        if (targetUI.zoomOutButton != null) connected++;
        if (targetUI.resetZoomButton != null) connected++;
        if (targetUI.zoomLevelText != null) connected++;
        if (targetUI.panLeftButton != null) connected++;
        if (targetUI.panRightButton != null) connected++;
        if (targetUI.panUpButton != null) connected++;
        if (targetUI.panDownButton != null) connected++;
        if (targetUI.stopPresentationButton != null) connected++;
        if (targetUI.presentationStatusText != null) connected++;

        string missingPan = "";
        if (targetUI.panLeftButton == null || targetUI.panRightButton == null || 
            targetUI.panUpButton == null || targetUI.panDownButton == null)
        {
            missingPan = "\n\nNote: Pan buttons not found in prefab.\nYou can add them manually if needed.";
        }

        EditorUtility.DisplayDialog("Success", 
            $"Connected {connected}/14 references to FileSharingUI!\n\n" +
            $"Panel: {panel.name}\n" +
            $"Target: {targetUI.gameObject.name}\n\n" +
            "The panel has been deactivated and will appear during presentations." +
            missingPan,
            "OK");
    }
}
