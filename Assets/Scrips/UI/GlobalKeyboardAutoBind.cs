using System.Reflection;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard;

public class GlobalKeyboardAutoBind : MonoBehaviour
{
    [Header("Debug Info")]
    public GlobalNonNativeKeyboard linkedKeyboard;
    public Transform linkedPlayerRoot;
    public Transform linkedCamera;

    void OnEnable()
    {
        VRGameManager.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
    }

    void OnDisable()
    {
        VRGameManager.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
    }

    void Start()
    {
        FindKeyboard();
    }

    void FindKeyboard()
    {
        if (linkedKeyboard == null)
        {
            linkedKeyboard = FindFirstObjectByType<GlobalNonNativeKeyboard>();
            if (linkedKeyboard == null)
                Debug.LogWarning("[KeyboardBind] GlobalNonNativeKeyboard introuvable dans la scène au démarrage.");
            else
                Debug.Log("[KeyboardBind] GlobalNonNativeKeyboard trouvé.");
        }
    }

    public void OnLocalPlayerSpawned(GameObject player)
    {
        BindToPlayer(player);
    }

    public void BindToPlayer(GameObject player)
    {
        FindKeyboard();
        
        if (linkedKeyboard == null)
        {
            Debug.LogError("[KeyboardBind] Impossible de lier : Clavier introuvable !");
            return;
        }

        var cam = player.GetComponentInChildren<Camera>(true);
        if (cam == null)
        {
            Debug.LogError("[KeyboardBind] Aucune Camera trouvée dans le player spawné.");
            return;
        }

        var xrOrigin = player.GetComponentInChildren<XROrigin>(true);
        Transform playerRoot = xrOrigin != null ? xrOrigin.transform : player.transform;

        // Mise à jour des références locales pour debug
        linkedCamera = cam.transform;
        linkedPlayerRoot = playerRoot;

        // Injection via réflexion
        bool camSet = SetPrivateField(linkedKeyboard, "m_CameraTransform", linkedCamera);
        bool rootSet = SetPrivateField(linkedKeyboard, "m_PlayerRoot", linkedPlayerRoot);

        if (camSet && rootSet)
            Debug.Log($"[KeyboardBind] ✅ SUCCÈS : Clavier lié au joueur '{player.name}' (Cam: {cam.name})");
        else
            Debug.LogError($"[KeyboardBind] ❌ ÉCHEC : Impossible de définir les champs privés du clavier via réflexion.");
            
        // Forcer le Canvas du clavier à utiliser la caméra du joueur si nécessaire
        var keyboardCanvas = linkedKeyboard.GetComponentInChildren<Canvas>(true);
        if (keyboardCanvas != null && keyboardCanvas.renderMode == RenderMode.WorldSpace && keyboardCanvas.worldCamera == null)
        {
            keyboardCanvas.worldCamera = cam;
            Debug.Log("[KeyboardBind] ✅ Canvas du clavier lié à la caméra du joueur.");
        }
    }

    static bool SetPrivateField(object obj, string fieldName, object value)
    {
        var type = obj.GetType();
        var f = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        
        if (f == null)
        {
            Debug.LogWarning($"[KeyboardBind] Champ '{fieldName}' introuvable dans '{type.Name}'. Champs disponibles :");
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Debug.Log($" - {field.Name} ({field.FieldType})");
            }
            return false;
        }
        
        f.SetValue(obj, value);
        return true;
    }
}
