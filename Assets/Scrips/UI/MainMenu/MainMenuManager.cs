using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

/// <summary>
/// Gestionnaire du menu principal.
/// Assigne les références UI dans l'Inspector.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    public static MainMenuManager Instance { get; private set; }

    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject optionsPanel;
    public GameObject quitDialog;

    [Header("Boutons - Main Panel")]
    public Button startButton;
    public Button optionsButton;
    public Button quitButton;

    [Header("Boutons - Options Panel")]
    public Button backButton;

    [Header("Boutons - Quit Dialog")]
    public Button quitYesButton;
    public Button quitNoButton;

    [Header("Scene Settings")]
    public string gameSceneName = "Meet";

    [Header("A détruire après chargement")]
    public GameObject[] objectsToDestroy;

    [Header("A réactiver après chargement")]
    public GameObject[] objectsToEnable;

    [Header("A désactiver après chargement (pas détruire)")]
    public GameObject[] objectsToDisable;

    [Header("Auth")]
    [Tooltip("Reference to AuthUI (auto-detected if null)")]
    public AuthUI authUI;

    // Events
    public static event Action OnGameStarting;
    public static event Action OnGameStarted;

    // Auth data from login
    private AuthCompletionData _authData;

    // State
    private bool _isLoading = false;
    private bool _isVRMode = false;
    private LocomotionMediator _playerLocomotionMediator;

    public bool IsVRMode => _isVRMode;
    public bool IsLoading => _isLoading;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DetectVRMode();
    }

    void Start()
    {
        SetupButtons();
        ShowMainPanel();

        // Auto-detect AuthUI if not assigned (include inactive objects)
        if (authUI == null)
        {
            authUI = FindAnyObjectByType<AuthUI>(FindObjectsInactive.Include);
            if (authUI == null)
            {
                Debug.LogWarning("[MainMenu] AuthUI not found - auth will be skipped!");
            }
            else
            {
                Debug.Log($"[MainMenu] Found AuthUI on: {authUI.gameObject.name}");
            }
        }

        // Disable MenuToogle during menu phase if it exists
        var menuToggle = FindAnyObjectByType<VRMenuToggle>();
        if (menuToggle != null)
        {
            menuToggle.enabled = false;
            Debug.Log("[MainMenu] Disabled MenuToggle for menu phase");
        }

        Debug.Log($"[MainMenu] Ready - VR Mode: {_isVRMode}");
    }

    void OnEnable()
    {
        VRGameManager.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        AuthUI.OnAuthComplete += OnAuthComplete;
    }

    void OnDisable()
    {
        VRGameManager.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
        AuthUI.OnAuthComplete -= OnAuthComplete;
    }

    void OnLocalPlayerSpawned(GameObject localPlayer)
    {
        // Disable locomotion when player spawns during menu phase
        if (localPlayer != null && !_isLoading)
        {
            DisablePlayerLocomotion(localPlayer);
        }
    }

    // Cache components to re-enable later
    private UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer _playerBodyTransformer;
    private GameObject _locomotionGameObject;

    void DisablePlayerLocomotion(GameObject player)
    {
        if (player == null) return;

        // Find and disable the entire Locomotion GameObject (contains all locomotion components)
        var locomotionTransform = player.transform.Find("Locomotion");
        if (locomotionTransform != null)
        {
            _locomotionGameObject = locomotionTransform.gameObject;
            _locomotionGameObject.SetActive(false);
            Debug.Log("[MainMenu] Disabled entire Locomotion GameObject for menu");
        }

        // Also disable LocomotionMediator as backup
        _playerLocomotionMediator = player.GetComponentInChildren<LocomotionMediator>(true);
        if (_playerLocomotionMediator != null)
        {
            _playerLocomotionMediator.enabled = false;
            Debug.Log("[MainMenu] Disabled LocomotionMediator for menu");
        }

        // Disable XRBodyTransformer (calls CharacterController.Move)
        _playerBodyTransformer = player.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>(true);
        if (_playerBodyTransformer != null)
        {
            _playerBodyTransformer.enabled = false;
            Debug.Log("[MainMenu] Disabled XRBodyTransformer for menu");
        }

        // Note: XR Interaction Simulator kept enabled for controller tracking in editor
        // Movement in Bootstrap is allowed but will be reset on teleport to Meet
    }

    void EnablePlayerLocomotion(GameObject player)
    {
        if (player == null) return;

        // Re-enable Locomotion GameObject
        if (_locomotionGameObject != null)
        {
            _locomotionGameObject.SetActive(true);
            Debug.Log("[MainMenu] Enabled Locomotion GameObject for game");
        }

        // Re-enable LocomotionMediator
        if (_playerLocomotionMediator != null)
        {
            _playerLocomotionMediator.enabled = true;
            Debug.Log("[MainMenu] Enabled LocomotionMediator for game");
        }

        // Re-enable XRBodyTransformer
        if (_playerBodyTransformer != null)
        {
            _playerBodyTransformer.enabled = true;
            Debug.Log("[MainMenu] Enabled XRBodyTransformer for game");
        }

    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void DetectVRMode()
    {
        var xrSettings = XRGeneralSettings.Instance;
        _isVRMode = xrSettings != null &&
                    xrSettings.Manager != null &&
                    xrSettings.Manager.activeLoader != null;
    }

    void SetupButtons()
    {
        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);

        if (optionsButton != null)
            optionsButton.onClick.AddListener(OnOptionsClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);

        if (quitYesButton != null)
            quitYesButton.onClick.AddListener(OnQuitConfirmed);

        if (quitNoButton != null)
            quitNoButton.onClick.AddListener(OnQuitCancelled);
    }

    // ========== NAVIGATION ==========

    public void ShowMainPanel()
    {
        SetPanel(mainPanel, true);
        SetPanel(optionsPanel, false);
        SetPanel(quitDialog, false);
    }

    public void ShowOptionsPanel()
    {
        SetPanel(mainPanel, false);
        SetPanel(optionsPanel, true);
        SetPanel(quitDialog, false);
    }

    public void ShowQuitDialog()
    {
        SetPanel(quitDialog, true);
    }

    public void HideQuitDialog()
    {
        SetPanel(quitDialog, false);
    }

    void SetPanel(GameObject panel, bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }

    // ========== BUTTON HANDLERS ==========

    void OnStartClicked()
    {
        if (_isLoading) return;
        Debug.Log("[MainMenu] Start clicked - showing auth");

        // Hide main panel
        SetPanel(mainPanel, false);

        // Show auth UI (it will trigger OnAuthComplete when done)
        if (authUI != null)
        {
            authUI.Show();
        }
        else
        {
            // No AuthUI, proceed directly as guest
            Debug.LogWarning("[MainMenu] No AuthUI - proceeding as guest");
            OnAuthComplete(new AuthCompletionData
            {
                IsGuest = true,
                DisplayName = $"Guest-{UnityEngine.Random.Range(1000, 9999)}",
                AvatarConfig = null
            });
        }
    }

    /// <summary>
    /// Called when auth is complete (login, register, or guest).
    /// Proceeds to load the game.
    /// </summary>
    void OnAuthComplete(AuthCompletionData data)
    {
        _authData = data;
        Debug.Log($"[MainMenu] Auth complete - {(data.IsGuest ? "Guest" : "User")}: {data.DisplayName}");

        // Set player name
        if (VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.SetPlayerName(data.DisplayName);
        }

        // Start the game
        StartCoroutine(StartGameSequence());
    }

    void OnOptionsClicked()
    {
        Debug.Log("[MainMenu] Options clicked");
        ShowOptionsPanel();
    }

    void OnQuitClicked()
    {
        Debug.Log("[MainMenu] Quit clicked");
        ShowQuitDialog();
    }

    void OnBackClicked()
    {
        Debug.Log("[MainMenu] Back clicked");
        ShowMainPanel();
    }

    void OnQuitConfirmed()
    {
        Debug.Log("[MainMenu] Quit confirmed");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnQuitCancelled()
    {
        HideQuitDialog();
    }

    // ========== GAME LOADING ==========

    IEnumerator StartGameSequence()
    {
        _isLoading = true;
        OnGameStarting?.Invoke();

        // Afficher loading si BootstrapManager en a un
        if (BootstrapManager.Instance != null && BootstrapManager.Instance.loadingScreen != null)
        {
            BootstrapManager.Instance.loadingScreen.SetActive(true);
        }

        // Cacher le menu
        SetPanel(mainPanel, false);
        SetPanel(optionsPanel, false);
        SetPanel(quitDialog, false);

        yield return null;

        // Attendre connexion serveur (optionnel)
        if (VRNetworkManager.Instance != null)
        {
            float timeout = 5f;
            float elapsed = 0f;

            while (!VRNetworkManager.IsConnected && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // Charger la scène
        if (BootstrapManager.Instance != null)
        {
            BootstrapManager.Instance.LoadScene(gameSceneName);

            while (BootstrapManager.Instance.IsLoading)
            {
                yield return null;
            }
        }

        yield return new WaitForSeconds(0.2f);

        // Nettoyer
        CleanupMenu();

        _isLoading = false;
        OnGameStarted?.Invoke();
        Debug.Log("[MainMenu] Game started");
    }

    void CleanupMenu()
    {
        // Re-enable player locomotion for the game
        var localPlayer = VRGameManager.Instance?.GetLocalPlayer();
        if (localPlayer != null)
        {
            EnablePlayerLocomotion(localPlayer);
        }

        // Re-enable MenuToggle for the game
        var menuToggle = FindAnyObjectByType<VRMenuToggle>();
        if (menuToggle != null)
        {
            menuToggle.enabled = true;
            Debug.Log("[MainMenu] Re-enabled MenuToggle for game");
        }

        // Destroy menu floor (it's in Bootstrap, not needed after loading Meet)
        var menuFloor = GameObject.Find("MenuFloor");
        if (menuFloor != null)
        {
            Destroy(menuFloor);
            Debug.Log("[MainMenu] Destroyed MenuFloor");
        }

        // Destroy MainMenuUI canvas
        var mainMenuUI = GameObject.Find("MainMenuUI");
        if (mainMenuUI != null)
        {
            Destroy(mainMenuUI);
            Debug.Log("[MainMenu] Destroyed MainMenuUI");
        }

        // Réactiver les objets spécifiés
        if (objectsToEnable != null)
        {
            foreach (var obj in objectsToEnable)
            {
                if (obj != null)
                    obj.SetActive(true);
            }
        }

        // Désactiver puis détruire les objets XR avec délai (évite conflits)
        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable)
            {
                if (obj != null)
                {
                    obj.SetActive(false); // Désactiver immédiatement
                    Destroy(obj, 1f); // Détruire après délai
                }
            }
        }

        // Détruire les objets spécifiés
        if (objectsToDestroy != null)
        {
            foreach (var obj in objectsToDestroy)
            {
                if (obj != null)
                    Destroy(obj);
            }
        }

        // Se détruire
        Destroy(gameObject);
    }
}
