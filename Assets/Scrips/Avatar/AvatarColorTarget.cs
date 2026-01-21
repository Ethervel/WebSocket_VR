using UnityEngine;

/// <summary>
/// Marque ce GameObject comme cible pour le changement de couleur d'avatar.
/// Ajoutez ce composant aux parties de l'avatar qui doivent changer de couleur
/// (ex: corps, tête) mais pas aux parties qui doivent garder leur couleur d'origine
/// (ex: yeux, accessoires).
/// </summary>
public class AvatarColorTarget : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Si true, applique aussi aux enfants de ce GameObject")]
    public bool includeChildren = false;

    [Tooltip("Noms des propriétés shader à modifier (ex: _Color, _BaseColor)")]
    public string[] colorPropertyNames = new string[] { "_Color", "_BaseColor" };

    // Cache du renderer pour éviter GetComponent à chaque frame
    private Renderer _cachedRenderer;
    private Renderer[] _cachedChildRenderers;
    private static MaterialPropertyBlock _propertyBlock;

    void Awake()
    {
        // Cache les renderers
        _cachedRenderer = GetComponent<Renderer>();
        if (includeChildren)
        {
            _cachedChildRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    /// <summary>
    /// Applique une couleur à ce renderer (et ses enfants si includeChildren)
    /// </summary>
    public void ApplyColor(Color color)
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        if (_cachedRenderer != null)
        {
            ApplyColorToRenderer(_cachedRenderer, color);
        }

        if (includeChildren && _cachedChildRenderers != null)
        {
            foreach (var renderer in _cachedChildRenderers)
            {
                if (renderer != null)
                    ApplyColorToRenderer(renderer, color);
            }
        }
    }

    void ApplyColorToRenderer(Renderer renderer, Color color)
    {
        renderer.GetPropertyBlock(_propertyBlock);

        foreach (var propertyName in colorPropertyNames)
        {
            _propertyBlock.SetColor(propertyName, color);
        }

        renderer.SetPropertyBlock(_propertyBlock);
    }

    /// <summary>
    /// Trouve tous les AvatarColorTarget sur un GameObject et ses enfants,
    /// et applique la couleur à tous.
    /// </summary>
    public static void ApplyColorToAll(GameObject root, Color color)
    {
        if (root == null) return;

        var targets = root.GetComponentsInChildren<AvatarColorTarget>(true);
        foreach (var target in targets)
        {
            target.ApplyColor(color);
        }
    }

    /// <summary>
    /// Vérifie si un GameObject a des AvatarColorTarget
    /// </summary>
    public static bool HasColorTargets(GameObject root)
    {
        if (root == null) return false;
        return root.GetComponentInChildren<AvatarColorTarget>(true) != null;
    }
}
