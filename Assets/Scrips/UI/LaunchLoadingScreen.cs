using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gere l'ecran de chargement au lancement de l'application.
/// Affiche la progression de l'initialisation des systemes.
/// Utilise ScreenFader pour le fade (compatible VR).
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

    [Header("VR")]
    [Tooltip("Desactiver le loading screen en mode VR")]
    public bool disableInVR = true;

    [Header("Fade")]
    public CanvasGroup canvasGroup;
    public float fadeDuration = 0.5f;

    // Etat
    private float _targetProgress = 0f;
    private float _currentProgress = 0f;
    private float _startTime;
    private bool _isComplete = false;
    private static bool _hasRunOnce = false;

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

        // Auto-find references
        AutoFindReferences();

        // Afficher immediatement
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
    }

    void OnEnable()
    {
        if (_hasRunOnce)
        {
            if (canvasGroup != null)
                canvasGroup.alpha = 1f;
            enabled = false;
        }
    }

    void AutoFindReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (progressSlider == null)
            progressSlider = GetComponentInChildren<Slider>(true);

        if (progressBarFill == null && progressSlider == null)
        {
            var fill = transform.Find("ProgressContainer/ProgressBarBG/ProgressBarFill");
            if (fill != null) progressBarFill = fill.GetComponent<Image>();
        }

        if (statusText == null)
        {
            var text = transform.Find("Panel/Text (TMP)");
            if (text != null)
                statusText = text.GetComponent<TextMeshProUGUI>();
            else
                statusText = GetComponentInChildren<TextMeshProUGUI>(true);
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
        if (_hasRunOnce)
        {
            enabled = false;
            return;
        }

        _hasRunOnce = true;

        // Skip loading screen en VR si desactive
        if (disableInVR && UnityEngine.XR.XRSettings.isDeviceActive)
        {
            Debug.Log("[LaunchLoading] VR mode detected - skipping loading screen");
            if (canvasGroup != null)
                canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
            OnLoadingComplete?.Invoke();
            return;
        }

        _startTime = Time.time;

        // LaunchLoadingScreen gere son propre affichage
        // ScreenFader n'est PAS utilise ici - il est reserve pour les transitions de scene
        Debug.Log("[LaunchLoading] Starting initialization sequence");

        StartCoroutine(RunInitializationSequence());
    }

    void Update()
    {
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

    IEnumerator RunInitializationSequence()
    {
        // Attendre une frame pour que ScreenFader soit pret
        yield return null;

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

        // Fade out avec ScreenFader ou fallback CanvasGroup
        yield return StartCoroutine(FadeOut());

        OnLoadingComplete?.Invoke();

        enabled = false;
        gameObject.SetActive(false);
    }

    #region Initialization Steps

    IEnumerator InitializeXR()
    {
        SetStatus("VR Initialisation...");
        SetProgress(0f);

        // Attendre que XR soit pret
        float timeout = 3f;
        float elapsed = 0f;

        while (!UnityEngine.XR.XRSettings.isDeviceActive && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        bool xrReady = UnityEngine.XR.XRSettings.isDeviceActive;
        Debug.Log($"[LaunchLoading] XR Ready: {xrReady}");

        SetProgress(0.2f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator InitializeNetwork()
    {
        SetStatus("Connect to Server...");
        SetProgress(0.2f);

        float timeout = networkTimeout;
        float elapsed = 0f;

        if (VRNetworkManager.Instance == null)
        {
            Debug.LogWarning("[LaunchLoading] VRNetworkManager not found - skipping network init");
            SetProgress(0.5f);
            yield break;
        }

        while (!VRNetworkManager.IsConnected && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
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

        if (AuthManager.Instance != null && !string.IsNullOrEmpty(AuthManager.Instance.Token))
        {
            SetStatus("Checking account...");
            yield return new WaitForSeconds(0.5f);
        }

        SetProgress(0.7f);
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator LoadSettings()
    {
        SetStatus("Load Settings...");
        SetProgress(0.7f);

        if (MainMenuSettings.Instance != null)
        {
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
        // Fade out via CanvasGroup (pas ScreenFader - reserve pour transitions de scene)
        if (canvasGroup != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - (elapsed / fadeDuration);
                yield return null;
            }
            canvasGroup.alpha = 0f;
        }
    }

    #endregion

    public void ForceComplete()
    {
        StopAllCoroutines();
        _isComplete = true;
        OnLoadingComplete?.Invoke();
        gameObject.SetActive(false);
    }

    public bool IsComplete => _isComplete;
}
