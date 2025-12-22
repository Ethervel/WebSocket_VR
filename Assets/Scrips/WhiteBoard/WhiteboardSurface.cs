using UnityEngine;

/// <summary>
/// Surface de dessin whiteboard physique dans le monde VR
/// Gère la texture et le rendu des strokes
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class WhiteboardSurface : MonoBehaviour
{
    [Header("Identification")]
    [Tooltip("ID unique de ce whiteboard (doit être unique dans la scène)")]
    public string whiteboardId = "whiteboard_01";
    
    [Header("Texture Settings")]
    [Tooltip("Résolution de la texture (2048 recommandé)")]
    public int textureResolution = 2048;
    
    [Tooltip("Couleur de fond du whiteboard")]
    public Color backgroundColor = Color.white;
    
    [Tooltip("Activer la persistance (sauvegarder les dessins)")]
    public bool enablePersistence = false;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    // Composants
    private Texture2D _texture;
    private Color[] _pixels;
    private MeshRenderer _renderer;
    private Material _material;
    
    // État
    private bool _isDirty = false;
    private float _lastApplyTime = 0f;
    private const float ApplyInterval = 0.016f; // ~60 FPS
    
    void Start()
    {
        InitializeTexture();
        
        // S'enregistrer auprès du manager
        if (VRWhiteboardManager.Instance != null)
        {
            VRWhiteboardManager.Instance.RegisterWhiteboard(whiteboardId, this);
        }
        else
        {
            Debug.LogWarning($"[Whiteboard] Manager not found! Whiteboard {whiteboardId} won't be synced.");
        }
    }
    
    void OnDestroy()
    {
        // Désenregistrer
        if (VRWhiteboardManager.Instance != null)
        {
            VRWhiteboardManager.Instance.UnregisterWhiteboard(whiteboardId);
        }
        
        // Nettoyer la texture
        if (_texture != null)
        {
            Destroy(_texture);
        }
        
        // Nettoyer le material
        if (_material != null)
        {
            Destroy(_material);
        }
    }
    
    void Update()
    {
        // Appliquer les changements de texture périodiquement
        if (_isDirty && Time.time - _lastApplyTime >= ApplyInterval)
        {
            ApplyTexture();
        }
    }
    
    #region Initialization
    
    void InitializeTexture()
    {
        _renderer = GetComponent<MeshRenderer>();
        
        // Créer une nouvelle texture
        _texture = new Texture2D(textureResolution, textureResolution, TextureFormat.RGBA32, false);
        _texture.filterMode = FilterMode.Bilinear;
        _texture.wrapMode = TextureWrapMode.Clamp;
        
        // Initialiser le tableau de pixels
        int pixelCount = textureResolution * textureResolution;
        _pixels = new Color[pixelCount];
        
        // Remplir avec la couleur de fond
        for (int i = 0; i < pixelCount; i++)
        {
            _pixels[i] = backgroundColor;
        }
        
        _texture.SetPixels(_pixels);
        _texture.Apply();
        
        // Créer un material unique pour ce whiteboard
        _material = new Material(_renderer.material);
        _material.mainTexture = _texture;
        _renderer.material = _material;
        
        LogDebug($"[Whiteboard] Initialized {whiteboardId}: {textureResolution}x{textureResolution}");
    }
    
    #endregion
    
    #region Drawing
    
    /// <summary>
    /// Dessine un point sur le whiteboard
    /// </summary>
    public void DrawPoint(WhiteboardStrokePoint point)
    {
        if (_texture == null || _pixels == null) return;
        
        // Convertir UV (0-1) en coordonnées pixel
        int centerX = Mathf.RoundToInt(point.uv.x * textureResolution);
        int centerY = Mathf.RoundToInt(point.uv.y * textureResolution);
        
        // Calculer le rayon en pixels
        int radius = Mathf.Max(1, Mathf.RoundToInt(point.size * textureResolution * 50f));
        
        // Dessiner un cercle antialiasé
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                float distance = Mathf.Sqrt(x * x + y * y);
                
                if (distance <= radius)
                {
                    int px = centerX + x;
                    int py = centerY + y;
                    
                    // Vérifier les limites
                    if (px < 0 || px >= textureResolution || py < 0 || py >= textureResolution)
                        continue;
                    
                    int index = py * textureResolution + px;
                    
                    // Antialiasing simple
                    float alpha = 1f - (distance / radius);
                    alpha = Mathf.Clamp01(alpha);
                    
                    // Mélanger avec la couleur existante
                    Color existingColor = _pixels[index];
                    _pixels[index] = Color.Lerp(existingColor, point.color, alpha);
                }
            }
        }
        
        _isDirty = true;
    }
    
    /// <summary>
    /// Efface complètement le whiteboard
    /// </summary>
    public void Clear()
    {
        if (_pixels == null) return;
        
        for (int i = 0; i < _pixels.Length; i++)
        {
            _pixels[i] = backgroundColor;
        }
        
        _texture.SetPixels(_pixels);
        _texture.Apply();
        
        _isDirty = false;
        
        LogDebug($"[Whiteboard] Cleared {whiteboardId}");
    }
    
    /// <summary>
    /// Applique les changements à la texture
    /// </summary>
    void ApplyTexture()
    {
        if (_texture == null || _pixels == null) return;
        
        _texture.SetPixels(_pixels);
        _texture.Apply();
        
        _isDirty = false;
        _lastApplyTime = Time.time;
    }
    
    #endregion
    
    #region UV Conversion
    
    /// <summary>
    /// Convertit une position 3D en coordonnées UV (0-1) sur le whiteboard
    /// </summary>
    public bool WorldToUV(Vector3 worldPos, out Vector2 uv)
    {
        uv = Vector2.zero;
        
        // Convertir en espace local du whiteboard
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        
        // Pour un Quad Unity standard, les limites sont de -0.5 à 0.5
        // Vérifier que le point est dans les limites
        if (Mathf.Abs(localPos.x) > 0.5f || Mathf.Abs(localPos.y) > 0.5f)
        {
            return false;
        }
        
        // Convertir en coordonnées UV (0-1)
        uv.x = localPos.x + 0.5f;
        uv.y = localPos.y + 0.5f;
        
        // Clamper pour être sûr
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);
        
        return true;
    }
    
    /// <summary>
    /// Convertit un RaycastHit en coordonnées UV
    /// </summary>
    public bool HitToUV(RaycastHit hit, out Vector2 uv)
    {
        // Unity fournit directement les UV du hit
        uv = hit.textureCoord;
        return true;
    }
    
    #endregion
    
    #region Persistence (optionnel)
    
    /// <summary>
    /// Sauvegarde le contenu du whiteboard en PNG
    /// </summary>
    public void SaveToPNG(string filename)
    {
        if (_texture == null) return;
        
        byte[] bytes = _texture.EncodeToPNG();
        System.IO.File.WriteAllBytes(filename, bytes);
        
        LogDebug($"[Whiteboard] Saved to {filename}");
    }
    
    /// <summary>
    /// Charge le contenu depuis un PNG
    /// </summary>
    public void LoadFromPNG(string filename)
    {
        if (!System.IO.File.Exists(filename)) return;
        
        byte[] bytes = System.IO.File.ReadAllBytes(filename);
        _texture.LoadImage(bytes);
        _pixels = _texture.GetPixels();
        
        LogDebug($"[Whiteboard] Loaded from {filename}");
    }
    
    #endregion
    
    #region Debug
    
    void LogDebug(string message)
    {
        if (showDebugInfo)
            Debug.Log(message);
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugInfo) return;
        
        // Afficher les limites du whiteboard
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }
    
    #endregion
}