using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections;

/// Gère le chargement des scènes. Cette scène contient tous les managers
/// et charge la scène principale en mode additif.
public class BootstrapManager : MonoBehaviour
{
    public static BootstrapManager Instance { get; private set; }

    [Header("Scene Settings")]
    [Tooltip("Nom de la scène principale à charger")]
    public string mainSceneName = "MainScene";

    [Tooltip("Charger la scène principale au démarrage")]
    public bool loadMainSceneOnStart = true;

    [Tooltip("Délai avant de charger la scène principale (secondes)")]
    public float loadDelay = 0.5f;

    [Header("Avatar Customization")]
    [Tooltip("Afficher l'écran de personnalisation avant de charger la scène")]
    public bool showCustomizationOnStart = true;

    [Tooltip("Panel de personnalisation d'avatar")]
    public GameObject customizationPanel;

    [Header("Loading UI (Optionnel)")]
    public GameObject loadingScreen;
    public UnityEngine.UI.Slider progressBar;
    public TMPro.TextMeshProUGUI loadingText;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // État
    private bool _isLoading = false;
    private string _currentLoadedScene = "";

    // Référence à l'EventSystem persistant
    private EventSystem _persistentEventSystem;

    void Awake()
    {
        if (Instance != null)
        {
            Debug.LogWarning("[Bootstrap] Another instance exists, destroying this one");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Rendre l'EventSystem persistant
        SetupPersistentEventSystem();

        if (showDebugLogs)
            Debug.Log("[Bootstrap] Bootstrap initialized");
    }

    void Start()
    {
        if (loadMainSceneOnStart)
        {
            StartCoroutine(LoadMainSceneDelayed());
        }
    }

    /// Configure l'EventSystem de Bootstrap comme persistant
    void SetupPersistentEventSystem()
    {
        // Chercher l'EventSystem dans la scène Bootstrap
        var allEventSystems = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

        foreach (var es in allEventSystems)
        {
            // Prendre celui avec XRUIInputModule (priorité) ou le premier trouvé
            var xrModule = es.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            if (xrModule != null || _persistentEventSystem == null)
            {
                _persistentEventSystem = es;
                DontDestroyOnLoad(es.gameObject);

                if (showDebugLogs)
                    Debug.Log($"[Bootstrap] ✅ EventSystem '{es.gameObject.name}' rendu persistant (XRUIInputModule: {xrModule != null})");

                if (xrModule != null) break; // Si on a trouvé un XR, on arrête
            }
        }

        if (_persistentEventSystem == null)
        {
            Debug.LogError("[Bootstrap] ❌ Aucun EventSystem trouvé dans la scène Bootstrap!");
        }
    }
    
    IEnumerator LoadMainSceneDelayed()
    {
        yield return new WaitForSeconds(loadDelay);
        LoadScene(mainSceneName);
    }
    
    /// Charge une scène en mode additif.
    public void LoadScene(string sceneName)
    {
        if (_isLoading)
        {
            Debug.LogWarning($"[Bootstrap] Already loading a scene, ignoring request for {sceneName}");
            return;
        }
        
        StartCoroutine(LoadSceneAsync(sceneName));
    }
    
    IEnumerator LoadSceneAsync(string sceneName)
    {
        _isLoading = true;

        if (showDebugLogs)
            Debug.Log($"[Bootstrap] Starting to load scene: {sceneName}");

        // Afficher l'écran de chargement
        if (loadingScreen != null)
            loadingScreen.SetActive(true);

        // Décharger l'ancienne scène si elle existe
        if (!string.IsNullOrEmpty(_currentLoadedScene))
        {
            if (showDebugLogs)
                Debug.Log($"[Bootstrap] Unloading previous scene: {_currentLoadedScene}");

            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(_currentLoadedScene);
            while (unloadOp != null && !unloadOp.isDone)
            {
                yield return null;
            }
        }

        // Charger la nouvelle scène
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        while (!loadOp.isDone)
        {
            float progress = Mathf.Clamp01(loadOp.progress / 0.9f);

            if (progressBar != null)
                progressBar.value = progress;

            if (loadingText != null)
                loadingText.text = $"Loading... {(progress * 100):F0}%";

            yield return null;
        }

        _currentLoadedScene = sceneName;

        // Définir la scène comme active (pour que les nouveaux objets y soient créés)
        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid())
        {
            SceneManager.SetActiveScene(loadedScene);
        }

        // Attendre une frame puis configurer l'UI
        yield return null;

        // Rafraîchir l'interaction UI avec le joueur local (si déjà spawné)
        if (VRGameManager.Instance != null)
        {
            VRGameManager.Instance.RefreshUIInteraction();
        }

        // Cacher l'écran de chargement
        if (loadingScreen != null)
            loadingScreen.SetActive(false);

        _isLoading = false;

        if (showDebugLogs)
            Debug.Log($"[Bootstrap] Scene loaded: {sceneName}");
    }

    /// Retourne l'EventSystem persistant (utile pour d'autres scripts)
    public EventSystem GetPersistentEventSystem()
    {
        return _persistentEventSystem;
    }
    
    /// Recharge la scène actuelle.
    public void ReloadCurrentScene()
    {
        if (!string.IsNullOrEmpty(_currentLoadedScene))
        {
            LoadScene(_currentLoadedScene);
        }
    }
    
    /// Retourne le nom de la scène actuellement chargée.
    public string GetCurrentSceneName()
    {
        return _currentLoadedScene;
    }
    
    /// Vérifie si une scène est en cours de chargement.
    public bool IsLoading => _isLoading;
}