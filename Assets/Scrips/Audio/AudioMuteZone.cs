using UnityEngine;

/// <summary>
/// Zone qui mute/démute l'ambiance sonore quand le joueur entre/sort.
/// Détecte la position de la caméra principale (fonctionne en VR et Desktop).
/// </summary>
public class AudioMuteZone : MonoBehaviour
{
    [Header("=== Zone Shape ===")]
    public ZoneType zoneType = ZoneType.Box;
    public Vector3 boxSize = new Vector3(5f, 3f, 5f);
    public float sphereRadius = 3f;

    [Header("=== Audio Settings ===")]
    [Tooltip("Durée du fade in/out")]
    public float fadeDuration = 1f;

    [Tooltip("Volume cible dans la zone (0 = silence complet)")]
    [Range(0f, 1f)]
    public float targetVolume = 0f;

    [Header("=== What to Mute ===")]
    public bool muteAmbience = true;
    public bool muteSFX = false;

    [Header("=== Debug ===")]
    public bool showDebugLogs = true;

    public enum ZoneType { Box, Sphere }

    private float _originalAmbienceVolume = 0.3f;
    private float _originalSFXVolume = 1f;
    private bool _playerInZone = false;
    private Coroutine _fadeCoroutine;
    private Transform _playerHead;

    void Start()
    {
        // Sauvegarder les volumes originaux
        if (AmbienceManager.Instance != null)
        {
            _originalAmbienceVolume = AmbienceManager.Instance.maxVolume;
        }
        if (SoundManager.Instance != null)
        {
            _originalSFXVolume = SoundManager.Instance.sfxVolume;
        }
    }

    void Update()
    {
        // Trouver la tête du joueur
        if (_playerHead == null)
        {
            _playerHead = FindPlayerHead();
            if (_playerHead == null)
            {
                if (showDebugLogs && Time.frameCount % 120 == 0)
                    Debug.LogWarning("[AudioMuteZone] Camera.main is NULL!");
                return;
            }
            else if (showDebugLogs)
            {
                Debug.Log($"[AudioMuteZone] Found player head: {_playerHead.name}");
            }
        }

        bool isInZone = IsPointInZone(_playerHead.position);

        // Debug position toutes les 2 secondes
        // if (showDebugLogs && Time.frameCount % 120 == 0)
        // {
        //     Debug.Log($"[AudioMuteZone] Player pos: {_playerHead.position}, InZone: {isInZone}, AmbienceManager: {(AmbienceManager.Instance != null ? "OK" : "NULL")}");
        // }

        // Détecter entrée dans la zone
        if (isInZone && !_playerInZone)
        {
            _playerInZone = true;
            OnEnterZone();
        }
        // Détecter sortie de la zone
        else if (!isInZone && _playerInZone)
        {
            _playerInZone = false;
            OnExitZone();
        }
    }

    Transform FindPlayerHead()
    {
        // Option 1: Camera.main
        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        // Option 2: Chercher la caméra XR par nom
        GameObject xrCamera = GameObject.Find("Main Camera");
        if (xrCamera != null)
        {
            return xrCamera.transform;
        }

        // Option 3: Chercher dans XR Origin
        GameObject xrOrigin = GameObject.Find("XR Origin");
        if (xrOrigin == null) xrOrigin = GameObject.Find("XR Origin (XR Rig)");
        if (xrOrigin == null) xrOrigin = GameObject.Find("XR Rig");

        if (xrOrigin != null)
        {
            Camera cam = xrOrigin.GetComponentInChildren<Camera>();
            if (cam != null) return cam.transform;
        }

        // Option 4: Trouver n'importe quelle caméra active
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in cameras)
        {
            if (cam.enabled && cam.gameObject.activeInHierarchy)
            {
                if (showDebugLogs)
                    Debug.Log($"[AudioMuteZone] Using fallback camera: {cam.name}");
                return cam.transform;
            }
        }

