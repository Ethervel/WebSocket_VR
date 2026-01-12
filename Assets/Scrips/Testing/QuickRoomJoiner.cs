using UnityEngine;

/// <summary>
/// Script temporaire pour faciliter les tests de room
/// Ajouter à un GameObject vide dans la scène
/// </summary>
public class QuickRoomJoiner : MonoBehaviour
{
    [Header("Quick Actions")]
    [Tooltip("Créer automatiquement une room au démarrage")]
    public bool autoCreateRoomOnStart = false;

    [Tooltip("Joindre automatiquement cette room au démarrage (laisser vide pour créer)")]
    public string autoJoinRoomCode = "";

    [Header("Room Settings")]
    public RoomType defaultRoomType = RoomType.MeetingRoomA;
    public string defaultRoomName = "Test Room";

    [Header("UI Settings")]
    public bool showDebugUI = true;
    public KeyCode toggleUIKey = KeyCode.F1;

    [Header("Quick Keys")]
    public KeyCode createRoomKey = KeyCode.F2;
    public KeyCode leaveRoomKey = KeyCode.F3;

    private bool _showUI = true;
    private string _roomCodeInput = "";
    private string _roomNameInput = "Test Room";
    private Vector2 _scrollPosition;

    // GUI Style
    private GUIStyle _boxStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;
    private bool _stylesInitialized = false;

    // Retry limit for auto-connect
    private int _autoConnectRetryCount = 0;
    private const int MAX_AUTO_CONNECT_RETRIES = 10;

    void Start()
    {
        // Auto join/create au démarrage
        if (autoCreateRoomOnStart)
        {
            _autoConnectRetryCount = 0;
            Invoke(nameof(AutoJoinOrCreate), 2f); // Attendre 2s que le réseau se connecte
        }
    }

    void AutoJoinOrCreate()
    {
        if (VRNetworkManager.Instance == null || !VRNetworkManager.IsConnected)
        {
            _autoConnectRetryCount++;
            if (_autoConnectRetryCount >= MAX_AUTO_CONNECT_RETRIES)
            {
                Debug.LogError($"[QuickRoomJoiner] Abandon après {MAX_AUTO_CONNECT_RETRIES} tentatives de connexion");
                return;
            }
            Debug.LogWarning($"[QuickRoomJoiner] Réseau pas encore connecté, retry {_autoConnectRetryCount}/{MAX_AUTO_CONNECT_RETRIES}...");
            Invoke(nameof(AutoJoinOrCreate), 1f);
            return;
        }

        if (VRRoomManager.Instance == null)
        {
            Debug.LogError("[QuickRoomJoiner] VRRoomManager introuvable!");
            return;
        }

        if (!string.IsNullOrEmpty(autoJoinRoomCode))
        {
            Debug.Log($"[QuickRoomJoiner] Auto-join room: {autoJoinRoomCode}");
            VRRoomManager.Instance.JoinRoom(autoJoinRoomCode);
        }
        else
        {
            Debug.Log("[QuickRoomJoiner] Auto-create room");
            VRRoomManager.Instance.CreateRoom(defaultRoomType, defaultRoomName);
        }
    }

    void Update()
    {
        // Toggle UI
        if (Input.GetKeyDown(toggleUIKey))
        {
            _showUI = !_showUI;
        }

        // Quick create room
        if (Input.GetKeyDown(createRoomKey))
        {
            if (VRRoomManager.Instance != null && !VRRoomManager.Instance.IsInRoom)
            {
                VRRoomManager.Instance.CreateRoom(defaultRoomType, defaultRoomName);
                Debug.Log($"[QuickRoomJoiner] Room créée avec {createRoomKey}");
            }
        }

        // Quick leave room
        if (Input.GetKeyDown(leaveRoomKey))
        {
            if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
            {
                VRRoomManager.Instance.LeaveRoom();
                Debug.Log($"[QuickRoomJoiner] Room quittée avec {leaveRoomKey}");
            }
        }
    }

