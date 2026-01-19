using UnityEngine;
using System;

/// <summary>
/// Whiteboard de fond - affiche blanc par défaut, ou le screen share quand actif.
/// Le dessin est géré séparément par WhiteboardDrawingSurface.
/// </summary>
public class Whiteboard : MonoBehaviour
{
    [Header("Network Identity")]
    [Tooltip("ID unique pour ce tableau - doit correspondre à la DrawingSurface associée")]
    public string id = "Whiteboard_01";

    [Header("Texture Settings")]
    public Vector2 textureSize = new Vector2(2048, 2048);
    public Color defaultColor = Color.white;

    [Header("References")]
    public Renderer targetRenderer;

    [Header("Screen Share")]
    [HideInInspector] public Texture2D texture; // Texture de fond (blanc)

    // Mode présentation (screen share)
    private bool _isPresentationMode = false;
    private string _presenterName;
    private Texture2D _savedTexture; // Sauvegarde du fond avant screen share

    // Ressources screen share
    private RenderTexture _screenShareRT;

    // Events
    public static event Action<Whiteboard, bool> OnPresentationModeChanged;

    #region Properties

    /// <summary>
    /// Est-ce que le whiteboard affiche un screen share ?
    /// </summary>
    public bool IsPresentationMode => _isPresentationMode;

    /// <summary>
    /// Nom du présentateur actuel
    /// </summary>
    public string PresenterName => _presenterName;

    #endregion

    void Start()
    {
        InitializeTexture();
    }

    void OnDestroy()
    {
        CleanupResources();
    }

