using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// UI pour sélectionner le microphone et contrôler le voice chat
public class VoiceChatUI : MonoBehaviour
{
    [Header("Microphone Selection")]
    [Tooltip("Dropdown pour sélectionner le microphone")]
    public TMP_Dropdown microphoneDropdown;
    
    [Tooltip("Bouton pour rafraîchir la liste des micros")]
    public Button refreshMicrophoneButton;
    
    [Header("Microphone Control")]
    [Tooltip("Toggle pour activer/désactiver le micro")]
    public Toggle microphoneToggle;
    
    [Tooltip("Texte affichant l'état du micro")]
    public TextMeshProUGUI microphoneStatusText;
    
    [Tooltip("Image/Icon du micro (optionnel)")]
    public Image microphoneIcon;
    
    [Tooltip("Sprite quand le micro est ON")]
    public Sprite micOnSprite;
    
    [Tooltip("Sprite quand le micro est OFF")]
    public Sprite micOffSprite;
    
    [Header("Volume Control")]
    [Tooltip("Slider pour le volume du micro")]
    public Slider microphoneVolumeSlider;
    
    [Tooltip("Texte affichant le volume du micro")]
    public TextMeshProUGUI microphoneVolumeText;
    
    [Tooltip("Slider pour le volume des autres joueurs")]
    public Slider playbackVolumeSlider;
    
    [Tooltip("Texte affichant le volume de playback")]
    public TextMeshProUGUI playbackVolumeText;
    
    [Header("Voice Activity")]
    [Tooltip("Afficher qui parle actuellement")]
    public Transform voiceActivityContainer;
    
    [Tooltip("Prefab pour afficher un joueur qui parle")]
    public GameObject voiceActivityItemPrefab;
    
    [Header("Push-to-Talk")]
    [Tooltip("Toggle pour activer/désactiver Push-to-Talk")]
    public Toggle pushToTalkToggle;
    
    [Tooltip("Texte affichant la touche PTT")]
    public TextMeshProUGUI pushToTalkKeyText;
    
    [Header("Connection Status")]
    [Tooltip("Texte affichant le nombre de connexions vocales")]
    public TextMeshProUGUI connectionCountText;
    
    [Tooltip("Container pour la liste des joueurs connectés")]
    public Transform connectedPlayersContainer;
    
    [Tooltip("Prefab pour un joueur connecté")]
    public GameObject connectedPlayerItemPrefab;
    
    // État
    private Dictionary<string, GameObject> _connectedPlayerItems = new Dictionary<string, GameObject>();
    
    void Start()
    {
        InitializeUI();
        RegisterEvents();
    }
    
    void OnDestroy()
    {
        UnregisterEvents();
    }
    
    void Update()
    {
        UpdateConnectionStatus();
    }
    
    #region Initialization
    
    void InitializeUI()
    {
        // Microphone dropdown
        if (microphoneDropdown != null)
        {
            RefreshMicrophoneList();
            microphoneDropdown.onValueChanged.AddListener(OnMicrophoneSelected);
        }
        
        // Refresh button
        if (refreshMicrophoneButton != null)
        {
            refreshMicrophoneButton.onClick.AddListener(RefreshMicrophoneList);
        }
        
        // Microphone toggle
        if (microphoneToggle != null)
        {
            microphoneToggle.isOn = VoiceChatManager.Instance?.IsMicrophoneActive ?? false;
            microphoneToggle.onValueChanged.AddListener(OnMicrophoneToggleChanged);
        }
        
        // Volume sliders
        if (microphoneVolumeSlider != null)
        {
            microphoneVolumeSlider.value = VoiceChatManager.Instance?.microphoneVolume ?? 1f;
            microphoneVolumeSlider.onValueChanged.AddListener(OnMicrophoneVolumeChanged);
        }
        
        if (playbackVolumeSlider != null)
        {
            playbackVolumeSlider.value = VoiceChatManager.Instance?.playbackVolume ?? 0.8f;
            playbackVolumeSlider.onValueChanged.AddListener(OnPlaybackVolumeChanged);
        }
        
        // Push-to-talk toggle
        if (pushToTalkToggle != null)
        {
            pushToTalkToggle.isOn = VoiceChatManager.Instance?.usePushToTalk ?? false;
            pushToTalkToggle.onValueChanged.AddListener(OnPushToTalkToggleChanged);
        }
        
        UpdateAllUI();
    }
    
