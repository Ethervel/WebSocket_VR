using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Gère les ambiances sonores par zone avec crossfade.
/// Fonctionne avec le système de RoomType existant.
/// </summary>
public class AmbienceManager : MonoBehaviour
{
    public static AmbienceManager Instance { get; private set; }

    [Header("=== Ambiances par Zone ===")]
    public AmbienceZone[] zones;

    [Header("=== Settings ===")]
    [Range(0.5f, 5f)]
    public float crossfadeDuration = 2f;

    [Range(0f, 1f)]
    public float maxVolume = 0.3f;

    [Header("=== Audio Sources ===")]
    [Tooltip("Créés automatiquement si non assignés")]
    public AudioSource sourceA;
    public AudioSource sourceB;

    // État
    private AudioSource _activeSource;
    private AudioSource _inactiveSource;
    private AmbienceZone _currentZone;
    private Coroutine _crossfadeCoroutine;

    [Serializable]
    public class AmbienceZone
    {
        public string zoneName;
        public RoomType roomType;

        [Header("Audio")]
        public AudioClip mainLoop;
        public AudioClip[] randomSounds;  // Sons aléatoires (optionnel)

        [Header("Settings")]
        [Range(0f, 1f)]
        public float volume = 1f;

        [Range(0f, 1f)]
        public float randomSoundChance = 0.1f;  // Chance par minute