    void OnGUI()
    {
        if (!showDebugUI || !_showUI) return;

        InitializeStyles();

        // Fenêtre principale
        GUILayout.BeginArea(new Rect(Screen.width - 420, 10, 410, Screen.height - 20));
        
        GUILayout.BeginVertical(_boxStyle);
        
        // Header
        GUILayout.Label("🎮 QUICK ROOM JOINER", _labelStyle);
        GUILayout.Label($"Appuyez sur {toggleUIKey} pour masquer/afficher", GUI.skin.box);
        
        GUILayout.Space(10);

        // Status
        DrawStatus();
        
        GUILayout.Space(10);

        // Actions
        if (VRRoomManager.Instance != null && !VRRoomManager.Instance.IsInRoom)
        {
            DrawRoomCreation();
            GUILayout.Space(10);
            DrawRoomJoining();
            GUILayout.Space(10);
            DrawAvailableRooms();
        }
        else if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            DrawCurrentRoom();
            GUILayout.Space(10);
            DrawTeleportOptions();
        }
        
        GUILayout.Space(10);
        DrawQuickKeys();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void InitializeStyles()
    {
        if (_stylesInitialized) return;

        _boxStyle = new GUIStyle(GUI.skin.box);
        _boxStyle.padding = new RectOffset(10, 10, 10, 10);

        _buttonStyle = new GUIStyle(GUI.skin.button);
        _buttonStyle.fontSize = 12;
        _buttonStyle.padding = new RectOffset(10, 10, 5, 5);

        _labelStyle = new GUIStyle(GUI.skin.label);
        _labelStyle.fontSize = 14;
        _labelStyle.fontStyle = FontStyle.Bold;
        _labelStyle.alignment = TextAnchor.MiddleCenter;

        _stylesInitialized = true;
    }

    void DrawStatus()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("📊 STATUS", _labelStyle);
        
        // Réseau
        bool isConnected = VRNetworkManager.IsConnected;
        string networkStatus = isConnected ? "✅ Connecté" : "❌ Déconnecté";
        GUILayout.Label($"Réseau: {networkStatus}");
        
        if (isConnected)
        {
            GUILayout.Label($"ID: {VRNetworkManager.LocalId?.Substring(0, 8) ?? "N/A"}");
        }
        
        // Room
        if (VRRoomManager.Instance != null)
        {
            if (VRRoomManager.Instance.IsInRoom)
            {
                GUILayout.Label($"Room: ✅ {VRRoomManager.Instance.CurrentRoomId}");
                GUILayout.Label($"Nom: {VRRoomManager.Instance.CurrentRoomName}");
                GUILayout.Label($"Zone: {VRRoomManager.Instance.CurrentRoomType}");
                GUILayout.Label($"Joueurs: {VRRoomManager.Instance.PlayerCount}");
                GUILayout.Label($"Host: {(VRRoomManager.Instance.IsHost ? "✅ Oui" : "❌ Non")}");
            }
            else
            {
                GUILayout.Label("Room: ❌ Aucune");
            }
        }
        