    void RegisterEvents()
    {
        if (VoiceChatManager.Instance != null)
        {
            VoiceChatManager.OnVoiceChatReady += OnVoiceChatReady;
            VoiceChatManager.OnMicrophoneStateChanged += OnMicrophoneStateChanged;
            VoiceChatManager.OnPeerVoiceConnected += OnPeerConnected;
            VoiceChatManager.OnPeerVoiceDisconnected += OnPeerDisconnected;
        }
    }
    
    void UnregisterEvents()
    {
        if (VoiceChatManager.Instance != null)
        {
            VoiceChatManager.OnVoiceChatReady -= OnVoiceChatReady;
            VoiceChatManager.OnMicrophoneStateChanged -= OnMicrophoneStateChanged;
            VoiceChatManager.OnPeerVoiceConnected -= OnPeerConnected;
            VoiceChatManager.OnPeerVoiceDisconnected -= OnPeerDisconnected;
        }
    }
    
    #endregion
    
    #region Microphone Selection
    
    void RefreshMicrophoneList()
    {
        if (microphoneDropdown == null) return;
        if (VoiceChatManager.Instance == null) return;
        
        string[] devices = VoiceChatManager.Instance.GetAvailableMicrophones();
        
        microphoneDropdown.ClearOptions();
        
        if (devices.Length == 0)
        {
            microphoneDropdown.options.Add(new TMP_Dropdown.OptionData("No microphone found"));
            microphoneDropdown.interactable = false;
            Debug.LogWarning("[VoiceChatUI] No microphone devices found!");
            return;
        }
        
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        
        foreach (string device in devices)
        {
            options.Add(new TMP_Dropdown.OptionData(device));
        }
        
        microphoneDropdown.AddOptions(options);
        microphoneDropdown.interactable = true;
        
        // Sélectionner le premier par défaut
        microphoneDropdown.value = 0;
        
        Debug.Log($"[VoiceChatUI] Found {devices.Length} microphone(s)");
    }
    
    void OnMicrophoneSelected(int index)
    {
        if (VoiceChatManager.Instance == null) return;
        
        string[] devices = VoiceChatManager.Instance.GetAvailableMicrophones();
        
        if (index >= 0 && index < devices.Length)
        {
            string selectedDevice = devices[index];
            VoiceChatManager.Instance.SetMicrophone(selectedDevice);
            
            Debug.Log($"[VoiceChatUI] Selected microphone: {selectedDevice}");
            
            // Redémarrer le micro si il était actif
            if (VoiceChatManager.Instance.IsMicrophoneActive)
            {
                VoiceChatManager.Instance.StartMicrophone();
            }
        }
    }
    
    #endregion
    
    #region Microphone Control
    
    void OnMicrophoneToggleChanged(bool isOn)
    {
        if (VoiceChatManager.Instance == null) return;
        
        if (isOn)
        {
            VoiceChatManager.Instance.StartMicrophone();
        }
        else
        {
            VoiceChatManager.Instance.StopMicrophone();
        }
    }
    
    void OnMicrophoneStateChanged(bool isActive)
    {
        UpdateMicrophoneStatus(isActive);
    }
    
    void UpdateMicrophoneStatus(bool isActive)
    {
        // Update toggle
        if (microphoneToggle != null)
        {
            microphoneToggle.isOn = isActive;
        }
        
        // Update status text
        if (microphoneStatusText != null)
        {
            microphoneStatusText.text = isActive ? "Microphone ON" : "Microphone OFF";
            microphoneStatusText.color = isActive ? Color.green : Color.red;
        }
        
        // Update icon
        if (microphoneIcon != null)
        {
            if (isActive && micOnSprite != null)
            {
                microphoneIcon.sprite = micOnSprite;
                microphoneIcon.color = Color.white;
            }
            else if (!isActive && micOffSprite != null)
            {
                microphoneIcon.sprite = micOffSprite;
                microphoneIcon.color = Color.gray;
            }
        }
    }
    
    #endregion
    
    #region Volume Control
    
    void OnMicrophoneVolumeChanged(float value)
    {
        if (VoiceChatManager.Instance == null) return;
        
        VoiceChatManager.Instance.SetMicrophoneVolume(value);
        
        if (microphoneVolumeText != null)
        {
            microphoneVolumeText.text = $"{(int)(value * 100)}%";
        }
    }
    
    void OnPlaybackVolumeChanged(float value)
    {
        if (VoiceChatManager.Instance == null) return;
        
        VoiceChatManager.Instance.SetPlaybackVolume(value);
        
        if (playbackVolumeText != null)
        {
            playbackVolumeText.text = $"{(int)(value * 100)}%";
        }
    }
    
    #endregion
    
    #region Push-to-Talk
    
