using UnityEngine;

/// <summary>
/// Shared utility methods for Transform operations.
/// Consolidates duplicate FindChildRecursive implementations across the codebase.
/// </summary>
public static class TransformUtility
{
    /// <summary>
    /// Recursively searches for a child transform whose name contains the specified string.
    /// Case-insensitive and ignores spaces.
    /// </summary>
    /// <param name="parent">The parent transform to search from</param>
    /// <param name="nameContains">The substring to search for in child names</param>
    /// <returns>The first matching transform, or null if not found</returns>
    public static Transform FindChildRecursive(Transform parent, string nameContains)
    {
        if (parent == null || string.IsNullOrEmpty(nameContains))
            return null;

        string searchNormalized = nameContains.ToLowerInvariant().Replace(" ", "");

        return FindChildRecursiveInternal(parent, searchNormalized);
    }

    private static Transform FindChildRecursiveInternal(Transform parent, string searchNormalized)
    {
        foreach (Transform child in parent)
        {
            string childNormalized = child.name.ToLowerInvariant().Replace(" ", "");

            if (childNormalized.Contains(searchNormalized))
                return child;

            var result = FindChildRecursiveInternal(child, searchNormalized);
            if (result != null)
                return result;
        }

        return null;
    }

    /// <summary>
    /// Recursively searches for a child transform with an exact name match.
    /// Case-sensitive.
    /// </summary>
    /// <param name="parent">The parent transform to search from</param>
    /// <param name="exactName">The exact name to match</param>
    /// <returns>The first matching transform, or null if not found</returns>
    public static Transform FindChildByExactName(Transform parent, string exactName)
    {
        if (parent == null || string.IsNullOrEmpty(exactName))
            return null;

        foreach (Transform child in parent)
        {
            if (child.name == exactName)
                return child;

            var result = FindChildByExactName(child, exactName);
            if (result != null)
                return result;
        }

        return null;
    }
}
