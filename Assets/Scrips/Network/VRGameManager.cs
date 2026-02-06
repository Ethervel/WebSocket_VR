using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils; // XROrigin
using UnityEngine.XR.Management; // XRGeneralSettings pour détection VR

public class VRGameManager : MonoBehaviour
{
    public static VRGameManager Instance { get; private set; }

    [Header("Player Prefabs")]
    [Tooltip("Prefab du joueur local (XR Rig)")]
    public GameObject localPlayerPrefab;

    [Tooltip("Prefab des joueurs distants (avatar VR) - ancien système avec tout intégré")]
    public GameObject remotePlayerPrefab;

    [Header("Remote Player Separate Prefabs (nouveau système)")]
    [Tooltip("Si assignés, ces prefabs seront utilisés à la place du remotePlayerPrefab unique")]
    public GameObject remotePlayerHeadPrefab;
    public GameObject remotePlayerLeftHandPrefab;
    public GameObject remotePlayerRightHandPrefab;

    [Header("Spawn Points")]
    public Transform lobbySpawnPoint;

    [Header("Sync Settings")]
    [Tooltip("Fréquence de synchronisation (updates par seconde)")]
    public float syncRate = 30f;

    [Tooltip("Vitesse d'interpolation des positions distantes")]
    public float interpolationSpeed = 15f;

    [Tooltip("Synchroniser les mains des avatars")]
    public bool syncHands = true;
    
    [Header("Movement Detection (Optimization)")]
    [Tooltip("Seuil de mouvement en mètres pour envoyer une mise à jour")]
    public float movementThreshold = 0.01f;
    
    [Tooltip("Seuil de rotation en degrés pour envoyer une mise à jour")]
    public float rotationThreshold = 1f;

    [Header("Spawn Settings")]
    [Tooltip("Spawner le joueur local au démarrage. Si désactivé, utilise un XR Origin existant dans la scène.")]
    public bool spawnPlayerOnStart = true;

    [Tooltip("Si spawnPlayerOnStart est false, utilise cet XR Origin existant dans la scène au lieu de spawner.")]
    public GameObject existingXROriginInScene;

    [Header("Desktop Mode")]
    [Tooltip("Prefab du joueur Desktop (non-VR)")]
    public GameObject desktopPlayerPrefab;

    // Desktop mode detection
    private bool _isDesktopMode = false;
    public bool IsDesktopMode => _isDesktopMode;

    // Local
    private GameObject _localPlayer;
    private XROrigin _localXrOrigin;
    private Transform _localHead;
    private Transform _localLeftHand;
    private Transform _localRightHand;

    // Remotes
    private readonly Dictionary<string, VRRemotePlayer> _remotePlayers = new Dictionary<string, VRRemotePlayer>();

    // Sync
    private float _syncTimer;
    
    // Prévention Race Condition
    private bool _isSpawning = false;
    
    // Détection de mouvement (optimisation)
    private Vector3 _lastSyncPosition;
    private Quaternion _lastSyncRotation;
    private Vector3 _lastSyncHeadPos;
    private Quaternion _lastSyncHeadRot;
    private Vector3 _lastSyncLeftHandPos;
    private Vector3 _lastSyncRightHandPos;

    // Cache pour éviter allocations GC à chaque sync
    private readonly VRPositionData _cachedPositionData = new VRPositionData();

    // GC FIX: Cache pour la réception des messages (évite new à chaque message reçu)
    private readonly VRPositionData _cachedReceivedPositionData = new VRPositionData();
    private readonly LaserPointerData _cachedReceivedLaserData = new LaserPointerData();

    // GC FIX: Cache pour GetAllRemotePlayers (évite new Dictionary à chaque appel)
    private readonly Dictionary<string, GameObject> _cachedRemotePlayersResult = new Dictionary<string, GameObject>();
    private bool _remotePlayersCacheDirty = true;

    // Cache XRInteractionManager pour éviter FindFirstObjectByType répété et fuites mémoire
    private XRInteractionManager _cachedInteractionManager;

    // P1 FIX: Cache FindObjectsByType results to avoid O(n) scene searches
    private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea[] _cachedTeleportAreas;
    private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor[] _cachedTeleportAnchors;
    private Canvas[] _cachedWorldSpaceCanvases;
    private bool _teleportCacheValid = false;
    private bool _canvasCacheValid = false;

    // Container for detached remote player parts (head/hands) to avoid memory leaks
    // Using a parent container instead of individual DontDestroyOnLoad calls
    private Transform _detachedPartsContainer;

    // MINOR FIX: Constants for layer names to avoid magic strings
    private const string LAYER_WHITEBOARD = "Whiteboard";

    // VR FIX: Cached URP shader - Sprites/Default does NOT support Single Pass Instanced
    private static Shader _cachedURPUnlitShader;

    // GC FIX: Cache materials to avoid creating new ones (key = color + renderQueue hash)
    private static readonly Dictionary<int, Material> _cachedMaterials = new Dictionary<int, Material>();

    // Events
    public static event Action<GameObject> OnLocalPlayerSpawned;
    public static event Action<string, GameObject> OnRemotePlayerSpawned;
    public static event Action<string> OnRemotePlayerDespawned;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // P0 FIX: Create a persistent container for detached remote player parts
        // This prevents memory leaks by keeping all detached objects under a single parent
        // that gets cleaned up properly when remote players leave
        _detachedPartsContainer = new GameObject("DetachedRemotePlayerParts").transform;
        _detachedPartsContainer.SetParent(transform);