        [Range(0f, 30f)]
        public float randomSoundInterval = 15f;  // Intervalle min entre sons random
    }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        SetupAudioSources();
    }

    void OnEnable()
    {
        // Écouter les changements de room
        VRRoomManager.OnRoomTypeChanged += OnRoomTypeChanged;
        VRRoomManager.OnRoomJoined += OnRoomJoined;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
        BootstrapManager.OnSceneReady += OnSceneReady;
    }

    void OnDisable()
    {
        VRRoomManager.OnRoomTypeChanged -= OnRoomTypeChanged;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        BootstrapManager.OnSceneReady -= OnSceneReady;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void SetupAudioSources()
    {
        if (sourceA == null)
        {
            GameObject objA = new GameObject("AmbienceSource_A");
            objA.transform.SetParent(transform);
            sourceA = objA.AddComponent<AudioSource>();
            ConfigureAmbienceSource(sourceA);
        }

        if (sourceB == null)
        {
            GameObject objB = new GameObject("AmbienceSource_B");
            objB.transform.SetParent(transform);
            sourceB = objB.AddComponent<AudioSource>();
            ConfigureAmbienceSource(sourceB);
        }

        _activeSource = sourceA;
        _inactiveSource = sourceB;
    }

    void ConfigureAmbienceSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;  // 2D
        source.volume = 0f;
        source.priority = 256;  // Basse priorité
    }

    // ==================== EVENT HANDLERS ====================

    void OnSceneReady(string sceneName)
    {
        if (sceneName == "Bootstrap")
        {
            // Menu principal - jouer ambiance lobby
            PlayAmbienceForRoomType(RoomType.Lobby);
        }
    }

    void OnRoomJoined(string roomId)
    {
        // Récupérer le type de room actuel
        if (VRRoomManager.Instance != null)
        {
            PlayAmbienceForRoomType(VRRoomManager.Instance.CurrentRoomType);
        }
    }

    void OnRoomLeft()
    {
        // Retour au lobby
        PlayAmbienceForRoomType(RoomType.Lobby);
    }

    void OnRoomTypeChanged(RoomType newType)
    {
        PlayAmbienceForRoomType(newType);
    }

    // ==================== PUBLIC API ====================

    /// <summary>
    /// Joue l'ambiance correspondant au type de room avec crossfade.
    /// </summary>
    public void PlayAmbienceForRoomType(RoomType roomType)
    {
        AmbienceZone zone = GetZoneForRoomType(roomType);

        if (zone == null)
        {
            Debug.LogWarning($"[Ambience] Pas d'ambiance définie pour {roomType}");
            return;
        }

        if (zone == _currentZone)
        {
            Debug.Log($"[Ambience] Déjà en train de jouer {zone.zoneName}");
            return;
        }

        Debug.Log($"[Ambience] Crossfade vers {zone.zoneName}");
        CrossfadeTo(zone);
    }

    /// <summary>
    /// Joue une ambiance spécifique par son nom.
    /// </summary>
    public void PlayAmbienceByName(string zoneName)
    {
        AmbienceZone zone = GetZoneByName(zoneName);
        if (zone != null)
        {
            CrossfadeTo(zone);
        }
    }

    /// <summary>
    /// Arrête l'ambiance avec fade out.
    /// </summary>
    public void StopAmbience(float fadeOutDuration = 1f)
    {
        if (_crossfadeCoroutine != null)
        {
            StopCoroutine(_crossfadeCoroutine);
        }

        _crossfadeCoroutine = StartCoroutine(FadeOutAll(fadeOutDuration));
        _currentZone = null;
    }

    /// <summary>
    /// Change le volume global des ambiances.
    /// </summary>
    public void SetMaxVolume(float volume)
    {
        maxVolume = Mathf.Clamp01(volume);

        // Appliquer immédiatement si une ambiance joue
        if (_activeSource != null && _activeSource.isPlaying && _currentZone != null)
        {
            _activeSource.volume = maxVolume * _currentZone.volume;
        }
    }

    // ==================== CROSSFADE ====================

    void CrossfadeTo(AmbienceZone newZone)
    {
        if (_crossfadeCoroutine != null)
        {
            StopCoroutine(_crossfadeCoroutine);
        }

        _crossfadeCoroutine = StartCoroutine(CrossfadeCoroutine(newZone));
    }

    IEnumerator CrossfadeCoroutine(AmbienceZone newZone)
    {
        // Swap sources
        AudioSource fadeOutSource = _activeSource;
        AudioSource fadeInSource = _inactiveSource;

        // Préparer la nouvelle source
        fadeInSource.clip = newZone.mainLoop;
        fadeInSource.volume = 0f;

        if (newZone.mainLoop != null)
        {
            fadeInSource.Play();
        }

        float targetVolume = maxVolume * newZone.volume;
        float startVolumeOut = fadeOutSource.volume;
        float elapsed = 0f;

        // Crossfade
        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / crossfadeDuration;

            // Courbe de fade smooth
            float smoothT = t * t * (3f - 2f * t);  // Smoothstep

            fadeOutSource.volume = Mathf.Lerp(startVolumeOut, 0f, smoothT);
            fadeInSource.volume = Mathf.Lerp(0f, targetVolume, smoothT);

            yield return null;
        }

        // Finaliser
        fadeOutSource.volume = 0f;
        fadeOutSource.Stop();
        fadeInSource.volume = targetVolume;

        // Swap les références
        _activeSource = fadeInSource;
        _inactiveSource = fadeOutSource;
        _currentZone = newZone;

        // Démarrer les sons aléatoires si configurés
        if (newZone.randomSounds != null && newZone.randomSounds.Length > 0)
        {
            StartCoroutine(RandomSoundsCoroutine(newZone));
        }

        Debug.Log($"[Ambience] Crossfade terminé vers {newZone.zoneName}");
    }

    IEnumerator FadeOutAll(float duration)
    {
        float startVolumeA = sourceA.volume;
        float startVolumeB = sourceB.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            sourceA.volume = Mathf.Lerp(startVolumeA, 0f, t);
            sourceB.volume = Mathf.Lerp(startVolumeB, 0f, t);

            yield return null;
        }

        sourceA.Stop();
        sourceB.Stop();
    }

    // ==================== RANDOM SOUNDS ====================

    IEnumerator RandomSoundsCoroutine(AmbienceZone zone)
    {
        while (_currentZone == zone)
        {
            yield return new WaitForSeconds(zone.randomSoundInterval);

            if (_currentZone != zone) break;

            // Chance de jouer un son random
            if (UnityEngine.Random.value < zone.randomSoundChance && zone.randomSounds.Length > 0)
            {
                AudioClip randomClip = zone.randomSounds[UnityEngine.Random.Range(0, zone.randomSounds.Length)];

                if (randomClip != null)
                {
                    // Jouer en one-shot sur une source temporaire
                    AudioSource.PlayClipAtPoint(randomClip, Camera.main.transform.position, maxVolume * 0.5f);
                    Debug.Log($"[Ambience] Son random: {randomClip.name}");
                }
            }
        }
    }

    // ==================== HELPERS ====================

    AmbienceZone GetZoneForRoomType(RoomType roomType)
    {
        if (zones == null) return null;

        foreach (var zone in zones)
        {
            if (zone.roomType == roomType)
                return zone;
        }

        return null;
    }

    AmbienceZone GetZoneByName(string name)
    {
        if (zones == null) return null;

        foreach (var zone in zones)
        {
            if (zone.zoneName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return zone;
        }

        return null;
    }

    // ==================== DEBUG ====================

    [ContextMenu("Test Crossfade to Lobby")]
    void TestLobby() => PlayAmbienceForRoomType(RoomType.Lobby);

    [ContextMenu("Test Crossfade to MeetingRoomA")]
    void TestMeetingA() => PlayAmbienceForRoomType(RoomType.MeetingRoomA);

    [ContextMenu("Stop Ambience")]
    void TestStop() => StopAmbience();
}
