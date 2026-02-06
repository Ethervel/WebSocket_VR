using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;

namespace VoiceChat
{
    /// <summary>
    /// Manages WebRTC peer connection lifecycle.
    /// Handles creation, offer/answer exchange, ICE candidates, and cleanup.
    /// </summary>
    public class WebRTCPeerManager : MonoBehaviour
    {
        public static WebRTCPeerManager Instance { get; private set; }

        [Header("Connection Settings")]
        [Tooltip("Timeout in seconds for peer connections that don't complete")]
        public float peerConnectionTimeout = 15f;

        [Header("Debug")]
        public bool showDebugInfo = true;

        // Events
        public static event Action<string> OnPeerConnected;
        public static event Action<string> OnPeerDisconnected;

        // Peer connections
        private readonly Dictionary<string, RTCPeerConnection> _peerConnections = new Dictionary<string, RTCPeerConnection>();
        private readonly Dictionary<string, float> _pendingConnectionStartTimes = new Dictionary<string, float>();

        // Dependencies
        private WebRTCConfiguration _config;
        private WebRTCSignaling _signaling;
        private MicrophoneManager _micManager;
        private RemoteAudioManager _audioManager;

        private RTCConfiguration _rtcConfig;
        private Coroutine _timeoutCheckCoroutine;
        private bool _isCleaningUp = false;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            CleanupAll();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Initializes the peer manager with required dependencies.
        /// </summary>
        public void Initialize(WebRTCConfiguration config, WebRTCSignaling signaling,
                               MicrophoneManager micManager, RemoteAudioManager audioManager)
        {
            _config = config;
            _signaling = signaling;
            _micManager = micManager;
            _audioManager = audioManager;

            // Get RTCConfiguration
            _rtcConfig = _config.GetConfiguration();

            // Subscribe to signaling events
            _signaling.OnOfferReceived += HandleOfferReceived;
            _signaling.OnAnswerReceived += HandleAnswerReceived;
            _signaling.OnIceCandidateReceived += HandleIceCandidateReceived;

            // Start timeout checker
            if (_timeoutCheckCoroutine != null)
                StopCoroutine(_timeoutCheckCoroutine);
            _timeoutCheckCoroutine = StartCoroutine(ConnectionTimeoutChecker());

            LogDebug("Initialized");
        }

        /// <summary>
        /// Handles a player joining the room.
        /// Uses mesh topology: lower ID initiates connection.
        /// </summary>
        public void OnPlayerJoined(VRPlayerData player)
        {
            if (_isCleaningUp) return;
            if (player.playerId == VRNetworkManager.LocalId) return;

            string localId = VRNetworkManager.LocalId;

            // Mesh topology: player with smaller ID initiates connection
            if (string.Compare(localId, player.playerId, StringComparison.Ordinal) < 0)
            {
                LogDebug($"MESH: {localId} < {player.playerId} - Initiating connection");
                StartCoroutine(CreatePeerConnection(player.playerId, true));
            }
            else
            {
                LogDebug($"MESH: {localId} > {player.playerId} - Waiting for them to initiate");
            }
        }

        /// <summary>
        /// Handles a player leaving the room.
        /// </summary>
        public void OnPlayerLeft(string playerId)
        {
            LogDebug($"Player left: {playerId}");
            ClosePeerConnection(playerId);
        }

        /// <summary>
        /// Handles leaving the current room.
        /// </summary>
        public void OnRoomLeft()
        {
            LogDebug("Left room, closing all connections");
            CloseAllConnections();
        }

        /// <summary>
        /// Creates a peer connection to a remote player.
        /// </summary>
        public IEnumerator CreatePeerConnection(string peerId, bool createOffer)
        {
            if (_isCleaningUp) yield break;

            if (_peerConnections.ContainsKey(peerId))
            {
                LogDebug($"Peer connection already exists for: {peerId}");
                yield break;
            }

            LogDebug($"Creating peer connection for: {peerId} (initiator: {createOffer})");

            var pc = new RTCPeerConnection(ref _rtcConfig);
            _peerConnections[peerId] = pc;
            _pendingConnectionStartTimes[peerId] = Time.time;

            // Create audio source for this peer
            AudioSource audioSource = _audioManager?.CreateAudioSourceForPlayer(peerId);

            // Set up event handlers
            pc.OnIceCandidate = candidate =>
            {
                if (candidate != null)
                {
                    _signaling?.SendIceCandidate(peerId, candidate);
                }
            };

            pc.OnIceConnectionChange = state =>
            {
                if (_isCleaningUp) return;

                LogDebug($"ICE state for {peerId}: {state}");

                if (state == RTCIceConnectionState.Connected)
                {
                    _pendingConnectionStartTimes.Remove(peerId);
                    OnPeerConnected?.Invoke(peerId);
                }
                else if (state == RTCIceConnectionState.Disconnected ||
                         state == RTCIceConnectionState.Failed)
                {
                    _pendingConnectionStartTimes.Remove(peerId);
                    OnPeerDisconnected?.Invoke(peerId);
                }
            };

            pc.OnTrack = e =>
            {
                if (_isCleaningUp) return;

                LogDebug($"Received track from {peerId}");

                if (e.Track is AudioStreamTrack audioTrack && audioSource != null)
                {
                    _audioManager?.SetTrackOnPlayer(peerId, audioTrack);
                }
            };

            // Add local audio track if microphone is active
            if (_micManager != null && _micManager.IsActive && _micManager.LocalAudioTrack != null)
            {
                pc.AddTrack(_micManager.LocalAudioTrack, _micManager.LocalStream);
                LogDebug($"Added local audio track to {peerId}");
            }

            // Create offer if we're the initiator
            if (createOffer)
            {
                yield return StartCoroutine(CreateAndSendOffer(peerId, pc));
            }
        }