    void OnPushToTalkToggleChanged(bool isOn)
    {
        if (VoiceChatManager.Instance == null) return;
        
        VoiceChatManager.Instance.usePushToTalk = isOn;
        
        if (pushToTalkKeyText != null)
        {
            if (isOn)
            {
                pushToTalkKeyText.text = $"Hold [{VoiceChatManager.Instance.pushToTalkKey}] to talk";
            }
            else
            {
                pushToTalkKeyText.text = "Always listening";
            }
        }
        
        // Si on désactive PTT, démarrer le micro automatiquement
        if (!isOn && !VoiceChatManager.Instance.IsMicrophoneActive)
        {
            VoiceChatManager.Instance.StartMicrophone();
        }
    }
    
    #endregion
    
    #region Connection Status
    
    void UpdateConnectionStatus()
    {
        if (VoiceChatManager.Instance == null) return;
        
        int connectionCount = VoiceChatManager.Instance.GetActiveConnectionCount();
        
        if (connectionCountText != null)
        {
            connectionCountText.text = $"Voice Connections: {connectionCount}";
        }
    }
    
    void OnPeerConnected(string playerId)
    {
        Debug.Log($"[VoiceChatUI] Peer voice connected: {playerId}");
        AddConnectedPlayer(playerId);
    }
    
    void OnPeerDisconnected(string playerId)
    {
        Debug.Log($"[VoiceChatUI] Peer voice disconnected: {playerId}");
        RemoveConnectedPlayer(playerId);
    }
    
    void AddConnectedPlayer(string playerId)
    {
        if (connectedPlayersContainer == null || connectedPlayerItemPrefab == null) return;
        if (_connectedPlayerItems.ContainsKey(playerId)) return;
        
        GameObject item = Instantiate(connectedPlayerItemPrefab, connectedPlayersContainer);
        
        // Trouver le nom du joueur
        string playerName = GetPlayerName(playerId);
        
        var text = item.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
        {
            text.text = $"🎤 {playerName}";
        }
        
        // Ajouter un bouton mute (optionnel)
        var muteButton = item.GetComponentInChildren<Button>();
        if (muteButton != null)
        {
            muteButton.onClick.AddListener(() => TogglePlayerMute(playerId));
        }
        
        _connectedPlayerItems[playerId] = item;
    }
    
    void RemoveConnectedPlayer(string playerId)
    {
        if (_connectedPlayerItems.TryGetValue(playerId, out GameObject item))
        {
            Destroy(item);
            _connectedPlayerItems.Remove(playerId);
        }
    }
    
    void TogglePlayerMute(string playerId)
    {
        // Fonctionnalité mute par joueur non implémentée
        // Nécessite: VoiceChatManager.IsPlayerMuted() et SetPlayerMuted()
        Debug.LogWarning($"[VoiceChatUI] Mute non implémenté pour: {playerId}");
    }
    
    string GetPlayerName(string playerId)
    {
        // Essayer de récupérer le nom via VRRoomManager
        if (VRRoomManager.Instance != null)
        {
            var players = VRRoomManager.Instance.GetPlayers();
            foreach (var player in players)
            {
                if (player.playerId == playerId)
                {
                    return player.playerName;
                }
            }
        }
        
        // Fallback: afficher une partie de l'ID
        return playerId.Substring(0, Mathf.Min(8, playerId.Length));
    }
    
    #endregion
    
    #region Events
    
    void OnVoiceChatReady()
    {
        Debug.Log("[VoiceChatUI] Voice chat ready!");
        RefreshMicrophoneList();
        UpdateAllUI();
    }
    
    #endregion
    
    #region Helpers
    
    void UpdateAllUI()
    {
        if (VoiceChatManager.Instance == null) return;
        
        UpdateMicrophoneStatus(VoiceChatManager.Instance.IsMicrophoneActive);
        
        if (microphoneVolumeSlider != null)
        {
            OnMicrophoneVolumeChanged(microphoneVolumeSlider.value);
        }
        
        if (playbackVolumeSlider != null)
        {
            OnPlaybackVolumeChanged(playbackVolumeSlider.value);
        }
        
        if (pushToTalkToggle != null)
        {
            OnPushToTalkToggleChanged(pushToTalkToggle.isOn);
        }
    }
    
    #endregion
    
    #region Public API
    
    /// Affiche/Cache le panneau de voice chat
    public void SetPanelVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }
    
    /// Force le refresh de la liste des micros
    public void ForceRefreshMicrophones()
    {
        RefreshMicrophoneList();
    }
    
    #endregion
}