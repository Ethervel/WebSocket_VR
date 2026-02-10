using UnityEngine;

/// <summary>
/// Connecte automatiquement le SoundManager aux événements du jeu.
/// Ajouter ce script au même GameObject que SoundManager.
/// </summary>
[RequireComponent(typeof(SoundManager))]
public class SoundManagerIntegration : MonoBehaviour
{
    private SoundManager _soundManager;

    void Awake()
    {
        _soundManager = GetComponent<SoundManager>();
    }

    void OnEnable()
    {
        // Network events
        VRNetworkManager.OnConnected += OnNetworkConnected;
        VRNetworkManager.OnDisconnected += OnNetworkDisconnected;
        VRNetworkManager.OnConnectionError += OnConnectionError;

        // Room events
        VRRoomManager.OnRoomCreated += OnRoomCreated;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        VRRoomManager.OnPlayerJoined += OnPlayerJoined;
        VRRoomManager.OnPlayerLeft += OnPlayerLeft;

        // Voice events
        VoiceChatManager.OnVoiceChatReady += OnVoiceChatReady;
        VoiceChatManager.OnPeerVoiceConnected += OnPeerVoiceConnected;
        VoiceChatManager.OnPeerVoiceDisconnected += OnPeerVoiceDisconnected;

        // Game events
        VRGameManager.OnRemotePlayerSpawned += OnRemotePlayerSpawned;
        VRGameManager.OnRemotePlayerDespawned += OnRemotePlayerDespawned;

        // Scene events
        BootstrapManager.OnSceneReady += OnSceneReady;

        Debug.Log("[SoundIntegration] Events subscribed");
    }

    void OnDisable()
    {
        // Network events
        VRNetworkManager.OnConnected -= OnNetworkConnected;
        VRNetworkManager.OnDisconnected -= OnNetworkDisconnected;
        VRNetworkManager.OnConnectionError -= OnConnectionError;

        // Room events
        VRRoomManager.OnRoomCreated -= OnRoomCreated;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnPlayerJoined -= OnPlayerJoined;
        VRRoomManager.OnPlayerLeft -= OnPlayerLeft;

        // Voice events
        VoiceChatManager.OnVoiceChatReady -= OnVoiceChatReady;
        VoiceChatManager.OnPeerVoiceConnected -= OnPeerVoiceConnected;
        VoiceChatManager.OnPeerVoiceDisconnected -= OnPeerVoiceDisconnected;

        // Game events
        VRGameManager.OnRemotePlayerSpawned -= OnRemotePlayerSpawned;
        VRGameManager.OnRemotePlayerDespawned -= OnRemotePlayerDespawned;

        // Scene events
        BootstrapManager.OnSceneReady -= OnSceneReady;
    }

    // ==================== NETWORK ====================

    void OnNetworkConnected()
    {
        _soundManager?.PlayConnected();
        Debug.Log("[SoundIntegration] Connected sound");
    }

    void OnNetworkDisconnected()
    {
        _soundManager?.PlayDisconnected();
        Debug.Log("[SoundIntegration] Disconnected sound");
    }

    void OnConnectionError(string error)
    {
        _soundManager?.PlayError();
    }

    // ==================== ROOM ====================

    void OnRoomCreated(string roomId)
    {
        // Son désactivé
        // _soundManager?.PlayRoomCreated();
    }

    void OnRoomJoined(string roomId)
    {
        _soundManager?.PlayRoomJoined();
        Debug.Log($"[SoundIntegration] Room joined sound: {roomId}");
    }

    void OnRoomLeft()
    {
        _soundManager?.PlayBack();
    }

    void OnPlayerJoined(VRPlayerData player)
    {
        _soundManager?.PlayPlayerJoin();
        Debug.Log($"[SoundIntegration] Player join sound: {player.playerName}");
    }

    void OnPlayerLeft(string playerId)
    {
        _soundManager?.PlayPlayerLeave();
        Debug.Log($"[SoundIntegration] Player leave sound: {playerId}");
    }

    // ==================== VOICE ====================
    // Voice sounds disabled by user preference

    void OnVoiceChatReady()
    {
        // No sound
    }

    void OnPeerVoiceConnected(string peerId)
    {
        // No sound
    }

    void OnPeerVoiceDisconnected(string peerId)
    {
        // No sound
    }

    // ==================== GAME ====================

    void OnRemotePlayerSpawned(string playerId, GameObject player)
    {
        // Le son est déjà joué par OnPlayerJoined
    }

    void OnRemotePlayerDespawned(string playerId)
    {
        // Le son est déjà joué par OnPlayerLeft
    }

    // ==================== SCENE ====================

    void OnSceneReady(string sceneName)
    {
        if (sceneName == "Meet")
        {
            // Démarrer l'ambiance de la salle
            _soundManager?.PlayRoomAmbience();
        }
    }
}
