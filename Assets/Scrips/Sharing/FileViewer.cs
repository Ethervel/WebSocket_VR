using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Gestion de l'ouverture et de l'affichage des fichiers partagés.
/// - Viewer intégré pour images et texte
/// - Ouverture externe pour PDF et autres documents
/// </summary>
public class FileViewer : MonoBehaviour
{
    public static FileViewer Instance { get; private set; }

    [Header("Image Viewer")]
    [Tooltip("Panel pour afficher les images")]
    public GameObject imageViewerPanel;
    public RawImage imageDisplay;
    public TextMeshProUGUI imageNameText;
    public Button imageCloseButton;

    [Header("Text Viewer")]
    [Tooltip("Panel pour afficher le texte")]
    public GameObject textViewerPanel;
    public TextMeshProUGUI textDisplay;
    public TextMeshProUGUI textNameText;
    public Button textCloseButton;
    public ScrollRect textScrollRect;

    [Header("Settings")]
    [Tooltip("Taille max du texte à afficher (caractères)")]
    public int maxTextLength = 50000;

    [Tooltip("Taille max d'image à charger en mémoire (pixels)")]
    public int maxImageSize = 4096;

    [Header("Whiteboard Display")]
    [Tooltip("Afficher les images sur le whiteboard au lieu du viewer intégré")]
    public bool displayOnWhiteboard = true;

    [Tooltip("Whiteboard cible (auto-détecté si vide)")]
    public Whiteboard targetWhiteboard;

    [Header("Debug")]
    public bool showDebugLogs = true;

    // State
    private Texture2D _currentTexture;
    private string _currentFilePath;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Setup close buttons
        if (imageCloseButton != null)
            imageCloseButton.onClick.AddListener(CloseImageViewer);

        if (textCloseButton != null)
            textCloseButton.onClick.AddListener(CloseTextViewer);

        // Hide viewers by default
        if (imageViewerPanel != null)
            imageViewerPanel.SetActive(false);

