using System;
using System.Collections.Generic;
using UnityEngine;

// VR meeting room manager
// Handles room lifecycle (create, join, leave), player tracking, and room discovery
// Works in conjunction with VRNetworkManager for WebSocket communication
public class VRRoomManager : MonoBehaviour
{
    // Singleton instance (one room manager for the whole app)
    public static VRRoomManager Instance { get; private set; }

    [Header("Room Settings")]

    // Maximum number of players allowed per room
    [Tooltip("Maximum number of players per room")]
    public int maxPlayersPerRoom = 10;

    // Timeout duration for inactive rooms (currently unused)
    [Tooltip("Time before an inactive room should be removed (seconds)")]
    public float roomTimeoutDuration = 300f;

    // Current room ID (6-character code)
    public string CurrentRoomId { get; private set; }
    
    // Current room name (custom or auto-generated)
    public string CurrentRoomName { get; private set; }

    // True when the local player is inside a room
    public bool IsInRoom { get; private set; }

    // True if local player created this room
    public bool IsHost { get; private set; }

    // Current zone/area within the room
    public RoomType CurrentRoomType { get; private set; } = RoomType.Lobby;

    // Players currently in this room (playerId -> player data)
    private readonly Dictionary<string, VRPlayerData> _players = new Dictionary<string, VRPlayerData>();

    // Available rooms discovered from server (roomId -> room info)
    private readonly Dictionary<string, RoomInfo> _availableRooms = new Dictionary<string, RoomInfo>();

    // Public events for UI and game systems
    public static event Action<string> OnRoomCreated;
    public static event Action<string> OnRoomJoined;
    public static event Action OnRoomLeft;
    public static event Action<string> OnRoomError;
    public static event Action<VRPlayerData> OnPlayerJoined;
    public static event Action<string> OnPlayerLeft;
    public static event Action<Dictionary<string, RoomInfo>> OnRoomListUpdated;
    public static event Action<RoomType> OnRoomTypeChanged;

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

    void OnEnable()
    {
        // Subscribe to network events
        VRNetworkManager.OnConnected += OnNetworkConnected;
        VRNetworkManager.OnDisconnected += OnNetworkDisconnected;
        VRNetworkManager.OnPeerDisconnected += OnPeerDisconnected;
        VRNetworkManager.OnMessageReceived += HandleMessage;
    }

