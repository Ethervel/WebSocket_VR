using UnityEngine;
using UnityEngine.SceneManagement;
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
    
    [Header("Loading UI (Optionnel)")]
    public GameObject loadingScreen;
    public UnityEngine.UI.Slider progressBar;
    public TMPro.TextMeshProUGUI loadingText;
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    
    // État
    private bool _isLoading = false;
    private string _currentLoadedScene = "";
    
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
        
        // Cacher l'écran de chargement
        if (loadingScreen != null)
            loadingScreen.SetActive(false);
        
        _isLoading = false;
        
        if (showDebugLogs)
            Debug.Log($"[Bootstrap] Scene loaded: {sceneName}");
    }
    
    /// Recharge la scène actuelle.
    public void ReloadCurrentScene()
    {
        if (!string.IsNullOrEmpty(_currentLoadedScene))
        {
            LoadScene(_currentLoadedScene);
        }
    }
    
    /// Retourne le nom de la scène actuellement chargée.>
    public string GetCurrentSceneName()
    {
        return _currentLoadedScene;
    }
    
    /// Vérifie si une scène est en cours de chargement.
    public bool IsLoading => _isLoading;
}