        if (textViewerPanel != null)
            textViewerPanel.SetActive(false);
    }

    void OnDestroy()
    {
        // Clean up texture
        if (_currentTexture != null)
        {
            Destroy(_currentTexture);
            _currentTexture = null;
        }
    }

    #region Public API

    /// <summary>
    /// Ouvre un fichier avec le viewer approprié ou l'application externe
    /// </summary>
    public void OpenFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"[FileViewer] File not found: {filePath}");
            return;
        }

        string extension = Path.GetExtension(filePath);
        var viewerType = FileTypeHelper.GetViewerType(extension);

        if (showDebugLogs)
            Debug.Log($"[FileViewer] Opening '{Path.GetFileName(filePath)}' with {viewerType} viewer");

        switch (viewerType)
        {
            case FileViewerType.Image:
                OpenImageViewer(filePath);
                break;

            case FileViewerType.Text:
                OpenTextViewer(filePath);
                break;

            case FileViewerType.PDF:
            case FileViewerType.Document:
            case FileViewerType.Unknown:
            default:
                OpenWithExternalApp(filePath);
                break;
        }
    }

    /// <summary>
    /// Ouvre un fichier directement avec l'application système par défaut
    /// </summary>
    public void OpenWithExternalApp(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"[FileViewer] File not found: {filePath}");
            return;
        }

        try
        {
            // Use file:// protocol to open with default app
            string url = "file:///" + filePath.Replace("\\", "/");
            Application.OpenURL(url);

            if (showDebugLogs)
                Debug.Log($"[FileViewer] Opened with external app: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileViewer] Failed to open file: {e.Message}");
        }
    }

    /// <summary>
    /// Ouvre le dossier contenant le fichier
    /// </summary>
    public void OpenContainingFolder(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        string folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            Application.OpenURL("file:///" + folder.Replace("\\", "/"));
        }
    }

    /// <summary>
    /// Ferme tous les viewers
    /// </summary>
    public void CloseAllViewers()
    {
        CloseImageViewer();
        CloseTextViewer();
    }

    #endregion

    #region Image Viewer

    void OpenImageViewer(string filePath)
    {
        try
        {
            // Load image
            byte[] imageData = File.ReadAllBytes(filePath);

            // Clean up previous texture
            if (_currentTexture != null)
            {
                Destroy(_currentTexture);
            }

            _currentTexture = new Texture2D(2, 2);
            if (_currentTexture.LoadImage(imageData))
            {
                // Check size
                if (_currentTexture.width > maxImageSize || _currentTexture.height > maxImageSize)
                {
                    Debug.LogWarning($"[FileViewer] Image too large ({_currentTexture.width}x{_currentTexture.height}), opening externally");
                    Destroy(_currentTexture);
                    _currentTexture = null;
                    OpenWithExternalApp(filePath);
                    return;
                }

                _currentFilePath = filePath;
                string fileName = Path.GetFileName(filePath);

                // Afficher sur le whiteboard si activé
                if (displayOnWhiteboard)
                {
                    DisplayImageOnWhiteboard(_currentTexture, fileName);
                }
                else
                {
                    // Viewer intégré classique
                    DisplayImageInPanel(_currentTexture, fileName);
                }

                if (showDebugLogs)
                    Debug.Log($"[FileViewer] Image loaded: {_currentTexture.width}x{_currentTexture.height}");
            }
            else
            {
                Debug.LogError("[FileViewer] Failed to load image");
                OpenWithExternalApp(filePath);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileViewer] Error loading image: {e.Message}");
            OpenWithExternalApp(filePath);
        }
    }

    void DisplayImageOnWhiteboard(Texture2D image, string fileName)
    {
        // Auto-détecter le whiteboard si pas assigné
        if (targetWhiteboard == null)
        {
            targetWhiteboard = FindFirstObjectByType<Whiteboard>();
        }

        if (targetWhiteboard == null)
        {
            Debug.LogWarning("[FileViewer] No whiteboard found, falling back to panel");
            DisplayImageInPanel(image, fileName);
            return;
        }

        // Afficher sur le whiteboard
        string presenterId = VRNetworkManager.LocalId ?? "local";
        targetWhiteboard.DisplayImage(image, presenterId, fileName);

        if (showDebugLogs)
            Debug.Log($"[FileViewer] Image displayed on whiteboard: {fileName}");
    }

    void DisplayImageInPanel(Texture2D image, string fileName)
    {
        if (imageViewerPanel == null || imageDisplay == null)
        {
            Debug.LogWarning("[FileViewer] Image viewer panel not configured, opening externally");
            OpenWithExternalApp(_currentFilePath);
            return;
        }

        imageDisplay.texture = image;
        AdjustImageAspectRatio();

        if (imageNameText != null)
            imageNameText.text = fileName;

        imageViewerPanel.SetActive(true);

        // Close text viewer if open
        if (textViewerPanel != null)
            textViewerPanel.SetActive(false);
    }

    void AdjustImageAspectRatio()
    {
        if (_currentTexture == null || imageDisplay == null)
            return;

        var aspectRatioFitter = imageDisplay.GetComponent<AspectRatioFitter>();
        if (aspectRatioFitter != null)
        {
            aspectRatioFitter.aspectRatio = (float)_currentTexture.width / _currentTexture.height;
        }
    }

    public void CloseImageViewer()
    {
        // Fermer le panel intégré
        if (imageViewerPanel != null)
            imageViewerPanel.SetActive(false);

        // Arrêter le mode présentation du whiteboard
        if (targetWhiteboard != null && targetWhiteboard.IsPresentationMode)
        {
            targetWhiteboard.StopPresentationMode();
        }

        if (_currentTexture != null)
        {
            Destroy(_currentTexture);
            _currentTexture = null;
        }

        if (imageDisplay != null)
            imageDisplay.texture = null;

        _currentFilePath = null;
    }

    #endregion

    #region Text Viewer

    void OpenTextViewer(string filePath)
    {
        if (textViewerPanel == null || textDisplay == null)
        {
            Debug.LogWarning("[FileViewer] Text viewer panel not configured, opening externally");
            OpenWithExternalApp(filePath);
            return;
        }

        try
        {
            string text = File.ReadAllText(filePath);

            // Truncate if too long
            if (text.Length > maxTextLength)
            {
                text = text.Substring(0, maxTextLength) + "\n\n... (truncated, file too large)";
            }

            textDisplay.text = text;

            if (textNameText != null)
                textNameText.text = Path.GetFileName(filePath);

            _currentFilePath = filePath;
            textViewerPanel.SetActive(true);

            // Reset scroll position
            if (textScrollRect != null)
            {
                textScrollRect.verticalNormalizedPosition = 1f;
            }

            // Close image viewer if open
            if (imageViewerPanel != null)
                imageViewerPanel.SetActive(false);

            if (showDebugLogs)
                Debug.Log($"[FileViewer] Text loaded: {text.Length} chars");
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileViewer] Error loading text: {e.Message}");
            OpenWithExternalApp(filePath);
        }
    }

    public void CloseTextViewer()
    {
        if (textViewerPanel != null)
            textViewerPanel.SetActive(false);

        if (textDisplay != null)
            textDisplay.text = "";

        _currentFilePath = null;
    }

    #endregion

    #region Utility

    /// <summary>
    /// Ouvre le fichier actuellement affiché avec l'application externe
    /// </summary>
    public void OpenCurrentInExternalApp()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            OpenWithExternalApp(_currentFilePath);
        }
    }

    /// <summary>
    /// Copie le chemin du fichier actuel dans le presse-papiers
    /// </summary>
    public void CopyCurrentPath()
    {
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            GUIUtility.systemCopyBuffer = _currentFilePath;
            if (showDebugLogs)
                Debug.Log($"[FileViewer] Path copied: {_currentFilePath}");
        }
    }

    #endregion
}