        // Detect VR vs Desktop mode
        DetectMode();
    }

    void DetectMode()
    {
        var xrSettings = XRGeneralSettings.Instance;
        _isDesktopMode = xrSettings == null ||
                         xrSettings.Manager == null ||
                         xrSettings.Manager.activeLoader == null;

        Debug.Log($"[VRGame] Mode: {(_isDesktopMode ? "Desktop" : "VR")}");

        // In Desktop mode, unlock cursor for UI interaction initially
        if (_isDesktopMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    /// <summary>
    /// Uses an existing XR Origin in the scene instead of spawning one.
    /// This allows the XR Origin to use its native TrackedPoseDriver without script interference.
    /// </summary>
    void UseExistingXROrigin()
    {
        // Find existing XR Origin
        if (existingXROriginInScene != null)
        {
            _localPlayer = existingXROriginInScene;
        }
        else
        {
            // Auto-find XR Origin in scene
            var xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                _localPlayer = xrOrigin.gameObject;
            }
        }

        if (_localPlayer == null)
        {
            Debug.LogError("[VRGame] No existing XR Origin found in scene! Either assign existingXROriginInScene or enable spawnPlayerOnStart.");
            return;
        }

        Debug.Log($"[VRGame] Using existing XR Origin: {_localPlayer.name}");

        // Don't destroy on load so it persists across scenes
        DontDestroyOnLoad(_localPlayer);

        // Find references (same as spawned player)
        if (_isDesktopMode)
        {
            FindDesktopReferences();
            SetupDesktopInput();
        }
        else
        {
            FindVRReferences();
            SetupTeleportation();
        }
        SetupUIInteraction();

        // Initialize sync positions
        Transform originTf = (_localXrOrigin != null) ? _localXrOrigin.transform : _localPlayer.transform;
        _lastSyncPosition = originTf.position;
        _lastSyncRotation = originTf.rotation;

        if (_localHead != null)
        {
            _lastSyncHeadPos = _localHead.position;
            _lastSyncHeadRot = _localHead.rotation;
        }

        if (!_isDesktopMode)
        {
            if (_localLeftHand != null)
                _lastSyncLeftHandPos = _localLeftHand.position;
            if (_localRightHand != null)
                _lastSyncRightHandPos = _localRightHand.position;
        }

        // Add LaserPointer if needed
        if (_localPlayer.GetComponent<LaserPointer>() == null)
        {
            _localPlayer.AddComponent<LaserPointer>();
            Debug.Log("[VRGame] LaserPointer added to existing XR Origin");
        }

        Debug.Log($"[VRGame] Existing XR Origin configured - Head: {_localHead != null}, LeftHand: {_localLeftHand != null}, RightHand: {_localRightHand != null}");
        OnLocalPlayerSpawned?.Invoke(_localPlayer);
    }

    void Start()
    {
        if (spawnPlayerOnStart)
        {
            // Spawn player immediately in Bootstrap scene
            // This allows VR controllers to work in the main menu
            Debug.Log("[VRGame] Spawning local player immediately in Bootstrap");
            SpawnLocalPlayer(RoomType.Lobby);
        }
        else
        {
            // Use existing XR Origin in scene instead of spawning
            Debug.Log("[VRGame] Using existing XR Origin in scene (spawnPlayerOnStart = false)");
            UseExistingXROrigin();
        }
    }

    void OnEnable()
    {
        VRRoomManager.OnRoomCreated += OnRoomEntered;
        VRRoomManager.OnRoomJoined += OnRoomEntered;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnPlayerJoined += OnPlayerJoined;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
        VRRoomManager.OnAvatarUpdated += OnAvatarUpdated;
        VRRoomManager.OnRoomTypeChanged += OnRoomTypeChanged;
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;

        // P1 FIX: Subscribe to scene loaded event to invalidate caches
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        // FIX: Subscribe to OnSceneReady pour spawner le joueur quand la scène est prête
        BootstrapManager.OnSceneReady += OnMainSceneReady;
    }

    void OnDisable()
    {
        VRRoomManager.OnRoomCreated -= OnRoomEntered;
        VRRoomManager.OnRoomJoined -= OnRoomEntered;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
        VRRoomManager.OnAvatarUpdated -= OnAvatarUpdated;
        VRRoomManager.OnRoomTypeChanged -= OnRoomTypeChanged;
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;

        // P1 FIX: Unsubscribe from scene loaded event
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        // FIX: Unsubscribe from OnSceneReady
        BootstrapManager.OnSceneReady -= OnMainSceneReady;
    }

    // P1 FIX: Invalidate caches when a new scene is loaded
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        _teleportCacheValid = false;
        _canvasCacheValid = false;
        Debug.Log($"[VRGame] P1 FIX: Invalidated caches after scene load: {scene.name}");
    }

    /// <summary>
    /// Appelé quand la scène principale est complètement chargée et prête.
    /// Téléporte le joueur au spawn point de la scène.
    /// </summary>
    void OnMainSceneReady(string sceneName)
    {
        Debug.Log($"[VRGame] Scene '{sceneName}' is ready");

        // P1 FIX: Ensure appropriate quality level for game scenes
        // VR needs at least Medium quality (level 2) for acceptable visuals
        // but we cap at High (level 3) to maintain performance
        EnsureMinimumQualityLevel(2, 4); // Min: Medium, Max: High

        if (_localPlayer != null)
        {
            // Player already exists (spawned in Bootstrap), teleport to new scene's spawn point
            Debug.Log("[VRGame] Teleporting player to scene spawn point");

            // Invalidate caches to find new spawn points
            _teleportCacheValid = false;
            _canvasCacheValid = false;

            // Setup teleportation for the new scene
            SetupTeleportation();

            // Teleport to lobby spawn point
            TeleportLocalPlayer(RoomType.Lobby);
        }
        else if (spawnPlayerOnStart && !_isSpawning)
        {
            // Fallback: spawn player if not already spawned
            Debug.Log("[VRGame] Spawning local player now that scene is ready");
            SpawnLocalPlayer(RoomType.Lobby);
        }
    }

    void Update()
    {
        if (_localPlayer != null && VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            _syncTimer += Time.deltaTime;
            if (_syncTimer >= 1f / syncRate)
            {
                _syncTimer = 0f;
                SendPositionUpdate();
            }
        }

        InterpolateRemotePlayers();
    }

    #region Room Events

    void OnRoomEntered(string roomId)
    {
        Debug.Log($"[VRGame] Entered room: {roomId}");

        if (_localPlayer == null && !_isSpawning)
        {
            SpawnLocalPlayer(RoomType.Lobby);
        }
    }

    void OnRoomLeft()
    {
        Debug.Log("[VRGame] Left room");
        DespawnAllRemotePlayers();
    }

    void OnPlayerJoined(VRPlayerData player)
    {
        Debug.Log($"[VRGame] Player joined: {player.playerId} ({player.playerName})");

        if (player.playerId == VRNetworkManager.LocalId)
            return;

        SpawnRemotePlayer(player);
    }

    void OnPlayerLeft(string playerId)
    {
        Debug.Log($"[VRGame] Player left: {playerId}");
        DespawnRemotePlayer(playerId);
    }

    void OnAvatarUpdated(VRPlayerData playerData)
    {
        Debug.Log($"[VRGame] Avatar updated for: {playerData.playerId}");

        if (!_remotePlayers.TryGetValue(playerData.playerId, out var remote))
            return;

        // Update name
        remote.playerName = playerData.playerName;

        // Update name tag text
        if (remote.nameTag != null)
        {
            var textMesh = remote.nameTag.GetComponent<TMPro.TextMeshPro>();
            if (textMesh != null)
            {
                textMesh.text = playerData.playerName;
            }
        }

        // Update avatar color
        Color newColor = new Color(playerData.colorR, playerData.colorG, playerData.colorB, 1f);
        ApplyAvatarColor(remote, newColor);

        Debug.Log($"[VRGame] Avatar visuals updated: {playerData.playerName}, color: {newColor}");
    }

    void OnRoomTypeChanged(RoomType roomType)
    {
        Debug.Log($"[VRGame] Room type changed to: {roomType}");
        TeleportRemotePlayersToCurrentZone(roomType);
    }

    #endregion

    #region Local Player

    // P0 FIX: Lock object for thread-safe spawn checking
    private readonly object _spawnLock = new object();

    void SpawnLocalPlayer(RoomType roomType)
    {
        // P0 FIX: Atomic check-and-set to prevent race condition
        // The flag must be set BEFORE any checks, then reset if returning early
        lock (_spawnLock)
        {
            if (_isSpawning)
            {
                Debug.LogWarning("[VRGame] Spawn already in progress, ignoring...");
                return;
            }
            _isSpawning = true; // P0 FIX: Set flag IMMEDIATELY inside lock
        }

        if (_localPlayer != null)
        {
            Debug.Log("[VRGame] Local player already exists");
            _isSpawning = false; // P0 FIX: Reset flag on early return
            return;
        }

        // Select prefab based on mode
        GameObject prefabToSpawn = _isDesktopMode ? desktopPlayerPrefab : localPlayerPrefab;

        if (prefabToSpawn == null)
        {
            Debug.LogError($"[VRGame] {(_isDesktopMode ? "desktopPlayerPrefab" : "localPlayerPrefab")} not assigned!");
            _isSpawning = false; // P0 FIX: Reset flag on early return
            return;
        }

        // _isSpawning already set above

        GetSpawnPoint(roomType, true, out var position, out var rotation);

        _localPlayer = Instantiate(prefabToSpawn, Vector3.zero, Quaternion.identity);
        _localPlayer.name = _isDesktopMode ? "LocalDesktopPlayer" : "LocalVRPlayer";
        
        var charController = _localPlayer.GetComponent<CharacterController>();
        bool hadCharController = charController != null;
        if (hadCharController)
        {
            charController.enabled = false;
            Debug.Log("[SPAWN FIX] CharacterController désactivé temporairement");
        }
        
        _localPlayer.transform.SetPositionAndRotation(position, rotation);
        Debug.Log($"[SPAWN FIX] Local player positionné à {position}");

        if (_isDesktopMode)
        {
            FindDesktopReferences();
            SetupDesktopInput();
        }
        else
        {
            FindVRReferences();
            SetupTeleportation();
            // Note: La gravité et les collisions sont gérées par le système XR Interaction Toolkit
            // (GravityProvider + CharacterController) directement dans le prefab
        }
        SetupUIInteraction(); // ✅ FIX: Configurer l'interaction UI après le spawn
        
        // Initialiser toutes les dernières positions
        // Desktop mode uses _localPlayer.transform, VR mode uses _localXrOrigin
        Transform originTf = (_localXrOrigin != null) ? _localXrOrigin.transform : _localPlayer.transform;
        _lastSyncPosition = originTf.position;
        _lastSyncRotation = originTf.rotation;

        if (_localHead != null)
        {
            _lastSyncHeadPos = _localHead.position;
            _lastSyncHeadRot = _localHead.rotation;
        }

        // Only initialize hand positions in VR mode
        if (!_isDesktopMode)
        {
            if (_localLeftHand != null)
            {
                _lastSyncLeftHandPos = _localLeftHand.position;
            }
            if (_localRightHand != null)
            {
                _lastSyncRightHandPos = _localRightHand.position;
            }
        }
        
        if (hadCharController && charController != null)
        {
            charController.enabled = true;
            Debug.Log("[SPAWN FIX] CharacterController réactivé");
        }

        // Add LaserPointer component for presentations
        if (_localPlayer.GetComponent<LaserPointer>() == null)
        {
            _localPlayer.AddComponent<LaserPointer>();
            Debug.Log("[VRGame] LaserPointer added to local player");
        }

        Debug.Log($"[VRGame] Local VR player spawned at {position}");
        OnLocalPlayerSpawned?.Invoke(_localPlayer);

        _isSpawning = false;
    }

    void FindVRReferences()
    {
        if (_localPlayer == null) return;

        _localXrOrigin = _localPlayer.GetComponent<XROrigin>();
        if (_localXrOrigin == null)
            _localXrOrigin = _localPlayer.GetComponentInChildren<XROrigin>(true);

        var cam = _localPlayer.GetComponentInChildren<Camera>(true);
        if (cam != null) _localHead = cam.transform;

        // Try multiple naming conventions for left hand
        _localLeftHand = FindChildRecursive(_localPlayer.transform, "Left Controller");
        if (_localLeftHand == null) _localLeftHand = FindChildRecursive(_localPlayer.transform, "Left Hand");
        if (_localLeftHand == null) _localLeftHand = FindChildRecursive(_localPlayer.transform, "LeftHand");

        // Try multiple naming conventions for right hand
        _localRightHand = FindChildRecursive(_localPlayer.transform, "Right Controller");
        if (_localRightHand == null) _localRightHand = FindChildRecursive(_localPlayer.transform, "Right Hand");
        if (_localRightHand == null) _localRightHand = FindChildRecursive(_localPlayer.transform, "RightHand");

        Debug.Log($"[VRGame] VR References - XROrigin: {_localXrOrigin != null}, Head: {_localHead != null}, L: {_localLeftHand?.name ?? "NULL"}, R: {_localRightHand?.name ?? "NULL"}");
    }

    void FindDesktopReferences()
    {
        if (_localPlayer == null) return;

        // Desktop mode: no XROrigin, no hands
        _localXrOrigin = null;
        _localLeftHand = null;
        _localRightHand = null;

        // Find camera for head tracking
        var cam = _localPlayer.GetComponentInChildren<Camera>(true);
        if (cam != null)
        {
            _localHead = cam.transform;
            Debug.Log($"[VRGame] Desktop References - Head/Camera: {_localHead.name}");
        }
        else
        {
            // Fallback: find Head transform
            _localHead = FindChildRecursive(_localPlayer.transform, "Head");
            Debug.Log($"[VRGame] Desktop References - Head: {(_localHead != null ? _localHead.name : "NOT FOUND")}");
        }
    }

    void SetupDesktopInput()
    {
        // Desktop mode specific setup
        // The DesktopPlayerController handles input, this is for any additional setup

        // P1 FIX: Removed redundant XR Simulator disable - already handled in BootstrapManager.DisableXRSimulatorInVRMode()
        // This was causing unnecessary FindFirstObjectByType calls during setup

        // Add PhysicsRaycaster to camera for pointer events on 3D objects (whiteboard drawing)
        if (_localHead != null)
        {
            Camera cam = _localHead.GetComponent<Camera>();
            if (cam != null && cam.GetComponent<UnityEngine.EventSystems.PhysicsRaycaster>() == null)
            {
                var physicsRaycaster = cam.gameObject.AddComponent<UnityEngine.EventSystems.PhysicsRaycaster>();
                // MINOR FIX: Use constant for layer name
                physicsRaycaster.eventMask = LayerMask.GetMask(LAYER_WHITEBOARD); // Only raycast to whiteboard layer
                Debug.Log("[VRGame] Added PhysicsRaycaster to camera for whiteboard drawing");
            }
        }

        // Add DesktopWhiteboardDrawer for drawing on whiteboards in desktop mode (fallback/legacy)
        if (_localPlayer != null && _localPlayer.GetComponent<DesktopWhiteboardDrawer>() == null)
        {
            var drawer = _localPlayer.AddComponent<DesktopWhiteboardDrawer>();
            Debug.Log("[VRGame] Added DesktopWhiteboardDrawer to local player");
        }

        Debug.Log("[VRGame] Desktop input setup complete");
    }

    // GC FIX: Cached StringBuilder for string operations
    private static readonly System.Text.StringBuilder _stringBuilder = new System.Text.StringBuilder(64);

    Transform FindChildRecursive(Transform parent, string nameContains)
    {
        // GC FIX: Use cached StringBuilder and avoid repeated ToLower/Replace
        _stringBuilder.Clear();
        foreach (char c in nameContains)
        {
            if (c != ' ')
                _stringBuilder.Append(char.ToLowerInvariant(c));
        }
        string cleanSearch = _stringBuilder.ToString();

        return FindChildRecursiveInternal(parent, cleanSearch, nameContains);
    }

    Transform FindChildRecursiveInternal(Transform parent, string cleanSearch, string originalName)
    {
        foreach (Transform child in parent)
        {
            // GC FIX: Build clean name without allocating new strings each recursion
            _stringBuilder.Clear();
            string childName = child.name;
            for (int i = 0; i < childName.Length; i++)
            {
                char c = childName[i];
                if (c != ' ')
                    _stringBuilder.Append(char.ToLowerInvariant(c));
            }

            // Check if contains (manual to avoid string allocation)
            bool contains = false;
            if (_stringBuilder.Length >= cleanSearch.Length)
            {
                for (int i = 0; i <= _stringBuilder.Length - cleanSearch.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < cleanSearch.Length; j++)
                    {
                        if (_stringBuilder[i + j] != cleanSearch[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        contains = true;
                        break;
                    }
                }
            }

            if (contains)
            {
                Debug.Log($"[VRGame] Found '{originalName}' -> Actual name: '{child.name}'");
                return child;
            }

            var result = FindChildRecursiveInternal(child, cleanSearch, originalName);
            if (result != null)
                return result;
        }
        return null;
    }

    void SetupTeleportation()
    {
        if (_localPlayer == null) return;

        // Utiliser le cache pour éviter FindFirstObjectByType répété et fuites mémoire
        if (_cachedInteractionManager == null)
        {
            _cachedInteractionManager = FindFirstObjectByType<XRInteractionManager>();
            if (_cachedInteractionManager == null)
            {
                var managerObj = new GameObject("XR Interaction Manager");
                managerObj.transform.SetParent(transform); // Attacher au manager pour éviter les orphelins
                _cachedInteractionManager = managerObj.AddComponent<XRInteractionManager>();
                Debug.Log("[VRGame] Created and cached XRInteractionManager");
            }
        }
        var interactionManager = _cachedInteractionManager;

        var interactors = _localPlayer.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(true);
        foreach (var interactor in interactors)
            interactor.interactionManager = interactionManager;

        var teleportProvider = _localPlayer.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>(true);
        if (teleportProvider == null)
        {
            Debug.LogWarning("[VRGame] No TeleportationProvider found in player");
            return;
        }

        // P1 FIX: Use cached teleport areas/anchors to avoid O(n) scene searches
        if (!_teleportCacheValid)
        {
            _cachedTeleportAreas = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>(FindObjectsSortMode.None);
            _cachedTeleportAnchors = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor>(FindObjectsSortMode.None);
            _teleportCacheValid = true;
            Debug.Log($"[VRGame] P1 FIX: Cached {_cachedTeleportAreas.Length} teleport areas and {_cachedTeleportAnchors.Length} anchors");
        }

        foreach (var area in _cachedTeleportAreas)
        {
            if (area == null) continue;
            area.teleportationProvider = teleportProvider;
            area.interactionManager = interactionManager;
        }

        foreach (var anchor in _cachedTeleportAnchors)
        {
            if (anchor == null) continue;
            anchor.teleportationProvider = teleportProvider;
            anchor.interactionManager = interactionManager;
        }
    }

    // ✅ FIX: Méthode publique pour forcer la configuration de l'interaction UI (appelée par BootstrapManager après nettoyage)
    public void RefreshUIInteraction()
    {
        SetupUIInteraction();
    }

    // ✅ FIX: Méthode pour configurer l'interaction UI avec le nouveau joueur
    void SetupUIInteraction()
    {
        if (_localHead == null)
        {
            Debug.LogWarning("[VRGame] ⚠️ Impossible de configurer UI : _localHead est null (Joueur pas encore spawné ?)");
            return;
        }

        var playerCamera = _localHead.GetComponent<Camera>();
        if (playerCamera == null)
        {
            Debug.LogWarning("[VRGame] ⚠️ Impossible de trouver la caméra sur _localHead pour l'UI !");
            return;
        }

        // 1. Chercher l'EventSystem actif dans la scène
        EventSystem targetES = null;

        // D'abord vérifier EventSystem.current s'il est actif
        if (EventSystem.current != null && EventSystem.current.gameObject.activeInHierarchy)
        {
            targetES = EventSystem.current;
        }
        else
        {
            // Sinon chercher tous les EventSystems et prendre le premier actif
            var allES = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            foreach (var es in allES)
            {
                if (es.gameObject.activeInHierarchy)
                {
                    targetES = es;
                    break;
                }
            }
        }

        if (targetES != null)
        {
            var xrModule = targetES.GetComponent<XRUIInputModule>();
            if (xrModule != null)
            {
                xrModule.uiCamera = playerCamera;
                Debug.Log($"[VRGame] ✅ UI Interaction LIÉE -> EventSystem: '{targetES.name}' utilise Camera: '{playerCamera.name}'");
            }
            else
            {
                // Si pas de module XR, on essaie de l'ajouter ou de prévenir
                Debug.LogWarning($"[VRGame] ⚠️ L'EventSystem actif '{targetES.name}' n'a pas de XRUIInputModule ! L'interaction VR risque de ne pas marcher.");
            }
        }
        else
        {
            Debug.LogError("[VRGame] ❌ Aucun EventSystem ACTIF trouvé pour configurer l'UI !");
        }

        // 2. ✅ Desktop Mode: Configurer tous les Canvas WorldSpace avec la caméra du joueur
        // P0 FIX: Use coroutine to spread canvas setup across multiple frames
        if (_isDesktopMode)
        {
            StartCoroutine(SetupWorldSpaceCanvasesCoroutine(playerCamera));
        }

        // Relancer aussi le binding du clavier si nécessaire
        var keyboardBinder = FindFirstObjectByType<GlobalKeyboardAutoBind>();
        if (keyboardBinder != null && _localPlayer != null)
        {
            keyboardBinder.BindToPlayer(_localPlayer);
        }
    }

    /// <summary>
    /// Configure tous les Canvas WorldSpace pour utiliser la caméra du joueur Desktop
    /// Nécessaire pour que GraphicRaycaster détecte les clics souris sur UI WorldSpace
    /// P0 FIX: Now a coroutine that spreads work across multiple frames to prevent performance spikes
    /// </summary>
    System.Collections.IEnumerator SetupWorldSpaceCanvasesCoroutine(Camera playerCamera)
    {
        // P1 FIX: Use cached canvas array to avoid O(n) scene searches
        if (!_canvasCacheValid)
        {
            var allCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            // Filter to only WorldSpace canvases
            var worldSpaceList = new System.Collections.Generic.List<Canvas>();
            foreach (var canvas in allCanvases)
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                    worldSpaceList.Add(canvas);
            }
            _cachedWorldSpaceCanvases = worldSpaceList.ToArray();
            _canvasCacheValid = true;
            Debug.Log($"[VRGame] P1 FIX: Cached {_cachedWorldSpaceCanvases.Length} WorldSpace canvases");
        }

        // P0 FIX: Yield after caching to let the frame complete
        yield return null;

        int worldSpaceCount = 0;
        int processedThisFrame = 0;
        const int BATCH_SIZE = 3; // Process 3 canvases per frame to spread the work

        foreach (var canvas in _cachedWorldSpaceCanvases)
        {
            if (canvas == null) continue;

            canvas.worldCamera = playerCamera;
            worldSpaceCount++;

            // Désactiver TrackedDeviceGraphicRaycaster en mode Desktop (interfère avec souris)
            var trackedRaycaster = canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            if (trackedRaycaster != null)
            {
                trackedRaycaster.enabled = false;
            }

            // S'assurer qu'il y a un GraphicRaycaster standard pour la souris
            var graphicRaycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (graphicRaycaster == null)
            {
                graphicRaycaster = canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            graphicRaycaster.enabled = true;

            // P0 FIX: Yield every BATCH_SIZE canvases to spread work across frames
            processedThisFrame++;
            if (processedThisFrame >= BATCH_SIZE)
            {
                processedThisFrame = 0;
                yield return null;
            }
        }

        if (worldSpaceCount > 0)
        {
            Debug.Log($"[VRGame] P0 FIX: {worldSpaceCount} Canvas WorldSpace configurés (spread across frames)");
        }
    }

    public void TeleportLocalPlayer(RoomType roomType)
    {
        if (_localPlayer == null) return;

        GetSpawnPoint(roomType, true, out var position, out var rotation);

        var characterController = _localPlayer.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
            Debug.Log("[SPAWN FIX] CharacterController désactivé pour téléportation");
        }

        StartCoroutine(TeleportAfterFrame(position, rotation, characterController));
    }

    private System.Collections.IEnumerator TeleportAfterFrame(Vector3 position, Quaternion rotation, CharacterController controller)
    {
        yield return null;

        // Use XROrigin.MoveCameraToWorldLocation for proper VR teleportation
        if (_localXrOrigin != null)
        {
            // First set rotation
            _localXrOrigin.transform.rotation = rotation;

            // Use built-in method to move camera to exact world position
            // This handles the camera offset automatically
            if (_localXrOrigin.MoveCameraToWorldLocation(position))
            {
                Debug.Log($"[SPAWN FIX] XROrigin.MoveCameraToWorldLocation -> {position}");
            }
            else
            {
                // Fallback: manual calculation
                Vector3 cameraOffset = _localHead != null
                    ? _localHead.position - _localXrOrigin.transform.position
                    : Vector3.zero;
                cameraOffset.y = 0;
                _localXrOrigin.transform.position = position - cameraOffset;
                Debug.Log($"[SPAWN FIX] Fallback teleport à {position - cameraOffset}");
            }
        }
        else
        {
            _localPlayer.transform.SetPositionAndRotation(position, rotation);
            Debug.Log($"[SPAWN FIX] Local player téléporté à {position}");
        }

        yield return null;

        if (controller != null)
        {
            controller.enabled = true;
            Debug.Log("[SPAWN FIX] CharacterController réactivé après téléportation");
        }
    }

    #endregion

    #region Remote Players

    void SpawnRemotePlayer(VRPlayerData playerData)
    {
        if (_remotePlayers.ContainsKey(playerData.playerId))
            return;

        // Vérifier si on utilise le nouveau système (prefabs séparés) ou l'ancien (prefab unique)
        bool useSeparatePrefabs = remotePlayerHeadPrefab != null ||
                                   remotePlayerLeftHandPrefab != null ||
                                   remotePlayerRightHandPrefab != null;

        if (!useSeparatePrefabs && remotePlayerPrefab == null)
        {
            Debug.LogWarning("[VRGame] Aucun prefab remote player assigné! Assigne soit remotePlayerPrefab, soit les prefabs séparés (Head/LeftHand/RightHand).");
            return;
        }

        GetSpawnPoint(playerData.roomType, false, out var position, out var rotation);

        string shortId = playerData.playerId.Substring(0, Mathf.Min(6, playerData.playerId.Length));
        GameObject bodyGo = null;
        VRRemotePlayer remote;

        if (useSeparatePrefabs)
        {
            // NOUVEAU SYSTÈME: Prefabs séparés pour tête et mains (pas de body)
            Debug.Log($"[VRGame] Utilisation du système de prefabs séparés pour {playerData.playerName}");

            // Créer un GameObject vide comme conteneur pour la référence
            bodyGo = new GameObject($"RemotePlayer_{playerData.playerName}_{shortId}");
            bodyGo.transform.SetPositionAndRotation(position, rotation);

            remote = new VRRemotePlayer
            {
                playerId = playerData.playerId,
                playerName = playerData.playerName,
                gameObject = bodyGo,
                targetPosition = position,
                targetRotation = rotation,
                hasReceivedData = false
            };

            // Instancier la tête comme prefab séparé
            if (remotePlayerHeadPrefab != null)
            {
                var headGo = Instantiate(remotePlayerHeadPrefab, position, Quaternion.identity);
                headGo.name = $"Head_{shortId}";
                headGo.transform.SetParent(_detachedPartsContainer);
                remote.head = headGo.transform;
                // Stocker la rotation du prefab comme offset
                remote.headRotationOffset = remotePlayerHeadPrefab.transform.rotation;
                CleanupRemotePlayerComponents(headGo);
                Debug.Log($"[VRGame] Spawned separate head prefab for {playerData.playerName}, rotation offset: {remote.headRotationOffset.eulerAngles}");
            }

            // Instancier la main gauche comme prefab séparé
            if (remotePlayerLeftHandPrefab != null)
            {
                var leftHandGo = Instantiate(remotePlayerLeftHandPrefab, position, Quaternion.identity);
                leftHandGo.name = $"LeftHand_{shortId}";
                leftHandGo.transform.SetParent(_detachedPartsContainer);
                remote.leftHand = leftHandGo.transform;
                // Stocker la rotation du prefab comme offset
                remote.leftHandRotationOffset = remotePlayerLeftHandPrefab.transform.rotation;
                CleanupRemotePlayerComponents(leftHandGo);
                Debug.Log($"[VRGame] Spawned separate left hand prefab for {playerData.playerName}, rotation offset: {remote.leftHandRotationOffset.eulerAngles}");
            }

            // Instancier la main droite comme prefab séparé
            if (remotePlayerRightHandPrefab != null)
            {
                var rightHandGo = Instantiate(remotePlayerRightHandPrefab, position, Quaternion.identity);
                rightHandGo.name = $"RightHand_{shortId}";
                rightHandGo.transform.SetParent(_detachedPartsContainer);
                remote.rightHand = rightHandGo.transform;
                // Stocker la rotation du prefab comme offset
                remote.rightHandRotationOffset = remotePlayerRightHandPrefab.transform.rotation;
                CleanupRemotePlayerComponents(rightHandGo);
                Debug.Log($"[VRGame] Spawned separate right hand prefab for {playerData.playerName}, rotation offset: {remote.rightHandRotationOffset.eulerAngles}");
            }
        }
        else
        {
            // ANCIEN SYSTÈME: Prefab unique avec tête et mains enfants
            Debug.Log($"[VRGame] Utilisation de l'ancien système (prefab unique) pour {playerData.playerName}");

            bodyGo = Instantiate(remotePlayerPrefab, Vector3.zero, Quaternion.identity);
            bodyGo.name = $"RemotePlayer_{playerData.playerName}_{shortId}";

            var charController = bodyGo.GetComponent<CharacterController>();
            bool hadCharController = charController != null;
            if (hadCharController)
            {
                charController.enabled = false;
            }

            bodyGo.transform.SetPositionAndRotation(position, rotation);

            foreach (var cam in bodyGo.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
            foreach (var al in bodyGo.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;

            var vrController = bodyGo.GetComponent<VRPlayerController>();
            if (vrController != null) Destroy(vrController);

            if (charController != null)
            {
                Destroy(charController);
            }

            remote = new VRRemotePlayer
            {
                playerId = playerData.playerId,
                playerName = playerData.playerName,
                gameObject = bodyGo,
                targetPosition = position,
                targetRotation = rotation,
                hasReceivedData = false
            };

            remote.head = FindChildRecursive(bodyGo.transform, "Head");
            remote.leftHand = FindChildRecursive(bodyGo.transform, "LeftHand");
            remote.rightHand = FindChildRecursive(bodyGo.transform, "RightHand");

            if (remote.leftHand == null)
            {
                remote.leftHand = FindChildRecursive(bodyGo.transform, "Left Controller");
                if (remote.leftHand == null)
                    remote.leftHand = FindChildRecursive(bodyGo.transform, "LeftHandAnchor");
            }

            if (remote.rightHand == null)
            {
                remote.rightHand = FindChildRecursive(bodyGo.transform, "Right Controller");
                if (remote.rightHand == null)
                    remote.rightHand = FindChildRecursive(bodyGo.transform, "RightHandAnchor");
            }

            // CRITICAL: Détacher tête et mains pour qu'ils suivent les positions world
            if (remote.head != null)
            {
                remote.head.SetParent(_detachedPartsContainer);
                remote.head.name = $"Head_{shortId}";
                Debug.Log($"[VRGame] Detached head for {playerData.playerName}");
            }

            if (remote.leftHand != null)
            {
                remote.leftHand.SetParent(_detachedPartsContainer);
                remote.leftHand.name = $"LeftHand_{shortId}";
                Debug.Log($"[VRGame] Detached left hand for {playerData.playerName}");
            }

            if (remote.rightHand != null)
            {
                remote.rightHand.SetParent(_detachedPartsContainer);
                remote.rightHand.name = $"RightHand_{shortId}";
                Debug.Log($"[VRGame] Detached right hand for {playerData.playerName}");
            }
        }

        Debug.Log($"[SPAWN FIX] Remote player {playerData.playerName} positionné à {position}");

        // Create or update name tag above head
        remote.nameTag = CreateOrUpdateNameTag(remote, playerData.playerName);

        // Apply avatar color
        Color avatarColor = new Color(playerData.colorR, playerData.colorG, playerData.colorB, 1f);
        ApplyAvatarColor(remote, avatarColor);

        _remotePlayers[playerData.playerId] = remote;
        _remotePlayersCacheDirty = true; // GC FIX: Mark cache dirty

        Debug.Log($"[VRGame] Remote player spawned: {playerData.playerName} - " +
                  $"Head: {remote.head != null}, LeftHand: {remote.leftHand != null}, RightHand: {remote.rightHand != null}, Color: {avatarColor}");
        OnRemotePlayerSpawned?.Invoke(playerData.playerId, bodyGo);
    }

    /// <summary>
    /// Nettoie les composants indésirables sur un prefab de joueur distant
    /// </summary>
    void CleanupRemotePlayerComponents(GameObject go)
    {
        foreach (var cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;

        var vrController = go.GetComponent<VRPlayerController>();
        if (vrController != null) Destroy(vrController);

        var charController = go.GetComponent<CharacterController>();
        if (charController != null) Destroy(charController);
    }

    void ApplyAvatarColor(VRRemotePlayer remote, Color color)
    {
        // Vérifier si l'avatar utilise le système AvatarColorTarget (ciblé)
        bool usesTargetSystem = false;

        // Check body
        if (remote.gameObject != null && AvatarColorTarget.HasColorTargets(remote.gameObject))
        {
            AvatarColorTarget.ApplyColorToAll(remote.gameObject, color);
            usesTargetSystem = true;
        }

        // Check head
        if (remote.head != null && AvatarColorTarget.HasColorTargets(remote.head.gameObject))
        {
            AvatarColorTarget.ApplyColorToAll(remote.head.gameObject, color);
            usesTargetSystem = true;
        }

        // Check hands
        if (remote.leftHand != null && AvatarColorTarget.HasColorTargets(remote.leftHand.gameObject))
        {
            AvatarColorTarget.ApplyColorToAll(remote.leftHand.gameObject, color);
            usesTargetSystem = true;
        }

        if (remote.rightHand != null && AvatarColorTarget.HasColorTargets(remote.rightHand.gameObject))
        {
            AvatarColorTarget.ApplyColorToAll(remote.rightHand.gameObject, color);
            usesTargetSystem = true;
        }

        // Fallback: si pas de AvatarColorTarget, utiliser l'ancien système (tout colorer)
        if (!usesTargetSystem)
        {
            if (remote.head != null)
                ApplyColorToRenderers(remote.head.gameObject, color);

            if (remote.leftHand != null)
                ApplyColorToRenderers(remote.leftHand.gameObject, color);

            if (remote.rightHand != null)
                ApplyColorToRenderers(remote.rightHand.gameObject, color);

            if (remote.gameObject != null)
                ApplyColorToRenderers(remote.gameObject, color);

            Debug.Log($"[VRGame] Applied avatar color {color} to {remote.playerName} (fallback mode - all renderers)");
        }
        else
        {
            Debug.Log($"[VRGame] Applied avatar color {color} to {remote.playerName} (targeted mode)");
        }
    }

    /// <summary>
    /// VR FIX: Creates a URP Unlit material compatible with Single Pass Instanced rendering.
    /// Sprites/Default does NOT support stereo instancing, causing broken visuals in VR headsets.
    /// GC FIX: Materials are now cached to avoid creating new ones each call.
    /// </summary>
    static Material CreateVRCompatibleUnlitMaterial(Color color, int renderQueue = 3000)
    {
        // GC FIX: Create a hash key from color and renderQueue
        int hashKey = color.GetHashCode() ^ (renderQueue * 397);

        // Check cache first
        if (_cachedMaterials.TryGetValue(hashKey, out var cachedMat) && cachedMat != null)
        {
            return cachedMat;
        }

        if (_cachedURPUnlitShader == null)
            _cachedURPUnlitShader = Shader.Find("Universal Render Pipeline/Unlit");

        if (_cachedURPUnlitShader == null)
        {
            Debug.LogWarning("[VRGame] URP Unlit shader not found, falling back to Sprites/Default");
            var fallback = new Material(Shader.Find("Sprites/Default"));
            fallback.color = color;
            _cachedMaterials[hashKey] = fallback;
            return fallback;
        }

        Material mat = new Material(_cachedURPUnlitShader);
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Surface", 1); // Transparent
        mat.SetFloat("_Blend", 0);   // Alpha blend
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0);
        mat.renderQueue = renderQueue;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        // GC FIX: Cache the material
        _cachedMaterials[hashKey] = mat;
        return mat;
    }

    // IMPORTANT FIX: Cached MaterialPropertyBlock to avoid memory allocations
    // Using MaterialPropertyBlock avoids creating new Material instances (memory leak)
    private static MaterialPropertyBlock _cachedPropertyBlock;

    void ApplyColorToRenderers(GameObject target, Color color)
    {
        // IMPORTANT FIX: Use MaterialPropertyBlock instead of renderer.materials
        // Accessing renderer.materials creates a copy of the materials array each time,
        // causing memory leaks. MaterialPropertyBlock sets per-renderer properties without
        // creating new Material instances.
        if (_cachedPropertyBlock == null)
            _cachedPropertyBlock = new MaterialPropertyBlock();

        // Apply to MeshRenderers
        foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.GetPropertyBlock(_cachedPropertyBlock);
            _cachedPropertyBlock.SetColor("_Color", color);
            _cachedPropertyBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(_cachedPropertyBlock);
        }

        // Apply to SkinnedMeshRenderers
        foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.GetPropertyBlock(_cachedPropertyBlock);
            _cachedPropertyBlock.SetColor("_Color", color);
            _cachedPropertyBlock.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(_cachedPropertyBlock);
        }
    }

    Transform CreateOrUpdateNameTag(VRRemotePlayer remote, string playerName)
    {
        // Check if nameTag already exists
        Transform existingTag = null;
        if (remote.head != null)
        {
            existingTag = remote.head.Find("NameTag");
        }

        if (existingTag != null)
        {
            var tmp = existingTag.GetComponent<TMPro.TextMeshPro>();
            if (tmp != null) tmp.text = playerName;
            return existingTag;
        }

        // Create new name tag
        GameObject nameTagObj = new GameObject("NameTag");

        // Parent to detached container (follows head in Update)
        nameTagObj.transform.SetParent(_detachedPartsContainer);

        // Add TextMeshPro
        var textMesh = nameTagObj.AddComponent<TMPro.TextMeshPro>();
        textMesh.text = playerName;
        textMesh.fontSize = 1.5f;
        textMesh.alignment = TMPro.TextAlignmentOptions.Center;
        textMesh.color = Color.white;

        // Configure RectTransform
        var rectTransform = nameTagObj.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(2f, 0.5f);

        // Create dark background
        GameObject bgObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgObj.name = "Background";
        bgObj.transform.SetParent(nameTagObj.transform, false);
        bgObj.transform.localPosition = new Vector3(0, 0, 0.01f); // Slightly behind text
        bgObj.transform.localScale = new Vector3(0.8f, 0.25f, 1f);

        // Remove collider from background
        var bgCollider = bgObj.GetComponent<Collider>();
        if (bgCollider != null) Destroy(bgCollider);

        // VR FIX: Use URP Unlit instead of Sprites/Default (stereo instancing support)
        var bgRenderer = bgObj.GetComponent<MeshRenderer>();
        if (bgRenderer != null)
        {
            bgRenderer.material = CreateVRCompatibleUnlitMaterial(new Color(0.1f, 0.1f, 0.1f, 0.8f), 3001);
        }

        // Position above head initially
        if (remote.head != null)
        {
            nameTagObj.transform.position = remote.head.position + Vector3.up * 0.55f;
        }

        Debug.Log($"[VRGame] Created name tag for {playerName}");
        return nameTagObj.transform;
    }

    void DespawnRemotePlayer(string playerId)
    {
        if (_remotePlayers.TryGetValue(playerId, out var remote))
        {
            if (remote.head != null)
            {
                Destroy(remote.head.gameObject);
                Debug.Log($"[VRGame] Destroyed detached head for {playerId}");
            }

            if (remote.leftHand != null)
            {
                Destroy(remote.leftHand.gameObject);
                Debug.Log($"[VRGame] Destroyed detached left hand for {playerId}");
            }

            if (remote.rightHand != null)
            {
                Destroy(remote.rightHand.gameObject);
                Debug.Log($"[VRGame] Destroyed detached right hand for {playerId}");
            }

            if (remote.nameTag != null)
            {
                Destroy(remote.nameTag.gameObject);
                Debug.Log($"[VRGame] Destroyed name tag for {playerId}");
            }

            if (remote.laserLine != null)
                Destroy(remote.laserLine.gameObject);
            if (remote.laserDot != null)
                Destroy(remote.laserDot);

            if (remote.gameObject != null)
                Destroy(remote.gameObject);

            _remotePlayers.Remove(playerId);
            _remotePlayersCacheDirty = true; // GC FIX: Mark cache dirty
            OnRemotePlayerDespawned?.Invoke(playerId);
        }
    }

    void DespawnAllRemotePlayers()
    {
        foreach (var remote in _remotePlayers.Values)
        {
            if (remote.head != null)
                Destroy(remote.head.gameObject);

            if (remote.leftHand != null)
                Destroy(remote.leftHand.gameObject);

            if (remote.rightHand != null)
                Destroy(remote.rightHand.gameObject);

            if (remote.laserLine != null)
                Destroy(remote.laserLine.gameObject);
            if (remote.laserDot != null)
                Destroy(remote.laserDot);

            if (remote.gameObject != null)
                Destroy(remote.gameObject);
        }
        _remotePlayers.Clear();
        _remotePlayersCacheDirty = true; // GC FIX: Mark cache dirty
    }
    
    void TeleportRemotePlayersToCurrentZone(RoomType roomType)
    {
        foreach (var kvp in _remotePlayers)
        {
            var remote = kvp.Value;
            if (remote.gameObject == null) continue;
            
            bool sameZone = (remote.currentRoomType == roomType);
            remote.gameObject.SetActive(sameZone);
            
            if (remote.head != null)
                remote.head.gameObject.SetActive(sameZone);
            
            if (remote.leftHand != null)
                remote.leftHand.gameObject.SetActive(sameZone);
            
            if (remote.rightHand != null)
                remote.rightHand.gameObject.SetActive(sameZone);
        }
    }

    #endregion

    #region Network Sync

    void SendPositionUpdate()
    {
        if (_localPlayer == null || VRNetworkManager.Instance == null) return;
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        Transform originTf = (_localXrOrigin != null) ? _localXrOrigin.transform : _localPlayer.transform;
        
        float posChange = Vector3.Distance(_lastSyncPosition, originTf.position);
        float rotChange = Quaternion.Angle(_lastSyncRotation, originTf.rotation);
        
        bool headMoved = false;
        if (_localHead != null)
        {
            float headPosChange = Vector3.Distance(_lastSyncHeadPos, _localHead.position);
            float headRotChange = Quaternion.Angle(_lastSyncHeadRot, _localHead.rotation);
            headMoved = headPosChange > movementThreshold || headRotChange > rotationThreshold;
        }
        
        // FIX: Détecter aussi le mouvement des mains (VR only)
        bool handsMoved = false;
        if (!_isDesktopMode && syncHands && _localLeftHand != null && _localRightHand != null)
        {
            float leftHandPosChange = Vector3.Distance(_lastSyncLeftHandPos, _localLeftHand.position);
            float rightHandPosChange = Vector3.Distance(_lastSyncRightHandPos, _localRightHand.position);
            handsMoved = leftHandPosChange > movementThreshold || rightHandPosChange > movementThreshold;
        }

        // FIX: Ne sync que si AU MOINS UNE partie a bougé (corps, tête, ou mains)
        if (posChange < movementThreshold && rotChange < rotationThreshold && !headMoved && !handsMoved)
        {
            return;
        }
        
        _lastSyncPosition = originTf.position;
        _lastSyncRotation = originTf.rotation;
        if (_localHead != null)
        {
            _lastSyncHeadPos = _localHead.position;
            _lastSyncHeadRot = _localHead.rotation;
        }
        
        //  FIX: Mettre à jour les dernières positions des mains
        if (_localLeftHand != null)
            _lastSyncLeftHandPos = _localLeftHand.position;
        if (_localRightHand != null)
            _lastSyncRightHandPos = _localRightHand.position;

        // Réutilise l'objet caché pour éviter les allocations GC
        _cachedPositionData.roomId = VRRoomManager.Instance.CurrentRoomId;
        _cachedPositionData.roomType = VRRoomManager.Instance.CurrentRoomType;
        _cachedPositionData.isDesktopMode = _isDesktopMode; // BUG FIX: Send explicit mode flag
        
        // ✅ OPTIMIZATION: Arrondir à 3 décimales (mm) pour réduire la taille du JSON
        _cachedPositionData.posX = Round(originTf.position.x);
        _cachedPositionData.posY = Round(originTf.position.y);
        _cachedPositionData.posZ = Round(originTf.position.z);
        _cachedPositionData.rotY = Round(originTf.eulerAngles.y);

        // Tête en WORLD
        if (_localHead != null)
        {
            _cachedPositionData.headPosX = Round(_localHead.position.x);
            _cachedPositionData.headPosY = Round(_localHead.position.y);
            _cachedPositionData.headPosZ = Round(_localHead.position.z);

            _cachedPositionData.headRotX = Round(_localHead.rotation.x);
            _cachedPositionData.headRotY = Round(_localHead.rotation.y);
            _cachedPositionData.headRotZ = Round(_localHead.rotation.z);
            _cachedPositionData.headRotW = Round(_localHead.rotation.w);
        }

        // Mains en WORLD (zeros for Desktop mode to signal no hands)
        if (_isDesktopMode)
        {
            // Desktop mode: send zeros to indicate no hands
            _cachedPositionData.leftHandPosX = 0;
            _cachedPositionData.leftHandPosY = 0;
            _cachedPositionData.leftHandPosZ = 0;
            _cachedPositionData.leftHandRotX = 0;
            _cachedPositionData.leftHandRotY = 0;
            _cachedPositionData.leftHandRotZ = 0;
            _cachedPositionData.leftHandRotW = 0;

            _cachedPositionData.rightHandPosX = 0;
            _cachedPositionData.rightHandPosY = 0;
            _cachedPositionData.rightHandPosZ = 0;
            _cachedPositionData.rightHandRotX = 0;
            _cachedPositionData.rightHandRotY = 0;
            _cachedPositionData.rightHandRotZ = 0;
            _cachedPositionData.rightHandRotW = 0;
        }
        else if (syncHands)
        {
            if (_localLeftHand != null)
            {
                _cachedPositionData.leftHandPosX = Round(_localLeftHand.position.x);
                _cachedPositionData.leftHandPosY = Round(_localLeftHand.position.y);
                _cachedPositionData.leftHandPosZ = Round(_localLeftHand.position.z);

                _cachedPositionData.leftHandRotX = Round(_localLeftHand.rotation.x);
                _cachedPositionData.leftHandRotY = Round(_localLeftHand.rotation.y);
                _cachedPositionData.leftHandRotZ = Round(_localLeftHand.rotation.z);
                _cachedPositionData.leftHandRotW = Round(_localLeftHand.rotation.w);
            }

            if (_localRightHand != null)
            {
                _cachedPositionData.rightHandPosX = Round(_localRightHand.position.x);
                _cachedPositionData.rightHandPosY = Round(_localRightHand.position.y);
                _cachedPositionData.rightHandPosZ = Round(_localRightHand.position.z);

                _cachedPositionData.rightHandRotX = Round(_localRightHand.rotation.x);
                _cachedPositionData.rightHandRotY = Round(_localRightHand.rotation.y);
                _cachedPositionData.rightHandRotZ = Round(_localRightHand.rotation.z);
                _cachedPositionData.rightHandRotW = Round(_localRightHand.rotation.w);
            }
        }

        VRNetworkManager.Instance.Send("vr-position", _cachedPositionData);
    }
    
    // Helper pour arrondir à 3 décimales
    float Round(float value)
    {
        return (float)Math.Round(value, 3);
    }

    void HandleNetworkMessage(NetworkMessage msg)
    {
        if (msg.type == "laser-pointer")
        {
            HandleLaserPointerMessage(msg);
            return;
        }

        if (msg.type != "vr-position")
            return;

        // IMPORTANT FIX: Validate JSON before processing
        if (string.IsNullOrEmpty(msg.data))
        {
            Debug.LogWarning("[VRGame] Empty vr-position data received");
            return;
        }

        // GC FIX: Use cached object with FromJsonOverwrite instead of FromJson (avoids allocation)
        try
        {
            JsonUtility.FromJsonOverwrite(msg.data, _cachedReceivedPositionData);
            if (string.IsNullOrEmpty(_cachedReceivedPositionData.roomId))
            {
                Debug.LogWarning("[VRGame] Invalid vr-position data");
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRGame] JSON parse error for vr-position: {e.Message}");
            return;
        }

        // GC FIX: Use local reference to cached data
        var data = _cachedReceivedPositionData;

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        if (_remotePlayers.TryGetValue(msg.senderId, out var remote))
        {
            // GC FIX: Set struct fields directly instead of new Vector3/Quaternion
            remote.targetPosition.x = data.posX;
            remote.targetPosition.y = data.posY;
            remote.targetPosition.z = data.posZ;
            remote.targetRotation = Quaternion.Euler(0f, data.rotY, 0f);

            remote.targetHeadPosition.x = data.headPosX;
            remote.targetHeadPosition.y = data.headPosY;
            remote.targetHeadPosition.z = data.headPosZ;
            remote.targetHeadRotation.x = data.headRotX;
            remote.targetHeadRotation.y = data.headRotY;
            remote.targetHeadRotation.z = data.headRotZ;
            remote.targetHeadRotation.w = data.headRotW;

            // BUG FIX: Use explicit flag instead of inferring from hand positions
            // Previous check (data.leftHandPosX == 0 && ...) caused false positives if VR player hands were at origin
            bool remoteIsDesktop = data.isDesktopMode;

            if (syncHands && !remoteIsDesktop)
            {
                // GC FIX: Set struct fields directly
                remote.targetLeftHandPosition.x = data.leftHandPosX;
                remote.targetLeftHandPosition.y = data.leftHandPosY;
                remote.targetLeftHandPosition.z = data.leftHandPosZ;
                remote.targetLeftHandRotation.x = data.leftHandRotX;
                remote.targetLeftHandRotation.y = data.leftHandRotY;
                remote.targetLeftHandRotation.z = data.leftHandRotZ;
                remote.targetLeftHandRotation.w = data.leftHandRotW;

                remote.targetRightHandPosition.x = data.rightHandPosX;
                remote.targetRightHandPosition.y = data.rightHandPosY;
                remote.targetRightHandPosition.z = data.rightHandPosZ;
                remote.targetRightHandRotation.x = data.rightHandRotX;
                remote.targetRightHandRotation.y = data.rightHandRotY;
                remote.targetRightHandRotation.z = data.rightHandRotZ;
                remote.targetRightHandRotation.w = data.rightHandRotW;

                // Show hands for VR players
                if (remote.leftHand != null) remote.leftHand.gameObject.SetActive(true);
                if (remote.rightHand != null) remote.rightHand.gameObject.SetActive(true);
            }
            else if (remoteIsDesktop)
            {
                // Hide hands for Desktop players
                if (remote.leftHand != null) remote.leftHand.gameObject.SetActive(false);
                if (remote.rightHand != null) remote.rightHand.gameObject.SetActive(false);
            }

            remote.currentRoomType = data.roomType;
            remote.hasReceivedData = true;
            remote.isDesktopMode = remoteIsDesktop;
            
            if (VRRoomManager.Instance != null)
            {
                bool sameZone = (data.roomType == VRRoomManager.Instance.CurrentRoomType);
                if (remote.gameObject != null)
                    remote.gameObject.SetActive(sameZone);
                
                if (remote.head != null)
                    remote.head.gameObject.SetActive(sameZone);
                
                if (remote.leftHand != null)
                    remote.leftHand.gameObject.SetActive(sameZone);
                
                if (remote.rightHand != null)
                    remote.rightHand.gameObject.SetActive(sameZone);
            }
        }
    }

    // PERF: Threshold for skipping interpolation when already at target
    private const float INTERPOLATION_THRESHOLD_SQR = 0.0001f; // 1cm squared

    void InterpolateRemotePlayers()
    {
        // PERF: Early exit if no remote players
        if (_remotePlayers.Count == 0) return;

        float t = Time.deltaTime * interpolationSpeed;

        foreach (var remote in _remotePlayers.Values)
        {
            if (remote.gameObject == null || !remote.hasReceivedData)
                continue;

            if (!remote.gameObject.activeSelf)
                continue;

            // PERF: Cache transform reference to avoid repeated property access
            Transform bodyTransform = remote.gameObject.transform;

            // Corps : world - PERF: Skip if already at target
            float bodyDistSqr = (bodyTransform.position - remote.targetPosition).sqrMagnitude;
            if (bodyDistSqr > INTERPOLATION_THRESHOLD_SQR)
            {
                bodyTransform.position = Vector3.Lerp(bodyTransform.position, remote.targetPosition, t);
            }

            bodyTransform.rotation = Quaternion.Slerp(bodyTransform.rotation, remote.targetRotation, t);

            // Tête : WORLD (avec offset de rotation du prefab)
            if (remote.head != null)
            {
                remote.head.position = Vector3.Lerp(
                    remote.head.position,
                    remote.targetHeadPosition,
                    t
                );
                // Appliquer l'offset de rotation du prefab
                Quaternion targetHeadRot = remote.targetHeadRotation * remote.headRotationOffset;
                remote.head.rotation = Quaternion.Slerp(
                    remote.head.rotation,
                    targetHeadRot,
                    t
                );

                // Name tag : follow head + billboard
                if (remote.nameTag != null && _localHead != null)
                {
                    remote.nameTag.position = remote.head.position + Vector3.up * 0.55f;

                    // Billboard - each name tag faces the local player's head
                    // TextMeshPro text faces +Z, so forward must point AWAY from viewer
                    Vector3 dirToViewer = remote.nameTag.position - _localHead.position;
                    dirToViewer.y = 0; // Keep upright (no tilt)
                    if (dirToViewer.sqrMagnitude > 0.001f)
                    {
                        remote.nameTag.rotation = Quaternion.LookRotation(dirToViewer);
                    }
                }
            }

            // Mains : WORLD (avec offset de rotation des prefabs)
            if (syncHands)
            {
                if (remote.leftHand != null)
                {
                    remote.leftHand.position = Vector3.Lerp(
                        remote.leftHand.position,
                        remote.targetLeftHandPosition,
                        t
                    );
                    // Appliquer l'offset de rotation du prefab
                    Quaternion targetLeftRot = remote.targetLeftHandRotation * remote.leftHandRotationOffset;
                    remote.leftHand.rotation = Quaternion.Slerp(
                        remote.leftHand.rotation,
                        targetLeftRot,
                        t
                    );
                }

                if (remote.rightHand != null)
                {
                    remote.rightHand.position = Vector3.Lerp(
                        remote.rightHand.position,
                        remote.targetRightHandPosition,
                        t
                    );
                    // Appliquer l'offset de rotation du prefab
                    Quaternion targetRightRot = remote.targetRightHandRotation * remote.rightHandRotationOffset;
                    remote.rightHand.rotation = Quaternion.Slerp(
                        remote.rightHand.rotation,
                        targetRightRot,
                        t
                    );
                }
            }
        }
    }

    #region Laser Pointer (Remote)

    // GC FIX: Cached vectors for laser pointer to avoid allocations
    private Vector3 _cachedLaserOrigin;
    private Vector3 _cachedLaserHitPoint;
    private Color _cachedLaserColor;

    void HandleLaserPointerMessage(NetworkMessage msg)
    {
        if (string.IsNullOrEmpty(msg.data)) return;

        // GC FIX: Use FromJsonOverwrite with cached object
        try
        {
            JsonUtility.FromJsonOverwrite(msg.data, _cachedReceivedLaserData);
            if (string.IsNullOrEmpty(_cachedReceivedLaserData.roomId)) return;
        }
        catch (Exception e)
        {
            Debug.LogError($"[VRGame] JSON parse error for laser-pointer: {e.Message}");
            return;
        }

        var data = _cachedReceivedLaserData;

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        if (!_remotePlayers.TryGetValue(msg.senderId, out var remote))
            return;

        if (data.isActive)
        {
            // Create or show laser visuals
            EnsureRemoteLaserVisuals(remote, data);

            // GC FIX: Reuse cached vectors instead of new
            _cachedLaserOrigin.x = data.originX;
            _cachedLaserOrigin.y = data.originY;
            _cachedLaserOrigin.z = data.originZ;
            _cachedLaserHitPoint.x = data.hitX;
            _cachedLaserHitPoint.y = data.hitY;
            _cachedLaserHitPoint.z = data.hitZ;
            _cachedLaserColor.r = data.colorR;
            _cachedLaserColor.g = data.colorG;
            _cachedLaserColor.b = data.colorB;
            _cachedLaserColor.a = 1f;

            // Update line
            if (remote.laserLine != null)
            {
                remote.laserLine.startColor = _cachedLaserColor;
                remote.laserLine.endColor = _cachedLaserColor;
                remote.laserLine.SetPosition(0, _cachedLaserOrigin);
                remote.laserLine.SetPosition(1, _cachedLaserHitPoint);
                remote.laserLine.enabled = true;
            }

            // Update dot
            if (remote.laserDot != null)
            {
                remote.laserDot.transform.position = _cachedLaserHitPoint;
                remote.laserDot.SetActive(true);

                // GC FIX: Use MaterialPropertyBlock instead of material.color
                var dotRenderer = remote.laserDot.GetComponent<MeshRenderer>();
                if (dotRenderer != null)
                {
                    if (_cachedPropertyBlock == null)
                        _cachedPropertyBlock = new MaterialPropertyBlock();
                    dotRenderer.GetPropertyBlock(_cachedPropertyBlock);
                    _cachedPropertyBlock.SetColor("_BaseColor", _cachedLaserColor);
                    _cachedPropertyBlock.SetColor("_Color", _cachedLaserColor);
                    dotRenderer.SetPropertyBlock(_cachedPropertyBlock);
                }
            }

            remote.laserActive = true;
        }
        else
        {
            // Hide laser
            if (remote.laserLine != null) remote.laserLine.enabled = false;
            if (remote.laserDot != null) remote.laserDot.SetActive(false);
            remote.laserActive = false;
        }
    }

    void EnsureRemoteLaserVisuals(VRRemotePlayer remote, LaserPointerData data)
    {
        if (remote.laserLine != null) return; // Already created

        Color color = new Color(data.colorR, data.colorG, data.colorB, 1f);

        // Create LineRenderer
        GameObject lineObj = new GameObject($"LaserBeam_{remote.playerId.Substring(0, 6)}");
        lineObj.transform.SetParent(_detachedPartsContainer);
        remote.laserLine = lineObj.AddComponent<LineRenderer>();
        remote.laserLine.positionCount = 2;
        remote.laserLine.startWidth = 0.005f;
        remote.laserLine.endWidth = 0.005f;
        // VR FIX: Use URP Unlit instead of Sprites/Default (stereo instancing support)
        remote.laserLine.material = CreateVRCompatibleUnlitMaterial(color);
        remote.laserLine.startColor = color;
        remote.laserLine.endColor = color;
        remote.laserLine.receiveShadows = false;
        remote.laserLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Create dot
        remote.laserDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        remote.laserDot.name = $"LaserDot_{remote.playerId.Substring(0, 6)}";
        remote.laserDot.transform.SetParent(_detachedPartsContainer);
        remote.laserDot.transform.localScale = Vector3.one * 0.03f;

        var col = remote.laserDot.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // VR FIX: Use URP Unlit instead of Sprites/Default (stereo instancing support)
        var renderer = remote.laserDot.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = CreateVRCompatibleUnlitMaterial(color);
            renderer.receiveShadows = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Debug.Log($"[VRGame] Created laser visuals for remote player {remote.playerName}");
    }

    #endregion

    #endregion

    #region Spawn Points

    void GetSpawnPoint(RoomType roomType, bool isLocalPlayer, out Vector3 position, out Quaternion rotation)
    {
        bool gameSceneLoaded = BootstrapManager.Instance != null &&
                               !string.IsNullOrEmpty(BootstrapManager.Instance.GetCurrentSceneName());

        if (gameSceneLoaded)
        {
            position = new Vector3(0f, 1.6f, -10f);
            rotation = Quaternion.identity;
            Debug.Log($"[VRGame] Using Meet spawn: {position}");
            return;
        }

        // Bootstrap only (before Meet loads)
        position = new Vector3(0f, 0.1f, -0.3f);
        rotation = Quaternion.Euler(0f, 180f, 0f);
        Debug.Log($"[VRGame] Using Bootstrap spawn: {position}");
    }

    #endregion

    #region Quality Settings

    /// <summary>
    /// P1 FIX: Ensures quality level is within acceptable range for VR performance
    /// </summary>
    void EnsureMinimumQualityLevel(int minLevel, int maxLevel)
    {
        int currentLevel = QualitySettings.GetQualityLevel();
        int maxAvailable = QualitySettings.names.Length - 1;
        int clampedMin = Mathf.Clamp(minLevel, 0, maxAvailable);
        int clampedMax = Mathf.Clamp(maxLevel, 0, maxAvailable);
        int targetLevel = Mathf.Clamp(currentLevel, clampedMin, clampedMax);

        if (targetLevel != currentLevel)
        {
            QualitySettings.SetQualityLevel(targetLevel, false);
            Debug.Log($"[VRGame] Quality level {currentLevel} -> {targetLevel} ({QualitySettings.names[targetLevel]})");
        }
    }

    #endregion

    #region Public Utilities

    public GameObject GetLocalPlayer() => _localPlayer;

    public GameObject GetRemotePlayer(string playerId)
        => _remotePlayers.TryGetValue(playerId, out var remote) ? remote.gameObject : null;

    /// Returns the head transform of a remote player (for spatial audio positioning)
    public Transform GetRemotePlayerHead(string playerId)
        => _remotePlayers.TryGetValue(playerId, out var remote) ? remote.head : null;

    public Dictionary<string, GameObject> GetAllRemotePlayers()
    {
        // GC FIX: Return cached dictionary, only rebuild when dirty
        if (_remotePlayersCacheDirty)
        {
            _cachedRemotePlayersResult.Clear();
            foreach (var kvp in _remotePlayers)
            {
                if (kvp.Value.gameObject != null)
                    _cachedRemotePlayersResult[kvp.Key] = kvp.Value.gameObject;
            }
            _remotePlayersCacheDirty = false;
        }
        return _cachedRemotePlayersResult;
    }

    #endregion
}

