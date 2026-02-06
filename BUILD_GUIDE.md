# VR Meeting Room - Build Guide

Complete guide for building the VR Meeting Room application for Quest, PCVR, and Desktop.

---

## Table of Contents

1. [Pre-Build Configuration](#pre-build-configuration)
2. [Quest Build (Android)](#quest-build-android)
3. [PCVR Build (Windows)](#pcvr-build-windows)
4. [Desktop Build (Windows)](#desktop-build-windows)
5. [Build Automation](#build-automation)
6. [Troubleshooting](#troubleshooting)

---

## Pre-Build Configuration

### Step 1: Configure Production Server URL

Before building, update the server URL in `VRNetworkManager`:

**Option A: Via Unity Inspector (Recommended for testing)**

1. Open scene `Bootstrap.unity`
2. Find `NetworkManager` GameObject
3. In `VRNetworkManager` component, change:
   - `Server Url`: `wss://vr.yourcompany.com`
   - `Enforce Secure Connection`: ✅ (checked)

**Option B: Via Script (Recommended for production)**

Edit `Assets/Scrips/Network/VRNetworkManager.cs` line 19:

```csharp
// Development
// public string serverUrl = "ws://localhost:8080";

// Production
public string serverUrl = "wss://vr.yourcompany.com";
public bool enforceSecureConnection = true;
```

### Step 2: Verify Project Settings

1. **Company Name**: `Edit > Project Settings > Player > Company Name`
2. **Product Name**: `Edit > Project Settings > Player > Product Name`
3. **Version**: `Edit > Project Settings > Player > Version` (e.g., `1.0.0`)

### Step 3: Scenes in Build

Verify scenes are in correct order in `File > Build Settings`:

| Index | Scene | Purpose |
|-------|-------|---------|
| 0 | `Scenes/Bootstrap` | Initialization, singletons |
| 1 | `Scenes/Meet` | Main meeting room |

---

## Quest Build (Android)

### Prerequisites

- Unity Android Build Support module
- Android SDK & NDK (via Unity Hub)
- Meta Quest Developer account
- Developer mode enabled on Quest

### Step 1: Switch Platform

```
File > Build Settings > Android > Switch Platform
```

Wait for reimport (first time takes several minutes).

### Step 2: Configure Player Settings

`Edit > Project Settings > Player > Android tab`

**Other Settings:**

| Setting | Value |
|---------|-------|
| Color Space | Linear |
| Auto Graphics API | ❌ Unchecked |
| Graphics APIs | OpenGLES3, Vulkan |
| Minimum API Level | Android 10.0 (API 29) |
| Target API Level | Android 12 (API 32) or higher |
| Scripting Backend | IL2CPP |
| Target Architectures | ARM64 only ✅ |
| Internet Access | Require |

**Identification:**

| Setting | Value |
|---------|-------|
| Package Name | `com.yourcompany.vrmeeting` |
| Version | `1.0.0` |
| Bundle Version Code | `1` (increment each build) |

### Step 3: Configure XR Settings

`Edit > Project Settings > XR Plug-in Management > Android tab`

| Setting | Value |
|---------|-------|
| OpenXR | ✅ Enabled |
| Oculus | ❌ Disabled (use OpenXR) |

Click on OpenXR settings (gear icon):

| Setting | Value |
|---------|-------|
| Render Mode | Single Pass Instanced |
| Depth Submission Mode | Depth 16 Bit |
| Meta Quest Support | ✅ Enabled |
| Motion Controller Profile | ✅ Enabled |
| Hand Tracking Subsystem | ✅ Enabled (if using hands) |

### Step 4: Configure Quality Settings

`Edit > Project Settings > Quality`

For Android/Quest, use "Medium" or custom "Quest" preset:

| Setting | Value |
|---------|-------|
| Pixel Light Count | 1 |
| Anti Aliasing | 2x or 4x |
| Shadows | Hard Shadows Only |
| Shadow Resolution | Medium |
| Texture Quality | Half Res or Full |

### Step 5: Build APK

1. `File > Build Settings`
2. Ensure Android platform is selected
3. Click **Build** (APK) or **Build And Run** (if Quest connected via USB)
4. Choose output location: `Builds/Quest/VRMeeting.apk`

### Step 6: Install on Quest

**Via USB (ADB):**

```bash
adb install -r Builds/Quest/VRMeeting.apk
```

**Via SideQuest:**

1. Connect Quest to PC
2. Open SideQuest
3. Drag APK onto SideQuest window

**Via Meta Quest Developer Hub:**

1. Open MQDH
2. Device Manager > Install APK

### Quest Build Checklist

- [ ] Server URL set to production
- [ ] Package name configured
- [ ] ARM64 only selected
- [ ] IL2CPP scripting backend
- [ ] OpenXR with Meta Quest Support enabled
- [ ] Quality settings optimized for mobile
- [ ] Test on device before release

---

## PCVR Build (Windows)

### Prerequisites

- Unity Windows Build Support (IL2CPP)
- SteamVR or Oculus PC software installed

### Step 1: Switch Platform

```
File > Build Settings > Windows, Mac, Linux > Switch Platform
```

### Step 2: Configure Player Settings

`Edit > Project Settings > Player > Windows tab`

**Resolution & Presentation:**

| Setting | Value |
|---------|-------|
| Fullscreen Mode | Fullscreen Window |
| Default Screen Width | 1920 |
| Default Screen Height | 1080 |
| Run In Background | ✅ Enabled |

**Other Settings:**

| Setting | Value |
|---------|-------|
| Color Space | Linear |
| Auto Graphics API | ✅ Checked |
| Scripting Backend | IL2CPP (recommended) or Mono |
| API Compatibility | .NET Standard 2.1 |
| Architecture | x86_64 |

### Step 3: Configure XR Settings

`Edit > Project Settings > XR Plug-in Management > Windows tab`

| Setting | Value |
|---------|-------|
| OpenXR | ✅ Enabled |

Click on OpenXR settings (gear icon):

| Setting | Value |
|---------|-------|
| Render Mode | Single Pass Instanced |
| Play Mode OpenXR Runtime | System Default |
| Valve Index Controller | ✅ |
| HTC Vive Controller | ✅ |
| Microsoft Motion Controller | ✅ |
| Meta Quest Touch Pro | ✅ |
| Oculus Touch Controller | ✅ |

### Step 4: Configure Quality Settings

`Edit > Project Settings > Quality`

Use "High" or "Ultra" preset for PC:

| Setting | Value |
|---------|-------|
| Pixel Light Count | 4 |
| Anti Aliasing | 4x or 8x |
| Shadows | Soft Shadows |
| Shadow Resolution | Very High |
| Shadow Distance | 150 |
| Texture Quality | Full Res |

### Step 5: Build

1. `File > Build Settings`
2. Ensure Windows platform is selected
3. Architecture: `x86_64`
4. Click **Build**
5. Choose output folder: `Builds/PCVR/`
6. Name: `VRMeeting.exe`

### PCVR Build Checklist

- [ ] Server URL set to production
- [ ] OpenXR enabled with controller profiles
- [ ] IL2CPP scripting backend (for performance)
- [ ] High quality settings
- [ ] Test with SteamVR and Oculus

---

## Desktop Build (Windows)

Desktop mode allows keyboard/mouse users without VR headset.

### Step 1: Switch Platform

Same as PCVR - Windows platform.

### Step 2: Configure Player Settings

Same as PCVR, but you may want:

| Setting | Value |
|---------|-------|
| Default Screen Width | 1920 |
| Default Screen Height | 1080 |
| Resizable Window | ✅ |
| Allow Fullscreen Switch | ✅ |

### Step 3: Disable XR for Desktop-Only Build (Optional)

If you want a **separate** Desktop build without VR:

`Edit > Project Settings > XR Plug-in Management > Windows tab`

| Setting | Value |
|---------|-------|
| OpenXR | ❌ Disabled |

> **Note:** The project already supports automatic switching. If no VR is detected, it falls back to Desktop mode automatically.

### Step 4: Build

1. `File > Build Settings`
2. Click **Build**
3. Choose output folder: `Builds/Desktop/`
4. Name: `VRMeeting_Desktop.exe`

### Desktop Controls Reference

| Action | Control |
|--------|---------|
| Move | WASD + Shift (run) |
| Look | Right-click + drag |
| Draw on whiteboard | Left-click |
| Laser pointer | L key |
| Push-to-talk | V key |

---

## Build Automation

### Editor Script for One-Click Builds

Create `Assets/Editor/BuildScript.cs`:

```csharp
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildScript
{
    private static string[] GetScenes()
    {
        return new[]
        {
            "Assets/Scenes/Bootstrap.unity",
            "Assets/Scenes/Meet.unity"
        };
    }

    [MenuItem("Build/Quest APK")]
    public static void BuildQuest()
    {
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = "Builds/Quest/VRMeeting.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        Build(options);
    }

    [MenuItem("Build/PCVR Windows")]
    public static void BuildPCVR()
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = "Builds/PCVR/VRMeeting.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Build(options);
    }

    [MenuItem("Build/Desktop Windows")]
    public static void BuildDesktop()
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = GetScenes(),
            locationPathName = "Builds/Desktop/VRMeeting_Desktop.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        Build(options);
    }

    [MenuItem("Build/All Platforms")]
    public static void BuildAll()
    {
        BuildQuest();
        BuildPCVR();
        BuildDesktop();
    }

    private static void Build(BuildPlayerOptions options)
    {
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build succeeded: {summary.totalSize / 1024 / 1024} MB");
            Debug.Log($"Output: {options.locationPathName}");
        }
        else
        {
            Debug.LogError($"Build failed: {summary.result}");
        }
    }
}
```

**Usage:**
- `Build > Quest APK`
- `Build > PCVR Windows`
- `Build > Desktop Windows`
- `Build > All Platforms`

---

## Troubleshooting

### Quest Build Issues

| Problem | Solution |
|---------|----------|
| "Gradle build failed" | Update Android SDK, check JDK version |
| "ARM64 not found" | Install NDK via Unity Hub |
| App crashes on start | Check Logcat: `adb logcat -s Unity` |
| Black screen | OpenXR not configured for Quest |
| No hand tracking | Enable Hand Tracking Subsystem |

### PCVR Build Issues

| Problem | Solution |
|---------|----------|
| VR not detected | Ensure SteamVR/Oculus running |
| Controllers not working | Add controller profiles in OpenXR |
| Poor performance | Reduce quality settings, use IL2CPP |
| Crash on start | Check Player.log in AppData |

### Desktop Build Issues

| Problem | Solution |
|---------|----------|
| Can't move/look | Check input bindings in InputSystem |
| No network | Check firewall, verify server URL |
| Crash on start | Check Player.log |

### Common Issues All Platforms

| Problem | Solution |
|---------|----------|
| Can't connect to server | Verify server URL, check SSL cert |
| WebSocket error | Use `wss://` for production |
| No voice chat | Check microphone permissions |
| Whiteboard not syncing | Verify room connection |

### Log Locations

| Platform | Log Path |
|----------|----------|
| Quest | `adb logcat -s Unity` |
| Windows | `%USERPROFILE%\AppData\LocalLow\CompanyName\ProductName\Player.log` |
| Editor | Console window |

---

## Build Output Structure

```
Builds/
├── Quest/
│   └── VRMeeting.apk
├── PCVR/
│   ├── VRMeeting.exe
│   ├── VRMeeting_Data/
│   ├── MonoBleedingEdge/
│   └── UnityPlayer.dll
└── Desktop/
    ├── VRMeeting_Desktop.exe
    ├── VRMeeting_Desktop_Data/
    └── ...
```

---

## Version Management

Before each release:

1. Update version in `Project Settings > Player > Version`
2. For Quest: Increment `Bundle Version Code`
3. Update changelog
4. Tag in Git: `git tag -a v1.0.0 -m "Release 1.0.0"`

---

*Last updated: February 2025*