    void InitializeTexture()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null)
        {
            Debug.LogError($"[Whiteboard:{id}] Aucun Renderer trouvé!");
            return;
        }

        // Créer texture blanche
        texture = new Texture2D((int)textureSize.x, (int)textureSize.y);
        FillWithColor(texture, defaultColor);

        targetRenderer.material.mainTexture = texture;

        Debug.Log($"[Whiteboard:{id}] Initialized. Renderer={targetRenderer.name}, TextureSize={textureSize}");
    }

    void FillWithColor(Texture2D tex, Color color)
    {
        Color[] pixels = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
    }

    #region Screen Share (Presentation Mode)

    /// <summary>
    /// Entre en mode présentation - le fond affichera le screen share
    /// </summary>
    public void EnterPresentationMode(string presenterName)
    {
        Debug.Log($"[Whiteboard] EnterPresentationMode called on {id} by {presenterName}");

        if (_isPresentationMode)
        {
            Debug.Log($"[Whiteboard] Already in presentation mode");
            return;
        }

        // Sauvegarder le fond actuel (au cas où on voudrait le restaurer)
        if (texture != null)
        {
            _savedTexture = new Texture2D((int)textureSize.x, (int)textureSize.y);
            _savedTexture.SetPixels(texture.GetPixels());
            _savedTexture.Apply();
        }

        // Créer RenderTexture pour le screen share
        _screenShareRT = new RenderTexture((int)textureSize.x, (int)textureSize.y, 0, RenderTextureFormat.ARGB32);
        _screenShareRT.name = $"PresentationRT_{id}";
        _screenShareRT.Create();

        Debug.Log($"[Whiteboard] Created RenderTexture: {_screenShareRT.name}, size={_screenShareRT.width}x{_screenShareRT.height}, isCreated={_screenShareRT.IsCreated()}");

        // Remplir de noir en attendant la première frame
        RenderTexture.active = _screenShareRT;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = null;

        // Créer une instance de material pour éviter les conflits
        if (targetRenderer.material != null)
        {
            // Force une nouvelle instance du material
            Material matInstance = new Material(targetRenderer.material);
            matInstance.name = $"WhiteboardMat_{id}_Presentation";
            matInstance.mainTexture = _screenShareRT;
            targetRenderer.material = matInstance;
            Debug.Log($"[Whiteboard] Material set: {matInstance.name}, mainTex={matInstance.mainTexture?.name}");
        }
        else
        {
            Debug.LogError($"[Whiteboard] targetRenderer.material is null!");
        }

        _isPresentationMode = true;
        _presenterName = presenterName;

        Debug.Log($"[Whiteboard] Presentation mode ACTIVATED on {id}, RT size={_screenShareRT.width}x{_screenShareRT.height}, renderer={targetRenderer?.name}");

        OnPresentationModeChanged?.Invoke(this, true);
    }

    /// <summary>
    /// Sort du mode présentation - restaure le fond blanc
    /// </summary>
    public void ExitPresentationMode()
    {
        if (!_isPresentationMode)
            return;

        Debug.Log($"[Whiteboard] ExitPresentationMode on {id}");

        // Restaurer le fond sauvegardé
        if (_savedTexture != null && texture != null)
        {
            texture.SetPixels(_savedTexture.GetPixels());
            texture.Apply();
            Destroy(_savedTexture);
            _savedTexture = null;
        }

        // Détruire le material de présentation et restaurer le material original avec la texture
        if (targetRenderer != null)
        {
            // Créer un nouveau material avec la texture de fond
            Material originalMat = targetRenderer.material;
            if (originalMat != null && originalMat.name.Contains("_Presentation"))
            {
                // Récupérer le shader avant de détruire
                Shader shader = originalMat.shader;
                Destroy(originalMat);

                // Créer un nouveau material propre
                Material newMat = new Material(shader);
                newMat.name = $"WhiteboardMat_{id}";
                newMat.mainTexture = texture;
                targetRenderer.material = newMat;
            }
            else
            {
                targetRenderer.material.mainTexture = texture;
            }
        }

        // Nettoyer la RenderTexture
        if (_screenShareRT != null)
        {
            _screenShareRT.Release();
            Destroy(_screenShareRT);
            _screenShareRT = null;
        }

        _isPresentationMode = false;
        _presenterName = null;

        Debug.Log($"[Whiteboard] Presentation mode DEACTIVATED on {id}");

        OnPresentationModeChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Met à jour l'affichage du screen share (appelé par ScreenShareManager)
    /// </summary>
    public void UpdateScreenShare(Texture2D frameTexture)
    {
        if (!_isPresentationMode)
        {
            Debug.LogWarning($"[Whiteboard:{id}] UpdateScreenShare appelé mais pas en mode présentation");
            return;
        }

        if (frameTexture == null)
        {
            Debug.LogWarning($"[Whiteboard:{id}] frameTexture est null");
            return;
        }

        if (_screenShareRT == null)
        {
            Debug.LogWarning($"[Whiteboard:{id}] _screenShareRT est null, recréation...");
            _screenShareRT = new RenderTexture((int)textureSize.x, (int)textureSize.y, 0, RenderTextureFormat.ARGB32);
            _screenShareRT.Create();
        }

        // Vérifier que le renderer et le material sont valides
        if (targetRenderer == null)
        {
            Debug.LogError($"[Whiteboard:{id}] targetRenderer est null!");
            return;
        }

        if (targetRenderer.material == null)
        {
            Debug.LogError($"[Whiteboard:{id}] material est null!");
            return;
        }

        // Vérifier que le material pointe vers notre RenderTexture
        if (targetRenderer.material.mainTexture != _screenShareRT)
        {
            Debug.Log($"[Whiteboard:{id}] Réassignation du RenderTexture au material");
            targetRenderer.material.mainTexture = _screenShareRT;
        }

        try
        {
            // Créer une texture temporaire avec la bonne orientation et proportions
            Texture2D displayTexture = CreateDisplayTexture(frameTexture);

            if (displayTexture == null)
            {
                Debug.LogError($"[Whiteboard:{id}] CreateDisplayTexture returned null!");
                return;
            }

            // Copier simplement vers la RenderTexture
            Graphics.Blit(displayTexture, _screenShareRT);

            Debug.Log($"[Whiteboard:{id}] Frame affichée: src={frameTexture.width}x{frameTexture.height}");

            // Nettoyer
            Destroy(displayTexture);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Whiteboard:{id}] Exception in UpdateScreenShare: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// Crée une texture avec fond noir, image centrée avec bonnes proportions,
    /// et orientation corrigée (flip horizontal si nécessaire)
    /// </summary>
    private Texture2D CreateDisplayTexture(Texture2D source)
    {
        int dstWidth = (int)textureSize.x;
        int dstHeight = (int)textureSize.y;

        // Créer texture de destination avec fond noir
        Texture2D result = new Texture2D(dstWidth, dstHeight, TextureFormat.RGB24, false);
        Color[] pixels = new Color[dstWidth * dstHeight];

        // Remplir de noir
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;

        // Calculer l'aspect ratio réel du whiteboard (basé sur le mesh/transform)
        float whiteboardAspect = 1f;
        if (targetRenderer != null)
        {
            Vector3 scale = targetRenderer.transform.lossyScale;
            Debug.Log($"[Whiteboard:{id}] Transform scale: {scale}");

            // Pour un Quad vertical, la largeur est X et la hauteur est Y
            // Pour un Plane horizontal tourné, ça peut être X/Z
            float xyRatio = Mathf.Abs(scale.x) / Mathf.Abs(scale.y);
            float xzRatio = Mathf.Abs(scale.x) / Mathf.Abs(scale.z);

            Debug.Log($"[Whiteboard:{id}] X/Y ratio: {xyRatio:F2}, X/Z ratio: {xzRatio:F2}");

            // Utiliser le ratio qui semble le plus raisonnable (entre 0.3 et 3)
            if (xyRatio >= 0.3f && xyRatio <= 3f)
            {
                whiteboardAspect = xyRatio;
            }
            else if (xzRatio >= 0.3f && xzRatio <= 3f)
            {
                whiteboardAspect = xzRatio;
            }

            Debug.Log($"[Whiteboard:{id}] Using whiteboard aspect: {whiteboardAspect:F2}");
        }

        // Calculer la zone de destination en préservant l'aspect ratio
        // On doit PRÉ-COMPENSER pour l'étirement du mesh whiteboard
        float srcAspect = (float)source.width / source.height;

        // L'aspect ratio de l'image DANS LA TEXTURE doit être ajusté
        // pour compenser l'étirement du whiteboard
        // Si whiteboard est 2.28:1 et texture est 1:1, le contenu sera étiré 2.28x horizontalement
        // Donc on doit dessiner l'image 2.28x plus étroite dans la texture
        float compensatedSrcAspect = srcAspect / whiteboardAspect;

        Debug.Log($"[Whiteboard:{id}] srcAspect={srcAspect:F2}, compensated={compensatedSrcAspect:F2}");

        int targetWidth, targetHeight, offsetX, offsetY;

        // Calculer par rapport à la texture carrée (1:1)
        float textureAspect = (float)dstWidth / dstHeight; // = 1.0 pour texture carrée

        if (compensatedSrcAspect > textureAspect)
        {
            // Image compensée plus large - utiliser toute la largeur
            targetWidth = dstWidth;
            targetHeight = Mathf.RoundToInt(dstWidth / compensatedSrcAspect);
            offsetX = 0;
            offsetY = (dstHeight - targetHeight) / 2;
        }
        else
        {
            // Image compensée plus haute - utiliser toute la hauteur
            targetHeight = dstHeight;
            targetWidth = Mathf.RoundToInt(dstHeight * compensatedSrcAspect);
            offsetX = (dstWidth - targetWidth) / 2;
            offsetY = 0;
        }

        // Protection contre les valeurs invalides
        targetWidth = Mathf.Max(targetWidth, 2);
        targetHeight = Mathf.Max(targetHeight, 2);

        Debug.Log($"[Whiteboard:{id}] target={targetWidth}x{targetHeight}, offset=({offsetX},{offsetY})");

        // Copier les pixels avec redimensionnement bilinéaire et rotation 180°
        Color[] srcPixels = source.GetPixels();
        int srcWidth = source.width;
        int srcHeight = source.height;

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                // Position normalisée dans la source (0-1)
                float u = (float)x / Mathf.Max(targetWidth - 1, 1);
                float v = (float)y / Mathf.Max(targetHeight - 1, 1);

                // Rotation 180° = flip horizontal + flip vertical
                u = 1f - u;
                v = 1f - v;

                // Position dans la source
                float srcX = u * (srcWidth - 1);
                float srcY = v * (srcHeight - 1);

                // Interpolation bilinéaire
                int x0 = Mathf.FloorToInt(srcX);
                int y0 = Mathf.FloorToInt(srcY);
                int x1 = Mathf.Min(x0 + 1, srcWidth - 1);
                int y1 = Mathf.Min(y0 + 1, srcHeight - 1);

                float fx = srcX - x0;
                float fy = srcY - y0;

                Color c00 = srcPixels[y0 * srcWidth + x0];
                Color c10 = srcPixels[y0 * srcWidth + x1];
                Color c01 = srcPixels[y1 * srcWidth + x0];
                Color c11 = srcPixels[y1 * srcWidth + x1];

                Color c = Color.Lerp(
                    Color.Lerp(c00, c10, fx),
                    Color.Lerp(c01, c11, fx),
                    fy
                );

                // Position dans la destination
                int dstX = offsetX + x;
                int dstY = offsetY + y;

                if (dstX >= 0 && dstX < dstWidth && dstY >= 0 && dstY < dstHeight)
                {
                    pixels[dstY * dstWidth + dstX] = c;
                }
            }
        }

        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    void CleanupResources()
    {
        Debug.Log($"[Whiteboard] CleanupResources on {id}");

        if (_screenShareRT != null)
        {
            _screenShareRT.Release();
            Destroy(_screenShareRT);
            _screenShareRT = null;
        }

        if (_savedTexture != null)
        {
            Destroy(_savedTexture);
            _savedTexture = null;
        }

        // Nettoyer le material créé dynamiquement
        if (targetRenderer != null && targetRenderer.material != null)
        {
            if (targetRenderer.material.name.Contains("_Presentation") || targetRenderer.material.name.Contains($"WhiteboardMat_{id}"))
            {
                Destroy(targetRenderer.material);
            }
        }

        _isPresentationMode = false;
    }

    #endregion

    #region Legacy API (pour compatibilité)

    /// <summary>
    /// [LEGACY] Retourne la texture de fond.
    /// Pour le dessin, utilisez WhiteboardDrawingSurface.drawingTexture
    /// </summary>
    public Texture2D ActiveTexture => texture;

    /// <summary>
    /// [LEGACY] Ne fait rien - le dessin est sur WhiteboardDrawingSurface
    /// </summary>
    public void RefreshCompositeDisplay()
    {
        // Plus nécessaire avec la nouvelle architecture
    }

    /// <summary>
    /// [LEGACY] Redirige vers UpdateScreenShare
    /// </summary>
    public void UpdatePresentationTexture(Texture2D frameTexture)
    {
        UpdateScreenShare(frameTexture);
    }

    #endregion
}