    void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        VRNetworkManager.OnConnected -= OnNetworkConnected;
        VRNetworkManager.OnDisconnected -= OnNetworkDisconnected;
        VRNetworkManager.OnPeerDisconnected -= OnPeerDisconnected;
        VRNetworkManager.OnMessageReceived -= HandleMessage;
    }

    // Called when WebSocket connection is established
    void OnNetworkConnected()
    {
        Debug.Log("[VRRoom] Network connected. Ready to create/join rooms.");

        // Request the current list of available rooms
        RequestRoomList();
    }

    // Called when WebSocket connection is lost
    void OnNetworkDisconnected()
    {
        // Reset room state if disconnected while in a room
        if (IsInRoom)
        {
            CurrentRoomId = null;
            CurrentRoomName = null;
            IsInRoom = false;
            IsHost = false;

            _players.Clear();
            OnRoomLeft?.Invoke();
        }
    }

    // Called when another player disconnects from the server
    void OnPeerDisconnected(string peerId)
    {
        // Remove disconnected player from room
        if (_players.ContainsKey(peerId))
        {
            _players.Remove(peerId);
            OnPlayerLeft?.Invoke(peerId);
            Debug.Log($"[VRRoom] Player disconnected: {peerId}");
        }
    }

    #region Public API

    // Creates a new meeting room with local player as host
    public void CreateRoom(RoomType roomType = RoomType.MeetingRoomA, string roomName = "")
    {
        if (IsInRoom)
        {
            OnRoomError?.Invoke("You are already in a room. Leave first.");
            return;
        }

        if (!VRNetworkManager.IsConnected)
        {
            OnRoomError?.Invoke("Not connected to the server.");
            return;
        }

        // Initialize local room state
        CurrentRoomId = GenerateRoomId();
        CurrentRoomName = string.IsNullOrEmpty(roomName) ? $"Room {CurrentRoomId}" : roomName;
        IsInRoom = true;
        IsHost = true;
        CurrentRoomType = roomType;

        // Add local player to room
        _players.Clear();
        var localPlayer = new VRPlayerData
        {
            playerId = VRNetworkManager.LocalId,
            playerName = PlayerPrefs.GetString("PlayerName", "Player"),
            isHost = true,
            roomType = roomType
        };
        _players[VRNetworkManager.LocalId] = localPlayer;

        // Broadcast room availability to server
        VRNetworkManager.Instance.Send("room-available", new RoomInfo
        {
            roomId = CurrentRoomId,
            hostId = VRNetworkManager.LocalId,
            roomName = CurrentRoomName,
            roomType = roomType,
            playerCount = 1,
            maxPlayers = maxPlayersPerRoom
        });

        Debug.Log($"[VRRoom] Created room: {CurrentRoomName} ({CurrentRoomId})");
        OnRoomCreated?.Invoke(CurrentRoomId);
        OnRoomTypeChanged?.Invoke(roomType);
    }

    // Joins an existing room by room code
    public void JoinRoom(string roomId)
    {
        if (IsInRoom)
        {
            OnRoomError?.Invoke("You are already in a room.");
            return;
        }

        if (string.IsNullOrEmpty(roomId))
        {
            OnRoomError?.Invoke("Invalid room code.");
            return;
        }

        roomId = roomId.ToUpper().Trim();

        // Verify room exists in available rooms list
        if (!_availableRooms.ContainsKey(roomId))
        {
            OnRoomError?.Invoke($"Room '{roomId}' not found.");
            return;
        }

        var roomInfo = _availableRooms[roomId];

        // Check if room is full
        if (roomInfo.playerCount >= roomInfo.maxPlayers)
        {
            OnRoomError?.Invoke("This room is full.");
            return;
        }

        // Initialize local room state
        CurrentRoomId = roomId;
        CurrentRoomName = roomInfo.roomName;
        IsInRoom = true;
        IsHost = false;
        CurrentRoomType = roomInfo.roomType;

        // Add local player to room
        _players.Clear();
        var localPlayer = new VRPlayerData
        {
            playerId = VRNetworkManager.LocalId,
            playerName = PlayerPrefs.GetString("PlayerName", "Player"),
            isHost = false,
            roomType = roomInfo.roomType
        };
        _players[VRNetworkManager.LocalId] = localPlayer;

        // Send join request to host via server
        VRNetworkManager.Instance.Send("room-join", new RoomJoinRequest
        {
            roomId = roomId,
            playerId = VRNetworkManager.LocalId,
            playerName = localPlayer.playerName
        });

        Debug.Log($"[VRRoom] Joining room: {CurrentRoomName} ({roomId})");
        OnRoomJoined?.Invoke(roomId);
        OnRoomTypeChanged?.Invoke(CurrentRoomType);
    }

    // Leaves the current room (closes if host)
    public void LeaveRoom()
    {
        if (!IsInRoom)
            return;

        // Notify all players of departure
        VRNetworkManager.Instance.Send("room-leave", new RoomLeaveData
        {
            roomId = CurrentRoomId,
            playerId = VRNetworkManager.LocalId
        });

        // Host closes the room for everyone
        if (IsHost)
        {
            VRNetworkManager.Instance.Send("room-closed", new RoomInfo
            {
                roomId = CurrentRoomId,
                hostId = VRNetworkManager.LocalId
            });
        }

        Debug.Log($"[VRRoom] Left room: {CurrentRoomId}");

        // Reset local room state
        CurrentRoomId = null;
        CurrentRoomName = null;
        IsInRoom = false;
        IsHost = false;
        CurrentRoomType = RoomType.Lobby;
        _players.Clear();

        OnRoomLeft?.Invoke();
        OnRoomTypeChanged?.Invoke(RoomType.Lobby);
    }

    // Changes zone/area within the same room (no reconnection required)
    public void TeleportToRoomType(RoomType roomType)
    {
        if (!IsInRoom)
        {
            OnRoomError?.Invoke("You must be in a room to teleport.");
            return;
        }

        CurrentRoomType = roomType;

        // Notify other players of zone change
        VRNetworkManager.Instance.Send("room-teleport", new RoomTeleportData
        {
            roomId = CurrentRoomId,
            playerId = VRNetworkManager.LocalId,
            targetRoomType = roomType
        });

        OnRoomTypeChanged?.Invoke(roomType);
        Debug.Log($"[VRRoom] Teleported to: {roomType}");
    }

    // Requests updated room list from server
    public void RequestRoomList()
    {
        VRNetworkManager.Instance.Send("room-list-request", "");
    }

    // Returns all players in the current room
    public List<VRPlayerData> GetPlayers()
    {
        return new List<VRPlayerData>(_players.Values);
    }

    // Returns the number of players in the current room
    public int PlayerCount => _players.Count;

    // Returns a copy of discovered rooms
    public Dictionary<string, RoomInfo> GetAvailableRooms()
    {
        return new Dictionary<string, RoomInfo>(_availableRooms);
    }

    // Updates local player name and notifies others if in a room
    public void SetPlayerName(string name)
    {
        PlayerPrefs.SetString("PlayerName", name);
        PlayerPrefs.Save();

        if (IsInRoom && _players.ContainsKey(VRNetworkManager.LocalId))
        {
            _players[VRNetworkManager.LocalId].playerName = name;

            // Broadcast name change to room
            VRNetworkManager.Instance.Send("player-name-update", new PlayerNameUpdate
            {
                roomId = CurrentRoomId,
                playerId = VRNetworkManager.LocalId,
                playerName = name
            });
        }
    }

    #endregion

    #region Message Handling

    // Routes incoming network messages to appropriate handlers
    void HandleMessage(NetworkMessage msg)
    {
        switch (msg.type)
        {
            case "room-available":
                HandleRoomAvailable(msg);
                break;

            case "room-closed":
                HandleRoomClosed(msg);
                break;

            case "room-join":
                HandleRoomJoin(msg);
                break;

            case "room-welcome":
                HandleRoomWelcome(msg);
                break;

            case "room-leave":
                HandleRoomLeave(msg);
                break;

            case "room-list":
                HandleRoomList(msg);
                break;

            case "room-teleport":
                HandleRoomTeleport(msg);
                break;

            case "player-name-update":
                HandlePlayerNameUpdate(msg);
                break;
        }
    }

    // Called when a new room becomes available
    void HandleRoomAvailable(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<RoomInfo>(msg.data);

        _availableRooms[data.roomId] = data;
        Debug.Log($"[VRRoom] Room available: {data.roomId} ({data.roomName})");
        OnRoomListUpdated?.Invoke(_availableRooms);
    }

    // Called when a room is closed by its host
    void HandleRoomClosed(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<RoomInfo>(msg.data);

        _availableRooms.Remove(data.roomId);
        Debug.Log($"[VRRoom] Room closed: {data.roomId}");
        OnRoomListUpdated?.Invoke(_availableRooms);

        // Force-leave if we were in that room
        if (IsInRoom && CurrentRoomId == data.roomId && !IsHost)
        {
            CurrentRoomId = null;
            CurrentRoomName = null;
            IsInRoom = false;
            CurrentRoomType = RoomType.Lobby;

            _players.Clear();

            OnRoomLeft?.Invoke();
            OnRoomTypeChanged?.Invoke(RoomType.Lobby);
            OnRoomError?.Invoke("The host closed the room.");
        }
    }

    // Called when a player requests to join this room (host only)
    void HandleRoomJoin(NetworkMessage msg)
    {
        var request = JsonUtility.FromJson<RoomJoinRequest>(msg.data);

        // Only process join requests for our own room if we're host
        if (!IsHost || request.roomId != CurrentRoomId)
            return;

        Debug.Log($"[VRRoom] Player joining: {request.playerId} ({request.playerName})");

        // Add new player to local roster
        var newPlayer = new VRPlayerData
        {
            playerId = request.playerId,
            playerName = request.playerName,
            isHost = false,
            roomType = CurrentRoomType
        };
        _players[request.playerId] = newPlayer;

        // Send current room state to all players
        var welcome = new RoomWelcomeData
        {
            roomId = CurrentRoomId,
            roomType = CurrentRoomType,
            players = new List<VRPlayerData>(_players.Values).ToArray()
        };
        VRNetworkManager.Instance.Send("room-welcome", welcome);

        // Update server with new player count
        UpdateRoomPlayerCount();

        OnPlayerJoined?.Invoke(newPlayer);
    }

    // Called when host sends the authoritative players list
    void HandleRoomWelcome(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<RoomWelcomeData>(msg.data);

        if (!IsInRoom || data.roomId != CurrentRoomId)
            return;

        // Host determines the room type
        CurrentRoomType = data.roomType;

        // Sync players list from host
        foreach (var player in data.players)
        {
            if (!_players.ContainsKey(player.playerId))
            {
                _players[player.playerId] = player;

                // Don't fire event for local player
                if (player.playerId != VRNetworkManager.LocalId)
                {
                    OnPlayerJoined?.Invoke(player);
                }
            }
        }

        Debug.Log($"[VRRoom] Welcome received. {_players.Count} players in room.");
    }

    // Called when a player leaves the room
    void HandleRoomLeave(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<RoomLeaveData>(msg.data);

        if (!IsInRoom || data.roomId != CurrentRoomId)
            return;

        if (_players.ContainsKey(data.playerId))
        {
            _players.Remove(data.playerId);
            OnPlayerLeft?.Invoke(data.playerId);
            Debug.Log($"[VRRoom] Player left: {data.playerId}");

            // Host updates server with new count
            if (IsHost)
            {
                UpdateRoomPlayerCount();
            }
        }
    }

    // Called when server sends updated room list
    void HandleRoomList(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<RoomListData>(msg.data);

        _availableRooms.Clear();
        foreach (var room in data.rooms)
        {
            _availableRooms[room.roomId] = room;
        }

        OnRoomListUpdated?.Invoke(_availableRooms);
        Debug.Log($"[VRRoom] Room list updated: {_availableRooms.Count} rooms.");
    }

    // Called when a player teleports to a different zone
    void HandleRoomTeleport(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<RoomTeleportData>(msg.data);

        if (!IsInRoom || data.roomId != CurrentRoomId)
            return;

        if (_players.ContainsKey(data.playerId))
        {
            _players[data.playerId].roomType = data.targetRoomType;
            Debug.Log($"[VRRoom] Player {data.playerId} teleported to {data.targetRoomType}");
        }
    }

    // Called when a player updates their display name
    void HandlePlayerNameUpdate(NetworkMessage msg)
    {
        var data = JsonUtility.FromJson<PlayerNameUpdate>(msg.data);

        if (!IsInRoom || data.roomId != CurrentRoomId)
            return;

        if (_players.ContainsKey(data.playerId))
        {
            _players[data.playerId].playerName = data.playerName;
            Debug.Log($"[VRRoom] Player name updated: {data.playerId} -> {data.playerName}");
        }
    }

    // Host-only: broadcasts updated player count to server
    void UpdateRoomPlayerCount()
    {
        VRNetworkManager.Instance.Send("room-update", new RoomInfo
        {
            roomId = CurrentRoomId,
            hostId = VRNetworkManager.LocalId,
            roomName = CurrentRoomName,
            playerCount = _players.Count,
            maxPlayers = maxPlayersPerRoom,
            roomType = CurrentRoomType
        });
    }

    #endregion

    #region Helpers

    // Generates a 6-character room code with unambiguous characters
    string GenerateRoomId()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] id = new char[6];
        var random = new System.Random();

        for (int i = 0; i < 6; i++)
        {
            id[i] = chars[random.Next(chars.Length)];
        }

        return new string(id);
    }

    #endregion
}

