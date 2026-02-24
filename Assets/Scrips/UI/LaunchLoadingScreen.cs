using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gere l'ecran de chargement au lancement de l'application.
/// Affiche la progression de l'initialisation des systemes.
/// Compatible avec Slider ou Image (fillAmount).
/// </summary>
public class LaunchLoadingScreen : MonoBehaviour
{
    public static LaunchLoadingScreen Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Slider de progression (si utilise)")]
    public Slider progressSlider;

    [Tooltip("Image de remplissage (alternative au Slider)")]
    public Image progressBarFill;

    [Tooltip("Texte affichant le pourcentage ou le statut")]
    public TextMeshProUGUI statusText;

    [Header("Settings")]
    [Tooltip("Temps minimum d'affichage du loading (secondes)")]
    public float minimumDisplayTime = 2f;

    [Tooltip("Vitesse d'animation de la barre (lerp)")]
    public float progressAnimSpeed = 3f;

    [Tooltip("Timeout pour la connexion serveur (secondes)")]
    public float networkTimeout = 10f;

    [Header("Fade")]
    public CanvasGroup canvasGroup;
    public float fadeDuration = 0.5f;

    // Etat
    private float _targetProgress = 0f;
    private float _currentProgress = 0f;
    private float _startTime;
    private bool _isComplete = false;
    private static bool _hasRunOnce = false; // Empeche de relancer apres la premiere fois

    // Event declenche quand le loading est termine
    public static event Action OnLoadingComplete;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-find references if not assigned
        AutoFindReferences();

