using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Gère le chargement de scènes avec transitions fade.
/// Singleton persistant - fonctionne avec BootstrapManager.
/// </summary>
public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [Header("References")]
    [Tooltip("ScreenFader pour les transitions")]
    public ScreenFader screenFader;

    [Tooltip("Indicator de chargement (spinner)")]
    public LoadingIndicator loadingIndicator;

    [Header("Settings")]
    [Tooltip("Temps minimum d'affichage du loading")]
    public float minimumLoadTime = 1f;

    [Tooltip("Délai après fade in avant de charger")]
    public float delayAfterFadeIn = 0.2f;

    [Tooltip("Délai après chargement avant le fade out (écran reste noir)")]
    public float delayAfterLoad = 0.5f;

    // Events
    [HideInInspector] public UnityEvent OnLoadBegin = new UnityEvent();
    [HideInInspector] public UnityEvent OnLoadEnd = new UnityEvent();

    // Events statiques pour faciliter l'accès
    public static event Action OnSceneLoadStarted;
    public static event Action<string> OnSceneActivated;  // Après chargement, AVANT fade out (écran noir)
    public static event Action<string> OnSceneLoadCompleted;  // Après fade out (écran visible)

    // État
    private bool _isLoading = false;
    private string _currentScene = "";

    public bool IsLoading => _isLoading;
    public string CurrentScene => _currentScene;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Auto-find components si non assignés
        if (screenFader == null)
            screenFader = GetComponentInChildren<ScreenFader>(true);

        if (loadingIndicator == null)
            loadingIndicator = GetComponentInChildren<LoadingIndicator>(true);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Charge une nouvelle scène avec transition fade.
    /// </summary>
    public void LoadNewScene(string sceneName)
    {
        if (_isLoading)
        {
            Debug.LogWarning($"[SceneLoader] Already loading, ignoring request for '{sceneName}'");
            return;
        }

        StartCoroutine(LoadSceneCoroutine(sceneName));
    }

    /// <summary>
    /// Fait un fade in (écran noir) sans charger de scène.
    /// Utile pour le lancement initial.
    /// </summary>
    public void FadeIn(Action onComplete = null)
    {
        StartCoroutine(FadeInCoroutine(onComplete));
    }

    /// <summary>
    /// Fait un fade out (écran visible) sans charger de scène.
    /// </summary>
    public void FadeOut(Action onComplete = null)
    {
        StartCoroutine(FadeOutCoroutine(onComplete));
    }

    private IEnumerator LoadSceneCoroutine(string sceneName)
    {
        _isLoading = true;
        float startTime = Time.time;

        Debug.Log($"[SceneLoader] Starting load: {sceneName}");

        // Notifier le début
        OnLoadBegin?.Invoke();
        OnSceneLoadStarted?.Invoke();

        // Fade to black
        if (screenFader != null)
        {
            yield return screenFader.StartFadeIn();
        }

        // Afficher l'indicator
        if (loadingIndicator != null)
        {
            loadingIndicator.Show();
        }

        yield return new WaitForSeconds(delayAfterFadeIn);

        // Décharger la scène actuelle si elle existe
        if (!string.IsNullOrEmpty(_currentScene))
        {
            yield return StartCoroutine(UnloadCurrentScene());
        }

        // Charger la nouvelle scène
        yield return StartCoroutine(LoadNewSceneAsync(sceneName));

        // Notifier que la scène est activée (écran encore noir)
        // C'est ici que le joueur doit être téléporté
        OnSceneActivated?.Invoke(sceneName);
        yield return null; // Attendre une frame pour que la téléportation soit appliquée

        // Attendre le temps minimum
        float elapsed = Time.time - startTime;
        if (elapsed < minimumLoadTime)
        {
            yield return new WaitForSeconds(minimumLoadTime - elapsed);
        }

        // Cacher l'indicator
        if (loadingIndicator != null)
        {
            loadingIndicator.Hide();
            yield return new WaitForSeconds(0.3f); // Attendre l'animation
        }

        // Délai après chargement (écran reste noir)
        if (delayAfterLoad > 0f)
        {
            yield return new WaitForSeconds(delayAfterLoad);
        }

        // Fade from black
        if (screenFader != null)
        {
            yield return screenFader.StartFadeOut();
        }

        _currentScene = sceneName;
        _isLoading = false;

        Debug.Log($"[SceneLoader] Load complete: {sceneName}");

        // Notifier la fin
        OnLoadEnd?.Invoke();
        OnSceneLoadCompleted?.Invoke(sceneName);
    }

    private IEnumerator UnloadCurrentScene()
    {
        Scene scene = SceneManager.GetSceneByName(_currentScene);
        if (scene.IsValid() && scene.isLoaded)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(_currentScene);
            while (unloadOp != null && !unloadOp.isDone)
            {
                yield return null;
            }
            Debug.Log($"[SceneLoader] Unloaded: {_currentScene}");
        }
    }

    private IEnumerator LoadNewSceneAsync(string sceneName)
    {
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        loadOp.allowSceneActivation = true;

        while (!loadOp.isDone)
        {
            // Progress va de 0 à 0.9, puis saute à 1 quand allowSceneActivation = true
            float progress = Mathf.Clamp01(loadOp.progress / 0.9f);

            if (loadingIndicator != null)
            {
                loadingIndicator.SetProgress(progress);
            }

            yield return null;
        }

        // Activer la nouvelle scène
        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid())
        {
            SceneManager.SetActiveScene(loadedScene);
        }

        Debug.Log($"[SceneLoader] Loaded: {sceneName}");
    }

    private IEnumerator FadeInCoroutine(Action onComplete)
    {
        if (screenFader != null)
        {
            yield return screenFader.StartFadeIn();
        }
        onComplete?.Invoke();
    }

    private IEnumerator FadeOutCoroutine(Action onComplete)
    {
        if (screenFader != null)
        {
            yield return screenFader.StartFadeOut();
        }
        onComplete?.Invoke();
    }

    /// <summary>
    /// Définit la scène actuelle sans la charger (pour initialisation).
    /// </summary>
    public void SetCurrentScene(string sceneName)
    {
        _currentScene = sceneName;
    }

    /// <summary>
    /// Affiche l'indicator de chargement (sans fade).
    /// </summary>
    public void ShowLoadingIndicator()
    {
        if (loadingIndicator != null)
        {
            loadingIndicator.Show();
        }
    }

    /// <summary>
    /// Cache l'indicator de chargement.
    /// </summary>
    public void HideLoadingIndicator()
    {
        if (loadingIndicator != null)
        {
            loadingIndicator.Hide();
        }
    }
}
