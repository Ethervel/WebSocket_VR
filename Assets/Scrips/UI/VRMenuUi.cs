using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// VR menu UI controller for room management
// Handles public room list, room creation, join by code, and in-room player list
public class VRMenuUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainPanel;
    public GameObject createRoomPanel;
    public GameObject inRoomPanel;
    
    [Header("Main Panel - Header")]
    public Button refreshButton;
    public TextMeshProUGUI titleText;
    
    [Header("Main Panel - Join by Code")]
    public TMP_InputField roomCodeInput;
    public Button joinButton;
    
    [Header("Main Panel - Room List")]
    public Transform roomListContainer;
    public GameObject roomListItemPrefab;
    
    [Header("Main Panel - Footer")]
    public Button newRoomButton;
    
    [Header("Create Panel - Fields")]
    public TMP_InputField roomNameInput;
    public TMP_InputField maxPlayersInput;
    public TextMeshProUGUI maxPlayersValueText;
    public Slider maxPlayersSlider;
    
    [Header("Create Panel - Buttons")]
    public Button createButton;
    public Button cancelButton;
    
    [Header("Status")]
    public TextMeshProUGUI statusText;
    
    [Header("In Room Panel")]
    public TextMeshProUGUI currentRoomNameText;
    public TextMeshProUGUI currentRoomCodeText;
    public TextMeshProUGUI currentPlayerCountText;
    public Transform playerListContainer;
    public GameObject playerListItemPrefab;
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
    
    void OnRefreshRooms()
    {
        SetStatus("Actualisation...");
        VRRoomManager.Instance?.RequestRoomList();
    }
    
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
    
    void OnNewRoomClicked()
    {
        ShowCreatePanel();
    }
    
    void OnCreateRoom()
    {
        string roomName = roomNameInput != null ? roomNameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(roomName))
        {
            roomName = "Room " + Random.Range(1000, 9999);
        }
        
        int maxPlayers = maxPlayersSlider != null ? (int)maxPlayersSlider.value : 10;
        
        if (VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.maxPlayersPerRoom = maxPlayers;
        }
        
        SetStatus("Création...");
        VRRoomManager.Instance?.CreateRoom(RoomType.Lobby, roomName);
    }
    
    void OnCancelCreate()
    {
        ShowMainPanel();
    }
    
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
    
    void OnLeaveRoom()
    {
        VRRoomManager.Instance?.LeaveRoom();
    }
    
    void OnJoinRoomFromList(string roomId)
    {
        SetStatus("Connexion...");
        VRRoomManager.Instance?.JoinRoom(roomId);
    }
    
    #endregion
    
    #region Network Events
    
    void OnConnected()
    {
        SetStatus("Connecté !");
        VRRoomManager.Instance?.RequestRoomList();
    }
    
    void OnDisconnected()
    {
        SetStatus("Déconnecté");
        ShowMainPanel();
    }
    
    #endregion
    
    #region Room Events
    
    void OnRoomCreated(string roomId)
    {
        SetStatus("Room créée !");
        ShowInRoomPanel();
    }
    
    void OnRoomJoined(string roomId)
    {
        SetStatus("Connecté à la room !");
        ShowInRoomPanel();
    }
    
    void OnRoomLeft()
    {
        ShowMainPanel();
        SetStatus("Room quittée");
    }
    
    void OnRoomError(string error)
    {
        SetStatus($"Erreur: {error}");
    }
    
    void OnRoomListUpdated(Dictionary<string, RoomInfo> rooms)
    {
        RefreshRoomList(rooms);
        SetStatus($"{rooms.Count} room(s) disponible(s)");
    }
    
    void OnPlayerChanged(VRPlayerData player)
    {
        UpdateInRoomPanel();
    }
    
    void OnPlayerLeftRoom(string playerId)
    {
        UpdateInRoomPanel();
    }
    
    #endregion
    
    #region UI Updates
    
    void ShowMainPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (createRoomPanel != null) createRoomPanel.SetActive(false);
        if (inRoomPanel != null) inRoomPanel.SetActive(false);
        
        if (roomCodeInput != null) roomCodeInput.text = "";
    }
    
    void ShowCreatePanel()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (createRoomPanel != null) createRoomPanel.SetActive(true);
        if (inRoomPanel != null) inRoomPanel.SetActive(false);
        
        if (roomNameInput != null) roomNameInput.text = "";
        if (maxPlayersSlider != null) maxPlayersSlider.value = 10;
    }
    
    void ShowInRoomPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (createRoomPanel != null) createRoomPanel.SetActive(false);
        if (inRoomPanel != null) inRoomPanel.SetActive(true);
        
        UpdateInRoomPanel();
    }
    
    void UpdateInRoomPanel()
    {
        if (VRRoomManager.Instance == null) return;
        
        // Nom de la room
        if (currentRoomNameText != null)
        {
            string roomName = VRRoomManager.Instance.CurrentRoomName;
            currentRoomNameText.text = string.IsNullOrEmpty(roomName) ? "Room" : roomName;
        }
        
        // Code de la room
        if (currentRoomCodeText != null)
            currentRoomCodeText.text = $"Code: {VRRoomManager.Instance.CurrentRoomId}";
        
        // Nombre de joueurs
        if (currentPlayerCountText != null)
            currentPlayerCountText.text = $"\nPlayers: {VRRoomManager.Instance.PlayerCount}";
        
        // Liste des joueurs
        RefreshPlayerList();
    }
    
    void RefreshRoomList(Dictionary<string, RoomInfo> rooms)
    {
        foreach (var item in roomListItems)
        {
            Destroy(item);
        }
        roomListItems.Clear();
        
        if (roomListContainer == null || roomListItemPrefab == null) return;
        
        foreach (var kvp in rooms)
        {
            RoomInfo room = kvp.Value;
            
            GameObject item = Instantiate(roomListItemPrefab, roomListContainer);
            roomListItems.Add(item);
            
            var texts = item.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0)
            {
                texts[0].text = $"{room.roomName}";
            }
            if (texts.Length > 1)
            {
                texts[1].text = $"{room.playerCount}/{room.maxPlayers}";
            }
            
            var button = item.GetComponent<Button>();
            if (button == null)
            {
                button = item.AddComponent<Button>();
            }
            
            string roomId = room.roomId;
            button.onClick.AddListener(() => OnJoinRoomFromList(roomId));
            
            if (room.playerCount >= room.maxPlayers)
            {
                button.interactable = false;
                var colors = button.colors;
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                button.colors = colors;
            }
        }
    }
    
    void RefreshPlayerList()
    {
        foreach (var item in playerListItems)
        {
            Destroy(item);
        }
        playerListItems.Clear();
        
        if (playerListContainer == null || playerListItemPrefab == null) return;
        if (VRRoomManager.Instance == null) return;
        
        var players = VRRoomManager.Instance.GetPlayers();
        
        foreach (var player in players)
        {
            GameObject item = Instantiate(playerListItemPrefab, playerListContainer);
            playerListItems.Add(item);
            
            var text = item.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                string prefix = player.isHost ? "★ " : "• ";
                string suffix = player.playerId == VRNetworkManager.LocalId ? " (Vous)" : "";
                text.text = $"{prefix}{player.playerName}{suffix}";
            }
        }
    }
    
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