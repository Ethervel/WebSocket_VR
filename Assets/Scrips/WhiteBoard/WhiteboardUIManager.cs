using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gère l'interface UI du whiteboard
/// - Bouton Clear
/// - Palette de couleurs
/// - Fonctionne en mode VR (WhiteboardMarker) et Desktop (DesktopWhiteboardDrawer)
/// - Auto-détecte le whiteboard le plus proche si non assigné
/// </summary>
public class WhiteboardUIManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Laisser vide pour auto-détecter le whiteboard le plus proche")]
    public Whiteboard targetWhiteboard;

    [Tooltip("Laisser vide pour auto-détecter tous les markers")]
    public WhiteboardMarker[] markers;

    [Header("Auto-Detection")]
    [Tooltip("Distance max pour auto-détection")]
    public float autoDetectRadius = 10f;

    [Header("UI Elements")]
    public Button clearButton;
    public Text statusText;

    [Header("Color Palette")]
    public Button[] colorButtons;
    public Color[] availableColors = new Color[] { Color.red, Color.blue, Color.green, Color.black };

    [Header("Visual Feedback")]
    [Tooltip("Indicateur de couleur sélectionnée")]
    public Image selectedColorIndicator;

    // Cache pour Desktop drawer
    private DesktopWhiteboardDrawer _desktopDrawer;

    void Start()
    {
        // Auto-détection du whiteboard le plus proche
        if (targetWhiteboard == null)
        {
            AutoDetectWhiteboard();
        }

        // Auto-détection des markers VR
        if (markers == null || markers.Length == 0)
        {
            markers = FindObjectsByType<WhiteboardMarker>(FindObjectsSortMode.None);
            if (markers.Length > 0)
            {
                Debug.Log($"[WhiteboardUI] Auto-détecté {markers.Length} marker(s) VR");
            }
        }

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

        // Subscribe au spawn du joueur local pour récupérer le DesktopWhiteboardDrawer
        VRGameManager.OnLocalPlayerSpawned += OnLocalPlayerSpawned;

        UpdateStatus();
    }

    /// <summary>
    /// Trouve le whiteboard le plus proche de ce panel UI
    /// </summary>
    void AutoDetectWhiteboard()
    {
        Whiteboard[] allWhiteboards = FindObjectsByType<Whiteboard>(FindObjectsSortMode.None);

        float closestDist = float.MaxValue;
        Whiteboard closest = null;

        foreach (var wb in allWhiteboards)
        {
            float dist = Vector3.Distance(transform.position, wb.transform.position);
            if (dist < closestDist && dist <= autoDetectRadius)
            {
                closestDist = dist;
                closest = wb;
            }
        }

        if (closest != null)
        {
            targetWhiteboard = closest;
            Debug.Log($"[WhiteboardUI] Auto-détecté whiteboard '{closest.id}' à {closestDist:F1}m");
        }
        else
        {
            Debug.LogWarning($"[WhiteboardUI] Aucun whiteboard trouvé dans un rayon de {autoDetectRadius}m");
        }
    }

    void OnLocalPlayerSpawned(GameObject player)
    {
        // Chercher le DesktopWhiteboardDrawer sur le joueur spawné
        _desktopDrawer = player.GetComponentInChildren<DesktopWhiteboardDrawer>();
        if (_desktopDrawer != null)
        {
            Debug.Log("[WhiteboardUI] DesktopWhiteboardDrawer trouvé");
        }
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

        VRGameManager.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
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

        // Appliquer aux feutres VR
        if (markers != null)
        {
            foreach (var marker in markers)
            {
                if (marker != null)
                    marker.SetColor(selectedColor);
            }
        }

        // Appliquer au drawer Desktop
        if (_desktopDrawer != null)
        {
            _desktopDrawer.SetColor(selectedColor);
        }
        else
        {
            // Chercher à nouveau si pas trouvé (joueur spawné après UI)
            var localPlayer = VRGameManager.Instance?.GetLocalPlayer();
            if (localPlayer != null)
            {
                _desktopDrawer = localPlayer.GetComponentInChildren<DesktopWhiteboardDrawer>();
                if (_desktopDrawer != null)
                {
                    _desktopDrawer.SetColor(selectedColor);
                }
            }
        }

        // Mettre à jour l'indicateur visuel
        if (selectedColorIndicator != null)
        {
            selectedColorIndicator.color = selectedColor;
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