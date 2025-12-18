using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// VR menu UI controller for room management
// Handles public room list, room creation, join by code, and in-room player list
public class VRMenuUI : MonoBehaviour
{
    [Header("Panels")]

    // Main panel displaying public rooms and join options
    public GameObject mainPanel;

    // Panel for creating a new room
    public GameObject createRoomPanel;
    
    [Header("Main Panel - Header")]

    // Button to refresh the room list
    public Button refreshButton;

    // Title text (e.g., "Public Rooms")
    public TextMeshProUGUI titleText;
    
    [Header("Main Panel - Join by Code")]

    // Input field for entering a 6-character room code
    public TMP_InputField roomCodeInput;

    // Button to join a room using the entered code
    public Button joinButton;
    
    [Header("Main Panel - Room List")]

    // Container for dynamically spawned room list items
    public Transform roomListContainer;

    // Prefab for each room list item
    public GameObject roomListItemPrefab;
    
    [Header("Main Panel - Footer")]

    // Button to open the create room panel
    public Button newRoomButton;
    
    [Header("Create Panel - Fields")]

    // Input field for custom room name
    public TMP_InputField roomNameInput;

    // Input field for max players (linked to slider)
    public TMP_InputField maxPlayersInput;

    // Text displaying current max players value
    public TextMeshProUGUI maxPlayersValueText;

    // Slider to select max players (2-20)
    public Slider maxPlayersSlider;
    
    [Header("Create Panel - Buttons")]

    // Button to confirm room creation
    public Button createButton;

    // Button to cancel and return to main panel
    public Button cancelButton;
    
    [Header("Status")]

    // Status text for feedback (e.g., "Connected", "Joining...")
    public TextMeshProUGUI statusText;
    
    [Header("In Room Panel")]

    // Panel shown when player is inside a room
    public GameObject inRoomPanel;

    // Displays current room name
    public TextMeshProUGUI currentRoomNameText;

    // Displays current room code
    public TextMeshProUGUI currentRoomCodeText;

    // Displays current player count
    public TextMeshProUGUI currentPlayerCountText;

    // Container for dynamically spawned player list items
    public Transform playerListContainer;

    // Prefab for each player list item
    public GameObject playerListItemPrefab;

    // Button to leave the current room
    public Button leaveRoomButton;
    
    // Dynamically instantiated UI elements
    private List<GameObject> roomListItems = new List<GameObject>();
    private List<GameObject> playerListItems = new List<GameObject>();
    
    void Start()
    {
        // Register main panel button listeners
        if (refreshButton != null)
            refreshButton.onClick.AddListener(OnRefreshRooms);
        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinWithCode);
        if (newRoomButton != null)
            newRoomButton.onClick.AddListener(OnNewRoomClicked);
        
