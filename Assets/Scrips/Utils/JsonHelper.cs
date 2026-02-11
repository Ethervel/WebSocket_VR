using System;
using UnityEngine;

/// <summary>
/// Shared utility methods for JSON serialization operations.
/// Consolidates duplicate TryDeserialize implementations across the codebase.
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// Safely deserializes JSON data with null checks and exception handling.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to</typeparam>
    /// <param name="json">The JSON string to parse</param>
    /// <param name="context">Context for error logging (e.g., message type)</param>
    /// <returns>The deserialized object, or null if parsing failed</returns>
    public static T TryDeserialize<T>(string json, string context) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning($"[JsonHelper] Empty JSON data for {context}");
            return null;
        }

        try
        {
            T result = JsonUtility.FromJson<T>(json);
            if (result == null)
            {
                Debug.LogWarning($"[JsonHelper] Null result from JSON for {context}");
                return null;
            }
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"[JsonHelper] JSON parse error for {context}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Serializes an array to JSON. Unity's JsonUtility doesn't support arrays directly,
    /// so we wrap it in a container object.
    /// </summary>
    /// <typeparam name="T">The type of array elements</typeparam>
    /// <param name="array">The array to serialize</param>
    /// <returns>JSON string representation of the array</returns>
    public static string ToJson<T>(T[] array)
    {
        var wrapper = new ArrayWrapper<T> { items = array };
        string json = JsonUtility.ToJson(wrapper);
        // Extract just the array part: {"items":[...]} -> [...]
        int startIndex = json.IndexOf('[');
        int endIndex = json.LastIndexOf(']');
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return json.Substring(startIndex, endIndex - startIndex + 1);
        }
        return "[]";
    }

    /// <summary>
    /// Wrapper class for array serialization.
    /// </summary>
    [Serializable]
    private class ArrayWrapper<T>
    {
        public T[] items;
    }

    /// <summary>
    /// Safely decodes Base64 data with exception handling.
    /// </summary>
    /// <param name="base64Data">The Base64 encoded string</param>
    /// <param name="context">Context for error logging</param>
    /// <returns>The decoded byte array, or null if decoding failed</returns>
    public static byte[] TryDecodeBase64(string base64Data, string context)
    {
        if (string.IsNullOrEmpty(base64Data))
            return null;

        try
        {
            return Convert.FromBase64String(base64Data);
        }
        catch (FormatException e)
        {
            Debug.LogError($"[JsonHelper] Base64 decode error for {context}: {e.Message}");
            return null;
        }
    }
}
