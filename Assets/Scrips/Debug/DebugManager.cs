using UnityEngine;
using System;
using System.Diagnostics;

/// <summary>
/// Gestionnaire centralisé pour activer/désactiver les logs de debug.
/// Utilisation: DebugManager.Log("Message", DebugCategory.Network);
/// </summary>
public static class DebugManager
{
    // ============================================
    // CONFIGURATION GLOBALE
    // ============================================

    /// <summary>
    /// Active/désactive TOUS les logs (master switch)
    /// </summary>
    public static bool EnableAllLogs = true;

    /// <summary>
    /// Désactive automatiquement les logs dans les builds (recommandé pour Quest)
    /// </summary>
    public static bool DisableInBuild = true;

    // ============================================
    // CONFIGURATION PAR CATÉGORIE
    // ============================================

    public static bool EnableNetwork = true;
    public static bool EnableVoiceChat = true;
    public static bool EnableWhiteboard = true;
    public static bool EnableSharing = true;
    public static bool EnableVR = true;
    public static bool EnableUI = true;
    public static bool EnableAvatar = true;
    public static bool EnableInteraction = true;
    public static bool EnableGame = true;

    // ============================================
    // MÉTHODES DE LOG
    // ============================================

    /// <summary>
    /// Log un message si la catégorie est activée
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("DEBUG_LOGS_ENABLED")]
    public static void Log(string message, DebugCategory category = DebugCategory.General)
    {
        if (!ShouldLog(category)) return;
        UnityEngine.Debug.Log($"[{category}] {message}");
    }

    /// <summary>
    /// Log un warning si la catégorie est activée
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("DEBUG_LOGS_ENABLED")]
    public static void LogWarning(string message, DebugCategory category = DebugCategory.General)
    {
        if (!ShouldLog(category)) return;
        UnityEngine.Debug.LogWarning($"[{category}] {message}");
    }

    /// <summary>
    /// Log une erreur (toujours affiché, même si les logs sont désactivés)
    /// </summary>
    public static void LogError(string message, DebugCategory category = DebugCategory.General)
    {
        // Les erreurs sont toujours loggées
        UnityEngine.Debug.LogError($"[{category}] {message}");
    }

    /// <summary>
    /// Log avec un préfixe personnalisé
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("DEBUG_LOGS_ENABLED")]
    public static void Log(string prefix, string message, DebugCategory category = DebugCategory.General)
    {
        if (!ShouldLog(category)) return;
        UnityEngine.Debug.Log($"[{prefix}] {message}");
    }

    // ============================================
    // HELPERS
    // ============================================

    private static bool ShouldLog(DebugCategory category)
    {
        // Check master switch
        if (!EnableAllLogs) return false;

        // Check build mode
        if (DisableInBuild && !Application.isEditor) return false;

        // Check category
        return category switch
        {
            DebugCategory.Network => EnableNetwork,
            DebugCategory.VoiceChat => EnableVoiceChat,
            DebugCategory.Whiteboard => EnableWhiteboard,
            DebugCategory.Sharing => EnableSharing,
            DebugCategory.VR => EnableVR,
            DebugCategory.UI => EnableUI,
            DebugCategory.Avatar => EnableAvatar,
            DebugCategory.Interaction => EnableInteraction,
            DebugCategory.Game => EnableGame,
            DebugCategory.General => true,
            _ => true
        };
    }

    /// <summary>
    /// Active toutes les catégories
    /// </summary>
    public static void EnableAll()
    {
        EnableAllLogs = true;
        EnableNetwork = true;
        EnableVoiceChat = true;
        EnableWhiteboard = true;
        EnableSharing = true;
        EnableVR = true;
        EnableUI = true;
        EnableAvatar = true;
        EnableInteraction = true;
        EnableGame = true;
    }

    /// <summary>
    /// Désactive toutes les catégories
    /// </summary>
    public static void DisableAll()
    {
        EnableAllLogs = false;
    }

    /// <summary>
    /// Active uniquement une catégorie spécifique
    /// </summary>
    public static void EnableOnly(DebugCategory category)
    {
        EnableAllLogs = true;
        EnableNetwork = category == DebugCategory.Network;
        EnableVoiceChat = category == DebugCategory.VoiceChat;
        EnableWhiteboard = category == DebugCategory.Whiteboard;
        EnableSharing = category == DebugCategory.Sharing;
        EnableVR = category == DebugCategory.VR;
        EnableUI = category == DebugCategory.UI;
        EnableAvatar = category == DebugCategory.Avatar;
        EnableInteraction = category == DebugCategory.Interaction;
        EnableGame = category == DebugCategory.Game;
    }
}

/// <summary>
/// Catégories de debug disponibles
/// </summary>
public enum DebugCategory
{
    General,
    Network,
    VoiceChat,
    Whiteboard,
    Sharing,
    VR,
    UI,
    Avatar,
    Interaction,
    Game
}
