using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Management;
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

    // État
    private bool _isLoading = false;
    private string _currentLoadedScene = "";

    // Référence à l'EventSystem persistant
    private EventSystem _persistentEventSystem;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SetupPersistentEventSystem();
    }

    void Start()
    {
        if (loadMainSceneOnStart)
        {
            StartCoroutine(LoadMainSceneDelayed());
        }
    }

    void SetupPersistentEventSystem()
    {
        var allEventSystems = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);

        foreach (var es in allEventSystems)
        {
            var xrModule = es.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            if (xrModule != null || _persistentEventSystem == null)
            {
                _persistentEventSystem = es;
                DontDestroyOnLoad(es.gameObject);

                if (xrModule != null) break;
            }
        }

        if (_persistentEventSystem == null)
        {
            Debug.LogError("[Bootstrap] Aucun EventSystem trouvé dans la scène Bootstrap!");
        }

        SetupDesktopInputModule();
    }

    void SetupDesktopInputModule()
    {
        bool isDesktopMode = false;
        var xrSettings = XRGeneralSettings.Instance;
        if (xrSettings == null || xrSettings.Manager == null || xrSettings.Manager.activeLoader == null)
        {
            isDesktopMode = true;
        }

        if (isDesktopMode && _persistentEventSystem != null)
        {
            var xrModule = _persistentEventSystem.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            if (xrModule != null)
            {
                xrModule.enabled = false;
            }

            var inputModule = _persistentEventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = _persistentEventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }
    }

    IEnumerator LoadMainSceneDelayed()
    {
        yield return new WaitForSeconds(loadDelay);
        LoadScene(mainSceneName);
    }

    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;

        StartCoroutine(LoadSceneAsync(sceneName));
    }

    IEnumerator LoadSceneAsync(string sceneName)
    {
        _isLoading = true;

        if (loadingScreen != null)
            loadingScreen.SetActive(true);

        if (!string.IsNullOrEmpty(_currentLoadedScene))
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(_currentLoadedScene);
            while (unloadOp != null && !unloadOp.isDone)
            {
                yield return null;
            }
        }

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

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid())
        {
            SceneManager.SetActiveScene(loadedScene);
        }

        yield return null;

        if (VRGameManager.Instance != null)
        {
            VRGameManager.Instance.RefreshUIInteraction();
        }

        if (loadingScreen != null)
            loadingScreen.SetActive(false);

        _isLoading = false;
    }

    public EventSystem GetPersistentEventSystem()
    {
        return _persistentEventSystem;
    }

    public void ReloadCurrentScene()
    {
        if (!string.IsNullOrEmpty(_currentLoadedScene))
        {
            LoadScene(_currentLoadedScene);
        }
    }

    public string GetCurrentSceneName()
    {
        return _currentLoadedScene;
    }

    public bool IsLoading => _isLoading;
}
