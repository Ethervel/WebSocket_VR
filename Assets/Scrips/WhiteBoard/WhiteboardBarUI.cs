using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controller pour l'UI en barre du Whiteboard.
/// L'UI est créée dans l'éditeur via le menu contextuel, pas au runtime.
/// Tu peux positionner l'UI manuellement dans la scène.
/// </summary>
public class WhiteboardBarUI : MonoBehaviour
{
    [Header("References (auto-détectées si vides)")]
    public Whiteboard targetWhiteboard;
    public WhiteboardDrawingSurface targetDrawingSurface;

    [Header("UI References (assignées auto par le setup)")]
    public GameObject menuContent;
    public GameObject sharePanel;
    public Text burgerText;
    public Dropdown screenDropdown;

    [Header("Animation")]
    public float animationDuration = 0.3f;

    [Header("Colors")]
    public Color[] availableColors = new Color[]
    {
        Color.blue, Color.red, Color.green,
        Color.yellow, Color.black, Color.white
    };

    // State
    private bool _isMenuOpen = true;
    private bool _isSharePanelOpen = false;
    private bool _isAnimating = false;
    private RectTransform _canvasRect;
    private float _menuFullWidth;
    private float _menuCollapsedWidth = 60f;

    // Cache
    private WhiteboardMarker[] _markers;
    private DesktopWhiteboardDrawer _desktopDrawer;
    private Color _currentColor;
    private List<WindowCapture.WindowInfo> _windowList = new List<WindowCapture.WindowInfo>();

    void Start()
    {
        AutoDetectReferences();
        CacheDrawingComponents();
        ConnectButtons(); // Connecter les boutons au runtime

        _currentColor = availableColors.Length > 0 ? availableColors[0] : Color.blue;

        // Get canvas rect for animation
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            _canvasRect = canvas.GetComponent<RectTransform>();
            _menuFullWidth = _canvasRect.sizeDelta.x;
        }

        // Events
        VRGameManager.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        ScreenShareManager.OnScreenShareStarted += OnScreenShareStarted;
        ScreenShareManager.OnScreenShareStopped += OnScreenShareStopped;

        // Initial state - menu ouvert
        if (sharePanel != null)
            sharePanel.SetActive(false);

