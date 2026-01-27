#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Forces the game to start from Bootstrap scene when pressing Play in the editor.
/// </summary>
[InitializeOnLoad]
public static class PlayFromBootstrap
{
    private const string BOOTSTRAP_SCENE = "Assets/Scenes/Bootstrap.unity";
    private const string MENU_PATH = "Tools/VR Meeting/Play From Bootstrap";

    private static bool IsEnabled
    {
        get => EditorPrefs.GetBool("PlayFromBootstrap_Enabled", true);
        set => EditorPrefs.SetBool("PlayFromBootstrap_Enabled", value);
    }

    static PlayFromBootstrap()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(MENU_PATH)]
    private static void TogglePlayFromBootstrap()
    {
        IsEnabled = !IsEnabled;
        UnityEngine.Debug.Log($"[PlayFromBootstrap] {(IsEnabled ? "Enabled" : "Disabled")}");
    }

    [MenuItem(MENU_PATH, true)]
    private static bool TogglePlayFromBootstrapValidate()
    {
        Menu.SetChecked(MENU_PATH, IsEnabled);
        return true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!IsEnabled) return;

        if (state == PlayModeStateChange.ExitingEditMode)
        {
            // Save current scene if needed
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    // User saved or discarded changes
                }
                else
                {
                    // User cancelled - stop play mode
                    EditorApplication.isPlaying = false;
                    return;
                }
            }

            // Set Bootstrap as the play mode start scene
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BOOTSTRAP_SCENE);
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            // Clear the play mode start scene when exiting play mode
            EditorSceneManager.playModeStartScene = null;
        }
    }
}
#endif
