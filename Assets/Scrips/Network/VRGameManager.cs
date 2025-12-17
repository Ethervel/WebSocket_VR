using System;
using System.Collections.Generic;
using UnityEngine;

// VR player spawning and synchronization manager
// Handles local player spawning, remote player avatars, and real-time VR pose sync (head + hands)
public class VRGameManager : MonoBehaviour
{
    // Singleton instance (one game manager for the whole app)
    public static VRGameManager Instance { get; private set; }
    
    [Header("Player Prefabs")]

    // Local player prefab (XR Rig with controls)
    [Tooltip("Prefab du joueur local (XR Rig)")]
    public GameObject localPlayerPrefab;
    
    // Remote player prefab (VR avatar representation)
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

    // Network synchronization rate (updates per second)
    [Tooltip("Fréquence de synchronisation (updates par seconde)")]
    public float syncRate = 30f;
    
    // Interpolation speed for smooth remote player movement
    [Tooltip("Vitesse d'interpolation des positions distantes")]
    public float interpolationSpeed = 15f;
    
    // Enable/disable hand tracking synchronization
    [Tooltip("Synchroniser les mains des avatars")]
    public bool syncHands = true;
    
    [Header("Spawn Settings")]

    // Automatically spawn local player on scene start
    [Tooltip("Spawner le joueur local au démarrage")]
    public bool spawnPlayerOnStart = true;
    
    // Local player references
    private GameObject _localPlayer;
    private Transform _localHead;
    private Transform _localLeftHand;
    private Transform _localRightHand;
    
    // Remote players dictionary (playerId -> remote player data)
    private Dictionary<string, VRRemotePlayer> _remotePlayers = new Dictionary<string, VRRemotePlayer>();
    
    // Synchronization timer
    private float _syncTimer;
    
    // Public events for external systems
    public static event Action<GameObject> OnLocalPlayerSpawned;
    public static event Action<string, GameObject> OnRemotePlayerSpawned;
    public static event Action<string> OnRemotePlayerDespawned;
    
    void Awake()
    {
        // Enforce singleton pattern
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
        // Spawn local player in lobby at startup if enabled
        if (spawnPlayerOnStart)
        {
            SpawnLocalPlayer(RoomType.Lobby);
        }
    }
    
    void OnEnable()
    {
        // Subscribe to room and network events
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
        // Unsubscribe to prevent memory leaks
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
        // Send position updates only when inside a room
        if (_localPlayer != null && VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            _syncTimer += Time.deltaTime;
            if (_syncTimer >= 1f / syncRate)
            {
                _syncTimer = 0f;
                SendPositionUpdate();
            }
        }
        
        // Smoothly interpolate remote player positions
        InterpolateRemotePlayers();
    }
    
    #region Room Events
    
    // Called when local player creates or joins a room
    void OnRoomEntered(string roomId)
    {
        Debug.Log($"[VRGame] Entered room: {roomId}");
        
        // Ensure local player exists (in case spawnPlayerOnStart is disabled)
        if (_localPlayer == null)
        {
            SpawnLocalPlayer(RoomType.Lobby);
        }
        
        // Do NOT auto-teleport - player stays in current position
        // Teleportation is handled via teleport pads
    }
    
    // Called when local player leaves a room
    void OnRoomLeft()
    {
        Debug.Log("[VRGame] Left room");
        
        // Despawn only remote players, NOT the local player
        DespawnAllRemotePlayers();
    }
    
    // Called when another player joins the room
    void OnPlayerJoined(VRPlayerData player)
    {
        Debug.Log($"[VRGame] Player joined: {player.playerId} ({player.playerName})");
        
        // Don't spawn our own avatar as a remote player
        if (player.playerId == VRNetworkManager.LocalId)
            return;
        
        SpawnRemotePlayer(player);
    }
    
    // Called when another player leaves the room
    void OnPlayerLeft(string playerId)
    {
        Debug.Log($"[VRGame] Player left: {playerId}");
        DespawnRemotePlayer(playerId);
    }
    
    // Called when room type/zone changes
    void OnRoomTypeChanged(RoomType roomType)
    {
        Debug.Log($"[VRGame] Room type changed to: {roomType}");
        // Teleportation is handled by TeleportOnGrab, not here
    }
    
    #endregion
    
    #region Local Player Management
    
    // Spawns the local VR player at the specified room type spawn point
    void SpawnLocalPlayer(RoomType roomType)
    {
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
        
        Vector3 position;
        Quaternion rotation;
        GetSpawnPoint(roomType, true, out position, out rotation);
        
        _localPlayer = Instantiate(localPlayerPrefab, position, rotation);
        _localPlayer.name = "LocalVRPlayer";
        
        // Find VR component references (head, hands)
        FindVRReferences();
        
        Debug.Log($"[VRGame] Local VR player spawned at {position}");
        OnLocalPlayerSpawned?.Invoke(_localPlayer);
    }
    
