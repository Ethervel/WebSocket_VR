using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;

namespace VoiceChat
{
    /// <summary>
    /// Manages WebRTC ICE server configuration (STUN/TURN).
    /// Supports custom private TURN servers for production deployment.
    /// </summary>
    public class WebRTCConfiguration : MonoBehaviour
    {
        public static WebRTCConfiguration Instance { get; private set; }

        [Header("TURN Server Configuration")]
        [Tooltip("Use your own private TURN server in production. Public servers are for development only!")]
        public bool useCustomTurnServer = false;

        [Tooltip("Your private TURN server URL (e.g., turn:your-server.com:3478)")]
        public string customTurnUrl = "";

        [Tooltip("TURN server username")]
        public string customTurnUsername = "";

        [Tooltip("TURN server credential/password")]
        public string customTurnCredential = "";

        [Tooltip("Enable TURN over TCP (helps with restrictive firewalls)")]
        public bool enableTurnTcp = true;

        // STUN server constants
        private const string STUN_GOOGLE_1 = "stun:stun.l.google.com:19302";
        private const string STUN_GOOGLE_2 = "stun:stun1.l.google.com:19302";
        private const string STUN_CLOUDFLARE = "stun:stun.cloudflare.com:3478";

        // Public TURN server fallback (development only)
        private const string TURN_PUBLIC_URL = "turn:openrelay.metered.ca";
        private const string TURN_PUBLIC_USERNAME = "openrelayproject";
        private const string TURN_PUBLIC_CREDENTIAL = "openrelayproject";

        private RTCConfiguration _cachedConfig;
        private bool _configDirty = true;

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
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Marks the configuration as dirty, forcing rebuild on next access.
        /// Call this after changing any TURN server settings.
        /// </summary>
        public void InvalidateConfig()
        {
            _configDirty = true;
        }

        /// <summary>
        /// Builds and returns the RTCConfiguration with appropriate ICE servers.
        /// Uses caching to avoid unnecessary rebuilds.
        /// </summary>
        public RTCConfiguration GetConfiguration()
        {
            if (!_configDirty)
                return _cachedConfig;

            _cachedConfig = BuildRTCConfiguration();
            _configDirty = false;
            return _cachedConfig;
        }

        /// <summary>
        /// Builds RTCConfiguration dynamically based on settings.
        /// Uses custom TURN server if configured, otherwise falls back to public servers with warnings.
        /// </summary>
        private RTCConfiguration BuildRTCConfiguration()
        {
            var iceServers = new List<RTCIceServer>();

            // STUN servers (always included - these are public and safe)
            iceServers.Add(new RTCIceServer { urls = new[] { STUN_GOOGLE_1 } });
            iceServers.Add(new RTCIceServer { urls = new[] { STUN_GOOGLE_2 } });
            iceServers.Add(new RTCIceServer { urls = new[] { STUN_CLOUDFLARE } });

            // TURN servers - use custom if configured, otherwise public fallback
            if (useCustomTurnServer && !string.IsNullOrEmpty(customTurnUrl))
            {
                AddCustomTurnServers(iceServers);
            }
            else
            {
                AddPublicTurnServers(iceServers);
            }

            return new RTCConfiguration
            {
                iceServers = iceServers.ToArray()
            };
        }

        private void AddCustomTurnServers(List<RTCIceServer> iceServers)
        {
            Debug.Log("[WebRTCConfig] Using custom TURN server");

            iceServers.Add(new RTCIceServer
            {
                urls = new[] { customTurnUrl },
                username = customTurnUsername,
                credential = customTurnCredential
            });

            // Add TCP variant if enabled
            if (enableTurnTcp && !customTurnUrl.Contains("transport="))
            {
                string tcpUrl = customTurnUrl.Contains("?")
                    ? customTurnUrl + "&transport=tcp"
                    : customTurnUrl + "?transport=tcp";

                iceServers.Add(new RTCIceServer
                {
                    urls = new[] { tcpUrl },
                    username = customTurnUsername,
                    credential = customTurnCredential
                });
            }
        }

        private void AddPublicTurnServers(List<RTCIceServer> iceServers)
        {
#if !UNITY_EDITOR
            Debug.LogWarning("[WebRTCConfig] SECURITY WARNING: Using public TURN servers with shared credentials. " +
                           "Configure useCustomTurnServer with your own TURN server for production.");
#else
            Debug.Log("[WebRTCConfig] Using public TURN servers (configure custom TURN for production)");
#endif

            iceServers.Add(new RTCIceServer
            {
                urls = new[] { $"{TURN_PUBLIC_URL}:80" },
                username = TURN_PUBLIC_USERNAME,
                credential = TURN_PUBLIC_CREDENTIAL
            });

            iceServers.Add(new RTCIceServer
            {
                urls = new[] { $"{TURN_PUBLIC_URL}:443" },
                username = TURN_PUBLIC_USERNAME,
                credential = TURN_PUBLIC_CREDENTIAL
            });

            iceServers.Add(new RTCIceServer
            {
                urls = new[] { $"{TURN_PUBLIC_URL}:443?transport=tcp" },
                username = TURN_PUBLIC_USERNAME,
                credential = TURN_PUBLIC_CREDENTIAL
            });
        }

        /// <summary>
        /// Convenience method to set custom TURN server at runtime.
        /// </summary>
        public void SetCustomTurnServer(string url, string username, string credential, bool useTcp = true)
        {
            useCustomTurnServer = true;
            customTurnUrl = url;
            customTurnUsername = username;
            customTurnCredential = credential;
            enableTurnTcp = useTcp;
            InvalidateConfig();
        }

        /// <summary>
        /// Resets to use public TURN servers (development mode).
        /// </summary>
        public void UsePublicTurnServers()
        {
            useCustomTurnServer = false;
            InvalidateConfig();
        }
    }
}
