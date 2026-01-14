using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Helper pour connecter les boutons UI aux fonctionnalités du Whiteboard.
/// Attache ce script à ton Canvas UI près du whiteboard.
/// Connecte les méthodes publiques aux onClick des boutons via l'Inspector.
/// </summary>
public class WhiteboardUIHelper : MonoBehaviour
{
    [Header("References (auto-détectées si vides)")]
    [Tooltip("Le Whiteboard associé (pour screen share)")]
    public Whiteboard targetWhiteboard;

    [Tooltip("La surface de dessin associée (pour clear)")]
    public WhiteboardDrawingSurface targetDrawingSurface;

    [Header("Auto-Detection")]
    [Tooltip("Distance max pour auto-détection des composants")]
    public float autoDetectRadius = 5f;

    [Header("Color Palette")]
    [Tooltip("Couleurs disponibles pour les boutons")]
    public Color[] colors = new Color[]
    {
        Color.blue,
        Color.red,
        Color.green,
        Color.yellow,
        Color.black,
        Color.white
    };

    [Header("Screen Share UI (optionnel)")]
    [Tooltip("Dropdown pour sélectionner l'écran à partager")]
    public Dropdown screenDropdown;

    [Tooltip("Texte affichant le statut du partage")]
    public Text shareStatusText;

    // Cache
    private WhiteboardMarker[] _markers;
    private DesktopWhiteboardDrawer _desktopDrawer;
    private Color _currentColor;
    private List<WindowCapture.WindowInfo> _windowList = new List<WindowCapture.WindowInfo>();

    void Start()
    {
        AutoDetectReferences();
        CacheDrawingComponents();

        _currentColor = colors.Length > 0 ? colors[0] : Color.blue;

        // Subscribe aux événements
        VRGameManager.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        ScreenShareManager.OnScreenShareStarted += OnScreenShareStarted;
        ScreenShareManager.OnScreenShareStopped += OnScreenShareStopped;

        UpdateShareStatus();
    }

    void OnDestroy()
    {
        VRGameManager.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
        ScreenShareManager.OnScreenShareStarted -= OnScreenShareStarted;
        ScreenShareManager.OnScreenShareStopped -= OnScreenShareStopped;
    }

    void AutoDetectReferences()
    {
        // Auto-detect Whiteboard le plus proche
        if (targetWhiteboard == null)
        {
            Whiteboard[] all = FindObjectsByType<Whiteboard>(FindObjectsSortMode.None);
            float closest = float.MaxValue;
            foreach (var wb in all)
            {
                float dist = Vector3.Distance(transform.position, wb.transform.position);
                if (dist < closest && dist <= autoDetectRadius)
                {
                    closest = dist;
                    targetWhiteboard = wb;
                }
            }
            if (targetWhiteboard != null)
                Debug.Log($"[WhiteboardUIHelper] Auto-détecté Whiteboard: {targetWhiteboard.id}");
        }

        // Auto-detect DrawingSurface la plus proche
        if (targetDrawingSurface == null)
        {
            WhiteboardDrawingSurface[] all = FindObjectsByType<WhiteboardDrawingSurface>(FindObjectsSortMode.None);
            float closest = float.MaxValue;
            foreach (var ds in all)
            {
                float dist = Vector3.Distance(transform.position, ds.transform.position);
                if (dist < closest && dist <= autoDetectRadius)
                {
                    closest = dist;
                    targetDrawingSurface = ds;
                }
            }
            if (targetDrawingSurface != null)
                Debug.Log($"[WhiteboardUIHelper] Auto-détecté DrawingSurface: {targetDrawingSurface.id}");
        }
    }

    void CacheDrawingComponents()
    {
        _markers = FindObjectsByType<WhiteboardMarker>(FindObjectsSortMode.None);
        Debug.Log($"[WhiteboardUIHelper] Trouvé {_markers.Length} marker(s)");
    }

