using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
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
    private bool _isSubscribed = false;

    void Awake()
    {
        // Subscribe to events in Awake so we always receive them, even when page is hidden
        SubscribeToEvents();
    }

    void OnEnable()
    {
        // Ensure subscribed (in case Awake wasn't called)
        SubscribeToEvents();

        // Refresh display when page becomes visible
        RefreshRoomInfo();
        RefreshPlayerList();
    }

    void OnDisable()
    {
        // Don't unsubscribe - we want to keep receiving events even when hidden
        // The page will refresh when it becomes visible again
    }

    void OnDestroy()
    {
        // Only unsubscribe when destroyed
        UnsubscribeFromEvents();
    }

    void SubscribeToEvents()
    {
        if (_isSubscribed) return;

        VRRoomManager.OnPlayerJoined += OnPlayerJoined;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;
        VRRoomManager.OnRoomCreated += OnRoomCreated;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;

        _isSubscribed = true;
    }

    void UnsubscribeFromEvents()
    {
        if (!_isSubscribed) return;

        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;
        VRRoomManager.OnRoomCreated -= OnRoomCreated;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;

        _isSubscribed = false;
    }

    void Start()
    {
        AutoFindReferences();

        if (leaveRoomButton != null)
        {
            leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        }

        if (copyCodeButton != null)
        {
            copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
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

        // Ensure container has proper layout
        EnsureContainerLayout();
    }

    void EnsureContainerLayout()
    {
        if (playerListContainer == null) return;

        // Add or configure VerticalLayoutGroup
        VerticalLayoutGroup vlg = playerListContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = playerListContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        // Force proper settings
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = true;  // Control height
        vlg.childControlWidth = true;   // Control width - THIS IS KEY
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;  // Expand width to fill
        vlg.spacing = 5;
        vlg.padding = new RectOffset(5, 5, 5, 5);

        // Add ContentSizeFitter if missing (for ScrollRect)
        ContentSizeFitter csf = playerListContainer.GetComponent<ContentSizeFitter>();
        if (csf == null)
        {
            csf = playerListContainer.gameObject.AddComponent<ContentSizeFitter>();
        }
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
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
        if (roomManager == null) return;

        if (playerListContainer == null)
        {
            AutoFindReferences();
            if (playerListContainer == null) return;
        }

        // Ensure container has proper layout components
        EnsureContainerLayout();

        // Add current players
        var players = roomManager.GetPlayers();
        foreach (var player in players)
        {
            AddPlayerItem(player);
        }

        // If no players found but we're in a room, show at least "You"
        if (players.Count == 0 && roomManager.IsInRoom)
        {
            CreateSimplePlayerItem("You", true);
        }

        // Force layout rebuild (immediate + delayed for proper sizing)
        if (playerListContainer != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(playerListContainer as RectTransform);
            StartCoroutine(DelayedLayoutRebuild());
        }
    }

    IEnumerator DelayedLayoutRebuild()
    {
        yield return new WaitForEndOfFrame();

        if (playerListContainer == null) yield break;

        RectTransform containerRect = playerListContainer as RectTransform;

        // Rebuild layout hierarchy
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);

        // Also rebuild parent (Viewport)
        if (containerRect.parent != null)
        {
            RectTransform parentRect = containerRect.parent as RectTransform;
            if (parentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
            }
        }
    }

    // Helper to validate RectTransform values - prevent AABB errors
    void ValidateRectTransform(RectTransform rect)
    {
        if (rect == null) return;

        Vector3 pos = rect.localPosition;
        if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
        {
            rect.localPosition = Vector3.zero;
        }

        Vector3 scale = rect.localScale;
        if (float.IsNaN(scale.x) || float.IsNaN(scale.y) || float.IsNaN(scale.z))
        {
            rect.localScale = Vector3.one;
        }
    }

    void CreateSimplePlayerItem(string name, bool isLocal)
    {
        if (playerListContainer == null) return;

        GameObject item = new GameObject($"Player_{name}");
        item.transform.SetParent(playerListContainer, false);

        // Configure RectTransform for the item (already added by Unity when parented to Canvas)
        RectTransform itemRect = item.GetComponent<RectTransform>();
        if (itemRect == null) itemRect = item.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 1);
        itemRect.anchorMax = new Vector2(1, 1);
        itemRect.pivot = new Vector2(0.5f, 1);
        itemRect.sizeDelta = new Vector2(0, 40);

        // Add background
        Image bg = item.AddComponent<Image>();
        bg.color = isLocal ? new Color(0.2f, 0.4f, 0.6f, 0.8f) : new Color(0.2f, 0.2f, 0.25f, 0.8f);

        // Add layout element
        LayoutElement layout = item.AddComponent<LayoutElement>();
        layout.minHeight = 40;
        layout.preferredHeight = 40;
        layout.flexibleWidth = 1;

        // Add text
        GameObject textObj = new GameObject("PlayerName");
        textObj.transform.SetParent(item.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = isLocal ? $"{name} (You)" : name;
        text.fontSize = 18;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;

        // Assign default TMP font
        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(15, 5);
        textRect.offsetMax = new Vector2(-15, -5);

        // Validate transforms to prevent AABB errors
        ValidateRectTransform(itemRect);
        ValidateRectTransform(textRect);
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

            // Force correct RectTransform settings
            RectTransform prefabRect = item.GetComponent<RectTransform>();
            if (prefabRect != null)
            {
                prefabRect.anchorMin = new Vector2(0, 1);
                prefabRect.anchorMax = new Vector2(1, 1);
                prefabRect.pivot = new Vector2(0.5f, 1);
                prefabRect.sizeDelta = new Vector2(0, 40);
                ValidateRectTransform(prefabRect);
            }

            // Ensure LayoutElement exists
            LayoutElement layout = item.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = item.AddComponent<LayoutElement>();
            }
            layout.minHeight = 40;
            layout.preferredHeight = 40;
            layout.flexibleWidth = 1;

            // Update text
            TextMeshProUGUI nameText = item.GetComponentInChildren<TextMeshProUGUI>();
            if (nameText != null)
            {
                nameText.text = displayName + hostTag + localTag;
                // Ensure font is assigned
                if (nameText.font == null && TMP_Settings.defaultFontAsset != null)
                {
                    nameText.font = TMP_Settings.defaultFontAsset;
                }
            }

        }
        else
        {
            // Create item manually if no prefab
            item = new GameObject($"Player_{player.playerId}");
            item.transform.SetParent(playerListContainer, false);

            // Configure RectTransform for the item (already added by Unity when parented to Canvas)
            RectTransform itemRect = item.GetComponent<RectTransform>();
            if (itemRect == null) itemRect = item.AddComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0, 1);
            itemRect.anchorMax = new Vector2(1, 1);
            itemRect.pivot = new Vector2(0.5f, 1);
            itemRect.sizeDelta = new Vector2(0, 40);

            Image bg = item.AddComponent<Image>();
            bg.color = isLocal ? new Color(0.2f, 0.4f, 0.6f, 0.8f) : new Color(0.2f, 0.2f, 0.25f, 0.8f);

            LayoutElement layout = item.AddComponent<LayoutElement>();
            layout.minHeight = 40;
            layout.preferredHeight = 40;
            layout.flexibleWidth = 1;

            GameObject textObj = new GameObject("PlayerName");
            textObj.transform.SetParent(item.transform, false);

            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = displayName + hostTag + localTag;
            text.fontSize = 18;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;

            // Assign default TMP font
            if (TMP_Settings.defaultFontAsset != null)
            {
                text.font = TMP_Settings.defaultFontAsset;
            }

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(15, 5);
            textRect.offsetMax = new Vector2(-15, -5);

            // Validate transforms to prevent AABB errors
            ValidateRectTransform(itemRect);
            ValidateRectTransform(textRect);
        }

        _playerItems[player.playerId] = item;
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

    void OnRoomCreated(string roomId)
    {
        RefreshRoomInfo();
        RefreshPlayerList();
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
        }
    }
}
