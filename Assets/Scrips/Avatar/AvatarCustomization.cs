using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Gère la personnalisation de l'avatar (couleur, pseudo).
/// S'affiche avant le MainPanel pour configurer le joueur.
/// </summary>
public class AvatarCustomization : MonoBehaviour
{
    public static AvatarCustomization Instance { get; private set; }

    [Header("Panel")]
    public GameObject avatarPanel;

    [Header("Username")]
    public TMP_InputField usernameInput;
    public TextMeshProUGUI usernamePreview;

    [Header("Color Selection")]
    public Button[] colorButtons;
    public Image selectedColorPreview;
    public Image avatarPreviewImage;

    [Header("Confirm")]
    public Button confirmButton;

    [Header("Available Colors")]
    public Color[] availableColors = new Color[]
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

    // Current selection
    private Color _selectedColor;
    private string _selectedUsername;
    private bool _isConfigured = false;

    // Events
    public static event Action<Color> OnColorChanged;
    public static event Action<string> OnUsernameChanged;
    public static event Action OnAvatarConfigured;

    // PlayerPrefs keys
    private const string PREF_COLOR_R = "AvatarColorR";
    private const string PREF_COLOR_G = "AvatarColorG";
    private const string PREF_COLOR_B = "AvatarColorB";
    private const string PREF_USERNAME = "PlayerName";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        LoadSavedPreferences();
        SetupUI();

        // Si déjà configuré, ne pas afficher le panel au démarrage
        if (_isConfigured && avatarPanel != null)
        {
            avatarPanel.SetActive(false);
        }
    }

    void LoadSavedPreferences()
    {
        // Charger le username
        _selectedUsername = PlayerPrefs.GetString(PREF_USERNAME, "");

        // Charger la couleur (défaut: bleu)
        float r = PlayerPrefs.GetFloat(PREF_COLOR_R, availableColors[0].r);
        float g = PlayerPrefs.GetFloat(PREF_COLOR_G, availableColors[0].g);
        float b = PlayerPrefs.GetFloat(PREF_COLOR_B, availableColors[0].b);
        _selectedColor = new Color(r, g, b, 1f);

        // Vérifier si déjà configuré (username non vide)
        _isConfigured = !string.IsNullOrEmpty(_selectedUsername);

        Debug.Log($"[AvatarCustomization] Loaded: username='{_selectedUsername}', color={_selectedColor}, configured={_isConfigured}");
    }

    void SetupUI()
    {
        // Setup username input
        if (usernameInput != null)
        {
            usernameInput.text = _selectedUsername;
            usernameInput.onValueChanged.AddListener(OnUsernameInputChanged);
        }

        // Setup color buttons
        SetupColorButtons();

        // Setup confirm button
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        // Update previews
        UpdatePreviews();
    }

    void SetupColorButtons()
    {
        for (int i = 0; i < colorButtons.Length && i < availableColors.Length; i++)
        {
            int index = i; // Capture for closure
            Button btn = colorButtons[i];

            if (btn != null)
            {
                // Set button color
                Image btnImage = btn.GetComponent<Image>();
                if (btnImage != null)
                {
                    btnImage.color = availableColors[index];
                }

                // Add click listener
                btn.onClick.AddListener(() => SelectColor(index));
            }
        }
    }

    void OnUsernameInputChanged(string newValue)
    {
        _selectedUsername = newValue.Trim();
        UpdatePreviews();
    }

    void SelectColor(int index)
    {
        if (index >= 0 && index < availableColors.Length)
        {
            _selectedColor = availableColors[index];
            UpdatePreviews();
            Debug.Log($"[AvatarCustomization] Selected color index {index}: {_selectedColor}");
        }
    }

    void UpdatePreviews()
    {
        // Update color preview
        if (selectedColorPreview != null)
        {
            selectedColorPreview.color = _selectedColor;
        }

        // Update avatar preview
        if (avatarPreviewImage != null)
        {
            avatarPreviewImage.color = _selectedColor;
        }

        // Update username preview
        if (usernamePreview != null)
        {
            string displayName = string.IsNullOrEmpty(_selectedUsername) ? "Player" : _selectedUsername;
            usernamePreview.text = displayName;
        }

        // Update confirm button interactability
        if (confirmButton != null)
        {
            // Require at least a username
            confirmButton.interactable = !string.IsNullOrEmpty(_selectedUsername);
        }
    }

    void OnConfirmClicked()
    {
        if (string.IsNullOrEmpty(_selectedUsername))
        {
            Debug.LogWarning("[AvatarCustomization] Username is required!");
            return;
        }

        // Save preferences
        SavePreferences();

        // Mark as configured
        _isConfigured = true;

        // Notify listeners
        OnColorChanged?.Invoke(_selectedColor);
        OnUsernameChanged?.Invoke(_selectedUsername);
        OnAvatarConfigured?.Invoke();

        // Hide panel
        if (avatarPanel != null)
        {
            avatarPanel.SetActive(false);
        }

        Debug.Log($"[AvatarCustomization] Confirmed: {_selectedUsername} with color {_selectedColor}");
    }

    void SavePreferences()
    {
        PlayerPrefs.SetString(PREF_USERNAME, _selectedUsername);
        PlayerPrefs.SetFloat(PREF_COLOR_R, _selectedColor.r);
        PlayerPrefs.SetFloat(PREF_COLOR_G, _selectedColor.g);
        PlayerPrefs.SetFloat(PREF_COLOR_B, _selectedColor.b);
        PlayerPrefs.Save();

        Debug.Log($"[AvatarCustomization] Saved preferences");
    }

    #region Public API

    /// <summary>
    /// Affiche le panel de customisation
    /// </summary>
    public void ShowPanel()
    {
        if (avatarPanel != null)
        {
            avatarPanel.SetActive(true);
        }

        // Refresh UI with current values
        if (usernameInput != null)
        {
            usernameInput.text = _selectedUsername;
        }
        UpdatePreviews();
    }

    /// <summary>
    /// Cache le panel de customisation
    /// </summary>
    public void HidePanel()
    {
        if (avatarPanel != null)
        {
            avatarPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Retourne true si l'avatar a été configuré
    /// </summary>
    public bool IsConfigured => _isConfigured;

    /// <summary>
    /// Retourne la couleur sélectionnée
    /// </summary>
    public Color SelectedColor => _selectedColor;

    /// <summary>
    /// Retourne le username sélectionné
    /// </summary>
    public string SelectedUsername => _selectedUsername;

    /// <summary>
    /// Définit la couleur (pour sync réseau)
    /// </summary>
    public void SetColor(Color color)
    {
        _selectedColor = color;
        UpdatePreviews();
    }

    /// <summary>
    /// Retourne les données de couleur pour le réseau (r, g, b)
    /// </summary>
    public (float r, float g, float b) GetColorData()
    {
        return (_selectedColor.r, _selectedColor.g, _selectedColor.b);
    }

    /// <summary>
    /// Crée une couleur à partir des données réseau
    /// </summary>
    public static Color ColorFromData(float r, float g, float b)
    {
        return new Color(r, g, b, 1f);
    }

    #endregion

    void OnDestroy()
    {
        if (usernameInput != null)
            usernameInput.onValueChanged.RemoveAllListeners();

        if (confirmButton != null)
            confirmButton.onClick.RemoveAllListeners();

        foreach (var btn in colorButtons)
        {
            if (btn != null)
                btn.onClick.RemoveAllListeners();
        }

        if (Instance == this)
            Instance = null;
    }
}
