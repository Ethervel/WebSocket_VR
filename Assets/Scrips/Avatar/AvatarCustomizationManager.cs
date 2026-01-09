using System;
using UnityEngine;

/// <summary>
/// Gère les préférences de personnalisation de l'avatar du joueur.
/// Singleton persistant - stocke pseudo, couleur, etc. dans PlayerPrefs.
/// </summary>
public class AvatarCustomizationManager : MonoBehaviour
{
    public static AvatarCustomizationManager Instance { get; private set; }

    // ============================
    // PLAYER PREFS KEYS
    // ============================
    private const string PREF_PSEUDO = "PlayerPseudo";
    private const string PREF_AVATAR_COLOR_R = "AvatarColorR";
    private const string PREF_AVATAR_COLOR_G = "AvatarColorG";
    private const string PREF_AVATAR_COLOR_B = "AvatarColorB";
    private const string PREF_HAS_CUSTOMIZED = "HasCustomized";

    // ============================
    // PROPERTIES
    // ============================

    /// <summary>
    /// Pseudo du joueur (nom affiché au-dessus de l'avatar)
    /// </summary>
    public string Pseudo
    {
        get => _pseudo;
        set
        {
            _pseudo = value;
            PlayerPrefs.SetString(PREF_PSEUDO, value);
            PlayerPrefs.Save();
            OnPseudoChanged?.Invoke(value);
        }
    }
    private string _pseudo = "Player";

    /// <summary>
    /// Couleur de l'avatar
    /// </summary>
    public Color AvatarColor
    {
        get => _avatarColor;
        set
        {
            _avatarColor = value;
            PlayerPrefs.SetFloat(PREF_AVATAR_COLOR_R, value.r);
            PlayerPrefs.SetFloat(PREF_AVATAR_COLOR_G, value.g);
            PlayerPrefs.SetFloat(PREF_AVATAR_COLOR_B, value.b);
            PlayerPrefs.Save();
            OnAvatarColorChanged?.Invoke(value);
        }
    }
    private Color _avatarColor = Color.white;

    /// <summary>
    /// Indique si le joueur a déjà personnalisé son avatar
    /// </summary>
    public bool HasCustomized
    {
        get => _hasCustomized;
        private set
        {
            _hasCustomized = value;
            PlayerPrefs.SetInt(PREF_HAS_CUSTOMIZED, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
    private bool _hasCustomized = false;

    // ============================
    // EVENTS
    // ============================
    public static event Action<string> OnPseudoChanged;
    public static event Action<Color> OnAvatarColorChanged;
    public static event Action OnCustomizationCompleted;

    // ============================
    // LIFECYCLE
    // ============================
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadPreferences();
    }

    // ============================
    // PERSISTENCE
    // ============================

    /// <summary>
    /// Charge les préférences sauvegardées
    /// </summary>
    void LoadPreferences()
    {
        _pseudo = PlayerPrefs.GetString(PREF_PSEUDO, "Player");

        float r = PlayerPrefs.GetFloat(PREF_AVATAR_COLOR_R, 1f);
        float g = PlayerPrefs.GetFloat(PREF_AVATAR_COLOR_G, 1f);
        float b = PlayerPrefs.GetFloat(PREF_AVATAR_COLOR_B, 1f);
        _avatarColor = new Color(r, g, b);

        _hasCustomized = PlayerPrefs.GetInt(PREF_HAS_CUSTOMIZED, 0) == 1;

        Debug.Log($"[AvatarCustomization] Loaded: Pseudo={_pseudo}, Color={_avatarColor}, HasCustomized={_hasCustomized}");
    }

    /// <summary>
    /// Réinitialise toutes les préférences
    /// </summary>
    public void ResetPreferences()
    {
        PlayerPrefs.DeleteKey(PREF_PSEUDO);
        PlayerPrefs.DeleteKey(PREF_AVATAR_COLOR_R);
        PlayerPrefs.DeleteKey(PREF_AVATAR_COLOR_G);
        PlayerPrefs.DeleteKey(PREF_AVATAR_COLOR_B);
        PlayerPrefs.DeleteKey(PREF_HAS_CUSTOMIZED);
        PlayerPrefs.Save();

        _pseudo = "Player";
        _avatarColor = Color.white;
        _hasCustomized = false;

        Debug.Log("[AvatarCustomization] Preferences reset");
    }

    // ============================
    // CUSTOMIZATION FLOW
    // ============================

    /// <summary>
    /// Marque la personnalisation comme terminée et notifie les listeners
    /// </summary>
    public void CompleteCustomization()
    {
        if (string.IsNullOrWhiteSpace(_pseudo))
        {
            _pseudo = "Player";
        }

        // Limiter la longueur du pseudo
        if (_pseudo.Length > 20)
        {
            _pseudo = _pseudo.Substring(0, 20);
        }

        HasCustomized = true;

        Debug.Log($"[AvatarCustomization] Customization completed: {_pseudo}");
        OnCustomizationCompleted?.Invoke();
    }

    /// <summary>
    /// Vérifie si le pseudo est valide
    /// </summary>
    public bool IsValidPseudo(string pseudo)
    {
        if (string.IsNullOrWhiteSpace(pseudo))
            return false;

        if (pseudo.Length < 2 || pseudo.Length > 20)
            return false;

        return true;
    }

    // ============================
    // NETWORK DATA
    // ============================

    /// <summary>
    /// Retourne les données de personnalisation pour envoi réseau
    /// </summary>
    public AvatarNetworkData GetNetworkData()
    {
        return new AvatarNetworkData
        {
            pseudo = _pseudo,
            colorR = _avatarColor.r,
            colorG = _avatarColor.g,
            colorB = _avatarColor.b
        };
    }

    /// <summary>
    /// Applique des données réseau reçues (pour les remote players)
    /// </summary>
    public static Color GetColorFromNetworkData(AvatarNetworkData data)
    {
        return new Color(data.colorR, data.colorG, data.colorB);
    }
}

/// <summary>
/// Données de personnalisation envoyées sur le réseau
/// </summary>
[Serializable]
public class AvatarNetworkData
{
    public string pseudo = "Player";
    public float colorR = 1f;
    public float colorG = 1f;
    public float colorB = 1f;
}
