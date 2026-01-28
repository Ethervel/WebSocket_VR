using System;
using UnityEngine;

/// <summary>
/// Gestionnaire des paramètres du jeu avec persistance PlayerPrefs.
/// Singleton accessible via Instance ou méthodes statiques.
/// </summary>
public class MainMenuSettings : MonoBehaviour
{
    public static MainMenuSettings Instance { get; private set; }

    // ========== KEYS PLAYERPREFS ==========
    private const string KEY_MASTER_VOLUME = "MasterVolume";
    private const string KEY_VOICE_VOLUME = "VoiceVolume";
    private const string KEY_MICROPHONE = "MicrophoneDevice";
    private const string KEY_QUALITY_LEVEL = "QualityLevel";
    private const string KEY_RESOLUTION_INDEX = "ResolutionIndex";
    private const string KEY_FULLSCREEN = "IsFullscreen";
    private const string KEY_TURN_MODE = "TurnMode";
    private const string KEY_SNAP_ANGLE = "SnapAngle";
    private const string KEY_SMOOTH_TURN_SPEED = "SmoothTurnSpeed";
    private const string KEY_MOUSE_SENSITIVITY = "MouseSensitivity";
    private const string KEY_INVERT_Y = "InvertY";

    // ========== DEFAULT VALUES ==========
    private const float DEFAULT_MASTER_VOLUME = 1f;
    private const float DEFAULT_VOICE_VOLUME = 1f;
    private const int DEFAULT_QUALITY_LEVEL = -1; // -1 = use current
    private const bool DEFAULT_FULLSCREEN = true;
    private const int DEFAULT_TURN_MODE = 0; // 0=Snap, 1=Smooth
    private const float DEFAULT_SNAP_ANGLE = 45f;
    private const float DEFAULT_SMOOTH_TURN_SPEED = 90f;
    private const float DEFAULT_MOUSE_SENSITIVITY = 2f;
    private const bool DEFAULT_INVERT_Y = false;

    // ========== CACHED VALUES ==========
    private float _masterVolume;
    private float _voiceVolume;
    private string _microphoneDevice;
    private int _qualityLevel;
    private int _resolutionIndex;
    private bool _isFullscreen;
    private int _turnMode;
    private float _snapAngle;
    private float _smoothTurnSpeed;
    private float _mouseSensitivity;
    private bool _invertY;

    // ========== EVENTS ==========
    public static event Action<float> OnMasterVolumeChanged;
    public static event Action<float> OnVoiceVolumeChanged;
    public static event Action<string> OnMicrophoneChanged;
    public static event Action<int> OnQualityLevelChanged;
    public static event Action<int, bool> OnResolutionChanged; // index, fullscreen
    public static event Action<int> OnTurnModeChanged;
    public static event Action<float> OnSnapAngleChanged;
    public static event Action<float> OnSmoothTurnSpeedChanged;
    public static event Action<float> OnMouseSensitivityChanged;
    public static event Action<bool> OnInvertYChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadAllSettings();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ========== LOAD ALL ==========
    void LoadAllSettings()
    {
        _masterVolume = PlayerPrefs.GetFloat(KEY_MASTER_VOLUME, DEFAULT_MASTER_VOLUME);
        _voiceVolume = PlayerPrefs.GetFloat(KEY_VOICE_VOLUME, DEFAULT_VOICE_VOLUME);
        _microphoneDevice = PlayerPrefs.GetString(KEY_MICROPHONE, "");
        _qualityLevel = PlayerPrefs.GetInt(KEY_QUALITY_LEVEL, DEFAULT_QUALITY_LEVEL);
        _resolutionIndex = PlayerPrefs.GetInt(KEY_RESOLUTION_INDEX, -1);
        _isFullscreen = PlayerPrefs.GetInt(KEY_FULLSCREEN, DEFAULT_FULLSCREEN ? 1 : 0) == 1;
        _turnMode = PlayerPrefs.GetInt(KEY_TURN_MODE, DEFAULT_TURN_MODE);
        _snapAngle = PlayerPrefs.GetFloat(KEY_SNAP_ANGLE, DEFAULT_SNAP_ANGLE);
        _smoothTurnSpeed = PlayerPrefs.GetFloat(KEY_SMOOTH_TURN_SPEED, DEFAULT_SMOOTH_TURN_SPEED);
        _mouseSensitivity = PlayerPrefs.GetFloat(KEY_MOUSE_SENSITIVITY, DEFAULT_MOUSE_SENSITIVITY);
        _invertY = PlayerPrefs.GetInt(KEY_INVERT_Y, DEFAULT_INVERT_Y ? 1 : 0) == 1;

        // Apply quality if set
        if (_qualityLevel >= 0 && _qualityLevel < QualitySettings.names.Length)
        {
            QualitySettings.SetQualityLevel(_qualityLevel, true);
        }
        else
        {
            _qualityLevel = QualitySettings.GetQualityLevel();
        }

        // Apply audio
        AudioListener.volume = _masterVolume;

        Debug.Log($"[Settings] Loaded - Master:{_masterVolume}, Voice:{_voiceVolume}, Quality:{_qualityLevel}, TurnMode:{_turnMode}");
    }

