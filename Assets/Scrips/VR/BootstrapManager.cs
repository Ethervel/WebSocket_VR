using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Management;
using System;
using System.Collections;

/// <summary>
/// Gère le chargement des scènes. Cette scène contient tous les managers
/// et charge la scène principale en mode additif.
/// Utilise SceneLoader pour les transitions avec fade.
/// </summary>
public class BootstrapManager : MonoBehaviour
{
    public static BootstrapManager Instance { get; private set; }

    // Event déclenché quand la scène est activée (écran encore noir) - pour téléportation
    public static event Action<string> OnSceneActivated;

    // Event déclenché quand la scène est complètement chargée et visible
    public static event Action<string> OnSceneReady;

    [Header("Scene Settings")]
    [Tooltip("Nom de la scène principale à charger")]
    public string mainSceneName = "Meet";

    [Tooltip("Charger la scène principale au démarrage (false = afficher menu principal d'abord)")]
    public bool loadMainSceneOnStart = false;

    [Tooltip("Délai avant de charger la scène principale (secondes)")]
    public float loadDelay = 0.5f;

    [Header("Launch Loading Screen")]
    [Tooltip("Écran de chargement au lancement (avec LaunchLoadingScreen)")]
    public GameObject launchLoadingScreen;

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

        // Ensure XR is initialized in builds
        EnsureXRInitialized();

        // Disable XR Interaction Simulator in real VR mode
        DisableXRSimulatorInVRMode();

