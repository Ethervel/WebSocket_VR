using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gère l'interface UI du whiteboard
/// - Bouton Clear
/// - Palette de couleurs (optionnel)
/// - Indicateurs de connexion
/// </summary>
public class WhiteboardUIManager : MonoBehaviour
{
    [Header("References")]
    public Whiteboard targetWhiteboard;
    public WhiteboardMarker[] markers; // Tous les feutres de la scène

    [Header("UI Elements")]
    public Button clearButton;
    public Text statusText;

    [Header("Color Palette (Optionnel)")]
    public Button[] colorButtons;
    public Color[] availableColors;

    void Start()
    {
        // Setup Clear Button
        if (clearButton != null)
        {
            clearButton.onClick.AddListener(OnClearButtonPressed);
        }

        // Setup Color Buttons
        if (colorButtons != null && colorButtons.Length > 0)
        {
            for (int i = 0; i < colorButtons.Length; i++)
            {
                int colorIndex = i; // Capture pour closure
                if (i < availableColors.Length)
                {
                    colorButtons[i].onClick.AddListener(() => OnColorButtonPressed(colorIndex));
                    
                    // Colorer le bouton
                    Image img = colorButtons[i].GetComponent<Image>();
                    if (img != null)
                        img.color = availableColors[colorIndex];
                }
            }
        }

        // Subscribe aux événements réseau
        if (VRNetworkManager.Instance != null)
        {
            VRNetworkManager.OnConnected += UpdateStatus;
            VRNetworkManager.OnDisconnected += UpdateStatus;
        }

        UpdateStatus();
    }

    void OnDestroy()
    {
        if (clearButton != null)
            clearButton.onClick.RemoveAllListeners();

        if (VRNetworkManager.Instance != null)
        {
            VRNetworkManager.OnConnected -= UpdateStatus;
            VRNetworkManager.OnDisconnected -= UpdateStatus;
        }
    }

    // ========================================
    // CLEAR BUTTON
    // ========================================

    void OnClearButtonPressed()
    {
        if (targetWhiteboard == null)
        {
            Debug.LogWarning("[WhiteboardUI] Aucun tableau assigné!");
            return;
        }

        Debug.Log($"[WhiteboardUI] Effacement du tableau {targetWhiteboard.id}");
        targetWhiteboard.RequestClear();
    }

    // ========================================
    // COLOR PALETTE
    // ========================================

    void OnColorButtonPressed(int colorIndex)
    {
        if (colorIndex >= availableColors.Length)
            return;

        Color selectedColor = availableColors[colorIndex];
        
        // Appliquer à tous les feutres
        if (markers != null)
        {
            foreach (var marker in markers)
            {
                if (marker != null)
                    marker.SetColor(selectedColor);
            }
        }

        Debug.Log($"[WhiteboardUI] Couleur changée: {selectedColor}");
    }

    // ========================================
    // STATUS
    // ========================================

    void UpdateStatus()
    {
        if (statusText == null) return;

        if (VRNetworkManager.IsConnected)
        {
            statusText.text = $"Connecté (ID: {VRNetworkManager.LocalId?.Substring(0, 8)})";
            statusText.color = Color.green;
        }
        else
        {
            statusText.text = "Déconnecté";
            statusText.color = Color.red;
        }
    }

    void Update()
    {
        // Update status régulièrement
        if (Time.frameCount % 60 == 0) // Toutes les 60 frames
        {
            UpdateStatus();
        }
    }
}