using System;
using System.Collections;
using UnityEngine;
using Unity.WebRTC;

namespace VoiceChat
{
    /// <summary>
    /// Manages local microphone capture and audio stream for WebRTC.
    /// Handles device selection, start/stop, and volume control.
    /// </summary>
    public class MicrophoneManager : MonoBehaviour
    {
        public static MicrophoneManager Instance { get; private set; }

        [Header("Microphone Settings")]
        [Tooltip("Volume du microphone (0-3)")]
        [Range(0f, 3f)]
        public float microphoneVolume = 1f;

        // State
        private bool _isActive = false;
        private string _selectedDevice;
        private AudioSource _audioSource;
        private AudioStreamTrack _localAudioTrack;
        private MediaStream _localStream;

        // Events
        public static event Action<bool> OnMicrophoneStateChanged;

        // Public accessors
        public bool IsActive => _isActive;
        public string SelectedDevice => _selectedDevice;
        public AudioStreamTrack LocalAudioTrack => _localAudioTrack;
        public MediaStream LocalStream => _localStream;

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
            StopMicrophone();
            if (Instance == this)
                Instance = null;
        }

        void OnApplicationQuit()
        {
            // Force stop microphone before application exits
            StopMicrophone();
        }

        /// <summary>
        /// Initializes the microphone manager with an AudioSource.
        /// Called by VoiceChatManager during setup.
        /// </summary>
        public void Initialize()
        {
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.loop = true;
                _audioSource.playOnAwake = false;
                _audioSource.volume = 0; // Don't play our own mic locally
            }

            // Select first available microphone
            if (Microphone.devices.Length > 0)
            {
                _selectedDevice = Microphone.devices[0];
                Debug.Log($"[MicrophoneManager] Default device: {_selectedDevice}");
            }
            else
            {
                Debug.LogWarning("[MicrophoneManager] No microphone found!");
            }

            // Load saved device from settings
            string savedMic = MainMenuSettings.GetMicrophoneDevice();
            if (!string.IsNullOrEmpty(savedMic))
            {
                SetMicrophone(savedMic);
            }
        }

        /// <summary>
        /// Returns list of available microphone devices.
        /// </summary>
        public string[] GetAvailableMicrophones()
        {
            return Microphone.devices;
        }

        /// <summary>
        /// Sets the active microphone device.
        /// </summary>
        public void SetMicrophone(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
            {
                Debug.LogWarning("[MicrophoneManager] SetMicrophone called with null/empty device");
                return;
            }

            // Validate device exists
            bool exists = false;
            foreach (string device in Microphone.devices)
            {
                if (device == deviceName)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                Debug.LogWarning($"[MicrophoneManager] Device not found: {deviceName}");
                return;
            }

            bool wasActive = _isActive;
            if (wasActive)
            {
                StopMicrophone();
            }

            _selectedDevice = deviceName;
            Debug.Log($"[MicrophoneManager] Device changed to: {deviceName}");

            if (wasActive)
            {
                StartMicrophone();
            }
        }

        /// <summary>
        /// Sets the microphone volume (0-3).
        /// </summary>
        public void SetVolume(float volume)
        {
            microphoneVolume = Mathf.Clamp(volume, 0f, 3f);
        }

        /// <summary>
        /// Starts microphone capture.
        /// </summary>
        public void StartMicrophone()
        {
            if (string.IsNullOrEmpty(_selectedDevice))
            {
                Debug.LogWarning("[MicrophoneManager] Cannot start - no device selected");
                return;
            }

            if (_isActive)
            {
                Debug.Log("[MicrophoneManager] Already active");
                return;
            }

            StartCoroutine(StartMicrophoneCoroutine());
        }

        private IEnumerator StartMicrophoneCoroutine()
        {
            Debug.Log($"[MicrophoneManager] Starting microphone: {_selectedDevice}");

            // Start recording
            _audioSource.clip = Microphone.Start(_selectedDevice, true, 1, 48000);

            // Wait for microphone to be ready
            int timeout = 100;
            while (Microphone.GetPosition(_selectedDevice) <= 0 && timeout > 0)
            {
                timeout--;
                yield return null;
            }

            if (timeout == 0)
            {
                Debug.LogError("[MicrophoneManager] Microphone timeout!");
                yield break;
            }

            _audioSource.Play();

            // Create WebRTC audio track and stream
            _localAudioTrack = new AudioStreamTrack(_audioSource);
            _localStream = new MediaStream();
            _localStream.AddTrack(_localAudioTrack);

            _isActive = true;

            Debug.Log("[MicrophoneManager] Microphone started");
            OnMicrophoneStateChanged?.Invoke(true);
        }

        /// <summary>
        /// Stops microphone capture and disposes resources.
        /// </summary>
        public void StopMicrophone()
        {
            if (!_isActive) return;

            Microphone.End(_selectedDevice);
            _audioSource?.Stop();

            if (_localAudioTrack != null)
            {
                _localAudioTrack.Dispose();
                _localAudioTrack = null;
            }

            if (_localStream != null)
            {
                _localStream.Dispose();
                _localStream = null;
            }

            _isActive = false;

            Debug.Log("[MicrophoneManager] Microphone stopped");
            OnMicrophoneStateChanged?.Invoke(false);
        }

        /// <summary>
        /// Toggles microphone on/off.
        /// </summary>
        public void ToggleMicrophone()
        {
            if (_isActive)
                StopMicrophone();
            else
                StartMicrophone();
        }
    }
}