        private IEnumerator CreateAndSendOffer(string peerId, RTCPeerConnection pc)
        {
            var createOp = pc.CreateOffer();
            yield return createOp;

            if (createOp.IsError)
            {
                Debug.LogError($"[WebRTCPeer] Error creating offer: {createOp.Error.message}");
                yield break;
            }

            var desc = createOp.Desc;
            var setOp = pc.SetLocalDescription(ref desc);
            yield return setOp;

            if (setOp.IsError)
            {
                Debug.LogError($"[WebRTCPeer] Error setting local description: {setOp.Error.message}");
                yield break;
            }

            _signaling?.SendOffer(peerId, desc);
            LogDebug($"Sent offer to: {peerId}");
        }

        private void HandleOfferReceived(string peerId, SignalingData data)
        {
            if (_isCleaningUp) return;
            StartCoroutine(ProcessOffer(peerId, data));
        }

        private IEnumerator ProcessOffer(string peerId, SignalingData data)
        {
            // Create connection if it doesn't exist
            if (!_peerConnections.ContainsKey(peerId))
            {
                yield return StartCoroutine(CreatePeerConnection(peerId, false));
            }

            if (!_peerConnections.TryGetValue(peerId, out var pc))
            {
                Debug.LogError($"[WebRTCPeer] No peer connection for: {peerId}");
                yield break;
            }

            // Set remote description
            var desc = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = data.sdp
            };

            var setRemoteOp = pc.SetRemoteDescription(ref desc);
            yield return setRemoteOp;

            if (setRemoteOp.IsError)
            {
                Debug.LogError($"[WebRTCPeer] Error setting remote description: {setRemoteOp.Error.message}");
                yield break;
            }

            // Create and send answer
            var createAnswerOp = pc.CreateAnswer();
            yield return createAnswerOp;

            if (createAnswerOp.IsError)
            {
                Debug.LogError($"[WebRTCPeer] Error creating answer: {createAnswerOp.Error.message}");
                yield break;
            }

            var answerDesc = createAnswerOp.Desc;
            var setLocalOp = pc.SetLocalDescription(ref answerDesc);
            yield return setLocalOp;

            if (setLocalOp.IsError)
            {
                Debug.LogError($"[WebRTCPeer] Error setting local description: {setLocalOp.Error.message}");
                yield break;
            }

            _signaling?.SendAnswer(peerId, answerDesc);
            LogDebug($"Sent answer to: {peerId}");
        }

        private void HandleAnswerReceived(string peerId, SignalingData data)
        {
            if (_isCleaningUp) return;
            StartCoroutine(ProcessAnswer(peerId, data));
        }

        private IEnumerator ProcessAnswer(string peerId, SignalingData data)
        {
            if (!_peerConnections.TryGetValue(peerId, out var pc))
            {
                Debug.LogError($"[WebRTCPeer] No peer connection for answer: {peerId}");
                yield break;
            }

            var desc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = data.sdp
            };

            var op = pc.SetRemoteDescription(ref desc);
            yield return op;