#region Helper Classes

[Serializable]
public class VRRemotePlayer
{
    public string playerId;
    public string playerName;
    public GameObject gameObject;

    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    public Transform nameTag;

    // Rotation offsets from prefabs (pour garder l'angle des prefabs)
    public Quaternion headRotationOffset = Quaternion.identity;
    public Quaternion leftHandRotationOffset = Quaternion.identity;
    public Quaternion rightHandRotationOffset = Quaternion.identity;

    // Laser pointer
    public LineRenderer laserLine;
    public GameObject laserDot;
    public bool laserActive;

    public Vector3 targetPosition;
    public Quaternion targetRotation;

    public Vector3 targetHeadPosition;
    public Quaternion targetHeadRotation;

    public Vector3 targetLeftHandPosition;
    public Quaternion targetLeftHandRotation;

    public Vector3 targetRightHandPosition;
    public Quaternion targetRightHandRotation;

    public bool hasReceivedData;
    public RoomType currentRoomType;
    public bool isDesktopMode;
}

[Serializable]
public class VRPositionData
{
    public string roomId;
    public RoomType roomType;

    // BUG FIX: Explicit desktop mode flag instead of inferring from hand positions
    public bool isDesktopMode;

    public float posX, posY, posZ;
    public float rotY;

    public float headPosX, headPosY, headPosZ;
    public float headRotX, headRotY, headRotZ, headRotW;

    public float leftHandPosX, leftHandPosY, leftHandPosZ;
    public float leftHandRotX, leftHandRotY, leftHandRotZ, leftHandRotW;

    public float rightHandPosX, rightHandPosY, rightHandPosZ;
    public float rightHandRotX, rightHandRotY, rightHandRotZ, rightHandRotW;
}

#endregion