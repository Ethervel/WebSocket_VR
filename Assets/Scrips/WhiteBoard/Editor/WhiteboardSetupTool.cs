using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Outil Editor pour configurer automatiquement les whiteboards avec la nouvelle architecture
/// </summary>
public class WhiteboardSetupTool : EditorWindow
{
    [MenuItem("Tools/Whiteboard/Setup Meet Scene")]
    public static void SetupMeetScene()
    {
        // Charger la scène Meet
        string scenePath = "Assets/Scenes/Meet.unity";

        if (!System.IO.File.Exists(Application.dataPath.Replace("Assets", "") + scenePath))
        {
            Debug.LogError($"[WhiteboardSetup] Scene not found: {scenePath}");
            return;
        }

        // Ouvrir la scène en additif
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        Debug.Log($"[WhiteboardSetup] Loaded scene: {scene.name}");

        // Chercher les whiteboards existants
        SetupWhiteboardsInScene();
    }

    [MenuItem("Tools/Whiteboard/Setup Whiteboards In Current Scene")]
    public static void SetupWhiteboardsInScene()
    {
        // Trouver tous les objets avec Whiteboard script
        Whiteboard[] whiteboards = GameObject.FindObjectsByType<Whiteboard>(FindObjectsSortMode.None);

        if (whiteboards.Length == 0)
        {
            Debug.LogWarning("[WhiteboardSetup] No Whiteboard found in scene. Creating new one...");
            CreateNewWhiteboardComplete();
            return;
        }

        Debug.Log($"[WhiteboardSetup] Found {whiteboards.Length} whiteboard(s)");

        foreach (var wb in whiteboards)
        {
            SetupWhiteboard(wb);
        }

        // Configurer les markers
        SetupMarkers();

        Debug.Log("[WhiteboardSetup] Setup complete!");
    }

    static void SetupWhiteboard(Whiteboard wb)
    {
        string wbId = wb.id;
        Transform wbTransform = wb.transform;

        Debug.Log($"[WhiteboardSetup] Setting up whiteboard '{wbId}'");

        // Vérifier si une DrawingSurface existe déjà
        WhiteboardDrawingSurface existingSurface = wbTransform.GetComponentInChildren<WhiteboardDrawingSurface>();
        if (existingSurface != null)
        {
            Debug.Log($"[WhiteboardSetup] DrawingSurface already exists for '{wbId}'");
            return;
        }

        // Créer la DrawingSurface comme enfant ou frère
        GameObject drawingSurfaceGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        drawingSurfaceGO.name = "DrawingSurface";

        // Si le whiteboard a un parent, mettre au même niveau
        if (wbTransform.parent != null)
        {
            drawingSurfaceGO.transform.SetParent(wbTransform.parent);
        }
        else
        {
            drawingSurfaceGO.transform.SetParent(wbTransform);
        }

        // Positionner légèrement devant le whiteboard
        drawingSurfaceGO.transform.position = wbTransform.position - wbTransform.forward * 0.002f;
        drawingSurfaceGO.transform.rotation = wbTransform.rotation;
        drawingSurfaceGO.transform.localScale = wbTransform.localScale;

        // Configurer le layer
        int whiteboardLayer = LayerMask.NameToLayer("Whiteboard");
        if (whiteboardLayer >= 0)
        {
            drawingSurfaceGO.layer = whiteboardLayer;
        }
        else
        {
            Debug.LogWarning("[WhiteboardSetup] Layer 'Whiteboard' not found!");
        }

        // Ajouter le script
        WhiteboardDrawingSurface surface = drawingSurfaceGO.AddComponent<WhiteboardDrawingSurface>();
        surface.id = wbId;
        surface.textureSize = wb.textureSize;
        surface.backgroundWhiteboard = wb;

        // Assigner le material transparent
        Material transparentMat = Resources.Load<Material>("WhiteboardDrawingSurfaceMat");
        if (transparentMat != null)
        {
            drawingSurfaceGO.GetComponent<Renderer>().material = transparentMat;
        }
        else
        {
            Debug.LogWarning("[WhiteboardSetup] Material 'WhiteboardDrawingSurfaceMat' not found in Resources!");
            // Créer un material transparent basique
            Material mat = new Material(Shader.Find("Custom/WhiteboardDrawingSurface"));
            if (mat.shader == null)
            {
                mat = new Material(Shader.Find("Transparent/Diffuse"));
            }
            drawingSurfaceGO.GetComponent<Renderer>().material = mat;
        }

        // S'assurer que le MeshCollider a Generate Colliders
        MeshCollider meshCollider = drawingSurfaceGO.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = drawingSurfaceGO.AddComponent<MeshCollider>();
        }

        Debug.Log($"[WhiteboardSetup] Created DrawingSurface for '{wbId}'");

        // Marquer la scène comme dirty
        EditorSceneManager.MarkSceneDirty(drawingSurfaceGO.scene);
    }

    static void SetupMarkers()
    {
        WhiteboardMarker[] markers = GameObject.FindObjectsByType<WhiteboardMarker>(FindObjectsSortMode.None);

        int whiteboardLayer = LayerMask.NameToLayer("Whiteboard");
        if (whiteboardLayer < 0)
        {
            Debug.LogWarning("[WhiteboardSetup] Layer 'Whiteboard' not found!");
            return;
        }

        LayerMask layerMask = 1 << whiteboardLayer;

        foreach (var marker in markers)
        {
            // Utiliser SerializedObject pour modifier le LayerMask
            SerializedObject so = new SerializedObject(marker);
            SerializedProperty layerProp = so.FindProperty("drawingSurfaceLayer");
            if (layerProp != null)
            {
                layerProp.intValue = layerMask;
                so.ApplyModifiedProperties();
                Debug.Log($"[WhiteboardSetup] Configured marker '{marker.name}' with layer mask {layerMask.value}");
            }
        }
    }

    static void CreateNewWhiteboardComplete()
    {
        // Charger le prefab
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Unity/WhiteboardComplete.prefab");

        if (prefab != null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(0, 1.5f, 2);
            Debug.Log("[WhiteboardSetup] Created WhiteboardComplete from prefab");

            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(instance.scene);
        }
        else
        {
            Debug.LogError("[WhiteboardSetup] Prefab 'WhiteboardComplete' not found!");
        }
    }
}
