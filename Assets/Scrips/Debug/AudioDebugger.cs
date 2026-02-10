using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Debug tool to trace all audio playing in the scene.
/// Attach to any GameObject to monitor all AudioSources.
/// Remove after debugging.
/// </summary>
public class AudioDebugger : MonoBehaviour
{
    [Header("Settings")]
    public bool enableLogging = true;
    public bool logStackTrace = true;

    private Dictionary<AudioSource, AudioClip> _lastClips = new Dictionary<AudioSource, AudioClip>();
    private Dictionary<AudioSource, bool> _wasPlaying = new Dictionary<AudioSource, bool>();

    void Update()
    {
        if (!enableLogging) return;

        // Find all AudioSources in scene
        AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);

        foreach (var source in allSources)
        {
            if (source == null) continue;

            bool wasPlayingBefore = _wasPlaying.ContainsKey(source) && _wasPlaying[source];
            AudioClip lastClip = _lastClips.ContainsKey(source) ? _lastClips[source] : null;

            // Detect when audio starts playing
            if (source.isPlaying && (!wasPlayingBefore || source.clip != lastClip))
            {
                LogAudioPlay(source);
            }

            _wasPlaying[source] = source.isPlaying;
            _lastClips[source] = source.clip;
        }
    }

    void LogAudioPlay(AudioSource source)
    {
        string clipName = source.clip != null ? source.clip.name : "NULL";
        string objectPath = GetGameObjectPath(source.gameObject);

        string message = $"[AUDIO PLAY] Clip: \"{clipName}\" | Source: {objectPath} | Volume: {source.volume}";

        if (logStackTrace)
        {
            Debug.Log(message + "\n" + System.Environment.StackTrace, source.gameObject);
        }
        else
        {
            Debug.Log(message, source.gameObject);
        }
    }

    string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform parent = obj.transform.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    [ContextMenu("Log All Active AudioSources")]
    void LogAllActiveSources()
    {
        AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        Debug.Log($"=== TOTAL AUDIOSOURCES: {allSources.Length} ===");

        foreach (var source in allSources)
        {
            string status = source.isPlaying ? "PLAYING" : "stopped";
            string clipName = source.clip != null ? source.clip.name : "no clip";
            string path = GetGameObjectPath(source.gameObject);

            Debug.Log($"[{status}] {clipName} @ {path}", source.gameObject);
        }
    }
}
