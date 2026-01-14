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
        if (_isPresentationMode)
            return;

        // Sauvegarder le fond actuel (au cas où on voudrait le restaurer)
        if (texture != null)
        {
            _savedTexture = new Texture2D((int)textureSize.x, (int)textureSize.y);
            _savedTexture.SetPixels(texture.GetPixels());
            _savedTexture.Apply();
        }

        // Créer RenderTexture pour le screen share
        _screenShareRT = new RenderTexture((int)textureSize.x, (int)textureSize.y, 0, RenderTextureFormat.ARGB32);
        _screenShareRT.Create();

        // Remplir de noir en attendant la première frame
        RenderTexture.active = _screenShareRT;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = null;

        // Afficher la RenderTexture
        targetRenderer.material.mainTexture = _screenShareRT;

        _isPresentationMode = true;
        _presenterName = presenterName;

        OnPresentationModeChanged?.Invoke(this, true);
    }

    /// <summary>
    /// Sort du mode présentation - restaure le fond blanc
    /// </summary>
    public void ExitPresentationMode()
    {
        if (!_isPresentationMode)
            return;

        // Restaurer le fond sauvegardé
        if (_savedTexture != null && texture != null)
        {
            texture.SetPixels(_savedTexture.GetPixels());
            texture.Apply();
            Destroy(_savedTexture);
            _savedTexture = null;
        }

        // Remettre la texture de fond
        targetRenderer.material.mainTexture = texture;

        // Nettoyer la RenderTexture
        if (_screenShareRT != null)
        {
            _screenShareRT.Release();
            Destroy(_screenShareRT);
            _screenShareRT = null;
        }

        _isPresentationMode = false;
        _presenterName = null;

        OnPresentationModeChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Met à jour l'affichage du screen share (appelé par ScreenShareManager)
    /// </summary>
    public void UpdateScreenShare(Texture2D frameTexture)
    {
        if (!_isPresentationMode)
            return;

        if (frameTexture == null || _screenShareRT == null)
            return;

        // Blit la frame sur la RenderTexture (redimensionne automatiquement)
        Graphics.Blit(frameTexture, _screenShareRT);
    }

    void CleanupResources()
    {
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
