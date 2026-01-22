using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Room page - Shows current room info and player list.
/// </summary>
public class VRMenuPageRoom : MonoBehaviour
{
    [Header("Room Info")]
    public TextMeshProUGUI roomNameText;
    public TextMeshProUGUI roomCodeText;
    public TextMeshProUGUI playerCountText;

    [Header("Player List")]
    public Transform playerListContainer;
    public GameObject playerItemPrefab;

    [Header("Actions")]
    public Button leaveRoomButton;
    public Button copyCodeButton;

    private Dictionary<string, GameObject> _playerItems = new Dictionary<string, GameObject>();

    void OnEnable()
    {
        // Subscribe to room events
        VRRoomManager.OnPlayerJoined += OnPlayerJoined;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;

        // Refresh display
        RefreshRoomInfo();
        RefreshPlayerList();
    }

    void OnDisable()
    {
        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
    }

    void Start()
    {
        AutoFindReferences();

        if (leaveRoomButton != null)
        {
            leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
            Debug.Log("[VRMenuPageRoom] Leave Room button connected");
        }

        if (copyCodeButton != null)
        {
            copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
            Debug.Log("[VRMenuPageRoom] Copy Code button connected");
        }

        RefreshRoomInfo();
    }

    void AutoFindReferences()
    {
        // Find texts
        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var txt in texts)
        {
            string n = txt.name.ToLower();
            if (roomNameText == null && n.Contains("roomname")) roomNameText = txt;
            else if (roomCodeText == null && n.Contains("roomcode")) roomCodeText = txt;
            else if (playerCountText == null && n.Contains("playercount")) playerCountText = txt;
        }

