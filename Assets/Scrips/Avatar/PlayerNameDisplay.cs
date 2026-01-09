using UnityEngine;
using TMPro;

/// <summary>
/// Affiche le nom du joueur au-dessus de son avatar.
/// Se positionne automatiquement au-dessus de la tête et fait face à la caméra.
/// </summary>
public class PlayerNameDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private Transform headTransform;

    [Header("Settings")]
    [SerializeField] private float heightOffset = 0.3f;
    [SerializeField] private bool lookAtCamera = true;
    [SerializeField] private bool useWorldSpace = true;

    [Header("Appearance")]
    [SerializeField] private float fontSize = 3f;
    [SerializeField] private Color defaultColor = Color.white;
    [SerializeField] private Color backgroundColor = new Color(0, 0, 0, 0.5f);

    private Camera _mainCamera;
    private string _currentName = "Player";

    void Awake()
    {
        // Create TextMeshPro if not assigned
        if (nameText == null)
        {
            CreateNameText();
        }
    }

    void Start()
    {
        _mainCamera = Camera.main;

        // Try to find head transform if not assigned
        if (headTransform == null)
        {
            headTransform = FindHeadTransform();
        }

        // Initial setup
        if (nameText != null)
        {
            nameText.fontSize = fontSize;
            nameText.color = defaultColor;
            nameText.alignment = TextAlignmentOptions.Center;
        }
    }

    void LateUpdate()
    {
        UpdatePosition();
        UpdateRotation();
    }

    void CreateNameText()
    {
        // Create a child GameObject for the name
        GameObject textObj = new GameObject("NameDisplay");
        textObj.transform.SetParent(transform, false);
        textObj.transform.localPosition = Vector3.up * heightOffset;

        nameText = textObj.AddComponent<TextMeshPro>();
        nameText.text = _currentName;
        nameText.fontSize = fontSize;
        nameText.color = defaultColor;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.enableWordWrapping = false;

        // Set sorting for proper rendering
        var renderer = nameText.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sortingOrder = 100;
        }

        Debug.Log("[PlayerNameDisplay] Created TextMeshPro component");
    }

    Transform FindHeadTransform()
    {
        // Common names for head transforms
        string[] headNames = { "Head", "HeadTarget", "CameraOffset", "Main Camera" };

        foreach (string name in headNames)
        {
            Transform found = FindChildRecursive(transform, name);
            if (found != null)
            {
                return found;
            }
        }

        // Fallback to this transform
        return transform;
    }

    Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains(name))
                return child;

            Transform found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    void UpdatePosition()
    {
        if (nameText == null) return;

        Vector3 targetPosition;

        if (useWorldSpace && headTransform != null)
        {
            // Position above head in world space
            targetPosition = headTransform.position + Vector3.up * heightOffset;
        }
        else
        {
            // Position relative to this transform
            targetPosition = transform.position + Vector3.up * heightOffset;
        }

        nameText.transform.position = targetPosition;
    }

    void UpdateRotation()
    {
        if (nameText == null || !lookAtCamera) return;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        // Make text face the camera (billboard effect)
        Vector3 lookDirection = nameText.transform.position - _mainCamera.transform.position;
        lookDirection.y = 0; // Keep upright

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            nameText.transform.rotation = Quaternion.LookRotation(lookDirection);
        }
    }

    // ============================
    // PUBLIC API
    // ============================

    /// <summary>
    /// Définit le nom à afficher
    /// </summary>
    public void SetName(string name)
    {
        _currentName = string.IsNullOrEmpty(name) ? "Player" : name;

        if (nameText != null)
        {
            nameText.text = _currentName;
        }
    }

    /// <summary>
    /// Définit la couleur du texte
    /// </summary>
    public void SetColor(Color color)
    {
        if (nameText != null)
        {
            nameText.color = color;
        }
    }

    /// <summary>
    /// Définit la taille du texte
    /// </summary>
    public void SetFontSize(float size)
    {
        fontSize = size;
        if (nameText != null)
        {
            nameText.fontSize = size;
        }
    }

    /// <summary>
    /// Active ou désactive l'affichage du nom
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (nameText != null)
        {
            nameText.gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// Définit la référence à la tête pour le positionnement
    /// </summary>
    public void SetHeadTransform(Transform head)
    {
        headTransform = head;
    }

    /// <summary>
    /// Retourne le nom actuellement affiché
    /// </summary>
    public string GetCurrentName()
    {
        return _currentName;
    }
}