        // Afficher immediatement
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
    }

    void OnEnable()
    {
        // Si deja execute, se desactiver pour laisser BootstrapManager gerer
        if (_hasRunOnce)
        {
            // Restaurer l'alpha (mis a 0 par FadeOut) pour les transitions de scene
            if (canvasGroup != null)
                canvasGroup.alpha = 1f;

            enabled = false;
        }
    }

    /// <summary>
    /// Auto-trouve les references UI si elles ne sont pas assignees.
    /// Compatible avec l'ancienne structure (Panel/Slider, Panel/Text)
    /// </summary>
    void AutoFindReferences()
    {
        // CanvasGroup sur ce GameObject
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        // Chercher Slider (ancienne structure)
        if (progressSlider == null)
        {
            progressSlider = GetComponentInChildren<Slider>(true);
        }

        // Chercher Image fill (nouvelle structure)
        if (progressBarFill == null && progressSlider == null)
        {
            var fill = transform.Find("ProgressContainer/ProgressBarBG/ProgressBarFill");
            if (fill != null) progressBarFill = fill.GetComponent<Image>();
        }

        // Chercher le texte
        if (statusText == null)
        {
            // Essayer l'ancienne structure d'abord
            var text = transform.Find("Panel/Text (TMP)");
            if (text != null)
            {
                statusText = text.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                // Sinon chercher n'importe quel TMP
                statusText = GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        // Log
        if (progressSlider != null)
            Debug.Log("[LaunchLoading] Using Slider for progress");
        else if (progressBarFill != null)
            Debug.Log("[LaunchLoading] Using Image fill for progress");
        else
            Debug.LogWarning("[LaunchLoading] No progress UI found!");
    }

    void Start()
    {
        // Ne s'execute qu'une seule fois au lancement de l'app
        // Apres ca, le loading screen est gere par BootstrapManager pour les transitions de scene
        if (_hasRunOnce)
        {
            // Desactiver ce composant pour laisser BootstrapManager gerer le loading screen
            enabled = false;
            return;
        }

        _hasRunOnce = true;
        _startTime = Time.time;
        StartCoroutine(RunInitializationSequence());
    }

    void Update()
    {
        // Animation fluide de la barre de progression
        if (_currentProgress < _targetProgress)
        {
            _currentProgress = Mathf.Lerp(_currentProgress, _targetProgress, Time.deltaTime * progressAnimSpeed);
            UpdateProgressUI(_currentProgress);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Sequence principale d'initialisation.
    /// </summary>
    IEnumerator RunInitializationSequence()
    {
        // Etape 1: XR (0-20%)
        yield return StartCoroutine(InitializeXR());

        // Etape 2: Network (20-50%)
        yield return StartCoroutine(InitializeNetwork());

        // Etape 3: Auth check (50-70%)
        yield return StartCoroutine(CheckAuthentication());

        // Etape 4: Settings (70-90%)
        yield return StartCoroutine(LoadSettings());

        // Etape 5: Finalize (90-100%)
        yield return StartCoroutine(Finalize());

        // Attendre le temps minimum d'affichage
        float elapsed = Time.time - _startTime;
        if (elapsed < minimumDisplayTime)
        {
            yield return new WaitForSeconds(minimumDisplayTime - elapsed);
        }

        // Termine
        _isComplete = true;
        yield return StartCoroutine(FadeOut());

        OnLoadingComplete?.Invoke();

        // Desactiver ce composant pour laisser BootstrapManager gerer les futurs loadings
        enabled = false;

        gameObject.SetActive(false);
    }

    #region Initialization Steps

    IEnumerator InitializeXR()
    {
        SetStatus("VR Initialisation...");
        SetProgress(0f);

        // Attendre que XR soit pret (BootstrapManager gere ca)
        yield return new WaitForSeconds(0.3f);

        // Verifier si XR est actif
        bool xrReady = UnityEngine.XR.XRSettings.isDeviceActive;
        Debug.Log($"[LaunchLoading] XR Ready: {xrReady}");

        SetProgress(0.2f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator InitializeNetwork()
    {
        SetStatus("Connect to Server...");
        SetProgress(0.2f);

        // Attendre la connexion WebSocket
        float timeout = networkTimeout;
        float elapsed = 0f;

        // Verifier si VRNetworkManager existe
        if (VRNetworkManager.Instance == null)
        {
            Debug.LogWarning("[LaunchLoading] VRNetworkManager not found - skipping network init");
            SetProgress(0.5f);
            yield break;
        }

        while (!VRNetworkManager.IsConnected && elapsed < timeout)
        {
            elapsed += Time.deltaTime;

            // Progression graduelle pendant l'attente
            float networkProgress = 0.2f + (elapsed / timeout) * 0.25f;
            SetProgress(Mathf.Min(networkProgress, 0.45f));

            yield return null;
        }

        if (VRNetworkManager.IsConnected)
        {
            Debug.Log("[LaunchLoading] Network connected");
            SetProgress(0.5f);
        }
        else
        {
            Debug.LogWarning("[LaunchLoading] Network timeout - continuing anyway");
            SetStatus("Mode hors-ligne...");
            SetProgress(0.5f);
            yield return new WaitForSeconds(0.5f);
        }
    }

    IEnumerator CheckAuthentication()
    {
        SetStatus("Checking...");
        SetProgress(0.5f);

        // Verifier si un token existe
        if (AuthManager.Instance != null && !string.IsNullOrEmpty(AuthManager.Instance.Token))
        {
            SetStatus("Checking account...");
            // Le token sera verifie automatiquement par AuthManager.OnNetworkConnected
            yield return new WaitForSeconds(0.5f);
        }

        SetProgress(0.7f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator LoadSettings()
    {
        SetStatus("Load Settings...");
        SetProgress(0.7f);

        // Charger les parametres utilisateur
        if (MainMenuSettings.Instance != null)
        {
            // Les settings sont charges automatiquement dans Awake
            yield return new WaitForSeconds(0.2f);
        }

        SetProgress(0.9f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator Finalize()
    {
        SetStatus("Ready");
        SetProgress(1f);
        yield return new WaitForSeconds(0.3f);
    }

    #endregion

    #region UI Updates

    void SetProgress(float progress)
    {
        _targetProgress = Mathf.Clamp01(progress);
    }

    void UpdateProgressUI(float progress)
    {
        // Supporter Slider ou Image
        if (progressSlider != null)
            progressSlider.value = progress;
        else if (progressBarFill != null)
            progressBarFill.fillAmount = progress;
    }

    void SetStatus(string status)
    {
        if (statusText != null)
            statusText.text = status;

        Debug.Log($"[LaunchLoading] {status}");
    }

    IEnumerator FadeOut()
    {
        if (canvasGroup == null)
            yield break;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = 1f - (elapsed / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = 0f;
    }

    #endregion

    /// <summary>
    /// Permet de forcer la completion (pour tests).
    /// </summary>
    public void ForceComplete()
    {
        StopAllCoroutines();
        _isComplete = true;
        OnLoadingComplete?.Invoke();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Verifie si le loading est termine.
    /// </summary>
    public bool IsComplete => _isComplete;
}