    // ========== AUDIO SETTINGS ==========

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KEY_MASTER_VOLUME, _masterVolume);
            PlayerPrefs.Save();
            AudioListener.volume = _masterVolume;
            OnMasterVolumeChanged?.Invoke(_masterVolume);
        }
    }

    public float VoiceVolume
    {
        get => _voiceVolume;
        set
        {
            _voiceVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KEY_VOICE_VOLUME, _voiceVolume);
            PlayerPrefs.Save();
            OnVoiceVolumeChanged?.Invoke(_voiceVolume);
        }
    }

    public string MicrophoneDevice
    {
        get => _microphoneDevice;
        set
        {
            _microphoneDevice = value ?? "";
            PlayerPrefs.SetString(KEY_MICROPHONE, _microphoneDevice);
            PlayerPrefs.Save();
            OnMicrophoneChanged?.Invoke(_microphoneDevice);
        }
    }

    // ========== GRAPHICS SETTINGS ==========

    public int QualityLevel
    {
        get => _qualityLevel;
        set
        {
            if (value >= 0 && value < QualitySettings.names.Length)
            {
                _qualityLevel = value;
                PlayerPrefs.SetInt(KEY_QUALITY_LEVEL, _qualityLevel);
                PlayerPrefs.Save();
                QualitySettings.SetQualityLevel(_qualityLevel, true);
                OnQualityLevelChanged?.Invoke(_qualityLevel);
            }
        }
    }

    public int ResolutionIndex
    {
        get => _resolutionIndex;
        set
        {
            var resolutions = Screen.resolutions;
            if (value >= 0 && value < resolutions.Length)
            {
                _resolutionIndex = value;
                PlayerPrefs.SetInt(KEY_RESOLUTION_INDEX, _resolutionIndex);
                PlayerPrefs.Save();
                var res = resolutions[_resolutionIndex];
                Screen.SetResolution(res.width, res.height, _isFullscreen);
                OnResolutionChanged?.Invoke(_resolutionIndex, _isFullscreen);
            }
        }
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            _isFullscreen = value;
            PlayerPrefs.SetInt(KEY_FULLSCREEN, _isFullscreen ? 1 : 0);
            PlayerPrefs.Save();
            Screen.fullScreen = _isFullscreen;
            OnResolutionChanged?.Invoke(_resolutionIndex, _isFullscreen);
        }
    }

    // ========== VR CONTROLS ==========

    public int TurnMode
    {
        get => _turnMode;
        set
        {
            _turnMode = Mathf.Clamp(value, 0, 1);
            PlayerPrefs.SetInt(KEY_TURN_MODE, _turnMode);
            PlayerPrefs.Save();
            OnTurnModeChanged?.Invoke(_turnMode);
        }
    }

    public float SnapAngle
    {
        get => _snapAngle;
        set
        {
            _snapAngle = Mathf.Clamp(value, 15f, 90f);
            PlayerPrefs.SetFloat(KEY_SNAP_ANGLE, _snapAngle);
            PlayerPrefs.Save();
            OnSnapAngleChanged?.Invoke(_snapAngle);
        }
    }

    public float SmoothTurnSpeed
    {
        get => _smoothTurnSpeed;
        set
        {
            _smoothTurnSpeed = Mathf.Clamp(value, 30f, 180f);
            PlayerPrefs.SetFloat(KEY_SMOOTH_TURN_SPEED, _smoothTurnSpeed);
            PlayerPrefs.Save();
            OnSmoothTurnSpeedChanged?.Invoke(_smoothTurnSpeed);
        }
    }

    // ========== DESKTOP CONTROLS ==========

    public float MouseSensitivity
    {
        get => _mouseSensitivity;
        set
        {
            _mouseSensitivity = Mathf.Clamp(value, 0.1f, 10f);
            PlayerPrefs.SetFloat(KEY_MOUSE_SENSITIVITY, _mouseSensitivity);
            PlayerPrefs.Save();
            OnMouseSensitivityChanged?.Invoke(_mouseSensitivity);
        }
    }

    public bool InvertY
    {
        get => _invertY;
        set
        {
            _invertY = value;
            PlayerPrefs.SetInt(KEY_INVERT_Y, _invertY ? 1 : 0);
            PlayerPrefs.Save();
            OnInvertYChanged?.Invoke(_invertY);
        }
    }

    // ========== STATIC GETTERS (pour accès sans Instance) ==========

    public static float GetMasterVolume() => Instance != null ? Instance.MasterVolume : PlayerPrefs.GetFloat(KEY_MASTER_VOLUME, DEFAULT_MASTER_VOLUME);
    public static float GetVoiceVolume() => Instance != null ? Instance.VoiceVolume : PlayerPrefs.GetFloat(KEY_VOICE_VOLUME, DEFAULT_VOICE_VOLUME);
    public static string GetMicrophoneDevice() => Instance != null ? Instance.MicrophoneDevice : PlayerPrefs.GetString(KEY_MICROPHONE, "");
    public static int GetQualityLevel() => Instance != null ? Instance.QualityLevel : PlayerPrefs.GetInt(KEY_QUALITY_LEVEL, QualitySettings.GetQualityLevel());
    public static int GetTurnMode() => Instance != null ? Instance.TurnMode : PlayerPrefs.GetInt(KEY_TURN_MODE, DEFAULT_TURN_MODE);
    public static float GetSnapAngle() => Instance != null ? Instance.SnapAngle : PlayerPrefs.GetFloat(KEY_SNAP_ANGLE, DEFAULT_SNAP_ANGLE);
    public static float GetSmoothTurnSpeed() => Instance != null ? Instance.SmoothTurnSpeed : PlayerPrefs.GetFloat(KEY_SMOOTH_TURN_SPEED, DEFAULT_SMOOTH_TURN_SPEED);
    public static float GetMouseSensitivity() => Instance != null ? Instance.MouseSensitivity : PlayerPrefs.GetFloat(KEY_MOUSE_SENSITIVITY, DEFAULT_MOUSE_SENSITIVITY);
    public static bool GetInvertY() => Instance != null ? Instance.InvertY : PlayerPrefs.GetInt(KEY_INVERT_Y, DEFAULT_INVERT_Y ? 1 : 0) == 1;

    // ========== RESET TO DEFAULTS ==========

    public void ResetToDefaults()
    {
        MasterVolume = DEFAULT_MASTER_VOLUME;
        VoiceVolume = DEFAULT_VOICE_VOLUME;
        MicrophoneDevice = "";
        QualityLevel = QualitySettings.GetQualityLevel();
        IsFullscreen = DEFAULT_FULLSCREEN;
        TurnMode = DEFAULT_TURN_MODE;
        SnapAngle = DEFAULT_SNAP_ANGLE;
        SmoothTurnSpeed = DEFAULT_SMOOTH_TURN_SPEED;
        MouseSensitivity = DEFAULT_MOUSE_SENSITIVITY;
        InvertY = DEFAULT_INVERT_Y;

        Debug.Log("[Settings] Reset to defaults");
    }
}
