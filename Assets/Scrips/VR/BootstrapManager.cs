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
        
        // ✅ FIX: Aussi nettoyer au démarrage au cas où Meet est déjà chargé
        StartCoroutine(CleanupEventSystemsDelayed());
    }
    
    // ✅ NOUVEAU : Nettoyer après un délai pour être sûr que tout est chargé
    IEnumerator CleanupEventSystemsDelayed()
    {
        yield return new WaitForSeconds(1f); // Attendre que tout soit initialisé
        CleanupDuplicateEventSystems();
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
        
        // ✅ FIX: Désactiver l'EventSystem de la scène Bootstrap UNIQUEMENT
        DisableBootstrapSceneEventSystem();
        
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
        
        // ✅ FIX: Nettoyer les EventSystems en double après chargement
        yield return null; // Attendre 1 frame que tout soit initialisé
        CleanupDuplicateEventSystems();
        
        // Cacher l'écran de chargement
        if (loadingScreen != null)
            loadingScreen.SetActive(false);
        
        _isLoading = false;
        
        if (showDebugLogs)
            Debug.Log($"[Bootstrap] Scene loaded: {sceneName}");
    }

    void DisableBootstrapSceneEventSystem()
    {
        var allEventSystems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
        foreach (var es in allEventSystems)
        {
            // Ne désactiver que s'il appartient à la scène Bootstrap (ou DontDestroyOnLoad)
            // Cela protège l'EventSystem de la scène cible si elle est déjà chargée ou si on est en éditeur
            if (es.gameObject.scene == gameObject.scene || es.gameObject.scene.name == "DontDestroyOnLoad")
            {
                if (es.gameObject.activeInHierarchy)
                {
                    if (showDebugLogs) Debug.Log($"[Bootstrap] Désactivation préventive EventSystem Bootstrap: {es.gameObject.name}");
                    es.gameObject.SetActive(false);
                }
            }
        }
    }
    
    /// ✅ Nettoie les EventSystems en double (désactive ceux dans Bootstrap)
    void CleanupDuplicateEventSystems()
    {
        // ✅ FIX: Chercher aussi les inactifs
        var allEventSystems = FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        
        if (allEventSystems.Length <= 1)
        {
            if (allEventSystems.Length > 0 && !allEventSystems[0].gameObject.activeInHierarchy)
            {
                 allEventSystems[0].gameObject.SetActive(true);
            }
            return;
        }
        
        if (showDebugLogs)
            Debug.Log($"[Bootstrap] ⚠️ {allEventSystems.Length} EventSystems détectés, nettoyage...");
        
        UnityEngine.EventSystems.EventSystem keepThis = null;
        Scene activeScene = SceneManager.GetActiveScene();

        // Priorité 1 : Garder celui avec XRUIInputModule (CRITIQUE POUR VR)
        foreach (var es in allEventSystems)
        {
            var xrModule = es.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            if (xrModule != null)
            {
                keepThis = es;
                if (showDebugLogs)
                    Debug.Log($"[Bootstrap] ✅ Garde EventSystem avec XR UI: {es.gameObject.name} (scène: {es.gameObject.scene.name})");
                break;
            }
        }

        // Priorité 2 : Garder celui dans la scène active (Meet)
        if (keepThis == null)
        {
            foreach (var es in allEventSystems)
            {
                if (es.gameObject.scene == activeScene)
                {
                    keepThis = es;
                    if (showDebugLogs)
                        Debug.Log($"[Bootstrap] ✅ Garde EventSystem de la scène active: {es.gameObject.name}");
                    break;
                }
            }
        }
        
        // Priorité 3 : Garder le premier (par sécurité)
        if (keepThis == null)
        {
            keepThis = allEventSystems[0];
            if (showDebugLogs)
                Debug.Log($"[Bootstrap] ✅ Garde le premier EventSystem: {keepThis.gameObject.name}");
        }
        
        // Désactiver (pas détruire) tous les autres et activer le gagnant
        foreach (var es in allEventSystems)
        {
            if (es == keepThis)
            {
                if (!es.gameObject.activeInHierarchy)
                    es.gameObject.SetActive(true);
            }
            else
            {
                if (es.gameObject.activeInHierarchy)
                {
                    if (showDebugLogs)
                        Debug.Log($"[Bootstrap] ❌ Désactive EventSystem: {es.gameObject.name} (scène: {es.gameObject.scene.name})");
                    es.gameObject.SetActive(false);
                }
            }
        }
        
        if (showDebugLogs)
            Debug.Log("[Bootstrap] ✅ Nettoyage EventSystem terminé");
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