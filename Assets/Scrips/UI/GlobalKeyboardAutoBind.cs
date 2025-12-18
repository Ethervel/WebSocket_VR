using System.Reflection;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard;

public class GlobalKeyboardAutoBind : MonoBehaviour
{
    GlobalNonNativeKeyboard _global;

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
        _global = FindFirstObjectByType<GlobalNonNativeKeyboard>();
        if (_global == null)
            Debug.LogError("[KeyboardBind] GlobalNonNativeKeyboard introuvable dans la scène.");
    }

    void OnLocalPlayerSpawned(GameObject player)
    {
        if (_global == null) _global = FindFirstObjectByType<GlobalNonNativeKeyboard>();
        if (_global == null) return;

        var cam = player.GetComponentInChildren<Camera>(true);
        if (cam == null)
        {
            Debug.LogError("[KeyboardBind] Aucune Camera trouvée dans le player spawné.");
            return;
        }

        var xrOrigin = player.GetComponentInChildren<XROrigin>(true);
        Transform playerRoot = xrOrigin != null ? xrOrigin.transform : player.transform;

        SetPrivateField(_global, "m_CameraTransform", cam.transform);
        SetPrivateField(_global, "m_PlayerRoot", playerRoot);

        Debug.Log($"[KeyboardBind] OK -> Camera={cam.name}, PlayerRoot={playerRoot.name}");
    }

    static void SetPrivateField(object obj, string fieldName, object value)
    {
        var f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null)
        {
            Debug.LogWarning($"[KeyboardBind] Champ '{fieldName}' introuvable (changement de version ?).");
            return;
        }
        f.SetValue(obj, value);
    }
}
