using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;

namespace VoiceChat
{
    /// <summary>
    /// Manages audio playback for remote players.
    /// Creates spatial 3D audio sources attached to player heads.
    /// </summary>
    public class RemoteAudioManager : MonoBehaviour
    {
        public static RemoteAudioManager Instance { get; private set; }

        [Header("Audio Settings")]
        [Tooltip("Volume des autres joueurs (0-1)")]
        [Range(0f, 1f)]
        public float playbackVolume = 0.8f;

        [Tooltip("Utiliser l'audio 3D spatialisé")]
        public bool use3DAudio = true;

        [Tooltip("Distance maximale d'audibilité (mètres)")]
        public float maxAudioDistance = 20f;

        // Track audio sources and tracks per player
        private readonly Dictionary<string, AudioSource> _audioSources = new Dictionary<string, AudioSource>();
        private readonly Dictionary<string, AudioStreamTrack> _audioTracks = new Dictionary<string, AudioStreamTrack>();

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
        /// Creates an AudioSource for a remote player.
        /// Attaches to player head for proper spatial audio positioning.
        /// </summary>
        public AudioSource CreateAudioSourceForPlayer(string playerId)
        {
            if (_isCleaningUp) return null;

            // Try to find the player's HEAD transform for accurate spatial positioning
            Transform headTransform = VRGameManager.Instance?.GetRemotePlayerHead(playerId);
            GameObject audioGO;

            if (headTransform != null)
            {
                audioGO = new GameObject("VoiceAudio");
                audioGO.transform.SetParent(headTransform);
                audioGO.transform.localPosition = Vector3.zero;
                Debug.Log($"[RemoteAudio] AudioSource attached to HEAD of {playerId}");
            }
            else
            {
                // Fallback: try player body
                GameObject playerGO = VRGameManager.Instance?.GetRemotePlayer(playerId);

                if (playerGO != null)
                {
                    audioGO = new GameObject("VoiceAudio");
                    audioGO.transform.SetParent(playerGO.transform);
                    audioGO.transform.localPosition = new Vector3(0, 1.6f, 0); // Approximate head height
                    Debug.LogWarning($"[RemoteAudio] Head not found for {playerId}, using body with offset");
                }
                else
                {
                    // Final fallback: child of this manager
                    string shortId = playerId.Length >= 8 ? playerId.Substring(0, 8) : playerId;
                    audioGO = new GameObject($"VoiceAudio_{shortId}");
                    audioGO.transform.SetParent(transform);
                    Debug.LogWarning($"[RemoteAudio] Remote player not found for {playerId}, creating fallback");
                }
            }

            AudioSource audioSource = audioGO.AddComponent<AudioSource>();
            ConfigureAudioSource(audioSource);

            _audioSources[playerId] = audioSource;
            return audioSource;
        }

        private void ConfigureAudioSource(AudioSource audioSource)
        {
            audioSource.volume = playbackVolume;
            audioSource.loop = true;
            audioSource.playOnAwake = false;

            if (use3DAudio)
            {
                audioSource.spatialBlend = 1f; // Full 3D
                audioSource.minDistance = 1f;
                audioSource.maxDistance = maxAudioDistance;
                audioSource.rolloffMode = AudioRolloffMode.Linear;
                audioSource.dopplerLevel = 0f; // No Doppler for voice
            }
            else
            {
                audioSource.spatialBlend = 0f; // 2D
            }
        }

        /// <summary>
        /// Attaches an audio track to a player's audio source and starts playback.
        /// </summary>
        public void SetTrackOnPlayer(string playerId, AudioStreamTrack track)
        {
            if (_isCleaningUp) return;

            if (!_audioSources.TryGetValue(playerId, out var audioSource))
            {
                Debug.LogWarning($"[RemoteAudio] No audio source for {playerId}");
                return;
            }

            if (audioSource == null) return;

            // Store track reference for proper disposal
            _audioTracks[playerId] = track;

            audioSource.SetTrack(track);
            audioSource.loop = true;
            audioSource.Play();

            Debug.Log($"[RemoteAudio] Playing audio from {playerId}");
        }

        /// <summary>
        /// Gets the audio source for a specific player.
        /// </summary>
        public AudioSource GetAudioSource(string playerId)
        {
            _audioSources.TryGetValue(playerId, out var audioSource);
            return audioSource;
        }

        /// <summary>
        /// Closes and cleans up audio for a specific player.
        /// </summary>
        public void CloseAudioForPlayer(string playerId)
        {
            // Dispose audio track first
            if (_audioTracks.TryGetValue(playerId, out var track))
            {
                try
                {
                    track?.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteAudio] Error disposing track for {playerId}: {e.Message}");
                }
                _audioTracks.Remove(playerId);
            }

            // Stop and destroy audio source
            if (_audioSources.TryGetValue(playerId, out var audioSource))
            {
                try
                {
                    if (audioSource != null)
                    {
                        audioSource.Stop();
                        audioSource.clip = null;
                        Destroy(audioSource.gameObject);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteAudio] Error destroying audio source for {playerId}: {e.Message}");
                }
                _audioSources.Remove(playerId);
            }

            Debug.Log($"[RemoteAudio] Closed audio for {playerId}");
        }

        /// <summary>
        /// Sets the playback volume for all remote players.
        /// </summary>
        public void SetPlaybackVolume(float volume)
        {
            playbackVolume = Mathf.Clamp01(volume);

            foreach (var audioSource in _audioSources.Values)
            {
                if (audioSource != null)
                    audioSource.volume = playbackVolume;
            }
        }

        /// <summary>
        /// Mutes or unmutes a specific player.
        /// </summary>
        public void SetPlayerMuted(string playerId, bool muted)
        {
            if (_audioSources.TryGetValue(playerId, out var audioSource))
            {
                if (audioSource != null)
                    audioSource.mute = muted;
            }
        }

        /// <summary>
        /// Cleans up all audio sources and tracks.
        /// </summary>
        public void CleanupAll()
        {
            _isCleaningUp = true;

            // Dispose all tracks
            foreach (var track in _audioTracks.Values)
            {
                try
                {
                    track?.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteAudio] Error disposing track: {e.Message}");
                }
            }
            _audioTracks.Clear();

            // Destroy all audio sources
            foreach (var audioSource in _audioSources.Values)
            {
                try
                {
                    if (audioSource != null)
                    {
                        audioSource.Stop();
                        Destroy(audioSource.gameObject);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RemoteAudio] Error destroying audio source: {e.Message}");
                }
            }
            _audioSources.Clear();

            _isCleaningUp = false;
            Debug.Log("[RemoteAudio] Cleanup complete");
        }
    }
}
