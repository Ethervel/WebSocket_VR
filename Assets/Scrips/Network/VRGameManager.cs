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
    private Vector3 _lastSyncLeftHandPos;   // ✅ NOUVEAU
    private Vector3 _lastSyncRightHandPos;  // ✅ NOUVEAU

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

        if (localPlayerPrefab == null)
        {
            Debug.LogError("[VRGame] localPlayerPrefab not assigned!");
            return;
        }

        _isSpawning = true;

        GetSpawnPoint(roomType, true, out var position, out var rotation);

        _localPlayer = Instantiate(localPlayerPrefab, Vector3.zero, Quaternion.identity);
        _localPlayer.name = "LocalVRPlayer";
        
        var charController = _localPlayer.GetComponent<CharacterController>();
        bool hadCharController = charController != null;
        if (hadCharController)
        {
            charController.enabled = false;
            Debug.Log("[SPAWN FIX] CharacterController désactivé temporairement");
        }
        
        _localPlayer.transform.SetPositionAndRotation(position, rotation);
        Debug.Log($"[SPAWN FIX] Local player positionné à {position}");

        FindVRReferences();
        SetupTeleportation();
        
        // ✅ Initialiser toutes les dernières positions
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
        if (_localLeftHand != null)
        {
            _lastSyncLeftHandPos = _localLeftHand.position;
        }
        if (_localRightHand != null)
        {
            _lastSyncRightHandPos = _localRightHand.position;
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

        var desktopController = go.GetComponent<DesktopPlayerController>();
        if (desktopController != null) Destroy(desktopController);

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
        if (remote.head != null)
        {
            remote.head.SetParent(null);
            DontDestroyOnLoad(remote.head.gameObject);
            Debug.Log($"[VRGame] Detached head for {playerData.playerName}");
        }

        if (remote.leftHand != null)
        {
            remote.leftHand.SetParent(null);
            DontDestroyOnLoad(remote.leftHand.gameObject);
            Debug.Log($"[VRGame] Detached left hand for {playerData.playerName}");
        }

        if (remote.rightHand != null)
        {
            remote.rightHand.SetParent(null);
            DontDestroyOnLoad(remote.rightHand.gameObject);
            Debug.Log($"[VRGame] Detached right hand for {playerData.playerName}");
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
        
        // ✅ FIX: Détecter aussi le mouvement des mains !
        bool handsMoved = false;
        if (syncHands && _localLeftHand != null && _localRightHand != null)
        {
            float leftHandPosChange = Vector3.Distance(_lastSyncLeftHandPos, _localLeftHand.position);
            float rightHandPosChange = Vector3.Distance(_lastSyncRightHandPos, _localRightHand.position);
            handsMoved = leftHandPosChange > movementThreshold || rightHandPosChange > movementThreshold;
        }
        
        // ✅ FIX: Ne sync que si AU MOINS UNE partie a bougé (corps, tête, ou mains)
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
        
        // ✅ FIX: Mettre à jour les dernières positions des mains
        if (_localLeftHand != null)
            _lastSyncLeftHandPos = _localLeftHand.position;
        if (_localRightHand != null)
            _lastSyncRightHandPos = _localRightHand.position;

        var data = new VRPositionData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            roomType = VRRoomManager.Instance.CurrentRoomType,

            posX = originTf.position.x,
            posY = originTf.position.y,
            posZ = originTf.position.z,
            rotY = originTf.eulerAngles.y
        };

        // Tête en WORLD
        if (_localHead != null)
        {
            data.headPosX = _localHead.position.x;
            data.headPosY = _localHead.position.y;
            data.headPosZ = _localHead.position.z;

            data.headRotX = _localHead.rotation.x;
            data.headRotY = _localHead.rotation.y;
            data.headRotZ = _localHead.rotation.z;
            data.headRotW = _localHead.rotation.w;
        }

        // Mains en WORLD
        if (syncHands)
        {
            if (_localLeftHand != null)
            {
                data.leftHandPosX = _localLeftHand.position.x;
                data.leftHandPosY = _localLeftHand.position.y;
                data.leftHandPosZ = _localLeftHand.position.z;
                
                data.leftHandRotX = _localLeftHand.rotation.x;
                data.leftHandRotY = _localLeftHand.rotation.y;
                data.leftHandRotZ = _localLeftHand.rotation.z;
                data.leftHandRotW = _localLeftHand.rotation.w;
            }

            if (_localRightHand != null)
            {
                data.rightHandPosX = _localRightHand.position.x;
                data.rightHandPosY = _localRightHand.position.y;
                data.rightHandPosZ = _localRightHand.position.z;
                
                data.rightHandRotX = _localRightHand.rotation.x;
                data.rightHandRotY = _localRightHand.rotation.y;
                data.rightHandRotZ = _localRightHand.rotation.z;
                data.rightHandRotW = _localRightHand.rotation.w;
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
            remote.targetPosition = new Vector3(data.posX, data.posY, data.posZ);
            remote.targetRotation = Quaternion.Euler(0f, data.rotY, 0f);

            remote.targetHeadPosition = new Vector3(data.headPosX, data.headPosY, data.headPosZ);
            remote.targetHeadRotation = new Quaternion(data.headRotX, data.headRotY, data.headRotZ, data.headRotW);

            if (syncHands)
            {
                remote.targetLeftHandPosition = new Vector3(data.leftHandPosX, data.leftHandPosY, data.leftHandPosZ);
                remote.targetLeftHandRotation = new Quaternion(data.leftHandRotX, data.leftHandRotY, data.leftHandRotZ, data.leftHandRotW);

                remote.targetRightHandPosition = new Vector3(data.rightHandPosX, data.rightHandPosY, data.rightHandPosZ);
                remote.targetRightHandRotation = new Quaternion(data.rightHandRotX, data.rightHandRotY, data.rightHandRotZ, data.rightHandRotW);
            }

            remote.currentRoomType = data.roomType;
            remote.hasReceivedData = true;
            
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