#region Enums

// Available room zones/areas in the application
public enum RoomType
{
    Lobby,
    MeetingRoomA,
    MeetingRoomB
}

#endregion

#region Data Classes

// Player data for room membership and VR pose synchronization
[Serializable]
public class VRPlayerData
{
    public string playerId;
    public string playerName;
    public bool isHost;
    public RoomType roomType;

    // Generic position and rotation
    public float posX, posY, posZ;
    public float rotX, rotY, rotZ, rotW;

    // VR headset transform
    public float headPosX, headPosY, headPosZ;
    public float headRotX, headRotY, headRotZ, headRotW;

    // Left hand transform
    public float leftHandPosX, leftHandPosY, leftHandPosZ;
    public float leftHandRotX, leftHandRotY, leftHandRotZ, leftHandRotW;

    // Right hand transform
    public float rightHandPosX, rightHandPosY, rightHandPosZ;
    public float rightHandRotX, rightHandRotY, rightHandRotZ, rightHandRotW;
}

// Public room information shared across all clients
[Serializable]
public class RoomInfo
{
    public string roomId;
    public string hostId;
    public string roomName;
    public RoomType roomType;
    public int playerCount;
    public int maxPlayers;
}

// Payload for room join requests
[Serializable]
public class RoomJoinRequest
{
    public string roomId;
    public string playerId;
    public string playerName;
}

// Payload for room leave notifications
[Serializable]
public class RoomLeaveData
{
    public string roomId;
    public string playerId;
}

// Host-sent payload containing authoritative player roster
[Serializable]
public class RoomWelcomeData
{
    public string roomId;
    public RoomType roomType;
    public VRPlayerData[] players;
}

// Server response containing all available rooms
[Serializable]
public class RoomListData
{
    public RoomInfo[] rooms;
}

// Payload for in-room zone changes
[Serializable]
public class RoomTeleportData
{
    public string roomId;
    public string playerId;
    public RoomType targetRoomType;
}

// Payload for player name changes
[Serializable]
public class PlayerNameUpdate
{
    public string roomId;
    public string playerId;
    public string playerName;
}

#endregion