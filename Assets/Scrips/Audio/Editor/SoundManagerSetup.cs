#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Outil pour créer et configurer le SoundManager rapidement.
/// </summary>
public class SoundManagerSetup : EditorWindow
{
    [MenuItem("VR Meeting/Audio/Create Complete Audio System")]
    public static void CreateCompleteAudioSystem()
    {
        // Créer le parent
        GameObject audioSystem = new GameObject("AudioSystem");

        // SoundManager
        SoundManager soundManager = audioSystem.AddComponent<SoundManager>();
        audioSystem.AddComponent<SoundManagerIntegration>();
        TryLoadExistingSounds(soundManager);

        // AmbienceManager
        AmbienceManager ambienceManager = audioSystem.AddComponent<AmbienceManager>();
        SetupDefaultAmbiences(ambienceManager);

        Selection.activeGameObject = audioSystem;

        EditorUtility.DisplayDialog("Audio System",
            "Système audio complet créé!\n\n" +
            "Contient:\n" +
            "- SoundManager (SFX)\n" +
            "- SoundManagerIntegration (events)\n" +
            "- AmbienceManager (ambiances par zone)\n\n" +
            "1. Assignez les AudioClips\n" +
            "2. Configurez les zones d'ambiance\n" +
            "3. Ajoutez à la scène Bootstrap", "OK");
    }

    [MenuItem("VR Meeting/Audio/Create Sound Manager")]
    public static void CreateSoundManager()
    {
        // Vérifier si un SoundManager existe déjà
        var existing = FindFirstObjectByType<SoundManager>();
        if (existing != null)
        {
            EditorUtility.DisplayDialog("SoundManager",
                "Un SoundManager existe déjà dans la scène.", "OK");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Créer le GameObject
        GameObject soundManagerObj = new GameObject("SoundManager");

        // Ajouter les composants
        SoundManager manager = soundManagerObj.AddComponent<SoundManager>();
        soundManagerObj.AddComponent<SoundManagerIntegration>();

        // Essayer de charger les sons existants
        TryLoadExistingSounds(manager);

        // Sélectionner l'objet
        Selection.activeGameObject = soundManagerObj;

        EditorUtility.DisplayDialog("SoundManager",
            "SoundManager créé!\n\n" +
            "1. Assignez les AudioClips dans l'Inspector\n" +
            "2. Ajoutez ce prefab à la scène Bootstrap\n" +
            "3. Marquez-le comme DontDestroyOnLoad", "OK");
    }

    [MenuItem("VR Meeting/Audio/Add Button Sounds to All Buttons")]
    public static void AddButtonSoundsToAll()
    {
        var buttons = FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var button in buttons)
        {
            if (button.GetComponent<UIButtonSounds>() == null)
            {
                button.gameObject.AddComponent<UIButtonSounds>();
                EditorUtility.SetDirty(button.gameObject);
                count++;
            }
        }

        EditorUtility.DisplayDialog("Button Sounds",
            $"UIButtonSounds ajouté à {count} boutons.", "OK");
    }

    static void TryLoadExistingSounds(SoundManager manager)
    {
        string basePath = "Assets/VRMPAssets/Audio/SFXClips/";

        // Essayer de charger les sons existants
        manager.uiClick = AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "Button_22_Click.wav");
        manager.uiHover = AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "Button_14_Hover.wav");
        manager.uiError = AssetDatabase.LoadAssetAtPath<AudioClip>(basePath + "NegativeSound.wav");

        // Ambiance
        manager.roomAmbience = AssetDatabase.LoadAssetAtPath<AudioClip>(
            basePath + "245773__kaumodaki__space-ship-bridge-loop.wav");

        if (manager.uiClick != null)
        {
            Debug.Log("[SoundManagerSetup] Sons existants chargés automatiquement");
        }
    }

    static void SetupDefaultAmbiences(AmbienceManager manager)
    {
        string sfxPath = "Assets/VRMPAssets/Audio/SFXClips/";
        string mxPath = "Assets/VRMPAssets/Audio/MXClips/";

        // Charger les ambiances existantes
        AudioClip spaceAmbience = AssetDatabase.LoadAssetAtPath<AudioClip>(
            sfxPath + "245773__kaumodaki__space-ship-bridge-loop.wav");
        AudioClip birdsAmbience = AssetDatabase.LoadAssetAtPath<AudioClip>(
            sfxPath + "spring-birds-loop-with-low-cut-new-jersey-6267.mp3");
        AudioClip lobbyMusic = AssetDatabase.LoadAssetAtPath<AudioClip>(
            mxPath + "2501 Wave-PsiMieC.mp3");

        // Créer les zones par défaut
        manager.zones = new AmbienceManager.AmbienceZone[]
        {
            new AmbienceManager.AmbienceZone
            {
                zoneName = "Lobby",
                roomType = RoomType.Lobby,
                mainLoop = lobbyMusic,
                volume = 0.3f,
                randomSoundChance = 0f,
                randomSoundInterval = 30f
            },
            new AmbienceManager.AmbienceZone
            {
                zoneName = "Meeting Room A",
                roomType = RoomType.MeetingRoomA,
                mainLoop = spaceAmbience,
                volume = 0.2f,
                randomSoundChance = 0.1f,
                randomSoundInterval = 20f
            },
            new AmbienceManager.AmbienceZone
            {
                zoneName = "Meeting Room B",
                roomType = RoomType.MeetingRoomB,
                mainLoop = birdsAmbience,
                volume = 0.25f,
                randomSoundChance = 0.1f,
                randomSoundInterval = 25f
            }
        };

        Debug.Log("[SoundManagerSetup] Ambiances par défaut configurées");
    }

    [MenuItem("VR Meeting/Audio/Find Missing Sounds")]
    public static void FindMissingSounds()
    {
        var manager = FindFirstObjectByType<SoundManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog("Error", "Aucun SoundManager trouvé.", "OK");
            return;
        }

        string missing = "";

        // UI
        if (manager.uiClick == null) missing += "- uiClick\n";
        if (manager.uiHover == null) missing += "- uiHover\n";
        if (manager.uiBack == null) missing += "- uiBack\n";
        if (manager.uiSuccess == null) missing += "- uiSuccess\n";
        if (manager.uiError == null) missing += "- uiError\n";
        if (manager.uiNotification == null) missing += "- uiNotification\n";

        // Network
        if (manager.playerJoin == null) missing += "- playerJoin\n";
        if (manager.playerLeave == null) missing += "- playerLeave\n";
        if (manager.connected == null) missing += "- connected\n";
        if (manager.disconnected == null) missing += "- disconnected\n";

        // Whiteboard
        if (manager.whiteboardClear == null) missing += "- whiteboardClear\n";
        if (manager.markerDraw == null) missing += "- markerDraw\n";
        if (manager.screenShareStart == null) missing += "- screenShareStart\n";
        if (manager.screenShareStop == null) missing += "- screenShareStop\n";

        if (string.IsNullOrEmpty(missing))
        {
            EditorUtility.DisplayDialog("Sons", "Tous les sons sont assignés!", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Sons manquants",
                "Les sons suivants ne sont pas assignés:\n\n" + missing, "OK");
        }
    }
}
#endif