            if (op.IsError)
            {
                Debug.LogError($"[WebRTCPeer] Error setting remote description: {op.Error.message}");
            }
            else
            {
                LogDebug($"Processed answer from: {peerId}");
            }
        }

        private void HandleIceCandidateReceived(string peerId, IceCandidateData data)
        {
            if (_isCleaningUp) return;

            if (!_peerConnections.TryGetValue(peerId, out var pc))
            {
                Debug.LogWarning($"[WebRTCPeer] No peer connection for ICE candidate: {peerId}");
                return;
            }

            var candidateInit = new RTCIceCandidateInit
            {
                candidate = data.candidate,
                sdpMid = data.sdpMid,
                sdpMLineIndex = data.sdpMLineIndex
            };

            var candidate = new RTCIceCandidate(candidateInit);
            pc.AddIceCandidate(candidate);

            LogDebug($"Added ICE candidate from: {peerId}");
        }

        /// <summary>
        /// Adds local audio track to all existing peer connections.
        /// Called when microphone is started after connections exist.
        /// </summary>
        public void AddLocalTrackToAllPeers()
        {
            if (_micManager == null || !_micManager.IsActive || _micManager.LocalAudioTrack == null)
                return;

            foreach (var kvp in _peerConnections)
            {
                kvp.Value.AddTrack(_micManager.LocalAudioTrack, _micManager.LocalStream);
                LogDebug($"Added audio track to peer: {kvp.Key}");
            }
        }

        /// <summary>
        /// Closes a specific peer connection.
        /// </summary>
        public void ClosePeerConnection(string peerId)
        {
            _pendingConnectionStartTimes.Remove(peerId);

            // Close audio for this peer
            _audioManager?.CloseAudioForPlayer(peerId);

            // Close peer connection
            if (_peerConnections.TryGetValue(peerId, out var pc))
            {
                try
                {
                    pc.OnIceCandidate = null;
                    pc.OnIceConnectionChange = null;
                    pc.OnTrack = null;
                    pc.Close();
                    pc.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[WebRTCPeer] Error disposing peer connection: {e.Message}");
                }
                _peerConnections.Remove(peerId);
            }

            LogDebug($"Closed peer connection: {peerId}");

            if (!_isCleaningUp)
            {
                OnPeerDisconnected?.Invoke(peerId);
            }
        }

        /// <summary>
        /// Closes all peer connections.
        /// </summary>
        public void CloseAllConnections()
        {
            var peerIds = new List<string>(_peerConnections.Keys);
            foreach (var peerId in peerIds)
            {
                ClosePeerConnection(peerId);
            }
        }

        /// <summary>
        /// Checks if a specific player is connected.
        /// </summary>
        public bool IsPlayerConnected(string playerId)
        {
            return _peerConnections.ContainsKey(playerId);
        }

        /// <summary>
        /// Returns the number of active connections.
        /// </summary>
        public int GetActiveConnectionCount()
        {
            return _peerConnections.Count;
        }

        /// <summary>
        /// Gets a peer connection by player ID.
        /// </summary>
        public RTCPeerConnection GetPeerConnection(string playerId)
        {
            _peerConnections.TryGetValue(playerId, out var pc);
            return pc;
        }

        /// <summary>
        /// Coroutine that periodically checks for timed out connections.
        /// </summary>
        private IEnumerator ConnectionTimeoutChecker()
        {
            var checkInterval = new WaitForSeconds(2f);

            while (true)
            {
                yield return checkInterval;

                if (_pendingConnectionStartTimes.Count == 0)
                    continue;

                float currentTime = Time.time;
                var timedOutPeers = new List<string>();

                foreach (var kvp in _pendingConnectionStartTimes)
                {
                    string peerId = kvp.Key;
                    float startTime = kvp.Value;

                    if (currentTime - startTime > peerConnectionTimeout)
                    {
                        if (_peerConnections.TryGetValue(peerId, out var pc))
                        {
                            var state = pc.IceConnectionState;
                            if (state != RTCIceConnectionState.Connected &&
                                state != RTCIceConnectionState.Completed)
                            {
                                timedOutPeers.Add(peerId);
                                Debug.LogWarning($"[WebRTCPeer] Connection to {peerId} timed out (state: {state})");
                            }
                        }
                    }
                }

                foreach (string peerId in timedOutPeers)
                {
                    _pendingConnectionStartTimes.Remove(peerId);
                    ClosePeerConnection(peerId);
                }
            }
        }

        private void CleanupAll()
        {
            _isCleaningUp = true;

            if (_timeoutCheckCoroutine != null)
            {
                StopCoroutine(_timeoutCheckCoroutine);
                _timeoutCheckCoroutine = null;
            }

            // Unsubscribe from signaling
            if (_signaling != null)
            {
                _signaling.OnOfferReceived -= HandleOfferReceived;
                _signaling.OnAnswerReceived -= HandleAnswerReceived;
                _signaling.OnIceCandidateReceived -= HandleIceCandidateReceived;
            }

            _pendingConnectionStartTimes.Clear();
            CloseAllConnections();

            _isCleaningUp = false;
            LogDebug("Cleanup complete");
        }

        private void LogDebug(string message)
        {
            if (showDebugInfo)
                Debug.Log($"[WebRTCPeer] {message}");
        }
    }
}