    // Automatically finds VR components (head camera, hand controllers)
    void FindVRReferences()
    {
        if (_localPlayer == null) return;
        
        // Find XR standard components
        // Camera (Head)
        var cameras = _localPlayer.GetComponentsInChildren<Camera>();
        foreach (var cam in cameras)
        {
            if (cam.CompareTag("MainCamera") || cam.name.Contains("Head") || cam.name.Contains("Camera"))
            {
                _localHead = cam.transform;
                break;
            }
        }
        
        // Fallback: search by name
        if (_localHead == null)
        {
            var headTransform = FindChildRecursive(_localPlayer.transform, "Head");
            if (headTransform == null)
                headTransform = FindChildRecursive(_localPlayer.transform, "Camera");
            _localHead = headTransform;
        }
        
        // Find hand controllers
        _localLeftHand = FindChildRecursive(_localPlayer.transform, "LeftHand");
        if (_localLeftHand == null)
            _localLeftHand = FindChildRecursive(_localPlayer.transform, "Left Controller");
        
        _localRightHand = FindChildRecursive(_localPlayer.transform, "RightHand");
        if (_localRightHand == null)
            _localRightHand = FindChildRecursive(_localPlayer.transform, "Right Controller");
        
        Debug.Log($"[VRGame] VR References found - Head: {_localHead != null}, LeftHand: {_localLeftHand != null}, RightHand: {_localRightHand != null}");
    }
    
