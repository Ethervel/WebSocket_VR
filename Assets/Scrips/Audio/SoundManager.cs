using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Gestionnaire centralisé des sons pour VR Meeting Rooms.
/// Singleton accessible via SoundManager.Instance
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("=== UI Sounds ===")]
    public AudioClip uiClick;
    public AudioClip uiHover;
    public AudioClip uiBack;
    public AudioClip uiSuccess;
    public AudioClip uiError;
    public AudioClip uiNotification;

    [Header("=== Network Sounds ===")]
    public AudioClip playerJoin;
    public AudioClip playerLeave;
    public AudioClip connected;
    public AudioClip disconnected;
    public AudioClip roomCreated;
    public AudioClip roomJoined;

    // Voice chat sounds removed - user preference

    [Header("=== Whiteboard / Sharing Sounds ===")]
    public AudioClip markerDraw;
    public AudioClip whiteboardClear;
    public AudioClip screenShareStart;
    public AudioClip screenShareStop;

    [Header("=== Ambience ===")]
    public AudioClip lobbyAmbience;
    public AudioClip roomAmbience;

    [Header("=== Audio Sources ===")]
    [Tooltip("Source pour les sons UI (2D)")]
    public AudioSource uiAudioSource;

    [Tooltip("Source pour les sons de notification (2D)")]
    public AudioSource notificationAudioSource;

    [Tooltip("Source pour l'ambiance (loop)")]
    public AudioSource ambienceAudioSource;

    [Header("=== Settings ===")]
    [Range(0f, 1f)]
    public float masterVolume = 1f;

    [Range(0f, 1f)]
    public float sfxVolume = 1f;

    [Range(0f, 1f)]
    public float ambienceVolume = 0.3f;

    [Tooltip("Pitch variation pour éviter la répétition")]
    [Range(0f, 0.2f)]
    public float pitchVariation = 0.05f;

    // Cache pour éviter de spammer le même son
    private Dictionary<AudioClip, float> _lastPlayTime = new Dictionary<AudioClip, float>();
    private const float MIN_REPEAT_DELAY = 0.05f;

    // Events pour synchroniser avec les settings
    public static event Action<float> OnMasterVolumeChanged;
    public static event Action<float> OnSFXVolumeChanged;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SetupAudioSources();
        LoadVolumeSettings();
    }

    void SetupAudioSources()
    {
        // Créer les AudioSources si non assignées
        if (uiAudioSource == null)
        {
            uiAudioSource = gameObject.AddComponent<AudioSource>();
            uiAudioSource.playOnAwake = false;
            uiAudioSource.spatialBlend = 0f; // 2D
        }

        if (notificationAudioSource == null)
        {
            notificationAudioSource = gameObject.AddComponent<AudioSource>();
            notificationAudioSource.playOnAwake = false;
            notificationAudioSource.spatialBlend = 0f; // 2D
        }

        if (ambienceAudioSource == null)
        {
            ambienceAudioSource = gameObject.AddComponent<AudioSource>();
            ambienceAudioSource.playOnAwake = false;
            ambienceAudioSource.spatialBlend = 0f; // 2D
            ambienceAudioSource.loop = true;
        }
    }

    void LoadVolumeSettings()
    {
        // Charger depuis MainMenuSettings si disponible
        if (MainMenuSettings.Instance != null)
        {
            masterVolume = MainMenuSettings.Instance.MasterVolume;
        }
        else
        {
            masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
            sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 1f);
            ambienceVolume = PlayerPrefs.GetFloat("AmbienceVolume", 0.3f);
        }

        ApplyVolumeSettings();
    }

    void ApplyVolumeSettings()
    {
        if (ambienceAudioSource != null)
        {
            ambienceAudioSource.volume = ambienceVolume * masterVolume;
        }
    }

    // ==================== PUBLIC API ====================

    #region UI Sounds

    public void PlayClick()
    {
        PlayUI(uiClick);
    }

    public void PlayHover()
    {
        PlayUI(uiHover, 0.5f); // Plus discret
    }

    public void PlayBack()
    {
        PlayUI(uiBack);
    }

    public void PlaySuccess()
    {
        PlayUI(uiSuccess);
    }

    public void PlayError()
    {
        PlayUI(uiError);
    }

    public void PlayNotification()
    {
        PlayNotificationSound(uiNotification);
    }

    #endregion

    #region Network Sounds

    public void PlayPlayerJoin()
    {
        PlayNotificationSound(playerJoin);
    }

    public void PlayPlayerLeave()
    {
        PlayNotificationSound(playerLeave);
    }

    public void PlayConnected()
    {
        PlayNotificationSound(connected);
    }

    public void PlayDisconnected()
    {
        PlayNotificationSound(disconnected);
    }

    public void PlayRoomCreated()
    {
        PlayNotificationSound(roomCreated);
    }

    public void PlayRoomJoined()
    {
        PlayNotificationSound(roomJoined);
    }

    #endregion

    #region Whiteboard / Sharing Sounds

    public void PlayMarkerDraw()
    {
        // Son de dessin - peut être appelé fréquemment
        PlayUI(markerDraw, 0.3f);
    }

    public void PlayWhiteboardClear()
    {
        PlayUI(whiteboardClear);
    }

    public void PlayScreenShareStart()
    {
        PlayNotificationSound(screenShareStart);
    }

    public void PlayScreenShareStop()
    {
        PlayUI(screenShareStop);
    }

    #endregion

    #region Ambience

    public void PlayLobbyAmbience()
    {
        PlayAmbience(lobbyAmbience);
    }

    public void PlayRoomAmbience()
    {
        PlayAmbience(roomAmbience);
    }

    public void StopAmbience()
    {
        if (ambienceAudioSource != null)
        {
            ambienceAudioSource.Stop();
        }
    }

    public void FadeOutAmbience(float duration = 1f)
    {
        StartCoroutine(FadeOutAmbienceCoroutine(duration));
    }

    private System.Collections.IEnumerator FadeOutAmbienceCoroutine(float duration)
    {
        if (ambienceAudioSource == null || !ambienceAudioSource.isPlaying)
            yield break;

        float startVolume = ambienceAudioSource.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            ambienceAudioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }

        ambienceAudioSource.Stop();
        ambienceAudioSource.volume = startVolume;
    }

    #endregion

    #region 3D Spatial Audio

    /// <summary>
    /// Joue un son en 3D à une position spécifique (pour VR)
    /// </summary>
    public void PlayAtPosition(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return;

        float finalVolume = volume * sfxVolume * masterVolume;
        AudioSource.PlayClipAtPoint(clip, position, finalVolume);
    }

    /// <summary>
    /// Joue un son 3D attaché à un transform (suit l'objet)
    /// </summary>
    public AudioSource PlayAttached(AudioClip clip, Transform parent, float volume = 1f, bool loop = false)
    {
        if (clip == null || parent == null) return null;

        GameObject audioObj = new GameObject($"Sound_{clip.name}");
        audioObj.transform.SetParent(parent);
        audioObj.transform.localPosition = Vector3.zero;

        AudioSource source = audioObj.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume * sfxVolume * masterVolume;
        source.spatialBlend = 1f; // 3D
        source.minDistance = 1f;
        source.maxDistance = 15f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.loop = loop;
        source.Play();

        if (!loop)
        {
            Destroy(audioObj, clip.length + 0.1f);
        }

        return source;
    }

    #endregion

    #region Volume Control

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("MasterVolume", masterVolume);
        ApplyVolumeSettings();
        OnMasterVolumeChanged?.Invoke(masterVolume);
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("SFXVolume", sfxVolume);
        OnSFXVolumeChanged?.Invoke(sfxVolume);
    }

    public void SetAmbienceVolume(float volume)
    {
        ambienceVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat("AmbienceVolume", ambienceVolume);
        ApplyVolumeSettings();
    }

    #endregion

    // ==================== INTERNAL METHODS ====================

    private void PlayUI(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (clip == null || uiAudioSource == null) return;
        if (!CanPlayClip(clip)) return;

        float finalVolume = volumeMultiplier * sfxVolume * masterVolume;

        // Légère variation de pitch pour éviter la répétition
        uiAudioSource.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        uiAudioSource.PlayOneShot(clip, finalVolume);

        _lastPlayTime[clip] = Time.unscaledTime;
    }

    private void PlayNotificationSound(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (clip == null || notificationAudioSource == null) return;
        if (!CanPlayClip(clip)) return;

        float finalVolume = volumeMultiplier * sfxVolume * masterVolume;
        notificationAudioSource.pitch = 1f;
        notificationAudioSource.PlayOneShot(clip, finalVolume);

        _lastPlayTime[clip] = Time.unscaledTime;
    }

    private void PlayAmbience(AudioClip clip)
    {
        if (clip == null || ambienceAudioSource == null) return;

        if (ambienceAudioSource.clip == clip && ambienceAudioSource.isPlaying)
            return; // Déjà en cours

        ambienceAudioSource.clip = clip;
        ambienceAudioSource.volume = ambienceVolume * masterVolume;
        ambienceAudioSource.Play();
    }

    private bool CanPlayClip(AudioClip clip)
    {
        if (clip == null) return false;

        if (_lastPlayTime.TryGetValue(clip, out float lastTime))
        {
            if (Time.unscaledTime - lastTime < MIN_REPEAT_DELAY)
                return false;
        }

        return true;
    }

    // ==================== DEBUG ====================

    [ContextMenu("Test UI Click")]
    void TestUIClick() => PlayClick();

    [ContextMenu("Test Player Join")]
    void TestPlayerJoin() => PlayPlayerJoin();

    [ContextMenu("Test Notification")]
    void TestNotification() => PlayNotification();
}
