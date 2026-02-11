using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Capture l'audio de la scene pour l'enregistrement.
/// Utilise OnAudioFilterRead pour capturer tout l'audio qui passe par l'AudioListener.
/// </summary>
[RequireComponent(typeof(AudioListener))]
public class AudioCapture : MonoBehaviour
{
    [Header("=== Settings ===")]
    [SerializeField] private int _sampleRate = 44100;
    [SerializeField] private int _channels = 2;

    [Header("=== Status ===")]
    [SerializeField] private bool _isCapturing = false;
    public bool IsCapturing => _isCapturing;

    [SerializeField] private float _capturedSeconds = 0f;
    public float CapturedSeconds => _capturedSeconds;

    // Buffer pour stocker les samples audio
    private List<float> _audioBuffer = new List<float>();
    private object _bufferLock = new object();
    private RecordingSettings _settings;

    // Pour la sauvegarde WAV
    private const int HEADER_SIZE = 44;

    public void Initialize(RecordingSettings settings)
    {
        _settings = settings;
        _sampleRate = AudioSettings.outputSampleRate;

        AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
        Debug.Log($"[AudioCapture] Initialise: {_sampleRate}Hz, buffer={bufferLength}");
    }

    /// <summary>
    /// Demarre la capture audio.
    /// </summary>
    public void StartCapture()
    {
        if (_isCapturing)
        {
            Debug.LogWarning("[AudioCapture] Capture deja en cours.");
            return;
        }

        lock (_bufferLock)
        {
            _audioBuffer.Clear();
        }

        _capturedSeconds = 0f;
        _isCapturing = true;

        Debug.Log("[AudioCapture] Capture demarree.");
    }

    /// <summary>
    /// Arrete la capture audio.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing)
        {
            Debug.LogWarning("[AudioCapture] Aucune capture en cours.");
            return;
        }

        _isCapturing = false;

        lock (_bufferLock)
        {
            _capturedSeconds = _audioBuffer.Count / (float)(_sampleRate * _channels);
        }

        Debug.Log($"[AudioCapture] Capture arretee. Duree: {_capturedSeconds:F1}s");
    }

    /// <summary>
    /// Callback Unity pour capturer l'audio.
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_isCapturing) return;

        lock (_bufferLock)
        {
            // Copier les samples dans notre buffer
            for (int i = 0; i < data.Length; i++)
            {
                _audioBuffer.Add(data[i]);
            }
        }

        // Note: On ne modifie pas data[], donc l'audio continue a jouer normalement
    }

    /// <summary>
    /// Sauvegarde l'audio capture dans un fichier WAV.
    /// </summary>
    public IEnumerator SaveToFile(string filePath)
    {
        Debug.Log($"[AudioCapture] Sauvegarde vers: {filePath}");

        float[] samples;
        lock (_bufferLock)
        {
            samples = _audioBuffer.ToArray();
        }

        if (samples.Length == 0)
        {
            Debug.LogWarning("[AudioCapture] Aucun audio a sauvegarder.");
            yield break;
        }

        // Convertir en bytes (16-bit PCM)
        byte[] wavData = CreateWavFile(samples, _sampleRate, _channels);

        // Ecrire le fichier
        try
        {
            File.WriteAllBytes(filePath, wavData);
            Debug.Log($"[AudioCapture] Fichier WAV sauvegarde: {samples.Length} samples, {wavData.Length} bytes");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioCapture] Erreur sauvegarde: {e.Message}");
        }

        yield return null;
    }

    /// <summary>
    /// Cree un fichier WAV a partir des samples.
    /// </summary>
    private byte[] CreateWavFile(float[] samples, int sampleRate, int channels)
    {
        int sampleCount = samples.Length;
        int byteRate = sampleRate * channels * 2; // 16-bit = 2 bytes
        int dataSize = sampleCount * 2;
        int fileSize = HEADER_SIZE + dataSize - 8;

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            // RIFF header
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(fileSize);
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });

            // fmt chunk
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16); // Chunk size
            writer.Write((short)1); // Audio format (PCM)
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * 2)); // Block align
            writer.Write((short)16); // Bits per sample

            // data chunk
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            // Audio data
            foreach (float sample in samples)
            {
                // Clamp et convertir en 16-bit
                float clamped = Mathf.Clamp(sample, -1f, 1f);
                short shortSample = (short)(clamped * 32767f);
                writer.Write(shortSample);
            }

            return stream.ToArray();
        }
    }

    /// <summary>
    /// Obtient les samples captures (pour traitement externe).
    /// </summary>
    public float[] GetCapturedSamples()
    {
        lock (_bufferLock)
        {
            return _audioBuffer.ToArray();
        }
    }

    /// <summary>
    /// Efface le buffer audio.
    /// </summary>
    public void ClearBuffer()
    {
        lock (_bufferLock)
        {
            _audioBuffer.Clear();
            _capturedSeconds = 0f;
        }
    }

    void OnDestroy()
    {
        if (_isCapturing)
        {
            StopCapture();
        }
    }
}
