using UnityEngine;
using TMPro;

/// <summary>
/// Écran virtuel pour afficher le partage d'écran dans la scène 3D.
/// - Affiche le flux vidéo reçu
/// - Peut être déplacé/redimensionné
/// - Affiche le nom du présentateur
/// </summary>
public class VirtualScreen : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Renderer pour afficher la texture")]
    public Renderer screenRenderer;

    [Tooltip("Material à utiliser (sera cloné)")]
    public Material screenMaterial;

    [Header("Info Panel")]
    [Tooltip("Texte pour le nom du présentateur")]
    public TextMeshPro presenterNameText;

    [Tooltip("Texte pour la résolution")]
    public TextMeshPro resolutionText;

    [Header("Frame")]
    [Tooltip("Bordure de l'écran")]
    public GameObject frameBorder;

    [Tooltip("Couleur de la bordure")]
    public Color frameColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    [Header("Settings")]
    [Tooltip("Épaisseur de la bordure")]
    public float borderThickness = 0.05f;

    [Tooltip("Offset du panneau info")]
    public Vector3 infoPanelOffset = new Vector3(0, -0.1f, 0);

    // State
    private string _presenterName;
    private int _width;
    private int _height;
    private Material _instanceMaterial;
    private bool _isInitialized = false;

    void Awake()
    {
        // Auto-setup if components not assigned
        SetupComponents();
    }

    void OnDestroy()
    {
        // Nettoyer le material cloné
        if (_instanceMaterial != null)
        {
            Destroy(_instanceMaterial);
            _instanceMaterial = null;
        }
    }

    #region Setup

    void SetupComponents()
    {
        // Renderer
        if (screenRenderer == null)
        {
            screenRenderer = GetComponent<Renderer>();
            if (screenRenderer == null)
            {
                screenRenderer = GetComponentInChildren<Renderer>();
            }
        }

        // Créer un material d'instance
        if (screenRenderer != null)
        {
            if (screenMaterial != null)
            {
                _instanceMaterial = new Material(screenMaterial);
            }
            else
            {
                // Créer un material unlit basique
                _instanceMaterial = new Material(Shader.Find("Unlit/Texture"));
                if (_instanceMaterial.shader == null)
                {
                    _instanceMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                }
            }

            screenRenderer.material = _instanceMaterial;
        }
    }

    /// <summary>
    /// Initialise l'écran virtuel avec les informations du partage
    /// </summary>
    public void Initialize(string presenterName, int width, int height)
    {
        _presenterName = presenterName;
        _width = width;
        _height = height;

        // Ajuster le ratio
        float aspectRatio = (float)width / height;
        Vector3 scale = transform.localScale;
        scale.y = scale.x / aspectRatio;
        transform.localScale = scale;

        // Mettre à jour les textes
        UpdateInfoPanel();

        // Créer la bordure si nécessaire
        if (frameBorder == null)
        {
            CreateFrameBorder();
        }

        _isInitialized = true;

        Debug.Log($"[VirtualScreen] Initialized - {presenterName} ({width}x{height})");
    }

    void UpdateInfoPanel()
    {
        if (presenterNameText != null)
        {
            presenterNameText.text = $"Presenting: {_presenterName}";
        }

        if (resolutionText != null)
        {
            resolutionText.text = $"{_width}x{_height}";
        }
    }

    void CreateFrameBorder()
    {
        // Créer un parent pour la bordure
        GameObject frameParent = new GameObject("Frame");
        frameParent.transform.SetParent(transform, false);
        frameParent.transform.localPosition = Vector3.zero;
        frameParent.transform.localRotation = Quaternion.identity;

        // Créer les 4 côtés de la bordure
        CreateBorderSide(frameParent.transform, "Top", new Vector3(0, 0.5f + borderThickness / 2, 0), new Vector3(1 + borderThickness * 2, borderThickness, 0.01f));
        CreateBorderSide(frameParent.transform, "Bottom", new Vector3(0, -0.5f - borderThickness / 2, 0), new Vector3(1 + borderThickness * 2, borderThickness, 0.01f));
        CreateBorderSide(frameParent.transform, "Left", new Vector3(-0.5f - borderThickness / 2, 0, 0), new Vector3(borderThickness, 1, 0.01f));
        CreateBorderSide(frameParent.transform, "Right", new Vector3(0.5f + borderThickness / 2, 0, 0), new Vector3(borderThickness, 1, 0.01f));

        frameBorder = frameParent;
    }

    void CreateBorderSide(Transform parent, string name, Vector3 localPos, Vector3 scale)
    {
        GameObject side = GameObject.CreatePrimitive(PrimitiveType.Cube);
        side.name = name;
        side.transform.SetParent(parent, false);
        side.transform.localPosition = localPos;
        side.transform.localScale = scale;

        // Supprimer le collider
        var collider = side.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        // Appliquer la couleur
        var renderer = side.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null)
            {
                mat = new Material(Shader.Find("Standard"));
            }
            mat.color = frameColor;
            renderer.material = mat;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Met à jour la texture affichée
    /// </summary>
    public void UpdateTexture(Texture texture)
    {
        if (_instanceMaterial != null && texture != null)
        {
            _instanceMaterial.mainTexture = texture;
        }
    }

    /// <summary>
    /// Affiche l'écran
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Masque l'écran
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Définit la position de l'écran
    /// </summary>
    public void SetPosition(Vector3 position)
    {
        transform.position = position;
    }

    /// <summary>
    /// Fait face à une cible (camera, joueur, etc.)
    /// </summary>
    public void LookAt(Transform target)
    {
        if (target != null)
        {
            Vector3 direction = target.position - transform.position;
            direction.y = 0; // Garder l'écran vertical
            if (direction != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(-direction);
            }
        }
    }

    /// <summary>
    /// Fait face à la caméra principale
    /// </summary>
    public void LookAtCamera()
    {
        if (Camera.main != null)
        {
            LookAt(Camera.main.transform);
        }
    }

    /// <summary>
    /// Redimensionne l'écran en gardant le ratio
    /// </summary>
    public void SetSize(float width)
    {
        if (_width <= 0 || _height <= 0) return;

        float aspectRatio = (float)_width / _height;
        transform.localScale = new Vector3(width, width / aspectRatio, 1f);
    }

    /// <summary>
    /// Active/désactive la bordure
    /// </summary>
    public void SetFrameVisible(bool visible)
    {
        if (frameBorder != null)
        {
            frameBorder.SetActive(visible);
        }
    }

    /// <summary>
    /// Change la couleur de la bordure
    /// </summary>
    public void SetFrameColor(Color color)
    {
        frameColor = color;

        if (frameBorder != null)
        {
            var renderers = frameBorder.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer.material != null)
                {
                    renderer.material.color = color;
                }
            }
        }
    }

    public string PresenterName => _presenterName;
    public int Width => _width;
    public int Height => _height;
    public bool IsInitialized => _isInitialized;

    #endregion

    #region Gizmos

    void OnDrawGizmos()
    {
        // Dessiner un aperçu en mode édition
        Gizmos.color = new Color(0.3f, 0.3f, 0.8f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }

    #endregion
}
