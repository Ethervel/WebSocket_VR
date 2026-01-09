using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Contrôleur UI pour l'écran de personnalisation d'avatar.
/// Affiche un écran permettant de saisir le pseudo avant d'entrer dans le jeu.
/// </summary>
public class AvatarCustomizationUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject customizationPanel;
    [SerializeField] private TMP_InputField pseudoInputField;
    [SerializeField] private Button continueButton;
    [SerializeField] private TextMeshProUGUI errorText;
    [SerializeField] private TextMeshProUGUI characterCountText;

    [Header("Color Selection (Optional)")]
    [SerializeField] private Button[] colorButtons;
    [SerializeField] private Image selectedColorPreview;

    [Header("Settings")]
    [SerializeField] private int minPseudoLength = 2;
    [SerializeField] private int maxPseudoLength = 20;
    [SerializeField] private bool skipIfAlreadyCustomized = true;

    [Header("Colors Presets")]
    [SerializeField] private Color[] availableColors = new Color[]
    {
        Color.white,
        new Color(0.2f, 0.6f, 1f),    // Bleu
        new Color(0.2f, 0.8f, 0.2f),  // Vert
        new Color(1f, 0.4f, 0.4f),    // Rouge
        new Color(1f, 0.8f, 0.2f),    // Jaune
        new Color(0.8f, 0.4f, 1f),    // Violet
        new Color(1f, 0.6f, 0.2f),    // Orange
        new Color(0.4f, 0.8f, 0.8f)   // Cyan
    };

    private Color _selectedColor = Color.white;

    void Start()
    {
        InitializeUI();
        CheckAutoSkip();
    }

    void InitializeUI()
    {
        // Configuration du champ pseudo
        if (pseudoInputField != null)
        {
            pseudoInputField.characterLimit = maxPseudoLength;
            pseudoInputField.onValueChanged.AddListener(OnPseudoChanged);

            // Charger le pseudo existant si disponible
            if (AvatarCustomizationManager.Instance != null)
            {
                pseudoInputField.text = AvatarCustomizationManager.Instance.Pseudo;
                _selectedColor = AvatarCustomizationManager.Instance.AvatarColor;
            }
        }

        // Configuration du bouton continuer
        if (continueButton != null)
        {
            continueButton.onClick.AddListener(OnContinueClicked);
        }

        // Configuration des boutons de couleur
        SetupColorButtons();

        // Mise à jour initiale
        UpdateUI();

        // Masquer l'erreur au démarrage
        if (errorText != null)
        {
            errorText.gameObject.SetActive(false);
        }
    }

    void SetupColorButtons()
    {
        if (colorButtons == null || colorButtons.Length == 0)
            return;

        for (int i = 0; i < colorButtons.Length && i < availableColors.Length; i++)
        {
            int colorIndex = i; // Capture pour closure
            Button btn = colorButtons[i];

            if (btn != null)
            {
                // Définir la couleur du bouton
                Image btnImage = btn.GetComponent<Image>();
                if (btnImage != null)
                {
                    btnImage.color = availableColors[colorIndex];
                }

                // Ajouter le listener
                btn.onClick.AddListener(() => OnColorSelected(colorIndex));
            }
        }

        // Mettre à jour l'aperçu
        UpdateColorPreview();
    }

    void CheckAutoSkip()
    {
        if (!skipIfAlreadyCustomized)
            return;

        if (AvatarCustomizationManager.Instance != null &&
            AvatarCustomizationManager.Instance.HasCustomized)
        {
            Debug.Log("[AvatarUI] Joueur déjà personnalisé, skip de l'écran");
            CompleteAndContinue();
        }
    }

    // ============================
    // EVENT HANDLERS
    // ============================

    void OnPseudoChanged(string newValue)
    {
        UpdateUI();
    }

    void OnColorSelected(int colorIndex)
    {
        if (colorIndex >= 0 && colorIndex < availableColors.Length)
        {
            _selectedColor = availableColors[colorIndex];
            UpdateColorPreview();
            Debug.Log($"[AvatarUI] Couleur sélectionnée: {_selectedColor}");
        }
    }

    void OnContinueClicked()
    {
        string pseudo = pseudoInputField != null ? pseudoInputField.text.Trim() : "Player";

        // Validation
        if (!ValidatePseudo(pseudo))
        {
            ShowError($"Le pseudo doit contenir entre {minPseudoLength} et {maxPseudoLength} caractères");
            return;
        }

        // Sauvegarder
        if (AvatarCustomizationManager.Instance != null)
        {
            AvatarCustomizationManager.Instance.Pseudo = pseudo;
            AvatarCustomizationManager.Instance.AvatarColor = _selectedColor;
        }

        CompleteAndContinue();
    }

    // ============================
    // UI UPDATES
    // ============================

    void UpdateUI()
    {
        string pseudo = pseudoInputField != null ? pseudoInputField.text : "";

        // Compteur de caractères
        if (characterCountText != null)
        {
            characterCountText.text = $"{pseudo.Length}/{maxPseudoLength}";

            // Colorer en rouge si invalide
            if (pseudo.Length < minPseudoLength)
            {
                characterCountText.color = new Color(1f, 0.5f, 0.5f);
            }
            else
            {
                characterCountText.color = Color.white;
            }
        }

        // État du bouton
        if (continueButton != null)
        {
            continueButton.interactable = ValidatePseudo(pseudo);
        }

        // Masquer l'erreur quand on tape
        if (errorText != null && errorText.gameObject.activeSelf)
        {
            errorText.gameObject.SetActive(false);
        }
    }

    void UpdateColorPreview()
    {
        if (selectedColorPreview != null)
        {
            selectedColorPreview.color = _selectedColor;
        }
    }

    void ShowError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
            errorText.gameObject.SetActive(true);
        }
        Debug.LogWarning($"[AvatarUI] {message}");
    }

    // ============================
    // VALIDATION
    // ============================

    bool ValidatePseudo(string pseudo)
    {
        if (string.IsNullOrWhiteSpace(pseudo))
            return false;

        string trimmed = pseudo.Trim();
        return trimmed.Length >= minPseudoLength && trimmed.Length <= maxPseudoLength;
    }

    // ============================
    // NAVIGATION
    // ============================

    void CompleteAndContinue()
    {
        // Notifier le manager
        if (AvatarCustomizationManager.Instance != null)
        {
            AvatarCustomizationManager.Instance.CompleteCustomization();
        }

        // Masquer le panel
        if (customizationPanel != null)
        {
            customizationPanel.SetActive(false);
        }

        // Charger la scène principale via BootstrapManager
        if (BootstrapManager.Instance != null)
        {
            BootstrapManager.Instance.LoadScene(BootstrapManager.Instance.mainSceneName);
        }
        else
        {
            Debug.LogWarning("[AvatarUI] BootstrapManager not found, cannot load main scene");
        }
    }

    // ============================
    // PUBLIC API
    // ============================

    /// <summary>
    /// Affiche l'écran de personnalisation
    /// </summary>
    public void Show()
    {
        if (customizationPanel != null)
        {
            customizationPanel.SetActive(true);
        }
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Masque l'écran de personnalisation
    /// </summary>
    public void Hide()
    {
        if (customizationPanel != null)
        {
            customizationPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Force le re-affichage même si déjà personnalisé (pour les settings)
    /// </summary>
    public void ShowForEdit()
    {
        skipIfAlreadyCustomized = false;
        Show();
    }
}