    void OnLocalPlayerSpawned(GameObject player)
    {
        _desktopDrawer = player.GetComponentInChildren<DesktopWhiteboardDrawer>();
        if (_desktopDrawer != null)
        {
            _desktopDrawer.SetColor(_currentColor);
            Debug.Log("[WhiteboardUIHelper] DesktopWhiteboardDrawer trouvé");
        }
    }

    // ============================================
    // COULEURS - Connecter aux boutons
    // ============================================

    /// <summary>
    /// Change la couleur par index (0, 1, 2, etc.)
    /// Connecter au onClick d'un bouton couleur
    /// </summary>
    public void SetColorByIndex(int index)
    {
        if (index < 0 || index >= colors.Length)
        {
            Debug.LogWarning($"[WhiteboardUIHelper] Index couleur invalide: {index}");
            return;
        }

        SetColor(colors[index]);
    }

    /// <summary>
    /// Couleurs prédéfinies - Connecter directement aux boutons
    /// </summary>
    public void SetColorBlue() => SetColor(Color.blue);
    public void SetColorRed() => SetColor(Color.red);
    public void SetColorGreen() => SetColor(Color.green);
    public void SetColorYellow() => SetColor(Color.yellow);
    public void SetColorBlack() => SetColor(Color.black);
    public void SetColorWhite() => SetColor(Color.white);
    public void SetColorCyan() => SetColor(Color.cyan);
    public void SetColorMagenta() => SetColor(Color.magenta);
    public void SetColorOrange() => SetColor(new Color(1f, 0.5f, 0f));

    /// <summary>
    /// Change la couleur pour tous les systèmes de dessin
    /// </summary>
    public void SetColor(Color color)
    {
        _currentColor = color;

        // Appliquer aux markers VR
        if (_markers != null)
        {
            foreach (var marker in _markers)
            {
                if (marker != null)
                    marker.SetColor(color);
            }
        }

        // Appliquer au drawer Desktop
        if (_desktopDrawer != null)
        {
            _desktopDrawer.SetColor(color);
        }
        else
        {
            // Essayer de trouver le drawer
            var localPlayer = VRGameManager.Instance?.GetLocalPlayer();
            if (localPlayer != null)
            {
                _desktopDrawer = localPlayer.GetComponentInChildren<DesktopWhiteboardDrawer>();
                if (_desktopDrawer != null)
                    _desktopDrawer.SetColor(color);
            }
        }

        Debug.Log($"[WhiteboardUIHelper] Couleur: {color}");
    }

    // ============================================
    // CLEAR - Connecter au bouton Effacer
    // ============================================

    /// <summary>
    /// Efface le whiteboard (synchronisé réseau)
    /// Connecter au onClick du bouton Clear
    /// </summary>
    public void Clear()
    {
        if (targetDrawingSurface != null)
        {
            targetDrawingSurface.RequestClear();
            Debug.Log("[WhiteboardUIHelper] Clear demandé");
        }
        else
        {
            Debug.LogWarning("[WhiteboardUIHelper] Pas de DrawingSurface assignée!");
        }
    }

    // ============================================
    // SCREEN SHARE - Connecter aux boutons
    // ============================================

    /// <summary>
    /// Démarre le partage d'écran
    /// Connecter au onClick du bouton "Partager"
    /// </summary>
    public void StartScreenShare()
    {
        if (ScreenShareManager.Instance == null)
        {
            Debug.LogError("[WhiteboardUIHelper] ScreenShareManager non trouvé!");
            return;
        }

        if (!ScreenShareManager.Instance.CanShare())
        {
            Debug.LogWarning("[WhiteboardUIHelper] Partage non disponible (mode VR ou pas en room)");
            UpdateShareStatus("Non disponible");
            return;
        }

        if (targetWhiteboard == null)
        {
            Debug.LogError("[WhiteboardUIHelper] Pas de Whiteboard assigné!");
            return;
        }

        ScreenShareManager.Instance.StartSharing(targetWhiteboard);
        Debug.Log("[WhiteboardUIHelper] Partage démarré");
    }

