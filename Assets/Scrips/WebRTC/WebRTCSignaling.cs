using System;
using UnityEngine;
using Unity.WebRTC;

namespace VoiceChat
{
    /// <summary>
    /// Handles WebRTC signaling protocol (offer/answer/ICE candidates).
    /// Routes messages via VRNetworkManager.
    /// </summary>
    public class WebRTCSignaling : MonoBehaviour
    {
        public static WebRTCSignaling Instance { get; private set; }

        [Header("Debug")]
        public bool showDebugInfo = true;

        // Events for signaling messages
        public event Action<string, SignalingData> OnOfferReceived;
        public event Action<string, SignalingData> OnAnswerReceived;
        public event Action<string, IceCandidateData> OnIceCandidateReceived;

        private bool _isActive = true;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnEnable()
        {
            VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        }

        void OnDisable()
        {
            VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        }

        void OnDestroy()
        {
            _isActive = false;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Processes incoming network messages for WebRTC signaling.
        /// </summary>
        private void HandleNetworkMessage(NetworkMessage msg)
        {
            if (!_isActive) return;

            switch (msg.type)
            {
                case "webrtc-offer":
                    HandleOffer(msg);
                    break;
                case "webrtc-answer":
                    HandleAnswer(msg);
                    break;
                case "webrtc-ice-candidate":
                    HandleIceCandidate(msg);
                    break;
            }
        }

        private void HandleOffer(NetworkMessage msg)
        {
            try
            {
                var data = JsonUtility.FromJson<SignalingData>(msg.data);
                string peerId = msg.senderId;

                if (string.IsNullOrEmpty(data?.sdp))
                {
                    Debug.LogError($"[WebRTCSignaling] Received offer with empty SDP from: {peerId}");
                    return;
                }

                LogDebug($"Received offer from: {peerId}");
                OnOfferReceived?.Invoke(peerId, data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCSignaling] Error parsing offer: {e.Message}");
            }
        }

        private void HandleAnswer(NetworkMessage msg)
        {
            try
            {
                var data = JsonUtility.FromJson<SignalingData>(msg.data);
                string peerId = msg.senderId;

                if (string.IsNullOrEmpty(data?.sdp))
                {
                    Debug.LogError($"[WebRTCSignaling] Received answer with empty SDP from: {peerId}");
                    return;
                }

                LogDebug($"Received answer from: {peerId}");
                OnAnswerReceived?.Invoke(peerId, data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCSignaling] Error parsing answer: {e.Message}");
            }
        }

        private void HandleIceCandidate(NetworkMessage msg)
        {
            try
            {
                var data = JsonUtility.FromJson<IceCandidateData>(msg.data);
                string peerId = msg.senderId;

                if (string.IsNullOrEmpty(data?.candidate))
                {
                    // ICE complete signal - this is normal
                    return;
                }

                LogDebug($"Received ICE candidate from: {peerId}");
                OnIceCandidateReceived?.Invoke(peerId, data);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCSignaling] Error parsing ICE candidate: {e.Message}");
            }
        }

        /// <summary>
        /// Sends an SDP offer to a specific peer.
        /// </summary>
        public void SendOffer(string peerId, RTCSessionDescription desc)
        {
            var wrapper = new WebRTCSignalingWrapper
            {
                targetId = peerId,
                sdp = desc.sdp
            };

            VRNetworkManager.Instance?.Send("webrtc-offer", wrapper);
            LogDebug($"Sent offer to: {peerId}");
        }

        /// <summary>
        /// Sends an SDP answer to a specific peer.
        /// </summary>
        public void SendAnswer(string peerId, RTCSessionDescription desc)
        {
            var wrapper = new WebRTCSignalingWrapper
            {
                targetId = peerId,
                sdp = desc.sdp
            };

            VRNetworkManager.Instance?.Send("webrtc-answer", wrapper);
            LogDebug($"Sent answer to: {peerId}");
        }

        /// <summary>
        /// Sends an ICE candidate to a specific peer.
        /// </summary>
        public void SendIceCandidate(string peerId, RTCIceCandidate candidate)
        {
            var wrapper = new WebRTCSignalingWrapper
            {
                targetId = peerId,
                candidate = candidate.Candidate,
                sdpMid = candidate.SdpMid,
                sdpMLineIndex = candidate.SdpMLineIndex ?? 0
            };

            VRNetworkManager.Instance?.Send("webrtc-ice-candidate", wrapper);
        }

        private void LogDebug(string message)
        {
            if (showDebugInfo)
                Debug.Log($"[WebRTCSignaling] {message}");
        }
    }
}
