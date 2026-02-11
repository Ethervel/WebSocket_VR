using UnityEngine;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

/// <summary>
/// Helper pour encoder les frames et l'audio en MP4 via FFmpeg.
/// FFmpeg doit etre installe sur le systeme et accessible dans le PATH.
/// </summary>
public static class FFmpegEncoder
{
    // Chemin vers FFmpeg (peut etre configure)
    private static string _ffmpegPath = "ffmpeg";

    /// <summary>
    /// Verifie si FFmpeg est disponible sur le systeme.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Definit le chemin vers l'executable FFmpeg.
    /// </summary>
    public static void SetFFmpegPath(string path)
    {
        _ffmpegPath = path;
    }

    /// <summary>
    /// Encode les frames JPEG et l'audio WAV en un fichier MP4.
    /// </summary>
    /// <param name="recordingPath">Chemin du dossier d'enregistrement</param>
    /// <param name="frameRate">Framerate de la video</param>
    /// <param name="onProgress">Callback de progression (0-100)</param>
    /// <param name="onComplete">Callback de completion (succes, chemin MP4 ou message erreur)</param>
    public static async Task EncodeToMp4Async(
        string recordingPath,
        int frameRate = 30,
        Action<float> onProgress = null,
        Action<bool, string> onComplete = null)
    {
        string framesPath = Path.Combine(recordingPath, "frames");
        string audioPath = Path.Combine(recordingPath, "audio.wav");
        string outputPath = Path.Combine(recordingPath, "recording.mp4");

        // Verifier que les fichiers existent
        if (!Directory.Exists(framesPath))
        {
            onComplete?.Invoke(false, "Dossier frames non trouve");
            return;
        }

        string[] frames = Directory.GetFiles(framesPath, "*.jpg");
        if (frames.Length == 0)
        {
            onComplete?.Invoke(false, "Aucune frame trouvee");
            return;
        }

        bool hasAudio = File.Exists(audioPath);

        // Construire la commande FFmpeg
        string framePattern = Path.Combine(framesPath, "frame_%06d.jpg");
        string arguments;

        if (hasAudio)
        {
            // Video + Audio
            arguments = $"-y -framerate {frameRate} -i \"{framePattern}\" -i \"{audioPath}\" " +
                       $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p " +
                       $"-c:a aac -b:a 128k -shortest \"{outputPath}\"";
        }
        else
        {
            // Video seule
            arguments = $"-y -framerate {frameRate} -i \"{framePattern}\" " +
                       $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p \"{outputPath}\"";
        }

        Debug.Log($"[FFmpeg] Demarrage encodage: {frames.Length} frames");
        Debug.Log($"[FFmpeg] Commande: {_ffmpegPath} {arguments}");

        try
        {
            await Task.Run(() =>
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _ffmpegPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                // Capturer la sortie pour la progression
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // FFmpeg ecrit la progression sur stderr
                        // Format: frame= 123 fps=30 ...
                        if (e.Data.Contains("frame="))
                        {
                            try
                            {
                                int frameIndex = e.Data.IndexOf("frame=");
                                string frameStr = e.Data.Substring(frameIndex + 6).TrimStart();
                                int spaceIndex = frameStr.IndexOf(' ');
                                if (spaceIndex > 0)
                                {
                                    frameStr = frameStr.Substring(0, spaceIndex);
                                    if (int.TryParse(frameStr, out int currentFrame))
                                    {
                                        float progress = (float)currentFrame / frames.Length * 100f;
                                        onProgress?.Invoke(progress);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Debug.Log($"[FFmpeg] Encodage termine: {outputPath}");
                    onComplete?.Invoke(true, outputPath);
                }
                else
                {
                    Debug.LogError($"[FFmpeg] Echec encodage (code {process.ExitCode})");
                    onComplete?.Invoke(false, $"FFmpeg exit code: {process.ExitCode}");
                }
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"[FFmpeg] Erreur: {e.Message}");
            onComplete?.Invoke(false, e.Message);
        }
    }

    /// <summary>
    /// Genere un script batch/shell pour encoder manuellement.
    /// Utile si FFmpeg n'est pas disponible en runtime.
    /// </summary>
    public static void GenerateEncodeScript(string recordingPath, int frameRate = 30)
    {
        string framesPath = Path.Combine(recordingPath, "frames");
        string audioPath = Path.Combine(recordingPath, "audio.wav");
        string outputPath = Path.Combine(recordingPath, "recording.mp4");

        bool hasAudio = File.Exists(audioPath);

        // Note: Pour Windows batch, %06d doit etre %%06d pour echapper le %
        string framePattern = "frame_%06d.jpg";  // Pour FFmpeg direct
        string framePatternBat = "frame_%%06d.jpg";  // Pour script .bat (% echappe)

        string command;
        string commandBat;
        if (hasAudio)
        {
            command = $"ffmpeg -y -framerate {frameRate} -i \"frames/{framePattern}\" -i \"audio.wav\" " +
                     $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p " +
                     $"-c:a aac -b:a 128k -shortest \"recording.mp4\"";
            commandBat = $"ffmpeg -y -framerate {frameRate} -i \"frames/{framePatternBat}\" -i \"audio.wav\" " +
                     $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p " +
                     $"-c:a aac -b:a 128k -shortest \"recording.mp4\"";
        }
        else
        {
            command = $"ffmpeg -y -framerate {frameRate} -i \"frames/{framePattern}\" " +
                     $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p \"recording.mp4\"";
            commandBat = $"ffmpeg -y -framerate {frameRate} -i \"frames/{framePatternBat}\" " +
                     $"-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p \"recording.mp4\"";
        }

#if UNITY_STANDALONE_WIN
        string scriptPath = Path.Combine(recordingPath, "encode.bat");
        string scriptContent = $"@echo off\ncd /d \"{recordingPath}\"\n{commandBat}\npause";
#else
        string scriptPath = Path.Combine(recordingPath, "encode.sh");
        string scriptContent = $"#!/bin/bash\ncd \"{recordingPath}\"\n{command}";
#endif

        File.WriteAllText(scriptPath, scriptContent);
        Debug.Log($"[FFmpeg] Script d'encodage genere: {scriptPath}");
    }

    /// <summary>
    /// Nettoie les frames apres encodage reussi.
    /// </summary>
    public static void CleanupFrames(string recordingPath)
    {
        string framesPath = Path.Combine(recordingPath, "frames");
        if (Directory.Exists(framesPath))
        {
            try
            {
                Directory.Delete(framesPath, true);
                Debug.Log("[FFmpeg] Frames nettoyees.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FFmpeg] Erreur nettoyage: {e.Message}");
            }
        }
    }
}