    /// <summary>
    /// Arrête le partage d'écran
    /// Connecter au onClick du bouton "Arrêter"
    /// </summary>
    public void StopScreenShare()
    {
        if (ScreenShareManager.Instance != null && ScreenShareManager.Instance.IsSharing)
        {
            ScreenShareManager.Instance.StopSharing();
            Debug.Log("[WhiteboardUIHelper] Partage arrêté");
        }
    }

    /// <summary>
    /// Rafraîchit la liste des fenêtres disponibles
    /// Connecter au onClick d'un bouton "Rafraîchir" ou appeler avant d'afficher le dropdown
    /// </summary>
    public void RefreshWindowList()
    {
        if (ScreenShareManager.Instance == null) return;

        _windowList = ScreenShareManager.Instance.GetAvailableWindows();

        if (screenDropdown != null)
        {
            screenDropdown.ClearOptions();

            List<string> options = new List<string> { "Écran principal (Unity)" };
            foreach (var window in _windowList)
            {
                string title = window.Title;
                if (string.IsNullOrEmpty(title)) title = "Fenêtre sans titre";
                if (title.Length > 30) title = title.Substring(0, 27) + "...";
                options.Add(title);
            }

            screenDropdown.AddOptions(options);
        }

        Debug.Log($"[WhiteboardUIHelper] {_windowList.Count} fenêtres trouvées");
    }

    /// <summary>
    /// Sélectionne une fenêtre par index du dropdown
    /// Connecter au onValueChanged du Dropdown
    /// </summary>
    public void SelectWindow(int dropdownIndex)
    {
        if (ScreenShareManager.Instance == null) return;

        if (dropdownIndex == 0)
        {
            // Écran principal Unity
            ScreenShareManager.Instance.SelectWindow(null);
            Debug.Log("[WhiteboardUIHelper] Sélectionné: Écran principal");
        }
        else
        {
            int windowIndex = dropdownIndex - 1;
            if (windowIndex >= 0 && windowIndex < _windowList.Count)
            {
                ScreenShareManager.Instance.SelectWindow(_windowList[windowIndex]);
                Debug.Log($"[WhiteboardUIHelper] Sélectionné: {_windowList[windowIndex].Title}");
            }
        }
    }

    /// <summary>
    /// Sélectionne directement une fenêtre par son index (0 = première fenêtre externe)
    /// -1 = écran Unity
    /// </summary>
    public void SelectWindowByIndex(int index)
    {
        if (ScreenShareManager.Instance == null) return;

        if (index < 0)
        {
            ScreenShareManager.Instance.SelectWindow(null);
        }
        else
        {
            ScreenShareManager.Instance.SelectWindowByIndex(index);
        }
    }

    // ============================================
    // STATUS
    // ============================================

    void OnScreenShareStarted(string whiteboardId, string sharerId, string sharerName)
    {
        UpdateShareStatus($"Partage: {sharerName}");
    }

    void OnScreenShareStopped(string whiteboardId, string sharerId)
    {
        UpdateShareStatus("Pas de partage");
    }

    void UpdateShareStatus(string status = null)
    {
        if (shareStatusText == null) return;

        if (status != null)
        {
            shareStatusText.text = status;
        }
        else if (ScreenShareManager.Instance != null && ScreenShareManager.Instance.IsSharing)
        {
            shareStatusText.text = "Partage en cours...";
        }
        else
        {
            shareStatusText.text = "Pas de partage";
        }
    }

    // ============================================
    // UTILITAIRES
    // ============================================

    /// <summary>
    /// Retourne true si on peut partager (Desktop mode + en room)
    /// Utiliser pour activer/désactiver le bouton partage
    /// </summary>
    public bool CanShare()
    {
        return ScreenShareManager.Instance != null && ScreenShareManager.Instance.CanShare();
    }

    /// <summary>
    /// Retourne true si un partage est en cours
    /// </summary>
    public bool IsSharing()
    {
        return ScreenShareManager.Instance != null && ScreenShareManager.Instance.IsSharing;
    }
}