        GUILayout.EndVertical();
    }

    void DrawRoomCreation()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("➕ CRÉER UNE ROOM", _labelStyle);
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("Nom:", GUILayout.Width(60));
        _roomNameInput = GUILayout.TextField(_roomNameInput, GUILayout.Width(200));
        GUILayout.EndHorizontal();
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("Type:", GUILayout.Width(60));
        if (GUILayout.Button("Lobby", GUILayout.Width(65)))
            defaultRoomType = RoomType.Lobby;
        if (GUILayout.Button("Room A", GUILayout.Width(65)))
            defaultRoomType = RoomType.MeetingRoomA;
        if (GUILayout.Button("Room B", GUILayout.Width(65)))
            defaultRoomType = RoomType.MeetingRoomB;
        GUILayout.EndHorizontal();
        
        GUILayout.Label($"Sélectionné: {defaultRoomType}", GUI.skin.box);
        
        if (GUILayout.Button("🎮 CRÉER ROOM", _buttonStyle, GUILayout.Height(40)))
        {
            if (VRRoomManager.Instance != null)
            {
                string roomName = string.IsNullOrEmpty(_roomNameInput) ? "Test Room" : _roomNameInput;
                VRRoomManager.Instance.CreateRoom(defaultRoomType, roomName);
                Debug.Log($"[QuickRoomJoiner] Room créée: {roomName}");
            }
        }
        
        GUILayout.EndVertical();
    }

    void DrawRoomJoining()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("🚪 REJOINDRE UNE ROOM", _labelStyle);
        
        GUILayout.BeginHorizontal();
        GUILayout.Label("Code:", GUILayout.Width(60));
        _roomCodeInput = GUILayout.TextField(_roomCodeInput.ToUpper(), 6, GUILayout.Width(100));
        GUILayout.EndHorizontal();
        
        if (GUILayout.Button("🔗 REJOINDRE", _buttonStyle, GUILayout.Height(40)))
        {
            if (!string.IsNullOrEmpty(_roomCodeInput) && VRRoomManager.Instance != null)
            {
                VRRoomManager.Instance.JoinRoom(_roomCodeInput);
                Debug.Log($"[QuickRoomJoiner] Tentative de join: {_roomCodeInput}");
            }
            else
            {
                Debug.LogWarning("[QuickRoomJoiner] Code room vide ou VRRoomManager null");
            }
        }
        
        GUILayout.EndVertical();
    }

    void DrawAvailableRooms()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("📋 ROOMS DISPONIBLES", _labelStyle);
        
        if (VRRoomManager.Instance != null)
        {
            var rooms = VRRoomManager.Instance.GetAvailableRooms();
            
            if (rooms.Count == 0)
            {
                GUILayout.Label("Aucune room disponible", GUI.skin.box);
                
                if (GUILayout.Button("🔄 Rafraîchir"))
                {
                    VRRoomManager.Instance.RequestRoomList();
                }
            }
            else
            {
                _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(150));
                
                foreach (var room in rooms.Values)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"🎮 {room.roomName}", _labelStyle);
                    GUILayout.Label($"Code: {room.roomId}");
                    GUILayout.Label($"Joueurs: {room.playerCount}/{room.maxPlayers}");
                    GUILayout.Label($"Type: {room.roomType}");
                    
                    bool isFull = room.playerCount >= room.maxPlayers;
                    GUI.enabled = !isFull;
                    
                    if (GUILayout.Button(isFull ? "COMPLET" : $"Rejoindre {room.roomId}"))
                    {
                        VRRoomManager.Instance.JoinRoom(room.roomId);
                    }
                    
                    GUI.enabled = true;
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                }
                
                GUILayout.EndScrollView();
            }
        }
        
        GUILayout.EndVertical();
    }

    void DrawCurrentRoom()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("🎮 ROOM ACTUELLE", _labelStyle);
        
        GUILayout.Label($"Code: {VRRoomManager.Instance.CurrentRoomId}", GUI.skin.box);
        GUILayout.Label($"Nom: {VRRoomManager.Instance.CurrentRoomName}");
        
        if (GUILayout.Button("🚪 QUITTER LA ROOM", _buttonStyle, GUILayout.Height(40)))
        {
            VRRoomManager.Instance.LeaveRoom();
            Debug.Log("[QuickRoomJoiner] Room quittée");
        }
        
        GUILayout.EndVertical();
    }

    void DrawTeleportOptions()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("🌀 TÉLÉPORTATION", _labelStyle);
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Lobby", GUILayout.Height(35)))
        {
            VRRoomManager.Instance.TeleportToRoomType(RoomType.Lobby);
        }
        if (GUILayout.Button("Room A", GUILayout.Height(35)))
        {
            VRRoomManager.Instance.TeleportToRoomType(RoomType.MeetingRoomA);
        }
        if (GUILayout.Button("Room B", GUILayout.Height(35)))
        {
            VRRoomManager.Instance.TeleportToRoomType(RoomType.MeetingRoomB);
        }
        GUILayout.EndHorizontal();
        
        GUILayout.EndVertical();
    }

    void DrawQuickKeys()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("⌨️ RACCOURCIS", _labelStyle);
        GUILayout.Label($"{toggleUIKey} - Toggle UI");
        GUILayout.Label($"{createRoomKey} - Créer Room rapide");
        GUILayout.Label($"{leaveRoomKey} - Quitter Room");
        GUILayout.EndVertical();
    }

    void OnEnable()
    {
        // Subscribe aux événements pour logs
        VRRoomManager.OnRoomCreated += OnRoomCreated;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnRoomError += OnRoomError;
    }

    void OnDisable()
    {
        VRRoomManager.OnRoomCreated -= OnRoomCreated;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnRoomError -= OnRoomError;
    }

    void OnRoomCreated(string roomId)
    {
        Debug.Log($"✅ [QuickRoomJoiner] Room créée: {roomId}");
    }

    void OnRoomJoined(string roomId)
    {
        Debug.Log($"✅ [QuickRoomJoiner] Room rejointe: {roomId}");
    }

    void OnRoomLeft()
    {
        Debug.Log($"🚪 [QuickRoomJoiner] Room quittée");
    }

    void OnRoomError(string error)
    {
        Debug.LogError($"❌ [QuickRoomJoiner] Erreur: {error}");
    }
}