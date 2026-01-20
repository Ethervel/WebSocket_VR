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

    // Zoom/Pan reçus du réseau (pour les récepteurs)
    private float _receivedZoomLevel = 1f;
    private Vector2 _receivedPanOffset = Vector2.zero;
    private Texture2D _lastReceivedTexture; // Cache pour re-render après zoom/pan

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

        // Réinitialiser le zoom/pan reçu
        _receivedZoomLevel = 1f;
        _receivedPanOffset = Vector2.zero;

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

        // Nettoyer la texture cachée
        if (_lastReceivedTexture != null)
        {
            Destroy(_lastReceivedTexture);
            _lastReceivedTexture = null;
        }

        // Réinitialiser le zoom/pan reçu
        _receivedZoomLevel = 1f;
        _receivedPanOffset = Vector2.zero;

        _isPresentationMode = false;
        _presenterName = null;

        Debug.Log($"[Whiteboard] Presentation mode DEACTIVATED on {id}");

        OnPresentationModeChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Met à jour l'affichage du screen share (appelé par ScreenShareManager)
    /// </summary>
    /// <param name="frameTexture">La texture à afficher</param>
    /// <param name="rotate180">True pour rotation 180° (PDF), False pour screen share</param>
    public void UpdateScreenShare(Texture2D frameTexture, bool rotate180 = false)
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
            if (rotate180)
            {
                // Pour PDF/File presentation: traitement complet avec rotation et aspect ratio
                // Cacher la texture pour pouvoir re-render si zoom/pan change
                if (_lastReceivedTexture == null || _lastReceivedTexture.width != frameTexture.width || _lastReceivedTexture.height != frameTexture.height)
                {
                    if (_lastReceivedTexture != null)
                        Destroy(_lastReceivedTexture);
                    _lastReceivedTexture = new Texture2D(frameTexture.width, frameTexture.height, TextureFormat.RGB24, false);
                }
                // Utiliser GetPixels/SetPixels au lieu de CopyTexture pour éviter les problèmes de mipmap
                _lastReceivedTexture.SetPixels(frameTexture.GetPixels());
                _lastReceivedTexture.Apply();

                Texture2D displayTexture = CreateDisplayTexture(frameTexture, rotate180);

                if (displayTexture == null)
                {
                    Debug.LogError($"[Whiteboard:{id}] CreateDisplayTexture returned null!");
                    return;
                }

                Graphics.Blit(displayTexture, _screenShareRT);
                Destroy(displayTexture);
            }
            else
            {
                // Pour screen share: utiliser Graphics.Blit direct (rapide)
                // Effacer avec du noir d'abord
                RenderTexture.active = _screenShareRT;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = null;

                // Blit simple - la texture sera étirée pour remplir le RT
                Graphics.Blit(frameTexture, _screenShareRT);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Whiteboard:{id}] Exception in UpdateScreenShare: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// Crée une texture avec fond noir, image centrée avec bonnes proportions,
    /// orientation corrigée si nécessaire, et zoom/pan appliqués
    /// </summary>
    /// <param name="source">Texture source</param>
    /// <param name="rotate180">Appliquer une rotation 180°</param>
    private Texture2D CreateDisplayTexture(Texture2D source, bool rotate180)
    {
        int dstWidth = (int)textureSize.x;
        int dstHeight = (int)textureSize.y;

        // Créer texture de destination avec fond noir
        Texture2D result = new Texture2D(dstWidth, dstHeight, TextureFormat.RGB24, false);
        Color[] pixels = new Color[dstWidth * dstHeight];

        // Remplir de noir
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;

        // Récupérer zoom et pan - utiliser les valeurs locales du présentateur ou celles reçues du réseau
        float zoomLevel = 1f;
        Vector2 panOffset = Vector2.zero;
        if (FilePresentationManager.Instance != null && FilePresentationManager.Instance.IsPresenting)
        {
            // On est le présentateur - utiliser nos propres valeurs
            zoomLevel = FilePresentationManager.Instance.ZoomLevel;
            panOffset = FilePresentationManager.Instance.PanOffset;
        }
        else
        {
            // On est un récepteur - utiliser les valeurs reçues du réseau
            zoomLevel = _receivedZoomLevel;
            panOffset = _receivedPanOffset;
        }

        // Calculer l'aspect ratio réel du whiteboard (basé sur le mesh/transform)
        float whiteboardAspect = 1f;
        if (targetRenderer != null)
        {
            Vector3 scale = targetRenderer.transform.lossyScale;

            // Pour un Quad vertical, la largeur est X et la hauteur est Y
            // Pour un Plane horizontal tourné, ça peut être X/Z
            float xyRatio = Mathf.Abs(scale.x) / Mathf.Abs(scale.y);
            float xzRatio = Mathf.Abs(scale.x) / Mathf.Abs(scale.z);

            // Utiliser le ratio qui semble le plus raisonnable (entre 0.3 et 3)
            if (xyRatio >= 0.3f && xyRatio <= 3f)
            {
                whiteboardAspect = xyRatio;
            }
            else if (xzRatio >= 0.3f && xzRatio <= 3f)
            {
                whiteboardAspect = xzRatio;
            }
        }

        // Calculer la zone de destination BASE (à zoom 1x) en préservant l'aspect ratio
        float srcAspect = (float)source.width / source.height;
        float compensatedSrcAspect = srcAspect / whiteboardAspect;

        int baseTargetWidth, baseTargetHeight;
        float textureAspect = (float)dstWidth / dstHeight;

        if (compensatedSrcAspect > textureAspect)
        {
            baseTargetWidth = dstWidth;
            baseTargetHeight = Mathf.RoundToInt(dstWidth / compensatedSrcAspect);
        }
        else
        {
            baseTargetHeight = dstHeight;
            baseTargetWidth = Mathf.RoundToInt(dstHeight * compensatedSrcAspect);
        }

        // Appliquer le zoom aux dimensions - l'image devient plus grande
        int zoomedWidth = Mathf.RoundToInt(baseTargetWidth * zoomLevel);
        int zoomedHeight = Mathf.RoundToInt(baseTargetHeight * zoomLevel);

        // Protection contre les valeurs invalides
        zoomedWidth = Mathf.Max(zoomedWidth, 2);
        zoomedHeight = Mathf.Max(zoomedHeight, 2);

        // Calculer l'offset pour centrer, puis appliquer le pan
        // Le pan est en coordonnées normalisées (-0.5 à 0.5 selon le zoom)
        int offsetX = (dstWidth - zoomedWidth) / 2 - Mathf.RoundToInt(panOffset.x * zoomedWidth);
        int offsetY = (dstHeight - zoomedHeight) / 2 - Mathf.RoundToInt(panOffset.y * zoomedHeight);

        // Copier les pixels avec redimensionnement bilinéaire et rotation 180°
        Color[] srcPixels = source.GetPixels();
        int srcWidth = source.width;
        int srcHeight = source.height;

        for (int y = 0; y < zoomedHeight; y++)
        {
            for (int x = 0; x < zoomedWidth; x++)
            {
                // Position dans la destination
                int dstX = offsetX + x;
                int dstY = offsetY + y;

                // Vérifier si on est dans les limites de la destination
                if (dstX < 0 || dstX >= dstWidth || dstY < 0 || dstY >= dstHeight)
                    continue;

                // Position normalisée dans la source (0-1)
                float u = (float)x / Mathf.Max(zoomedWidth - 1, 1);
                float v = (float)y / Mathf.Max(zoomedHeight - 1, 1);

                // Rotation 180° si demandé (pour PDF)
                if (rotate180)
                {
                    u = 1f - u;
                    v = 1f - v;
                }

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

                pixels[dstY * dstWidth + dstX] = c;
            }
        }

        result.SetPixels(pixels);
        result.Apply();
        return result;
    }

    /// <summary>
    /// Met à jour le zoom et le pan reçus du réseau et rafraîchit l'affichage.
    /// Appelé par FilePresentationManager quand on reçoit un message file-present-zoom-pan.
    /// </summary>
    public void SetPresentationZoomPan(float zoomLevel, Vector2 panOffset)
    {
        if (!_isPresentationMode)
        {
            Debug.LogWarning($"[Whiteboard:{id}] SetPresentationZoomPan appelé mais pas en mode présentation");
            return;
        }

        _receivedZoomLevel = zoomLevel;
        _receivedPanOffset = panOffset;

        Debug.Log($"[Whiteboard:{id}] Zoom/Pan mis à jour: zoom={zoomLevel}, pan={panOffset}");

        // Re-render avec le nouveau zoom/pan si on a une texture en cache
        if (_lastReceivedTexture != null)
        {
            UpdateScreenShare(_lastReceivedTexture, rotate180: true);
        }
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

        if (_lastReceivedTexture != null)
        {
            Destroy(_lastReceivedTexture);
            _lastReceivedTexture = null;
        }

        // Réinitialiser le zoom/pan
        _receivedZoomLevel = 1f;
        _receivedPanOffset = Vector2.zero;

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
    /// [LEGACY] Redirige vers UpdateScreenShare avec rotation 180° pour les PDF
    /// </summary>
    public void UpdatePresentationTexture(Texture2D frameTexture)
    {
        UpdateScreenShare(frameTexture, rotate180: true);
    }

    #endregion
}