        Debug.Log("[WhiteboardBarUI] Initialisé");
    }

    void ConnectButtons()
    {
        // Trouver tous les boutons par nom et les connecter
        Button[] allButtons = GetComponentsInChildren<Button>(true);

        foreach (Button btn in allButtons)
        {
            string name = btn.gameObject.name;

            // Clear existing listeners to avoid duplicates
            btn.onClick.RemoveAllListeners();

            if (name == "BurgerButton")
            {
                btn.onClick.AddListener(ToggleMenu);
                if (burgerText == null)
                    burgerText = btn.GetComponentInChildren<Text>();
            }
            else if (name == "Btn_Blue") btn.onClick.AddListener(SetColorBlue);
            else if (name == "Btn_Red") btn.onClick.AddListener(SetColorRed);
            else if (name == "Btn_Green") btn.onClick.AddListener(SetColorGreen);
            else if (name == "Btn_Yellow") btn.onClick.AddListener(SetColorYellow);
            else if (name == "Btn_Black") btn.onClick.AddListener(SetColorBlack);
            else if (name == "Btn_White") btn.onClick.AddListener(SetColorWhite);
            else if (name == "Btn_Clear") btn.onClick.AddListener(Clear);
            else if (name == "Btn_Share") btn.onClick.AddListener(ToggleSharePanel);
            else if (name == "Btn_StartShare") btn.onClick.AddListener(StartScreenShare);
            else if (name == "Btn_StopShare") btn.onClick.AddListener(StopScreenShare);
            else if (name == "Btn_Refresh") btn.onClick.AddListener(RefreshWindowList);
        }

        // Connecter le dropdown
        Dropdown[] dropdowns = GetComponentsInChildren<Dropdown>(true);
        foreach (Dropdown dd in dropdowns)
        {
            dd.onValueChanged.RemoveAllListeners();
            dd.onValueChanged.AddListener(OnScreenSelected);
            if (screenDropdown == null)
                screenDropdown = dd;
        }

        // Auto-find references si pas assignées
        if (menuContent == null)
        {
            Transform mc = transform.Find("Background/MenuContent");
            if (mc != null) menuContent = mc.gameObject;
        }

        if (sharePanel == null)
        {
            Transform sp = transform.Find("Background/SharePanel");
            if (sp != null) sharePanel = sp.gameObject;
        }

        Debug.Log($"[WhiteboardBarUI] {allButtons.Length} boutons connectés");
    }

    void OnDestroy()
    {
        VRGameManager.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
        ScreenShareManager.OnScreenShareStarted -= OnScreenShareStarted;
        ScreenShareManager.OnScreenShareStopped -= OnScreenShareStopped;
    }

    void AutoDetectReferences()
    {
        if (targetWhiteboard == null)
            targetWhiteboard = FindAnyObjectByType<Whiteboard>();

        if (targetDrawingSurface == null)
            targetDrawingSurface = FindAnyObjectByType<WhiteboardDrawingSurface>();

        _markers = FindObjectsByType<WhiteboardMarker>(FindObjectsSortMode.None);
    }

    void CacheDrawingComponents()
    {
        var localPlayer = VRGameManager.Instance?.GetLocalPlayer();
        if (localPlayer != null)
        {
            _desktopDrawer = localPlayer.GetComponentInChildren<DesktopWhiteboardDrawer>();
        }
    }

    void OnLocalPlayerSpawned(GameObject player)
    {
        _desktopDrawer = player.GetComponentInChildren<DesktopWhiteboardDrawer>();
        if (_desktopDrawer != null)
            _desktopDrawer.SetColor(_currentColor);
    }

    // ============================================
    // BOUTONS - Connecter via Inspector
    // ============================================

    /// <summary>
    /// Toggle le menu burger - connecter au bouton burger
    /// </summary>
    public void ToggleMenu()
    {
        if (_isAnimating) return;

        _isMenuOpen = !_isMenuOpen;

        if (burgerText != null)
            burgerText.text = _isMenuOpen ? "✕" : "≡";

        if (!_isMenuOpen && sharePanel != null)
            sharePanel.SetActive(false);

        if (menuContent != null)
            StartCoroutine(AnimateMenu(_isMenuOpen));
    }

    IEnumerator AnimateMenu(bool opening)
    {
        _isAnimating = true;

        if (_canvasRect == null)
        {
            if (menuContent != null)
                menuContent.SetActive(opening);
            _isAnimating = false;
            yield break;
        }

        float startWidth = opening ? _menuCollapsedWidth : _menuFullWidth;
        float endWidth = opening ? _menuFullWidth : _menuCollapsedWidth;

        if (opening && menuContent != null)
            menuContent.SetActive(true);

        CanvasGroup cg = menuContent?.GetComponent<CanvasGroup>();
        if (cg == null && menuContent != null)
            cg = menuContent.AddComponent<CanvasGroup>();

        float elapsed = 0f;
        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / animationDuration;

            _canvasRect.sizeDelta = new Vector2(Mathf.Lerp(startWidth, endWidth, t), _canvasRect.sizeDelta.y);

            if (cg != null)
                cg.alpha = opening ? t : (1 - t);

            yield return null;
        }

        _canvasRect.sizeDelta = new Vector2(endWidth, _canvasRect.sizeDelta.y);

        if (!opening && menuContent != null)
            menuContent.SetActive(false);

        if (cg != null)
            cg.alpha = opening ? 1 : 0;

        _isAnimating = false;
    }

    /// <summary>
    /// Toggle le panel de partage - connecter au bouton Share
    /// </summary>
    public void ToggleSharePanel()
    {
        _isSharePanelOpen = !_isSharePanelOpen;

        if (_isSharePanelOpen)
            RefreshWindowList();

        if (sharePanel != null)
            sharePanel.SetActive(_isSharePanelOpen);
    }

    /// <summary>
    /// Couleurs - connecter aux boutons couleur
    /// </summary>
    public void SetColorByIndex(int index)
    {
        if (index < 0 || index >= availableColors.Length) return;
        SetColor(availableColors[index]);
    }

    public void SetColorBlue() => SetColor(Color.blue);
    public void SetColorRed() => SetColor(Color.red);
    public void SetColorGreen() => SetColor(Color.green);
    public void SetColorYellow() => SetColor(Color.yellow);
    public void SetColorBlack() => SetColor(Color.black);
    public void SetColorWhite() => SetColor(Color.white);

    public void SetColor(Color color)
    {
        _currentColor = color;

        if (_markers != null)
        {
            foreach (var m in _markers)
                if (m != null) m.SetColor(color);
        }

        if (_desktopDrawer != null)
        {
            _desktopDrawer.SetColor(color);
        }
        else
        {
            var localPlayer = VRGameManager.Instance?.GetLocalPlayer();
            if (localPlayer != null)
            {
                _desktopDrawer = localPlayer.GetComponentInChildren<DesktopWhiteboardDrawer>();
                if (_desktopDrawer != null)
                    _desktopDrawer.SetColor(color);
            }
        }

        Debug.Log($"[WhiteboardBarUI] Couleur: {color}");
    }

    /// <summary>
    /// Clear - connecter au bouton Effacer
    /// </summary>
    public void Clear()
    {
        if (targetDrawingSurface != null)
        {
            targetDrawingSurface.RequestClear();
            Debug.Log("[WhiteboardBarUI] Clear");
        }
    }

    /// <summary>
    /// Rafraîchir la liste des fenêtres - connecter au bouton Refresh
    /// </summary>
    public void RefreshWindowList()
    {
        if (ScreenShareManager.Instance == null) return;

        _windowList = ScreenShareManager.Instance.GetAvailableWindows();

        if (screenDropdown != null)
        {
            screenDropdown.ClearOptions();
            List<string> options = new List<string> { "Écran principal (Unity)" };

            foreach (var w in _windowList)
            {
                string title = w.Title ?? "Fenêtre";
                if (title.Length > 35) title = title.Substring(0, 32) + "...";
                options.Add(title);
            }

            screenDropdown.AddOptions(options);
        }

        Debug.Log($"[WhiteboardBarUI] {_windowList.Count} fenêtres");
    }

    /// <summary>
    /// Sélection d'écran - connecter au onValueChanged du Dropdown
    /// </summary>
    public void OnScreenSelected(int index)
    {
        if (ScreenShareManager.Instance == null) return;

        if (index == 0)
        {
            ScreenShareManager.Instance.SelectWindow(null);
        }
        else if (index - 1 < _windowList.Count)
        {
            ScreenShareManager.Instance.SelectWindow(_windowList[index - 1]);
        }
    }

    /// <summary>
    /// Démarrer le partage - connecter au bouton Start
    /// </summary>
    public void StartScreenShare()
    {
        if (ScreenShareManager.Instance == null || targetWhiteboard == null) return;

        if (!ScreenShareManager.Instance.CanShare())
        {
            Debug.LogWarning("[WhiteboardBarUI] Partage non disponible");
            return;
        }

        ScreenShareManager.Instance.StartSharing(targetWhiteboard);

        if (sharePanel != null)
            sharePanel.SetActive(false);
        _isSharePanelOpen = false;
    }

    /// <summary>
    /// Arrêter le partage - connecter au bouton Stop
    /// </summary>
    public void StopScreenShare()
    {
        if (ScreenShareManager.Instance != null)
        {
            ScreenShareManager.Instance.StopSharing();
        }

        if (sharePanel != null)
            sharePanel.SetActive(false);
        _isSharePanelOpen = false;
    }

    void OnScreenShareStarted(string wbId, string sharerId, string sharerName)
    {
        Debug.Log($"[WhiteboardBarUI] Partage démarré par {sharerName}");
    }

    void OnScreenShareStopped(string wbId, string sharerId)
    {
        Debug.Log("[WhiteboardBarUI] Partage arrêté");
    }
}
