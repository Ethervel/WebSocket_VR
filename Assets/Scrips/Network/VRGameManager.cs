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

    [Tooltip("Prefab des joueurs distants (avatar VR)")]
    public GameObject remotePlayerPrefab;

    [Header("Spawn Points - Lobby")]
    public Transform lobbySpawnPoint;

    [Header("Spawn Points - Meeting Room A")]
    public Transform roomASpawnPoint;
    public Transform[] roomAAdditionalSpawns;

    [Header("Spawn Points - Meeting Room B")]
    public Transform roomBSpawnPoint;
    public Transform[] roomBAdditionalSpawns;

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
    [Tooltip("Spawner le joueur local au démarrage")]
    public bool spawnPlayerOnStart = true;

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

    // Cache XRInteractionManager pour éviter FindFirstObjectByType répété et fuites mémoire
    private XRInteractionManager _cachedInteractionManager;

    // Container for detached remote player parts (head/hands) to avoid memory leaks
    // Using a parent container instead of individual DontDestroyOnLoad calls
    private Transform _detachedPartsContainer;

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

    void Start()
    {
        if (spawnPlayerOnStart)
            SpawnLocalPlayer(RoomType.Lobby);
    }

    void OnEnable()
    {
        VRRoomManager.OnRoomCreated += OnRoomEntered;
        VRRoomManager.OnRoomJoined += OnRoomEntered;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnPlayerJoined += OnPlayerJoined;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
        VRRoomManager.OnRoomTypeChanged += OnRoomTypeChanged;
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
    }

    void OnDisable()
    {
        VRRoomManager.OnRoomCreated -= OnRoomEntered;
        VRRoomManager.OnRoomJoined -= OnRoomEntered;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
        VRRoomManager.OnRoomTypeChanged -= OnRoomTypeChanged;
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
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

    void OnRoomTypeChanged(RoomType roomType)
    {
        Debug.Log($"[VRGame] Room type changed to: {roomType}");
        TeleportRemotePlayersToCurrentZone(roomType);
    }

    #endregion

    #region Local Player

    void SpawnLocalPlayer(RoomType roomType)
    {
        if (_isSpawning)
        {
            Debug.LogWarning("[VRGame] Spawn already in progress, ignoring...");
            return;
        }

        if (_localPlayer != null)
        {
            Debug.Log("[VRGame] Local player already exists");
            return;
        }

        // Select prefab based on mode
        GameObject prefabToSpawn = _isDesktopMode ? desktopPlayerPrefab : localPlayerPrefab;

        if (prefabToSpawn == null)
        {
            Debug.LogError($"[VRGame] {(_isDesktopMode ? "desktopPlayerPrefab" : "localPlayerPrefab")} not assigned!");
            return;
        }

        _isSpawning = true;

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

        _localLeftHand = FindChildRecursive(_localPlayer.transform, "Left Controller");
        if (_localLeftHand == null) _localLeftHand = FindChildRecursive(_localPlayer.transform, "LeftHand");

        _localRightHand = FindChildRecursive(_localPlayer.transform, "Right Controller");
        if (_localRightHand == null) _localRightHand = FindChildRecursive(_localPlayer.transform, "RightHand");

        Debug.Log($"[VRGame] VR References - XROrigin: {_localXrOrigin != null}, Head: {_localHead != null}, L: {_localLeftHand != null}, R: {_localRightHand != null}");
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

        // IMPORTANT: Disable XR Interaction Simulator in Desktop mode
        // It captures mouse input for VR controller simulation, preventing normal mouse clicks
        var xrSimulator = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator>();
        if (xrSimulator != null)
        {
            xrSimulator.gameObject.SetActive(false);
            Debug.Log("[VRGame] Disabled XR Interaction Simulator for Desktop mode");
        }

        // Add PhysicsRaycaster to camera for pointer events on 3D objects (whiteboard drawing)
        if (_localHead != null)
        {
            Camera cam = _localHead.GetComponent<Camera>();
            if (cam != null && cam.GetComponent<UnityEngine.EventSystems.PhysicsRaycaster>() == null)
            {
                var physicsRaycaster = cam.gameObject.AddComponent<UnityEngine.EventSystems.PhysicsRaycaster>();
                physicsRaycaster.eventMask = LayerMask.GetMask("Whiteboard"); // Only raycast to whiteboard layer
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

    Transform FindChildRecursive(Transform parent, string nameContains)
    {
        string cleanSearch = nameContains.ToLower().Replace(" ", "");
        
        foreach (Transform child in parent)
        {
            string cleanChildName = child.name.ToLower().Replace(" ", "");
            
            if (cleanChildName.Contains(cleanSearch))
            {
                Debug.Log($"[VRGame] Found '{nameContains}' -> Actual name: '{child.name}'");
                return child;
            }
            
            var result = FindChildRecursive(child, nameContains);
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

        var areas = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>(FindObjectsSortMode.None);
        foreach (var area in areas)
        {
            area.teleportationProvider = teleportProvider;
            area.interactionManager = interactionManager;
        }

        var anchors = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor>(FindObjectsSortMode.None);
        foreach (var anchor in anchors)
        {
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
        if (_isDesktopMode)
        {
            SetupWorldSpaceCanvases(playerCamera);
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
    /// </summary>
    void SetupWorldSpaceCanvases(Camera playerCamera)
    {
        var allCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        int worldSpaceCount = 0;

        foreach (var canvas in allCanvases)
        {
            // RenderMode.WorldSpace = 2
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                canvas.worldCamera = playerCamera;
                worldSpaceCount++;

                // Désactiver TrackedDeviceGraphicRaycaster en mode Desktop (interfère avec souris)
                var trackedRaycaster = canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
                if (trackedRaycaster != null)
                {
                    trackedRaycaster.enabled = false;
                    Debug.Log($"[VRGame] TrackedDeviceGraphicRaycaster désactivé sur '{canvas.name}'");
                }

                // S'assurer qu'il y a un GraphicRaycaster standard pour la souris
                var graphicRaycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
                if (graphicRaycaster == null)
                {
                    graphicRaycaster = canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                    Debug.Log($"[VRGame] GraphicRaycaster ajouté sur '{canvas.name}'");
                }
                graphicRaycaster.enabled = true;

                Debug.Log($"[VRGame] ✅ Canvas WorldSpace '{canvas.name}' → Camera: '{playerCamera.name}'");
            }
        }

        if (worldSpaceCount > 0)
        {
            Debug.Log($"[VRGame] ✅ {worldSpaceCount} Canvas WorldSpace configurés pour mode Desktop");
        }
        else
        {
            Debug.Log("[VRGame] Aucun Canvas WorldSpace trouvé dans la scène");
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
        
        _localPlayer.transform.SetPositionAndRotation(position, rotation);
        Debug.Log($"[SPAWN FIX] Local player téléporté à {position}");
        
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

        if (remotePlayerPrefab == null)
        {
            Debug.LogWarning("[VRGame] remotePlayerPrefab not assigned!");
            return;
        }

        GetSpawnPoint(playerData.roomType, false, out var position, out var rotation);

        var go = Instantiate(remotePlayerPrefab, Vector3.zero, Quaternion.identity);
        go.name = $"RemotePlayer_{playerData.playerName}_{playerData.playerId.Substring(0, 6)}";
        
        var charController = go.GetComponent<CharacterController>();
        bool hadCharController = charController != null;
        if (hadCharController)
        {
            charController.enabled = false;
        }
        
        go.transform.SetPositionAndRotation(position, rotation);
        Debug.Log($"[SPAWN FIX] Remote player {playerData.playerName} positionné à {position}");

        foreach (var cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;

        var vrController = go.GetComponent<VRPlayerController>();
        if (vrController != null) Destroy(vrController);

        if (charController != null)
        {
            Destroy(charController);
            Debug.Log("[SPAWN FIX] CharacterController détruit sur remote player");
        }

        var remote = new VRRemotePlayer
        {
            playerId = playerData.playerId,
            playerName = playerData.playerName,
            gameObject = go,
            targetPosition = position,
            targetRotation = rotation,
            hasReceivedData = false
        };

        remote.head = FindChildRecursive(go.transform, "Head");
        remote.leftHand = FindChildRecursive(go.transform, "LeftHand");
        remote.rightHand = FindChildRecursive(go.transform, "RightHand");

        if (remote.leftHand == null)
        {
            remote.leftHand = FindChildRecursive(go.transform, "Left Controller");
            if (remote.leftHand == null)
                remote.leftHand = FindChildRecursive(go.transform, "LeftHandAnchor");
        }

        if (remote.rightHand == null)
        {
            remote.rightHand = FindChildRecursive(go.transform, "Right Controller");
            if (remote.rightHand == null)
                remote.rightHand = FindChildRecursive(go.transform, "RightHandAnchor");
        }

        // CRITICAL: Détacher tête et mains pour qu'ils suivent les positions world
        // P0 FIX: Parent to persistent container instead of using DontDestroyOnLoad individually
        // This prevents memory leaks - objects are now tracked and cleaned up properly
        if (remote.head != null)
        {
            remote.head.SetParent(_detachedPartsContainer);
            remote.head.name = $"Head_{playerData.playerId.Substring(0, 6)}";
            Debug.Log($"[VRGame] Detached head for {playerData.playerName} (parented to container)");
        }

        if (remote.leftHand != null)
        {
            remote.leftHand.SetParent(_detachedPartsContainer);
            remote.leftHand.name = $"LeftHand_{playerData.playerId.Substring(0, 6)}";
            Debug.Log($"[VRGame] Detached left hand for {playerData.playerName} (parented to container)");
        }

        if (remote.rightHand != null)
        {
            remote.rightHand.SetParent(_detachedPartsContainer);
            remote.rightHand.name = $"RightHand_{playerData.playerId.Substring(0, 6)}";
            Debug.Log($"[VRGame] Detached right hand for {playerData.playerName} (parented to container)");
        }

        var nameTag = go.GetComponentInChildren<TMPro.TextMeshPro>(true);
        if (nameTag != null) nameTag.text = playerData.playerName;

        _remotePlayers[playerData.playerId] = remote;

        Debug.Log($"[VRGame] Remote player spawned: {playerData.playerName} - " +
                  $"Head: {remote.head != null}, LeftHand: {remote.leftHand != null}, RightHand: {remote.rightHand != null}");
        OnRemotePlayerSpawned?.Invoke(playerData.playerId, go);
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
            
            if (remote.gameObject != null)
                Destroy(remote.gameObject);

            _remotePlayers.Remove(playerId);
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
            
            if (remote.gameObject != null)
                Destroy(remote.gameObject);
        }
        _remotePlayers.Clear();
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
        if (msg.type != "vr-position")
            return;

        var data = JsonUtility.FromJson<VRPositionData>(msg.data);

        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;

        if (_remotePlayers.TryGetValue(msg.senderId, out var remote))
        {
            remote.targetPosition = new Vector3(data.posX, data.posY, data.posZ);
            remote.targetRotation = Quaternion.Euler(0f, data.rotY, 0f);

            remote.targetHeadPosition = new Vector3(data.headPosX, data.headPosY, data.headPosZ);
            remote.targetHeadRotation = new Quaternion(data.headRotX, data.headRotY, data.headRotZ, data.headRotW);

            // Check if remote player is in Desktop mode (all hand positions are zero)
            bool remoteIsDesktop = data.leftHandPosX == 0 && data.leftHandPosY == 0 && data.leftHandPosZ == 0 &&
                                   data.rightHandPosX == 0 && data.rightHandPosY == 0 && data.rightHandPosZ == 0 &&
                                   data.leftHandRotW == 0 && data.rightHandRotW == 0;

            if (syncHands && !remoteIsDesktop)
            {
                remote.targetLeftHandPosition = new Vector3(data.leftHandPosX, data.leftHandPosY, data.leftHandPosZ);
                remote.targetLeftHandRotation = new Quaternion(data.leftHandRotX, data.leftHandRotY, data.leftHandRotZ, data.leftHandRotW);

                remote.targetRightHandPosition = new Vector3(data.rightHandPosX, data.rightHandPosY, data.rightHandPosZ);
                remote.targetRightHandRotation = new Quaternion(data.rightHandRotX, data.rightHandRotY, data.rightHandRotZ, data.rightHandRotW);

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

    void InterpolateRemotePlayers()
    {
        float t = Time.deltaTime * interpolationSpeed;

        foreach (var remote in _remotePlayers.Values)
        {
            if (remote.gameObject == null || !remote.hasReceivedData)
                continue;
                
            if (!remote.gameObject.activeSelf)
                continue;

            // Corps : world
            remote.gameObject.transform.position = Vector3.Lerp(
                remote.gameObject.transform.position,
                remote.targetPosition,
                t
            );

            remote.gameObject.transform.rotation = Quaternion.Slerp(
                remote.gameObject.transform.rotation,
                remote.targetRotation,
                t
            );

            // Tête : WORLD
            if (remote.head != null)
            {
                remote.head.position = Vector3.Lerp(
                    remote.head.position,
                    remote.targetHeadPosition,
                    t
                );
                remote.head.rotation = Quaternion.Slerp(
                    remote.head.rotation,
                    remote.targetHeadRotation,
                    t
                );
            }

            // Mains : WORLD
            if (syncHands)
            {
                if (remote.leftHand != null)
                {
                    remote.leftHand.position = Vector3.Lerp(
                        remote.leftHand.position,
                        remote.targetLeftHandPosition,
                        t
                    );
                    remote.leftHand.rotation = Quaternion.Slerp(
                        remote.leftHand.rotation,
                        remote.targetLeftHandRotation,
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
                    remote.rightHand.rotation = Quaternion.Slerp(
                        remote.rightHand.rotation,
                        remote.targetRightHandRotation,
                        t
                    );
                }
            }
        }
    }

    #endregion

    #region Spawn Points

    void GetSpawnPoint(RoomType roomType, bool isLocalPlayer, out Vector3 position, out Quaternion rotation)
    {
        Transform spawnPoint = null;

        switch (roomType)
        {
            case RoomType.Lobby:
                spawnPoint = lobbySpawnPoint;
                break;

            case RoomType.MeetingRoomA:
                spawnPoint = (isLocalPlayer || roomAAdditionalSpawns == null || roomAAdditionalSpawns.Length == 0)
                    ? roomASpawnPoint
                    : roomAAdditionalSpawns[UnityEngine.Random.Range(0, roomAAdditionalSpawns.Length)];
                break;

            case RoomType.MeetingRoomB:
                spawnPoint = (isLocalPlayer || roomBAdditionalSpawns == null || roomBAdditionalSpawns.Length == 0)
                    ? roomBSpawnPoint
                    : roomBAdditionalSpawns[UnityEngine.Random.Range(0, roomBAdditionalSpawns.Length)];
                break;
        }

        if (spawnPoint != null)
        {
            position = spawnPoint.position;
            rotation = spawnPoint.rotation;
        }
        else
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            Debug.LogWarning($"[VRGame] No spawn point found for {roomType}");
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
        var result = new Dictionary<string, GameObject>();
        foreach (var kvp in _remotePlayers)
            if (kvp.Value.gameObject != null)
                result[kvp.Key] = kvp.Value.gameObject;
        return result;
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