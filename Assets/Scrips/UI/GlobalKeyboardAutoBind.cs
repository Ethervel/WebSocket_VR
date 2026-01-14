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

        if (autoSetupInputFields)
        {
            Invoke(nameof(SetupAllInputFields), 1f);
        }
    }

    public void SetupAllInputFields()
    {
        TMP_InputField[] inputFields = FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None);

        foreach (var inputField in inputFields)
        {
            SetupInputField(inputField);
        }
    }

    public bool SetupInputField(TMP_InputField inputField)
    {
        if (inputField == null) return false;

        XRKeyboardDisplay display = inputField.GetComponent<XRKeyboardDisplay>();
        if (display == null)
        {
            display = inputField.gameObject.AddComponent<XRKeyboardDisplay>();
            display.inputField = inputField;
            display.updateOnKeyPress = true;

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
            Debug.LogError("[KeyboardBind] Impossible de lier : Clavier introuvable!");
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

        linkedCamera = cam.transform;
        linkedPlayerRoot = playerRoot;

        bool camSet = SetPrivateField(linkedKeyboard, "m_CameraTransform", linkedCamera);
        bool rootSet = SetPrivateField(linkedKeyboard, "m_PlayerRoot", linkedPlayerRoot);

        if (!camSet || !rootSet)
        {
            Debug.LogError("[KeyboardBind] Impossible de définir les champs privés du clavier via réflexion.");
        }

        ConfigureKeyboardCanvas(cam);

        if (autoSetupInputFields)
        {
            SetupAllInputFields();
        }
    }

    void ConfigureKeyboardCanvas(Camera cam)
    {
        if (linkedKeyboard == null) return;

        XRKeyboard keyboard = linkedKeyboard.keyboard;

        if (keyboard == null)
        {
            keyboard = TryInstantiateKeyboard();

            if (keyboard == null) return;
        }

        Canvas keyboardCanvas = keyboard.GetComponentInChildren<Canvas>(true);
        if (keyboardCanvas == null) return;

        if (keyboardCanvas.renderMode == RenderMode.WorldSpace && keyboardCanvas.worldCamera == null)
        {
            keyboardCanvas.worldCamera = cam;
        }

        var raycaster = keyboardCanvas.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            keyboardCanvas.gameObject.AddComponent<GraphicRaycaster>();
        }
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

    XRKeyboard TryInstantiateKeyboard()
    {
        if (linkedKeyboard == null) return null;

        GameObject prefab = linkedKeyboard.keyboardPrefab;
        if (prefab == null)
        {
            Debug.LogError("[KeyboardBind] keyboardPrefab est null dans GlobalNonNativeKeyboard!");
            return null;
        }

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

        SetPrivateField(linkedKeyboard, "m_Keyboard", keyboard);

        return keyboard;
    }

    static bool SetPrivateField(object obj, string fieldName, object value)
    {
        var type = obj.GetType();
        var f = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (f == null) return false;

        f.SetValue(obj, value);
        return true;
    }
}