        // Register create panel button listeners
        if (createButton != null)
            createButton.onClick.AddListener(OnCreateRoom);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelCreate);
        
        // Configure max players slider
        if (maxPlayersSlider != null)
        {
            maxPlayersSlider.onValueChanged.AddListener(OnMaxPlayersChanged);
            maxPlayersSlider.minValue = 2;
            maxPlayersSlider.maxValue = 20;
            maxPlayersSlider.value = 10;
            OnMaxPlayersChanged(10);
        }
        
        // Register leave room button
        if (leaveRoomButton != null)
            leaveRoomButton.onClick.AddListener(OnLeaveRoom);
        
        // Join on Enter key press
        if (roomCodeInput != null)
            roomCodeInput.onSubmit.AddListener((_) => OnJoinWithCode());
        
        // Subscribe to network events
        VRNetworkManager.OnConnected += OnConnected;
        VRNetworkManager.OnDisconnected += OnDisconnected;
        
        // Subscribe to room events
        VRRoomManager.OnRoomCreated += OnRoomCreated;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnRoomError += OnRoomError;
        VRRoomManager.OnRoomListUpdated += OnRoomListUpdated;
        VRRoomManager.OnPlayerJoined += OnPlayerChanged;
        VRRoomManager.OnPlayerLeft += OnPlayerLeftRoom;
        
        // Initial state
        ShowMainPanel();
        SetStatus("Connexion...");
    }
    
    void OnDestroy()
    {
        // Clean up button listeners
        if (refreshButton != null) refreshButton.onClick.RemoveAllListeners();
        if (joinButton != null) joinButton.onClick.RemoveAllListeners();
        if (newRoomButton != null) newRoomButton.onClick.RemoveAllListeners();
        if (createButton != null) createButton.onClick.RemoveAllListeners();
        if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
        if (maxPlayersSlider != null) maxPlayersSlider.onValueChanged.RemoveAllListeners();
        if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveAllListeners();
        if (roomCodeInput != null) roomCodeInput.onSubmit.RemoveAllListeners();
        
        // Unsubscribe from events
        VRNetworkManager.OnConnected -= OnConnected;
        VRNetworkManager.OnDisconnected -= OnDisconnected;
        VRRoomManager.OnRoomCreated -= OnRoomCreated;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnRoomError -= OnRoomError;
        VRRoomManager.OnRoomListUpdated -= OnRoomListUpdated;
        VRRoomManager.OnPlayerJoined -= OnPlayerChanged;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeftRoom;
    }
    
    #region Button Actions
    
    // Requests updated room list from server
    void OnRefreshRooms()
    {
        SetStatus("Actualisation...");
        VRRoomManager.Instance?.RequestRoomList();
    }
    
    // Joins a room using the entered 6-character code
    void OnJoinWithCode()
    {
        if (roomCodeInput == null)
        {
            SetStatus("Erreur: champ code non configuré");
            return;
        }
        
        string code = roomCodeInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Entrez un code !");
            return;
        }
        
        if (code.Length != 6)
        {
            SetStatus("Le code doit faire 6 caractères");
            return;
        }
        
        SetStatus($"Connexion à {code}...");
        Debug.Log($"[VRMenuUI] Attempting to join room: {code}");
        VRRoomManager.Instance?.JoinRoom(code);
    }
    
    // Opens the create room panel
    void OnNewRoomClicked()
    {
        ShowCreatePanel();
    }
    
    // Creates a new room with specified settings
    void OnCreateRoom()
    {
        string roomName = roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(roomName))
        {
            roomName = "Room " + Random.Range(1000, 9999);
        }
        
        int maxPlayers = maxPlayersSlider != null ? (int)maxPlayersSlider.value : 10;
        
        // Update max players in RoomManager
        if (VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.maxPlayersPerRoom = maxPlayers;
        }
        
        SetStatus("Création...");
        VRRoomManager.Instance?.CreateRoom(RoomType.Lobby, roomName);
    }
    
    // Cancels room creation and returns to main panel
    void OnCancelCreate()
    {
        ShowMainPanel();
    }
    
    // Updates max players text when slider value changes
    void OnMaxPlayersChanged(float value)
    {
        int intValue = (int)value;
        if (maxPlayersValueText != null)
        {
            maxPlayersValueText.text = intValue.ToString();
        }
        if (maxPlayersInput != null)
        {
            maxPlayersInput.text = intValue.ToString();
        }
    }
    
    // Leaves the current room
    void OnLeaveRoom()
    {
        VRRoomManager.Instance?.LeaveRoom();
    }
    
    // Joins a room from the public room list
    void OnJoinRoomFromList(string roomId)
    {
        SetStatus("Connexion...");
        VRRoomManager.Instance?.JoinRoom(roomId);
    }
    
    #endregion
    
    #region Network Events
    
    // Called when WebSocket connection is established
    void OnConnected()
    {
        SetStatus("Connecté !");
        VRRoomManager.Instance?.RequestRoomList();
    }
    
    // Called when WebSocket connection is lost
    void OnDisconnected()
    {
        SetStatus("Déconnecté");
        ShowMainPanel();
    }
    
    #endregion
    
    #region Room Events
    
    // Called when local player creates a room
    void OnRoomCreated(string roomId)
    {
        ShowInRoomPanel();
    }
    
    // Called when local player joins a room
    void OnRoomJoined(string roomId)
    {
        ShowInRoomPanel();
    }
    
    // Called when local player leaves a room
    void OnRoomLeft()
    {
        ShowMainPanel();
        SetStatus("Room quittée");
    }
    
    // Called when a room error occurs
    void OnRoomError(string error)
    {
        SetStatus($"Erreur: {error}");
    }
    
    // Called when room list is updated from server
    void OnRoomListUpdated(Dictionary<string, RoomInfo> rooms)
    {
        RefreshRoomList(rooms);
        SetStatus($"{rooms.Count} room(s) disponible(s)");
    }
    
    // Called when a player joins the room
    void OnPlayerChanged(VRPlayerData player)
    {
        UpdateInRoomPanel();
    }
    
    // Called when a player leaves the room
    void OnPlayerLeftRoom(string playerId)
    {
        UpdateInRoomPanel();
    }
    
    #endregion
    
    #region UI Updates
    
    // Shows the main panel (public rooms + join by code)
    void ShowMainPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (createRoomPanel != null) createRoomPanel.SetActive(false);
        if (inRoomPanel != null) inRoomPanel.SetActive(false);
        
        // Reset inputs
        if (roomCodeInput != null) roomCodeInput.text = "";
    }
    
    // Shows the create room panel
    void ShowCreatePanel()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (createRoomPanel != null) createRoomPanel.SetActive(true);
        if (inRoomPanel != null) inRoomPanel.SetActive(false);
        
        // Reset inputs
        if (roomNameInput != null) roomNameInput.text = "";
        if (maxPlayersSlider != null) maxPlayersSlider.value = 10;
    }
    
    // Shows the in-room panel (player list + room info)
    void ShowInRoomPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (createRoomPanel != null) createRoomPanel.SetActive(false);
        if (inRoomPanel != null) inRoomPanel.SetActive(true);
        
        UpdateInRoomPanel();
    }
    
    // Updates room info and player list in the in-room panel
    void UpdateInRoomPanel()
    {
        if (VRRoomManager.Instance == null) return;
        
        // Display room information
        if (currentRoomCodeText != null)
            currentRoomCodeText.text = $"Code: {VRRoomManager.Instance.CurrentRoomId}";
        
        if (currentPlayerCountText != null)
            currentPlayerCountText.text = $"\nPlayers: {VRRoomManager.Instance.PlayerCount}";
        
        // Refresh player list
        RefreshPlayerList();
    }
    
    // Refreshes the public room list display
    void RefreshRoomList(Dictionary<string, RoomInfo> rooms)
    {
        // Destroy old items
        foreach (var item in roomListItems)
        {
            Destroy(item);
        }
        roomListItems.Clear();
        
        if (roomListContainer == null || roomListItemPrefab == null) return;
        
        // Create new items for each room
        foreach (var kvp in rooms)
        {
            RoomInfo room = kvp.Value;
            
            GameObject item = Instantiate(roomListItemPrefab, roomListContainer);
            roomListItems.Add(item);
            
            // Configure room info text
            var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0)
            {
                texts[0].text = $"{room.roomName}";
            }
            if (texts.Length > 1)
            {
                texts[1].text = $"{room.playerCount}/{room.maxPlayers}";
            }
            
            // Add join button functionality
            var button = item.GetComponent<Button>();
            if (button == null)
            {
                button = item.AddComponent<Button>();
            }
            
            string roomId = room.roomId;
            button.onClick.AddListener(() => OnJoinRoomFromList(roomId));
            
            // Disable button if room is full
            if (room.playerCount >= room.maxPlayers)
            {
                button.interactable = false;
                var colors = button.colors;
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                button.colors = colors;
            }
        }
    }
    
    // Refreshes the in-room player list display
    void RefreshPlayerList()
    {
        // Destroy old items
        foreach (var item in playerListItems)
        {
            Destroy(item);
        }
        playerListItems.Clear();
        
        if (playerListContainer == null || playerListItemPrefab == null) return;
        if (VRRoomManager.Instance == null) return;
        
        var players = VRRoomManager.Instance.GetPlayers();
        
        // Create new items for each player
        foreach (var player in players)
        {
            GameObject item = Instantiate(playerListItemPrefab, playerListContainer);
            playerListItems.Add(item);
            
            var text = item.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                // Format: "★ HostName" or "• PlayerName (You)"
                string prefix = player.isHost ? "★ " : "• ";
                string suffix = player.playerId == VRNetworkManager.LocalId ? " (Vous)" : "";
                text.text = $"{prefix}{player.playerName}{suffix}";
            }
        }
    }
    
    // Updates status text and logs message
    void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[VRMenuUI] {message}");
    }
    
    #endregion
}