        SetupPersistentEventSystem();
    }

    void EnsureXRInitialized()
    {
        var xrSettings = XRGeneralSettings.Instance;
        if (xrSettings == null || xrSettings.Manager == null)
        {
            Debug.Log("[Bootstrap] XR General Settings not found - desktop mode");
            return;
        }

        if (xrSettings.Manager.activeLoader != null)
        {
            Debug.Log($"[Bootstrap] XR already initialized: {xrSettings.Manager.activeLoader.name}");
            return;
        }

        Debug.Log("[Bootstrap] XR loader not active, attempting manual initialization...");
        xrSettings.Manager.InitializeLoaderSync();

        if (xrSettings.Manager.activeLoader != null)
        {
            xrSettings.Manager.StartSubsystems();
            Debug.Log($"[Bootstrap] XR manually initialized: {xrSettings.Manager.activeLoader.name}");
        }
        else
        {
            Debug.LogWarning("[Bootstrap] XR initialization failed - running in desktop mode.");
        }
    }

    void DisableXRSimulatorInVRMode()
    {
        var xrSettings = XRGeneralSettings.Instance;
        bool isRealVR = xrSettings != null &&
                        xrSettings.Manager != null &&
                        xrSettings.Manager.activeLoader != null;

        if (isRealVR)
        {
            Application.targetFrameRate = 90;
            QualitySettings.vSyncCount = 0;
            Debug.Log("[Bootstrap] Set targetFrameRate=90, vSyncCount=0 for VR");

            var simulator = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator>();
            if (simulator != null)
            {
                simulator.gameObject.SetActive(false);
                Debug.Log("[Bootstrap] XR Interaction Simulator DISABLED");
            }

            var deviceSimulator = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator>();
            if (deviceSimulator != null)
            {
                deviceSimulator.gameObject.SetActive(false);
                Debug.Log("[Bootstrap] XR Device Simulator DISABLED");
            }
        }
        else
        {
            Debug.Log("[Bootstrap] Desktop mode detected");
        }
    }

    void Start()
    {
        // S'abonner aux events
        LaunchLoadingScreen.OnLoadingComplete += OnLaunchLoadingComplete;
        SceneLoader.OnSceneActivated += OnSceneActivatedHandler;
        SceneLoader.OnSceneLoadCompleted += OnSceneLoadCompleted;

        // Cacher le main menu pendant le loading initial
        if (MainMenuManager.Instance != null && MainMenuManager.Instance.mainPanel != null)
        {
            MainMenuManager.Instance.mainPanel.SetActive(false);
        }

        if (loadMainSceneOnStart)
        {
            StartCoroutine(LoadMainSceneDelayed());
        }
    }

    void OnLaunchLoadingComplete()
    {
        LaunchLoadingScreen.OnLoadingComplete -= OnLaunchLoadingComplete;

        // Afficher le main menu
        if (MainMenuManager.Instance != null)
        {
            MainMenuManager.Instance.ShowMainPanel();
        }

        Debug.Log("[Bootstrap] Launch loading complete - showing main menu");
    }

    void OnSceneActivatedHandler(string sceneName)
    {
        _currentLoadedScene = sceneName;

        Debug.Log($"[Bootstrap] Scene '{sceneName}' activated (screen still black) - teleporting player");

        // Déclencher l'event pour téléportation (écran noir)
        OnSceneActivated?.Invoke(sceneName);
    }

    void OnSceneLoadCompleted(string sceneName)
    {
        _currentLoadedScene = sceneName;
        _isLoading = false;

        // Refresh UI interaction
        if (VRGameManager.Instance != null)
        {
            VRGameManager.Instance.RefreshUIInteraction();
        }

        Debug.Log($"[Bootstrap] Scene '{sceneName}' is fully loaded and ready");
        OnSceneReady?.Invoke(sceneName);
    }

    void OnDestroy()
    {
        LaunchLoadingScreen.OnLoadingComplete -= OnLaunchLoadingComplete;
        SceneLoader.OnSceneActivated -= OnSceneActivatedHandler;
        SceneLoader.OnSceneLoadCompleted -= OnSceneLoadCompleted;
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
        var xrSettings = XRGeneralSettings.Instance;
        bool xrLoaderActive = xrSettings != null &&
                              xrSettings.Manager != null &&
                              xrSettings.Manager.activeLoader != null;

        // Vérifier si un HMD est réellement connecté (pas juste OpenXR chargé)
        bool hmdConnected = false;
        if (xrLoaderActive)
        {
            var hmdDevices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevicesAtXRNode(UnityEngine.XR.XRNode.Head, hmdDevices);
            hmdConnected = hmdDevices.Count > 0 && hmdDevices[0].isValid;
        }

        bool isDesktopMode = !xrLoaderActive || !hmdConnected;

        if (isDesktopMode && _persistentEventSystem != null)
        {
            // Désactiver le module XR
            var xrModule = _persistentEventSystem.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            if (xrModule != null)
            {
                xrModule.enabled = false;
                Debug.Log("[Bootstrap] XRUIInputModule désactivé pour le mode Desktop");
            }

            // Activer ou créer le StandaloneInputModule pour le Desktop
            var inputModule = _persistentEventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = _persistentEventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                Debug.Log("[Bootstrap] InputSystemUIInputModule ajouté");
            }

            // S'assurer que le module est activé et configuré
            inputModule.enabled = true;

            // Utiliser les actions par défaut du package Input System UI si non configurées
            if (inputModule.actionsAsset == null)
            {
                // Créer des actions par défaut pour Point et Click
                var defaultActions = UnityEngine.InputSystem.InputActionAsset.FromJson(@"{
                    ""name"": ""UI"",
                    ""maps"": [{
                        ""name"": ""UI"",
                        ""actions"": [
                            { ""name"": ""Point"", ""type"": ""PassThrough"", ""expectedControlType"": ""Vector2"" },
                            { ""name"": ""Click"", ""type"": ""PassThrough"", ""expectedControlType"": ""Button"" },
                            { ""name"": ""ScrollWheel"", ""type"": ""PassThrough"", ""expectedControlType"": ""Vector2"" },
                            { ""name"": ""MiddleClick"", ""type"": ""PassThrough"", ""expectedControlType"": ""Button"" },
                            { ""name"": ""RightClick"", ""type"": ""PassThrough"", ""expectedControlType"": ""Button"" }
                        ],
                        ""bindings"": [
                            { ""path"": ""<Mouse>/position"", ""action"": ""Point"" },
                            { ""path"": ""<Pen>/position"", ""action"": ""Point"" },
                            { ""path"": ""<Touchscreen>/touch*/position"", ""action"": ""Point"" },
                            { ""path"": ""<Mouse>/leftButton"", ""action"": ""Click"" },
                            { ""path"": ""<Pen>/tip"", ""action"": ""Click"" },
                            { ""path"": ""<Touchscreen>/touch*/press"", ""action"": ""Click"" },
                            { ""path"": ""<Mouse>/scroll"", ""action"": ""ScrollWheel"" },
                            { ""path"": ""<Mouse>/middleButton"", ""action"": ""MiddleClick"" },
                            { ""path"": ""<Mouse>/rightButton"", ""action"": ""RightClick"" }
                        ]
                    }]
                }");

                inputModule.actionsAsset = defaultActions;
                inputModule.point = UnityEngine.InputSystem.InputActionReference.Create(defaultActions.FindAction("UI/Point"));
                inputModule.leftClick = UnityEngine.InputSystem.InputActionReference.Create(defaultActions.FindAction("UI/Click"));
                inputModule.scrollWheel = UnityEngine.InputSystem.InputActionReference.Create(defaultActions.FindAction("UI/ScrollWheel"));
                inputModule.middleClick = UnityEngine.InputSystem.InputActionReference.Create(defaultActions.FindAction("UI/MiddleClick"));
                inputModule.rightClick = UnityEngine.InputSystem.InputActionReference.Create(defaultActions.FindAction("UI/RightClick"));

                Debug.Log("[Bootstrap] Actions UI par défaut configurées pour InputSystemUIInputModule");
            }

            Debug.Log("[Bootstrap] Mode Desktop configuré avec InputSystemUIInputModule");
        }
    }

    IEnumerator LoadMainSceneDelayed()
    {
        yield return new WaitForSeconds(loadDelay);
        LoadScene(mainSceneName);
    }

    /// <summary>
    /// Charge une scène avec transition fade via SceneLoader.
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (_isLoading) return;

        _isLoading = true;

        // Utiliser SceneLoader (avec fade)
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadNewScene(sceneName);
        }
        else
        {
            // Fallback sans SceneLoader
            Debug.LogWarning("[Bootstrap] SceneLoader not found - loading without fade");
            StartCoroutine(LoadSceneFallback(sceneName));
        }
    }

    /// <summary>
    /// Fallback si SceneLoader n'existe pas.
    /// </summary>
    IEnumerator LoadSceneFallback(string sceneName)
    {
        // Décharger scène actuelle
        if (!string.IsNullOrEmpty(_currentLoadedScene))
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(_currentLoadedScene);
            while (unloadOp != null && !unloadOp.isDone)
                yield return null;
        }

        // Charger nouvelle scène
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        while (!loadOp.isDone)
            yield return null;

        // Activer la scène
        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        if (loadedScene.IsValid())
            SceneManager.SetActiveScene(loadedScene);

        _currentLoadedScene = sceneName;
        _isLoading = false;

        yield return null;

        if (VRGameManager.Instance != null)
            VRGameManager.Instance.RefreshUIInteraction();

        Debug.Log($"[Bootstrap] Scene '{sceneName}' loaded (fallback)");
        OnSceneReady?.Invoke(sceneName);
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
