using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.IO;

/// <summary>
/// Automated build script for VR Meeting Room.
/// Provides one-click builds for Quest, PCVR, and Desktop.
/// Access via menu: Build > [Platform]
/// </summary>
public class BuildScript
{
    private const string BUILD_ROOT = "Builds";

    private static string[] GetScenes()
    {
        return new[]
        {
            "Assets/Scenes/Bootstrap.unity",
            "Assets/Scenes/Meet.unity"
        };
    }

    #region Individual Builds

    [MenuItem("Build/Quest APK", false, 100)]
    public static void BuildQuest()
    {
        Debug.Log("=== Building Quest APK ===");

        // Ensure Android platform
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("Switching to Android platform...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        // Configure for Quest (IL2CPP required for Android/Quest)
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        string outputPath = Path.Combine(BUILD_ROOT, "Quest", "VRMeeting.apk");
        EnsureDirectoryExists(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        ExecuteBuild(options, "Quest");
    }

    [MenuItem("Build/PCVR Windows", false, 101)]
    public static void BuildPCVR()
    {
        Debug.Log("=== Building PCVR Windows ===");

        // Ensure Windows platform
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("Switching to Windows platform...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }

        // Configure for PCVR - use IL2CPP if available, else Mono
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, GetPreferredScriptingBackend(NamedBuildTarget.Standalone));

        string outputPath = Path.Combine(BUILD_ROOT, "PCVR", "VRMeeting.exe");
        EnsureDirectoryExists(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        ExecuteBuild(options, "PCVR");
    }

    [MenuItem("Build/Desktop Windows", false, 102)]
    public static void BuildDesktop()
    {
        Debug.Log("=== Building Desktop Windows ===");

        // Ensure Windows platform
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("Switching to Windows platform...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }

        // Configure for Desktop - use IL2CPP if available, else Mono
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, GetPreferredScriptingBackend(NamedBuildTarget.Standalone));

        string outputPath = Path.Combine(BUILD_ROOT, "Desktop", "VRMeeting_Desktop.exe");
        EnsureDirectoryExists(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        ExecuteBuild(options, "Desktop");
    }

    #endregion

    #region Development Builds

    [MenuItem("Build/Quest APK (Development)", false, 200)]
    public static void BuildQuestDev()
    {
        Debug.Log("=== Building Quest APK (Development) ===");

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        string outputPath = Path.Combine(BUILD_ROOT, "Quest", "VRMeeting_Dev.apk");
        EnsureDirectoryExists(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        ExecuteBuild(options, "Quest Development");
    }

    [MenuItem("Build/PCVR Windows (Development)", false, 201)]
    public static void BuildPCVRDev()
    {
        Debug.Log("=== Building PCVR Windows (Development) ===");

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }

        // Configure for PCVR Dev - use IL2CPP if available, else Mono
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, GetPreferredScriptingBackend(NamedBuildTarget.Standalone));

        string outputPath = Path.Combine(BUILD_ROOT, "PCVR", "VRMeeting_Dev.exe");
        EnsureDirectoryExists(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        ExecuteBuild(options, "PCVR Development");
    }

    #endregion

    #region Batch Builds

    [MenuItem("Build/All Platforms (Release)", false, 300)]
    public static void BuildAllRelease()
    {
        Debug.Log("=== Building All Platforms (Release) ===");

        BuildQuest();
        BuildPCVR();
        BuildDesktop();

        Debug.Log("=== All builds completed ===");
        EditorUtility.DisplayDialog("Build Complete",
            "All platform builds completed.\nCheck the Builds/ folder.", "OK");
    }

    [MenuItem("Build/All Platforms (Development)", false, 301)]
    public static void BuildAllDev()
    {
        Debug.Log("=== Building All Platforms (Development) ===");

        BuildQuestDev();
        BuildPCVRDev();
        BuildDesktop();

        Debug.Log("=== All development builds completed ===");
    }

    #endregion

    #region Utilities

    [MenuItem("Build/Open Build Folder", false, 400)]
    public static void OpenBuildFolder()
    {
        string fullPath = Path.GetFullPath(BUILD_ROOT);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }
        EditorUtility.RevealInFinder(fullPath);
    }

    [MenuItem("Build/Clean Build Folder", false, 401)]
    public static void CleanBuildFolder()
    {
        if (EditorUtility.DisplayDialog("Clean Build Folder",
            "This will delete all files in the Builds/ folder. Continue?", "Yes", "Cancel"))
        {
            string fullPath = Path.GetFullPath(BUILD_ROOT);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
                Debug.Log("Build folder cleaned.");
            }
            Directory.CreateDirectory(fullPath);
        }
    }

    #endregion

    #region Helper Methods

    private static void ExecuteBuild(BuildPlayerOptions options, string buildName)
    {
        // Auto-clean: Delete target folder to prevent Mono/IL2CPP conflicts
        CleanTargetFolder(options.locationPathName);

        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        stopwatch.Stop();

        if (summary.result == BuildResult.Succeeded)
        {
            float sizeMB = summary.totalSize / 1024f / 1024f;
            Debug.Log($"[{buildName}] Build SUCCEEDED");
            Debug.Log($"[{buildName}] Size: {sizeMB:F2} MB");
            Debug.Log($"[{buildName}] Time: {stopwatch.Elapsed.TotalSeconds:F1}s");
            Debug.Log($"[{buildName}] Output: {options.locationPathName}");
        }
        else
        {
            Debug.LogError($"[{buildName}] Build FAILED: {summary.result}");

            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error)
                    {
                        Debug.LogError($"[{buildName}] {message.content}");
                    }
                }
            }
        }
    }

    private static void CleanTargetFolder(string outputPath)
    {
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Debug.Log($"Auto-cleaning build folder: {directory}");
            try
            {
                Directory.Delete(directory, true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not clean build folder: {e.Message}");
            }
        }
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static ScriptingImplementation GetPreferredScriptingBackend(NamedBuildTarget target)
    {
        // Check if IL2CPP is available, fall back to Mono if not
        // IL2CPP requires the module to be installed via Unity Hub
        bool il2cppAvailable = true;

        #if UNITY_EDITOR_WIN
        // Check for IL2CPP installation by looking for the backend
        string editorPath = EditorApplication.applicationPath;
        string editorDir = Path.GetDirectoryName(editorPath);
        string il2cppPath = Path.Combine(editorDir, "Data", "il2cpp");
        il2cppAvailable = Directory.Exists(il2cppPath);
        #endif

        if (!il2cppAvailable)
        {
            Debug.LogWarning("IL2CPP not installed. Using Mono scripting backend. For better performance, install IL2CPP via Unity Hub.");
            return ScriptingImplementation.Mono2x;
        }

        return ScriptingImplementation.IL2CPP;
    }

    #endregion
}
