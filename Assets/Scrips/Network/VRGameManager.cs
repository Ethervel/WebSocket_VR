using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils; // XROrigin
using UnityEngine.XR.Interaction.Toolkit;

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

    // Local
    private GameObject _localPlayer;
    private XROrigin _localXrOrigin;
    private Transform _localHead;      // Main Camera
    private Transform _localLeftHand;
    private Transform _localRightHand;

    // Remotes
    private readonly Dictionary<string, VRRemotePlayer> _remotePlayers = new Dictionary<string, VRRemotePlayer>();

    // Sync
    private float _syncTimer;
    
    // FIX #1: Prévention Race Condition
    private bool _isSpawning = false;
    
    // FIX #4 & OPTIMIZATION #1: Détection de mouvement
    private Vector3 _lastSyncPosition;
    private Quaternion _lastSyncRotation;
    private Vector3 _lastSyncHeadPos;
    private Quaternion _lastSyncHeadRot;

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

        // FIX #1: Prévention de la race condition sur le spawn
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
        
        // FIX #4: Téléporter les avatars distants quand on change de zone
        TeleportRemotePlayersToCurrentZone(roomType);
    }

    #endregion

    #region Local Player

    void SpawnLocalPlayer(RoomType roomType)
    {
        // FIX #1: Protection supplémentaire
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

        if (localPlayerPrefab == null)
        {
            Debug.LogError("[VRGame] localPlayerPrefab not assigned!");
            return;
        }

        _isSpawning = true;

        GetSpawnPoint(roomType, true, out var position, out var rotation);

        // FIX #5: Instancier à Vector3.zero pour éviter collision CharacterController
        _localPlayer = Instantiate(localPlayerPrefab, Vector3.zero, Quaternion.identity);
        _localPlayer.name = "LocalVRPlayer";
        
        // FIX #5: Désactiver CharacterController temporairement
        var charController = _localPlayer.GetComponent<CharacterController>();
        bool hadCharController = charController != null;
        if (hadCharController)
        {
            charController.enabled = false;
            Debug.Log("[SPAWN FIX] CharacterController désactivé temporairement");
        }
        
        // FIX #5: Positionner après désactivation du CharacterController
        _localPlayer.transform.SetPositionAndRotation(position, rotation);
        Debug.Log($"[SPAWN FIX] Local player positionné à {position}");

        FindVRReferences();
        SetupTeleportation();
        
        // Initialiser les dernières positions pour l'optimisation
        if (_localXrOrigin != null)
        {
            _lastSyncPosition = _localXrOrigin.transform.position;
            _lastSyncRotation = _localXrOrigin.transform.rotation;
        }
        if (_localHead != null)
        {
            _lastSyncHeadPos = _localHead.position;
            _lastSyncHeadRot = _localHead.rotation;
        }
        
        // FIX #5: Réactiver CharacterController
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

        // Head = Main Camera
        var cam = _localPlayer.GetComponentInChildren<Camera>(true);
        if (cam != null) _localHead = cam.transform;

        // Mains : dans ton rig ce sont "Left Controller" / "Right Controller"
        _localLeftHand = FindChildRecursive(_localPlayer.transform, "Left Controller");
        if (_localLeftHand == null) _localLeftHand = FindChildRecursive(_localPlayer.transform, "LeftHand");

        _localRightHand = FindChildRecursive(_localPlayer.transform, "Right Controller");
        if (_localRightHand == null) _localRightHand = FindChildRecursive(_localPlayer.transform, "RightHand");

        Debug.Log($"[VRGame] VR References - XROrigin: {_localXrOrigin != null}, Head: {_localHead != null}, L: {_localLeftHand != null}, R: {_localRightHand != null}");
    }

    Transform FindChildRecursive(Transform parent, string nameContains)
    {
        foreach (Transform child in parent)
        {
            if (child.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return child;

            var result = FindChildRecursive(child, nameContains);
            if (result != null)
                return result;
        }
        return null;
    }

    void SetupTeleportation()
    {
        if (_localPlayer == null) return;

        var interactionManager = FindFirstObjectByType<XRInteractionManager>();
        if (interactionManager == null)
        {
            var managerObj = new GameObject("XR Interaction Manager");
            interactionManager = managerObj.AddComponent<XRInteractionManager>();
        }

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

        // FIX #5: Attendre 1 frame pour être sûr que le controller est bien désactivé
        StartCoroutine(TeleportAfterFrame(position, rotation, characterController));
    }
    
    // FIX #5: Coroutine pour téléporter après désactivation du CharacterController
    private System.Collections.IEnumerator TeleportAfterFrame(Vector3 position, Quaternion rotation, CharacterController controller)
    {
        yield return null; // Attendre 1 frame
        
        _localPlayer.transform.SetPositionAndRotation(position, rotation);
        Debug.Log($"[SPAWN FIX] Local player téléporté à {position}");
        
        yield return null; // Attendre encore 1 frame
        
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

        // FIX #5: Instancier à Vector3.zero pour éviter collision CharacterController
        var go = Instantiate(remotePlayerPrefab, Vector3.zero, Quaternion.identity);
        go.name = $"RemotePlayer_{playerData.playerName}_{playerData.playerId.Substring(0, 6)}";
        
        // FIX #5: Désactiver CharacterController temporairement
        var charController = go.GetComponent<CharacterController>();
        bool hadCharController = charController != null;
        if (hadCharController)
        {
            charController.enabled = false;
        }
        
        // FIX #5: Positionner après désactivation du CharacterController
        go.transform.SetPositionAndRotation(position, rotation);
        Debug.Log($"[SPAWN FIX] Remote player {playerData.playerName} positionné à {position}");

        // Désactiver cam/audio
        foreach (var cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true)) al.enabled = false;

        // Désactiver scripts de contrôle potentiels
        var desktopController = go.GetComponent<DesktopPlayerController>();
        if (desktopController != null) Destroy(desktopController);

        var vrController = go.GetComponent<VRPlayerController>();
        if (vrController != null) Destroy(vrController);

        // FIX #5: Détruire le CharacterController (pas besoin sur remote)
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

        // Ton prefab remote a exactement ces enfants
        remote.head = FindChildRecursive(go.transform, "Head");
        remote.leftHand = FindChildRecursive(go.transform, "LeftHand");
        remote.rightHand = FindChildRecursive(go.transform, "RightHand");

        var nameTag = go.GetComponentInChildren<TMPro.TextMeshPro>(true);
        if (nameTag != null) nameTag.text = playerData.playerName;

        _remotePlayers[playerData.playerId] = remote;

        Debug.Log($"[VRGame] Remote player spawned: {playerData.playerName} ({playerData.playerId})");
        OnRemotePlayerSpawned?.Invoke(playerData.playerId, go);
    }

    void DespawnRemotePlayer(string playerId)
    {
        if (_remotePlayers.TryGetValue(playerId, out var remote))
        {
            if (remote.gameObject != null)
                Destroy(remote.gameObject);

            _remotePlayers.Remove(playerId);
            OnRemotePlayerDespawned?.Invoke(playerId);
        }
    }

    void DespawnAllRemotePlayers()
    {
        foreach (var remote in _remotePlayers.Values)
            if (remote.gameObject != null)
                Destroy(remote.gameObject);

        _remotePlayers.Clear();
    }
    
    // FIX #4: Téléportation visuelle des avatars distants
    void TeleportRemotePlayersToCurrentZone(RoomType roomType)
    {
        // Les joueurs dans une zone différente devraient être cachés ou téléportés
        foreach (var kvp in _remotePlayers)
        {
            var remote = kvp.Value;
            if (remote.gameObject == null) continue;
            
            // Si le joueur distant est dans une zone différente, on le cache
            if (remote.currentRoomType != roomType)
            {
                remote.gameObject.SetActive(false);
            }
            else
            {
                remote.gameObject.SetActive(true);
            }
        }
    }

    #endregion

    #region Network Sync (CORRIGÉ + OPTIMISÉ)

    void SendPositionUpdate()
    {
        if (_localPlayer == null || VRNetworkManager.Instance == null) return;
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom) return;

        // OPTIMIZATION #1: Détection de mouvement - ne sync que si changement significatif
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
        
        // Ne pas envoyer si mouvement insignifiant
        if (posChange < movementThreshold && rotChange < rotationThreshold && !headMoved)
        {
            return;
        }
        
        // Mettre à jour les dernières valeurs
        _lastSyncPosition = originTf.position;
        _lastSyncRotation = originTf.rotation;
        if (_localHead != null)
        {
            _lastSyncHeadPos = _localHead.position;
            _lastSyncHeadRot = _localHead.rotation;
        }

        var data = new VRPositionData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            roomType = VRRoomManager.Instance.CurrentRoomType,

            // Corps (XR Origin) en world
            posX = originTf.position.x,
            posY = originTf.position.y,
            posZ = originTf.position.z,
            rotY = originTf.eulerAngles.y
        };

        // Head local
        if (_localHead != null)
        {
            Vector3 headLocalPos = originTf.InverseTransformPoint(_localHead.position);
            Quaternion headLocalRot = Quaternion.Inverse(originTf.rotation) * _localHead.rotation;

            data.headPosX = headLocalPos.x;
            data.headPosY = headLocalPos.y;
            data.headPosZ = headLocalPos.z;

            data.headRotX = headLocalRot.x;
            data.headRotY = headLocalRot.y;
            data.headRotZ = headLocalRot.z;
            data.headRotW = headLocalRot.w;
        }

        // Hands local
        if (syncHands)
        {
            if (_localLeftHand != null)
            {
                Vector3 p = originTf.InverseTransformPoint(_localLeftHand.position);
                Quaternion r = Quaternion.Inverse(originTf.rotation) * _localLeftHand.rotation;

                data.leftHandPosX = p.x; data.leftHandPosY = p.y; data.leftHandPosZ = p.z;
                data.leftHandRotX = r.x; data.leftHandRotY = r.y; data.leftHandRotZ = r.z; data.leftHandRotW = r.w;
            }

            if (_localRightHand != null)
            {
                Vector3 p = originTf.InverseTransformPoint(_localRightHand.position);
                Quaternion r = Quaternion.Inverse(originTf.rotation) * _localRightHand.rotation;

                data.rightHandPosX = p.x; data.rightHandPosY = p.y; data.rightHandPosZ = p.z;
                data.rightHandRotX = r.x; data.rightHandRotY = r.y; data.rightHandRotZ = r.z; data.rightHandRotW = r.w;
            }
        }

        VRNetworkManager.Instance.Send("vr-position", data);
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
            // Corps en world
            remote.targetPosition = new Vector3(data.posX, data.posY, data.posZ);
            remote.targetRotation = Quaternion.Euler(0f, data.rotY, 0f);

            // Head LOCAL (stockage)
            remote.targetHeadLocalPosition = new Vector3(data.headPosX, data.headPosY, data.headPosZ);
            remote.targetHeadLocalRotation = new Quaternion(data.headRotX, data.headRotY, data.headRotZ, data.headRotW);

            // Hands LOCAL
            if (syncHands)
            {
                remote.targetLeftHandLocalPosition = new Vector3(data.leftHandPosX, data.leftHandPosY, data.leftHandPosZ);
                remote.targetLeftHandLocalRotation = new Quaternion(data.leftHandRotX, data.leftHandRotY, data.leftHandRotZ, data.leftHandRotW);

                remote.targetRightHandLocalPosition = new Vector3(data.rightHandPosX, data.rightHandPosY, data.rightHandPosZ);
                remote.targetRightHandLocalRotation = new Quaternion(data.rightHandRotX, data.rightHandRotY, data.rightHandRotZ, data.rightHandRotW);
            }

            remote.currentRoomType = data.roomType;
            remote.hasReceivedData = true;
            
            // FIX #4: Afficher/cacher selon la zone
            if (VRRoomManager.Instance != null)
            {
                bool sameZone = (data.roomType == VRRoomManager.Instance.CurrentRoomType);
                if (remote.gameObject != null)
                {
                    remote.gameObject.SetActive(sameZone);
                }
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
                
            // Ne pas interpoler si invisible
            if (!remote.gameObject.activeSelf)
                continue;

            // 1) Corps (root) : world
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

            // 2) Tête : LOCAL
            if (remote.head != null)
            {
                remote.head.localPosition = Vector3.Lerp(remote.head.localPosition, remote.targetHeadLocalPosition, t);
                remote.head.localRotation = Quaternion.Slerp(remote.head.localRotation, remote.targetHeadLocalRotation, t);
            }

            // 3) Mains : LOCAL
            if (syncHands)
            {
                if (remote.leftHand != null)
                {
                    remote.leftHand.localPosition = Vector3.Lerp(remote.leftHand.localPosition, remote.targetLeftHandLocalPosition, t);
                    remote.leftHand.localRotation = Quaternion.Slerp(remote.leftHand.localRotation, remote.targetLeftHandLocalRotation, t);
                }

                if (remote.rightHand != null)
                {
                    remote.rightHand.localPosition = Vector3.Lerp(remote.rightHand.localPosition, remote.targetRightHandLocalPosition, t);
                    remote.rightHand.localRotation = Quaternion.Slerp(remote.rightHand.localRotation, remote.targetRightHandLocalRotation, t);
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

    // LOCAL (par rapport au root du remote prefab)
    public Vector3 targetHeadLocalPosition;
    public Quaternion targetHeadLocalRotation;

    public Vector3 targetLeftHandLocalPosition;
    public Quaternion targetLeftHandLocalRotation;

    public Vector3 targetRightHandLocalPosition;
    public Quaternion targetRightHandLocalRotation;

    public bool hasReceivedData;
    public RoomType currentRoomType;
}

[Serializable]
public class VRPositionData
{
    public string roomId;
    public RoomType roomType;

    // Corps (XR Origin) world
    public float posX, posY, posZ;
    public float rotY;

    // Head LOCAL
    public float headPosX, headPosY, headPosZ;
    public float headRotX, headRotY, headRotZ, headRotW;

    // Hands LOCAL
    public float leftHandPosX, leftHandPosY, leftHandPosZ;
    public float leftHandRotX, leftHandRotY, leftHandRotZ, leftHandRotW;

    public float rightHandPosX, rightHandPosY, rightHandPosZ;
    public float rightHandRotX, rightHandRotY, rightHandRotZ, rightHandRotW;
}

#endregion