        // Find buttons
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            string n = btn.name.ToLower();
            if (leaveRoomButton == null && n.Contains("leave")) leaveRoomButton = btn;
            else if (copyCodeButton == null && n.Contains("copy")) copyCodeButton = btn;
        }

        // Find player list container (ScrollRect content)
        if (playerListContainer == null)
        {
            ScrollRect scroll = GetComponentInChildren<ScrollRect>(true);
            if (scroll != null && scroll.content != null)
            {
                playerListContainer = scroll.content;
            }
        }

        // Find player item prefab (inactive template)
        if (playerItemPrefab == null && playerListContainer != null)
        {
            foreach (Transform child in playerListContainer)
            {
                if (!child.gameObject.activeSelf && child.name.Contains("Template"))
                {
                    playerItemPrefab = child.gameObject;
                    break;
                }
            }
        }

        Debug.Log($"[VRMenuPageRoom] AutoFind: roomName={roomNameText != null}, leave={leaveRoomButton != null}, copy={copyCodeButton != null}, list={playerListContainer != null}");
    }

    void RefreshRoomInfo()
    {
        var roomManager = VRRoomManager.Instance;
        if (roomManager == null) return;

        string roomId = roomManager.CurrentRoomId;
        bool inRoom = !string.IsNullOrEmpty(roomId);

        if (roomNameText != null)
        {
            roomNameText.text = inRoom ? $"Room: {roomId}" : "Not in a room";
        }

        if (roomCodeText != null)
        {
            roomCodeText.text = inRoom ? $"Code: {roomId}" : "---";
        }

        if (playerCountText != null)
        {
            int count = roomManager.PlayerCount;
            playerCountText.text = $"Players: {count}";
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.interactable = inRoom;
        }

        if (copyCodeButton != null)
        {
            copyCodeButton.interactable = inRoom;
        }
    }

    void RefreshPlayerList()
    {
        // Clear existing items
        foreach (var item in _playerItems.Values)
        {
            if (item != null) Destroy(item);
        }
        _playerItems.Clear();

        var roomManager = VRRoomManager.Instance;
        if (roomManager == null)
        {
            Debug.Log("[VRMenuPageRoom] RoomManager is null");
            return;
        }

        if (playerListContainer == null)
        {
            Debug.LogWarning("[VRMenuPageRoom] playerListContainer is null - trying to find it");
            AutoFindReferences();
            if (playerListContainer == null) return;
        }

        // Create a simple player item if prefab is missing
        if (playerItemPrefab == null)
        {
            Debug.LogWarning("[VRMenuPageRoom] playerItemPrefab is null - will create items manually");
        }

        // Add current players
        var players = roomManager.GetPlayers();
        Debug.Log($"[VRMenuPageRoom] Found {players.Count} players in room");

        foreach (var player in players)
        {
            AddPlayerItem(player);
        }

        // If no players found but we're in a room, show at least "You"
        if (players.Count == 0 && roomManager.IsInRoom)
        {
            CreateSimplePlayerItem("You", true);
        }
    }

    void CreateSimplePlayerItem(string name, bool isLocal)
    {
        if (playerListContainer == null) return;

        GameObject item = new GameObject($"Player_{name}");
        item.transform.SetParent(playerListContainer, false);

        // Add background
        Image bg = item.AddComponent<Image>();
        bg.color = isLocal ? new Color(0.2f, 0.4f, 0.6f, 0.8f) : new Color(0.2f, 0.2f, 0.25f, 0.8f);

        // Add layout element
        LayoutElement layout = item.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;

        // Add text
        GameObject textObj = new GameObject("PlayerName");
        textObj.transform.SetParent(item.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = isLocal ? $"{name} (You)" : name;
        text.fontSize = 18;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(15, 5);
        textRect.offsetMax = new Vector2(-15, -5);
    }

    void AddPlayerItem(VRPlayerData player)
    {
        if (playerListContainer == null) return;
        if (_playerItems.ContainsKey(player.playerId)) return;

        string displayName = string.IsNullOrEmpty(player.playerName) ? player.playerId : player.playerName;
        bool isLocal = player.playerId == VRNetworkManager.LocalId;
        string hostTag = player.isHost ? " (Host)" : "";
        string localTag = isLocal ? " (You)" : "";

        GameObject item;

        if (playerItemPrefab != null)
        {
            item = Instantiate(playerItemPrefab, playerListContainer);
            item.name = $"Player_{player.playerId}";
            item.SetActive(true);

            TextMeshProUGUI nameText = item.GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null)
            {
                nameText.text = displayName + hostTag + localTag;
            }
        }
        else
        {
            // Create item manually if no prefab
            item = new GameObject($"Player_{player.playerId}");
            item.transform.SetParent(playerListContainer, false);

            Image bg = item.AddComponent<Image>();
            bg.color = isLocal ? new Color(0.2f, 0.4f, 0.6f, 0.8f) : new Color(0.2f, 0.2f, 0.25f, 0.8f);

            LayoutElement layout = item.AddComponent<LayoutElement>();
            layout.minHeight = 40;
            layout.preferredHeight = 40;

            GameObject textObj = new GameObject("PlayerName");
            textObj.transform.SetParent(item.transform, false);

            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = displayName + hostTag + localTag;
            text.fontSize = 18;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(15, 5);
            textRect.offsetMax = new Vector2(-15, -5);
        }

        _playerItems[player.playerId] = item;
        Debug.Log($"[VRMenuPageRoom] Added player: {displayName}{hostTag}{localTag}");
    }

    void RemovePlayerItem(string playerId)
    {
        if (_playerItems.TryGetValue(playerId, out GameObject item))
        {
            if (item != null) Destroy(item);
            _playerItems.Remove(playerId);
        }
    }

    // Event handlers
    void OnPlayerJoined(VRPlayerData player)
    {
        AddPlayerItem(player);
        RefreshRoomInfo();
    }

    void OnPlayerLeft(string playerId)
    {
        RemovePlayerItem(playerId);
        RefreshRoomInfo();
    }

    void OnRoomJoined(string roomId)
    {
        RefreshRoomInfo();
        RefreshPlayerList();
    }

    void OnRoomLeft()
    {
        RefreshRoomInfo();
        RefreshPlayerList();
    }

    // Button handlers
    void OnLeaveRoomClicked()
    {
        var roomManager = VRRoomManager.Instance;
        if (roomManager != null)
        {
            roomManager.LeaveRoom();
        }

        // Teleport player to lobby spawn point
        var gameManager = VRGameManager.Instance;
        if (gameManager != null)
        {
            gameManager.TeleportLocalPlayer(RoomType.Lobby);
            Debug.Log("[VRMenuPageRoom] Teleported to lobby");
        }

        // Hide the menu
        var menuToggle = FindFirstObjectByType<VRMenuToggle>();
        if (menuToggle != null)
        {
            menuToggle.HideMenu();
        }
    }

    void OnCopyCodeClicked()
    {
        var roomManager = VRRoomManager.Instance;
        if (roomManager != null && !string.IsNullOrEmpty(roomManager.CurrentRoomId))
        {
            GUIUtility.systemCopyBuffer = roomManager.CurrentRoomId;
            Debug.Log($"[VRMenuPageRoom] Room code copied: {roomManager.CurrentRoomId}");
        }
    }
}
