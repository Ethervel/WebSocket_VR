using System;

/// <summary>
/// Data classes for WebRTC voice chat signaling serialization.
/// Used by VoiceChatManager and WebRTCSignaling for network communication.
/// </summary>
namespace VoiceChat
{
    /// <summary>
    /// SDP offer/answer data for WebRTC session description exchange.
    /// </summary>
    [Serializable]
    public class SignalingData
    {
        public string sdp;
        public string type;
    }

    /// <summary>
    /// ICE candidate data for connection establishment.
    /// </summary>
    [Serializable]
    public class IceCandidateData
    {
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }

    /// <summary>
    /// Wrapper format for WebRTC signaling messages sent via VRNetworkManager.
    /// Matches the Node.js server protocol.
    /// </summary>
    [Serializable]
    public class WebRTCSignalingWrapper
    {
        public string targetId;
        public string sdp;
        public string candidate;
        public string sdpMid;
        public int sdpMLineIndex;
    }
}