        return null;
    }

    bool IsPointInZone(Vector3 point)
    {
        // Convertir en espace local
        Vector3 localPoint = transform.InverseTransformPoint(point);

        if (zoneType == ZoneType.Box)
        {
            Vector3 halfSize = boxSize * 0.5f;
            return Mathf.Abs(localPoint.x) <= halfSize.x &&
                   Mathf.Abs(localPoint.y) <= halfSize.y &&
                   Mathf.Abs(localPoint.z) <= halfSize.z;
        }
        else // Sphere
        {
            return localPoint.magnitude <= sphereRadius;
        }
    }

    void OnEnterZone()
    {
        if (showDebugLogs)
            Debug.Log($"[AudioMuteZone] Player ENTERED {gameObject.name} - muting audio");

        // Sauvegarder les volumes actuels
        if (AmbienceManager.Instance != null)
        {
            _originalAmbienceVolume = AmbienceManager.Instance.maxVolume;
        }
        if (SoundManager.Instance != null)
        {
            _originalSFXVolume = SoundManager.Instance.sfxVolume;
        }

        // Démarrer le fade vers le volume cible
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeAudio(targetVolume));
    }

    void OnExitZone()
    {
        if (showDebugLogs)
            Debug.Log($"[AudioMuteZone] Player EXITED {gameObject.name} - restoring audio");

        // Restaurer les volumes
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeAudioRestore());
    }

    System.Collections.IEnumerator FadeAudio(float target)
    {
        float elapsed = 0f;

        // Récupérer les volumes de départ
        float startAmbienceManager = AmbienceManager.Instance != null ? AmbienceManager.Instance.maxVolume : 0f;
        float startSoundManagerAmbience = SoundManager.Instance != null && SoundManager.Instance.ambienceAudioSource != null
            ? SoundManager.Instance.ambienceAudioSource.volume : 0f;
        float startSFX = SoundManager.Instance != null ? SoundManager.Instance.sfxVolume : 0f;

        // Calculer le volume cible pour SoundManager (proportionnel)
        float targetSoundManagerAmbience = target * _originalAmbienceVolume;

        if (showDebugLogs)
            Debug.Log($"[AudioMuteZone] Fading: AmbienceManager {startAmbienceManager}->{target}, SoundManager.ambience {startSoundManagerAmbience}->{targetSoundManagerAmbience}");

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            float smoothT = t * t * (3f - 2f * t); // Smoothstep

            if (muteAmbience)
            {
                // Fade AmbienceManager
                if (AmbienceManager.Instance != null)
                {
                    AmbienceManager.Instance.SetMaxVolume(Mathf.Lerp(startAmbienceManager, target, smoothT));
                }

                // Fade SoundManager.ambienceAudioSource directement
                if (SoundManager.Instance != null && SoundManager.Instance.ambienceAudioSource != null)
                {
                    SoundManager.Instance.ambienceAudioSource.volume = Mathf.Lerp(startSoundManagerAmbience, targetSoundManagerAmbience, smoothT);
                }
            }

            if (muteSFX && SoundManager.Instance != null)
            {
                SoundManager.Instance.SetSFXVolume(Mathf.Lerp(startSFX, target, smoothT));
            }

            yield return null;
        }

        // Finaliser
        if (muteAmbience)
        {
            if (AmbienceManager.Instance != null)
            {
                AmbienceManager.Instance.SetMaxVolume(target);
            }
            if (SoundManager.Instance != null && SoundManager.Instance.ambienceAudioSource != null)
            {
                SoundManager.Instance.ambienceAudioSource.volume = targetSoundManagerAmbience;
            }
        }
        if (muteSFX && SoundManager.Instance != null)
        {
            SoundManager.Instance.SetSFXVolume(target);
        }

        if (showDebugLogs)
            Debug.Log($"[AudioMuteZone] Fade complete - volume now {target}");
    }

    System.Collections.IEnumerator FadeAudioRestore()
    {
        float elapsed = 0f;

        float startAmbienceManager = AmbienceManager.Instance != null ? AmbienceManager.Instance.maxVolume : 0f;
        float startSoundManagerAmbience = SoundManager.Instance != null && SoundManager.Instance.ambienceAudioSource != null
            ? SoundManager.Instance.ambienceAudioSource.volume : 0f;
        float startSFX = SoundManager.Instance != null ? SoundManager.Instance.sfxVolume : 0f;

        if (showDebugLogs)
            Debug.Log($"[AudioMuteZone] Restoring to AmbienceManager:{_originalAmbienceVolume}, SoundManager.ambience:{_originalAmbienceVolume}");

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            float smoothT = t * t * (3f - 2f * t);

            if (muteAmbience)
            {
                if (AmbienceManager.Instance != null)
                {
                    AmbienceManager.Instance.SetMaxVolume(Mathf.Lerp(startAmbienceManager, _originalAmbienceVolume, smoothT));
                }
                if (SoundManager.Instance != null && SoundManager.Instance.ambienceAudioSource != null)
                {
                    SoundManager.Instance.ambienceAudioSource.volume = Mathf.Lerp(startSoundManagerAmbience, _originalAmbienceVolume, smoothT);
                }
            }

            if (muteSFX && SoundManager.Instance != null)
            {
                SoundManager.Instance.SetSFXVolume(Mathf.Lerp(startSFX, _originalSFXVolume, smoothT));
            }

            yield return null;
        }

        // Finaliser
        if (muteAmbience)
        {
            if (AmbienceManager.Instance != null)
            {
                AmbienceManager.Instance.SetMaxVolume(_originalAmbienceVolume);
            }
            if (SoundManager.Instance != null && SoundManager.Instance.ambienceAudioSource != null)
            {
                SoundManager.Instance.ambienceAudioSource.volume = _originalAmbienceVolume;
            }
        }
        if (muteSFX && SoundManager.Instance != null)
        {
            SoundManager.Instance.SetSFXVolume(_originalSFXVolume);
        }

        if (showDebugLogs)
            Debug.Log($"[AudioMuteZone] Restore complete - volume now {_originalAmbienceVolume}");
    }

    void OnDrawGizmos()
    {
        // Couleur différente si joueur dans la zone
        Gizmos.color = _playerInZone
            ? new Color(1f, 0f, 0f, 0.4f)  // Rouge si dedans
            : new Color(0f, 1f, 0f, 0.3f); // Vert si dehors

        Gizmos.matrix = transform.localToWorldMatrix;

        if (zoneType == ZoneType.Box)
        {
            Gizmos.DrawCube(Vector3.zero, boxSize);
            Gizmos.color = _playerInZone ? Color.red : Color.green;
            Gizmos.DrawWireCube(Vector3.zero, boxSize);
        }
        else
        {
            Gizmos.DrawSphere(Vector3.zero, sphereRadius);
            Gizmos.color = _playerInZone ? Color.red : Color.green;
            Gizmos.DrawWireSphere(Vector3.zero, sphereRadius);
        }
    }

    void OnDrawGizmosSelected()
    {
        // Afficher plus d'infos quand sélectionné
        Gizmos.color = Color.yellow;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (zoneType == ZoneType.Box)
        {
            Gizmos.DrawWireCube(Vector3.zero, boxSize);
        }
        else
        {
            Gizmos.DrawWireSphere(Vector3.zero, sphereRadius);
        }
    }
}
