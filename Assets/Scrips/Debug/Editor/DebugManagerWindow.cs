#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Fenêtre Editor pour configurer les logs de debug.
/// Menu: Tools > Debug Manager
/// </summary>
public class DebugManagerWindow : EditorWindow
{
    private Vector2 scrollPosition;

    [MenuItem("Tools/Debug Manager")]
    public static void ShowWindow()
    {
        var window = GetWindow<DebugManagerWindow>("Debug Manager");
        window.minSize = new Vector2(300, 400);
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.Space(10);

        // ============================================
        // MASTER CONTROLS
        // ============================================
        EditorGUILayout.LabelField("Master Controls", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        DebugManager.EnableAllLogs = EditorGUILayout.Toggle(
            new GUIContent("Enable All Logs", "Master switch pour tous les logs"),
            DebugManager.EnableAllLogs
        );

        DebugManager.DisableInBuild = EditorGUILayout.Toggle(
            new GUIContent("Disable In Build", "Désactive automatiquement les logs dans les builds"),
            DebugManager.DisableInBuild
        );

        EditorGUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Enable All", GUILayout.Height(25)))
        {
            DebugManager.EnableAll();
        }
        if (GUILayout.Button("Disable All", GUILayout.Height(25)))
        {
            DebugManager.DisableAll();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        // ============================================
        // CATEGORY TOGGLES
        // ============================================
        EditorGUILayout.LabelField("Categories", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        // Disable if master is off
        GUI.enabled = DebugManager.EnableAllLogs;

        DebugManager.EnableNetwork = DrawCategoryToggle(
            "Network",
            "VRNetworkManager, VRRoomManager",
            DebugManager.EnableNetwork,
            new Color(0.4f, 0.7f, 1f)
        );

        DebugManager.EnableGame = DrawCategoryToggle(
            "Game",
            "VRGameManager, spawning, sync",
            DebugManager.EnableGame,
            new Color(0.5f, 1f, 0.5f)
        );

        DebugManager.EnableVoiceChat = DrawCategoryToggle(
            "VoiceChat",
            "WebRTC, audio, microphone",
            DebugManager.EnableVoiceChat,
            new Color(1f, 0.8f, 0.4f)
        );

        DebugManager.EnableWhiteboard = DrawCategoryToggle(
            "Whiteboard",
            "Drawing, sync, presentation mode",
            DebugManager.EnableWhiteboard,
            new Color(1f, 0.5f, 0.5f)
        );

        DebugManager.EnableSharing = DrawCategoryToggle(
            "Sharing",
            "Screen share, file share, presentation",
            DebugManager.EnableSharing,
            new Color(0.8f, 0.5f, 1f)
        );

        DebugManager.EnableVR = DrawCategoryToggle(
            "VR",
            "Controllers, tracking, teleport",
            DebugManager.EnableVR,
            new Color(0.5f, 1f, 1f)
        );

        DebugManager.EnableUI = DrawCategoryToggle(
            "UI",
            "Menus, panels, buttons",
            DebugManager.EnableUI,
            new Color(1f, 1f, 0.5f)
        );

        DebugManager.EnableAvatar = DrawCategoryToggle(
            "Avatar",
            "Customization, colors, names",
            DebugManager.EnableAvatar,
            new Color(1f, 0.6f, 0.8f)
        );

        DebugManager.EnableInteraction = DrawCategoryToggle(
            "Interaction",
            "Laser pointer, grab, objects",
            DebugManager.EnableInteraction,
            new Color(0.7f, 0.7f, 0.7f)
        );

        GUI.enabled = true;

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        // ============================================
        // QUICK FILTERS
        // ============================================
        EditorGUILayout.LabelField("Quick Filters", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Network Only"))
        {
            DebugManager.EnableOnly(DebugCategory.Network);
        }
        if (GUILayout.Button("VoiceChat Only"))
        {
            DebugManager.EnableOnly(DebugCategory.VoiceChat);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Whiteboard Only"))
        {
            DebugManager.EnableOnly(DebugCategory.Whiteboard);
        }
        if (GUILayout.Button("Sharing Only"))
        {
            DebugManager.EnableOnly(DebugCategory.Sharing);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("VR Only"))
        {
            DebugManager.EnableOnly(DebugCategory.VR);
        }
        if (GUILayout.Button("Game Only"))
        {
            DebugManager.EnableOnly(DebugCategory.Game);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        // ============================================
        // INFO
        // ============================================
        EditorGUILayout.HelpBox(
            "Usage dans le code:\n" +
            "DebugManager.Log(\"message\", DebugCategory.Network);\n\n" +
            "Les logs sont automatiquement désactivés dans les builds si 'Disable In Build' est coché.",
            MessageType.Info
        );

        EditorGUILayout.EndScrollView();

        // Repaint on play mode change
        Repaint();
    }

    private bool DrawCategoryToggle(string name, string description, bool value, Color color)
    {
        EditorGUILayout.BeginHorizontal();

        // Color indicator
        var oldColor = GUI.backgroundColor;
        GUI.backgroundColor = value ? color : Color.gray;
        GUILayout.Box("", GUILayout.Width(10), GUILayout.Height(18));
        GUI.backgroundColor = oldColor;

        // Toggle
        bool newValue = EditorGUILayout.Toggle(value, GUILayout.Width(20));

        // Label
        EditorGUILayout.LabelField(
            new GUIContent(name, description),
            EditorStyles.boldLabel,
            GUILayout.Width(100)
        );

        // Description
        EditorGUILayout.LabelField(description, EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();

        return newValue;
    }
}
#endif
