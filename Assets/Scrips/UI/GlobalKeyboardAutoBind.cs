using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard;
using TMPro;

public class GlobalKeyboardAutoBind : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Ajouter automatiquement XRKeyboardDisplay aux InputFields")]
    public bool autoSetupInputFields = true;

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

        // Setup InputFields après un court délai (attendre que les scènes soient chargées)
        if (autoSetupInputFields)
        {
            Invoke(nameof(SetupAllInputFields), 1f);
        }
    }

    /// <summary>
    /// Configure tous les TMP_InputField de la scène avec XRKeyboardDisplay
    /// </summary>
    public void SetupAllInputFields()
    {
        TMP_InputField[] inputFields = FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None);
        int configured = 0;

        foreach (var inputField in inputFields)
        {
            if (SetupInputField(inputField))
                configured++;
        }

        if (configured > 0)
            Debug.Log($"[KeyboardBind] ✅ {configured} InputField(s) configuré(s) pour le clavier XR");
    }

    /// <summary>
    /// Configure un InputField spécifique pour utiliser le clavier XR
    /// </summary>
    public bool SetupInputField(TMP_InputField inputField)
    {
        if (inputField == null) return false;

        // Vérifier si XRKeyboardDisplay existe déjà
        XRKeyboardDisplay display = inputField.GetComponent<XRKeyboardDisplay>();
        if (display == null)
        {
            display = inputField.gameObject.AddComponent<XRKeyboardDisplay>();
            display.inputField = inputField;
            display.updateOnKeyPress = true;

            // Configurer l'InputField
            inputField.shouldHideSoftKeyboard = true;
            inputField.resetOnDeActivation = false;

            return true;
        }

        return false;
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

        // Configurer le Canvas du clavier (chercher via linkedKeyboard.keyboard, pas les enfants directs)
        ConfigureKeyboardCanvas(cam);

        // Re-setup InputFields maintenant que le joueur est spawné
        if (autoSetupInputFields)
        {
            SetupAllInputFields();
        }
    }

    void ConfigureKeyboardCanvas(Camera cam)
    {
        if (linkedKeyboard == null) return;

        // Le clavier est accessible via linkedKeyboard.keyboard, pas comme enfant
        XRKeyboard keyboard = linkedKeyboard.keyboard;

        // Si le clavier n'existe pas, essayer de l'instancier manuellement
        if (keyboard == null)
        {
            Debug.LogWarning("[KeyboardBind] Le clavier XR n'est pas instancié. Tentative d'instanciation manuelle...");
            keyboard = TryInstantiateKeyboard();

            if (keyboard == null)
            {
                Debug.LogError("[KeyboardBind] Impossible d'instancier le clavier XR!");
                return;
            }
        }

        Canvas keyboardCanvas = keyboard.GetComponentInChildren<Canvas>(true);
        if (keyboardCanvas == null)
        {
            Debug.LogWarning("[KeyboardBind] Canvas du clavier introuvable!");
            return;
        }

        // Assigner la caméra au Canvas WorldSpace
        if (keyboardCanvas.renderMode == RenderMode.WorldSpace && keyboardCanvas.worldCamera == null)
        {
            keyboardCanvas.worldCamera = cam;
            Debug.Log("[KeyboardBind] ✅ Canvas du clavier lié à la caméra du joueur.");
        }

        // Ajouter GraphicRaycaster pour le mode Desktop (interaction souris)
        var raycaster = keyboardCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            keyboardCanvas.gameObject.AddComponent<GraphicRaycaster>();
            Debug.Log("[KeyboardBind] ✅ GraphicRaycaster ajouté pour Desktop mode.");
        }

        Debug.Log($"[KeyboardBind] ✅ Clavier XR configuré: {keyboard.gameObject.name}");
    }

    System.Collections.IEnumerator RetryConfigureKeyboardCanvas(Camera cam, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (linkedKeyboard != null && linkedKeyboard.keyboard != null)
        {
            ConfigureKeyboardCanvas(cam);
        }
        else
        {
            Debug.LogError("[KeyboardBind] Le clavier XR n'a pas pu être configuré - keyboard toujours null!");
        }
    }

    /// <summary>
    /// Tente d'instancier manuellement le clavier XR si le prefab est disponible
    /// </summary>
    XRKeyboard TryInstantiateKeyboard()
    {
        if (linkedKeyboard == null) return null;

        // Récupérer le prefab via la propriété publique
        GameObject prefab = linkedKeyboard.keyboardPrefab;
        if (prefab == null)
        {
            Debug.LogError("[KeyboardBind] keyboardPrefab est null dans GlobalNonNativeKeyboard!");
            return null;
        }

        // Instancier le clavier sous le playerRoot (ou à la racine si null)
        Transform parent = linkedPlayerRoot != null ? linkedPlayerRoot : linkedKeyboard.transform;
        GameObject keyboardInstance = Instantiate(prefab, parent);
        keyboardInstance.SetActive(false);

        XRKeyboard keyboard = keyboardInstance.GetComponent<XRKeyboard>();
        if (keyboard == null)
        {
            Debug.LogError("[KeyboardBind] Le prefab instancié n'a pas de composant XRKeyboard!");
            Destroy(keyboardInstance);
            return null;
        }

        // Assigner le clavier au GlobalNonNativeKeyboard via réflexion
        SetPrivateField(linkedKeyboard, "m_Keyboard", keyboard);

        Debug.Log($"[KeyboardBind] ✅ Clavier XR instancié manuellement: {keyboardInstance.name}");
        return keyboard;
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