    // Recursively searches for a child transform by name
    // Recursively searches for a child transform by name (ignores spaces and case)
    Transform FindChildRecursive(Transform parent, string nameContains)
    {
        // Nettoyer le nom recherché (enlever espaces, mettre en lowercase)
        string cleanSearch = nameContains.ToLower().Replace(" ", "");
        
        foreach (Transform child in parent)
        {
            // Nettoyer le nom de l'enfant aussi
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
    
    // Teleports the local player to a different room type
    public void TeleportLocalPlayer(RoomType roomType)
    {
        if (_localPlayer == null) return;
        
        Vector3 position;
        Quaternion rotation;
        GetSpawnPoint(roomType, true, out position, out rotation);
        
        // Teleport VR player (handle CharacterController if present)
        var characterController = _localPlayer.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
            _localPlayer.transform.position = position;
            _localPlayer.transform.rotation = rotation;
            characterController.enabled = true;
        }
        else
        {
            _localPlayer.transform.position = position;
            _localPlayer.transform.rotation = rotation;
        }
        
        Debug.Log($"[VRGame] Local player teleported to {roomType} at {position}");
    }
    
    #endregion
    
    #region Remote Player Management
    
    // Spawns a remote player avatar for another player in the room
    void SpawnRemotePlayer(VRPlayerData playerData)
    {
        if (_remotePlayers.ContainsKey(playerData.playerId))
            return;
        
        if (remotePlayerPrefab == null)
        {
            Debug.LogWarning("[VRGame] remotePlayerPrefab not assigned!");
            return;
        }
        
        Vector3 position;
        Quaternion rotation;
        GetSpawnPoint(playerData.roomType, false, out position, out rotation);
        
        var go = Instantiate(remotePlayerPrefab, position, rotation);
        go.name = $"RemotePlayer_{playerData.playerName}_{playerData.playerId.Substring(0, 6)}";
        
        // CRITICAL: Disable camera on remote player avatar
        var cameras = go.GetComponentsInChildren<Camera>();
        foreach (var cam in cameras)
        {
            cam.enabled = false;
        }
        
        // Disable AudioListener if present
        var audioListeners = go.GetComponentsInChildren<AudioListener>();
        foreach (var listener in audioListeners)
        {
            listener.enabled = false;
        }
        
        // Remove control scripts (remote avatars are not controllable)
        var desktopController = go.GetComponent<DesktopPlayerController>();
        if (desktopController != null)
        {
            Destroy(desktopController);
        }
        
        var vrController = go.GetComponent<VRPlayerController>();
        if (vrController != null)
        {
            Destroy(vrController);
        }
        
        var charController = go.GetComponent<CharacterController>();
        if (charController != null)
        {
            Destroy(charController);
        }
        
        // Configure remote player data
        var remote = new VRRemotePlayer
        {
            playerId = playerData.playerId,
            playerName = playerData.playerName,
            gameObject = go,
            targetPosition = position,
            targetRotation = rotation,
            hasReceivedData = false
        };
        
        // Find avatar component references
        remote.head = FindChildRecursive(go.transform, "Head");
        remote.leftHand = FindChildRecursive(go.transform, "LeftHand");
        remote.rightHand = FindChildRecursive(go.transform, "RightHand");

        // Fallback search for hand controllers with alternative names
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

        // CRITICAL: Detach head and hands from body hierarchy
        // This ensures they follow network positions exactly without being affected by body rotation
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

        // Debug verification
        Debug.Log($"[VRGame] Remote player spawned: {playerData.playerName} - " +
                $"Head: {remote.head != null}, LeftHand: {remote.leftHand != null}, RightHand: {remote.rightHand != null}");
        
        // Set up name tag above avatar
        var nameTag = go.GetComponentInChildren<TMPro.TextMeshPro>();
        if (nameTag != null)
        {
            nameTag.text = playerData.playerName;
        }
        
        _remotePlayers[playerData.playerId] = remote;
        
        Debug.Log($"[VRGame] Remote player spawned: {playerData.playerName} ({playerData.playerId})");
        OnRemotePlayerSpawned?.Invoke(playerData.playerId, go);
    }
    
    // Removes a remote player avatar from the scene
    void DespawnRemotePlayer(string playerId)
    {
        if (_remotePlayers.TryGetValue(playerId, out var remote))
        {
            // Destroy detached body parts first
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
            
            // Destroy main body
            if (remote.gameObject != null)
            {
                Destroy(remote.gameObject);
            }
            
            _remotePlayers.Remove(playerId);
            Debug.Log($"[VRGame] Remote player despawned: {playerId}");
            OnRemotePlayerDespawned?.Invoke(playerId);
        }
    }
    
    // Removes all remote player avatars from the scene
    void DespawnAllRemotePlayers()
    {
        foreach (var remote in _remotePlayers.Values)
        {
            // Destroy detached parts
            if (remote.head != null)
            {
                Destroy(remote.head.gameObject);
            }
            
            if (remote.leftHand != null)
            {
                Destroy(remote.leftHand.gameObject);
            }
            
            if (remote.rightHand != null)
            {
                Destroy(remote.rightHand.gameObject);
            }
            
            // Destroy body
            if (remote.gameObject != null)
            {
                Destroy(remote.gameObject);
            }
        }
        _remotePlayers.Clear();
        Debug.Log("[VRGame] All remote players despawned");
    }
    
    // Removes all players (local and remote) from the scene
    void DespawnAll()
    {
        // Despawn local player
        if (_localPlayer != null)
        {
            Destroy(_localPlayer);
            _localPlayer = null;
            _localHead = null;
            _localLeftHand = null;
            _localRightHand = null;
        }
        
        // Despawn all remote players
        DespawnAllRemotePlayers();
        
        Debug.Log("[VRGame] All players despawned");
    }
    
    #endregion
    
    #region Network Sync
    
    // Sends local player's VR pose to other players in the room
    void SendPositionUpdate()
    {
        if (_localPlayer == null || VRNetworkManager.Instance == null)
            return;
        
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            return;
        
        var data = new VRPositionData
        {
            roomId = VRRoomManager.Instance.CurrentRoomId,
            roomType = VRRoomManager.Instance.CurrentRoomType,
            
            // XR Rig position and rotation
            posX = _localPlayer.transform.position.x,
            posY = _localPlayer.transform.position.y,
            posZ = _localPlayer.transform.position.z,
            rotY = _localPlayer.transform.eulerAngles.y
        };
        
        // Head tracking data
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
        
        // Hand tracking data
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
    
    // Handles incoming VR position updates from other players
    void HandleNetworkMessage(NetworkMessage msg)
    {
        if (msg.type != "vr-position")
            return;
        
        var data = JsonUtility.FromJson<VRPositionData>(msg.data);
        
        // Verify message is for current room
        if (VRRoomManager.Instance == null || data.roomId != VRRoomManager.Instance.CurrentRoomId)
            return;
        
        // Find corresponding remote player
        if (_remotePlayers.TryGetValue(msg.senderId, out var remote))
        {
            // Update target positions for interpolation
            remote.targetPosition = new Vector3(data.posX, data.posY, data.posZ);
            remote.targetRotation = Quaternion.Euler(0, data.rotY, 0);
            
            // Head target
            remote.targetHeadPosition = new Vector3(data.headPosX, data.headPosY, data.headPosZ);
            remote.targetHeadRotation = new Quaternion(data.headRotX, data.headRotY, data.headRotZ, data.headRotW);
            
            // Hand targets
            if (syncHands)
            {
                remote.targetLeftHandPosition = new Vector3(data.leftHandPosX, data.leftHandPosY, data.leftHandPosZ);
                remote.targetLeftHandRotation = new Quaternion(data.leftHandRotX, data.leftHandRotY, data.leftHandRotZ, data.leftHandRotW);
                
                remote.targetRightHandPosition = new Vector3(data.rightHandPosX, data.rightHandPosY, data.rightHandPosZ);
                remote.targetRightHandRotation = new Quaternion(data.rightHandRotX, data.rightHandRotY, data.rightHandRotZ, data.rightHandRotW);
            }
            
            remote.hasReceivedData = true;
            remote.currentRoomType = data.roomType;
        }
    }
    
    // Smoothly interpolates remote player positions each frame
    void InterpolateRemotePlayers()
    {
        float t = Time.deltaTime * interpolationSpeed;
        
        foreach (var remote in _remotePlayers.Values)
        {
            if (remote.gameObject == null || !remote.hasReceivedData)
                continue;
            
            // Interpolate body position/rotation
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
            
            // Interpolate head
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
            
            // Interpolate hands
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
    
    #region Spawn Point Management
    
    // Returns appropriate spawn point and rotation for a given room type
    void GetSpawnPoint(RoomType roomType, bool isLocalPlayer, out Vector3 position, out Quaternion rotation)
    {
        Transform spawnPoint = null;
        
        switch (roomType)
        {
            case RoomType.Lobby:
                spawnPoint = lobbySpawnPoint;
                break;
                
            case RoomType.MeetingRoomA:
                if (isLocalPlayer || roomAAdditionalSpawns == null || roomAAdditionalSpawns.Length == 0)
                {
                    spawnPoint = roomASpawnPoint;
                }
                else
                {
                    // Random spawn for remote players
                    int index = UnityEngine.Random.Range(0, roomAAdditionalSpawns.Length);
                    spawnPoint = roomAAdditionalSpawns[index];
                }
                break;
                
            case RoomType.MeetingRoomB:
                if (isLocalPlayer || roomBAdditionalSpawns == null || roomBAdditionalSpawns.Length == 0)
                {
                    spawnPoint = roomBSpawnPoint;
                }
                else
                {
                    // Random spawn for remote players
                    int index = UnityEngine.Random.Range(0, roomBAdditionalSpawns.Length);
                    spawnPoint = roomBAdditionalSpawns[index];
                }
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
    
    // Returns the local player GameObject
    public GameObject GetLocalPlayer()
    {
        return _localPlayer;
    }
    
    // Returns a specific remote player GameObject by ID
    public GameObject GetRemotePlayer(string playerId)
    {
        if (_remotePlayers.TryGetValue(playerId, out var remote))
        {
            return remote.gameObject;
        }
        return null;
    }
    
    // Returns all remote players as a dictionary
    public Dictionary<string, GameObject> GetAllRemotePlayers()
    {
        var result = new Dictionary<string, GameObject>();
        foreach (var kvp in _remotePlayers)
        {
            if (kvp.Value.gameObject != null)
            {
                result[kvp.Key] = kvp.Value.gameObject;
            }
        }
        return result;
    }
    
    #endregion
}

#region Helper Classes

// Remote player avatar data for tracking and synchronization
public class VRRemotePlayer
{
    public string playerId;
    public string playerName;
    public GameObject gameObject;
    
    // Avatar component references
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    
    // Target positions for interpolation (body)
    public Vector3 targetPosition;
    public Quaternion targetRotation;
    
    // Target positions for interpolation (head)
    public Vector3 targetHeadPosition;
    public Quaternion targetHeadRotation;
    
    // Target positions for interpolation (left hand)
    public Vector3 targetLeftHandPosition;
    public Quaternion targetLeftHandRotation;
    
    // Target positions for interpolation (right hand)
    public Vector3 targetRightHandPosition;
    public Quaternion targetRightHandRotation;
    
    // True once we've received at least one network update
    public bool hasReceivedData;

    // Current room zone this player is in
    public RoomType currentRoomType;
}

// VR position data packet for network synchronization
[Serializable]
public class VRPositionData
{
    public string roomId;
    public RoomType roomType;
    
    // XR Rig body position and Y rotation
    public float posX, posY, posZ;
    public float rotY;
    
    // Head position and rotation (full quaternion)
    public float headPosX, headPosY, headPosZ;
    public float headRotX, headRotY, headRotZ, headRotW;
    
    // Left hand position and rotation (full quaternion)
    public float leftHandPosX, leftHandPosY, leftHandPosZ;
    public float leftHandRotX, leftHandRotY, leftHandRotZ, leftHandRotW;
    
    // Right hand position and rotation (full quaternion)
    public float rightHandPosX, rightHandPosY, rightHandPosZ;
    public float rightHandRotX, rightHandRotY, rightHandRotZ, rightHandRotW;
}

#endregion