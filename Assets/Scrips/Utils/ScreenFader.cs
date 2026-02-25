using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Effet de fade écran pour les transitions.
/// Utilise un Canvas UI pour compatibilité VR et Desktop.
/// En VR, crée une sphère inversée pour couvrir tout le FOV.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _fadeSpeed = 2f;
    [SerializeField] private Color _fadeColor = Color.black;

    [Header("UI Mode (Desktop)")]
    [SerializeField] private Image _fadeImage;
    [SerializeField] private Canvas _fadeCanvas;

    [Header("VR Mode")]
    [SerializeField] private bool _useVRSphere = true;
    [SerializeField] private float _sphereRadius = 0.5f;

    // État
    private float _intensity = 0f;
    private bool _isVRMode = false;

    // VR sphere components
    private GameObject _vrSphere;
    private MeshRenderer _vrSphereRenderer;
    private Material _vrSphereMaterial;

    public float Intensity => _intensity;
    public bool IsFading { get; private set; }

    void Awake()
    {
        _isVRMode = UnityEngine.XR.XRSettings.isDeviceActive;

        // Auto-create UI fade si pas assigné
        if (_fadeImage == null && _fadeCanvas == null)
        {
            CreateFadeUI();
        }

        // Commencer transparent
        SetIntensity(0f);
    }

    void Start()
    {
        // Re-check VR après initialisation complète
        StartCoroutine(CheckVRDelayed());
    }

    IEnumerator CheckVRDelayed()
    {
        yield return new WaitForSeconds(0.5f);

        bool wasVR = _isVRMode;
        _isVRMode = UnityEngine.XR.XRSettings.isDeviceActive;

        if (_isVRMode && !wasVR && _useVRSphere)
        {
            CreateVRSphere();
        }
    }

    void OnDestroy()
    {
        CleanupVRSphere();
    }

    /// <summary>
    /// Crée l'UI de fade (Canvas + Image).
    /// </summary>
    void CreateFadeUI()
    {
        // Créer Canvas
        GameObject canvasObj = new GameObject("FadeCanvas");
        canvasObj.transform.SetParent(transform);
        _fadeCanvas = canvasObj.AddComponent<Canvas>();
        _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _fadeCanvas.sortingOrder = 9999; // Au dessus de tout

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Créer Image
        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform);
        _fadeImage = imageObj.AddComponent<Image>();
        _fadeImage.color = new Color(_fadeColor.r, _fadeColor.g, _fadeColor.b, 0f);
        _fadeImage.raycastTarget = false;

        // Stretch to fill
        RectTransform rt = _fadeImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Crée une sphère inversée pour le fade VR.
    /// </summary>
    void CreateVRSphere()
    {
        if (_vrSphere != null) return;

        Camera vrCamera = FindVRCamera();
        if (vrCamera == null) return;

        _vrSphere = new GameObject("VR_FadeSphere");
        _vrSphere.transform.SetParent(vrCamera.transform);
        _vrSphere.transform.localPosition = Vector3.zero;
        _vrSphere.transform.localScale = Vector3.one * _sphereRadius * 2f;

        var meshFilter = _vrSphere.AddComponent<MeshFilter>();
        _vrSphereRenderer = _vrSphere.AddComponent<MeshRenderer>();

        // Créer mesh inversé
        meshFilter.mesh = CreateInvertedSphereMesh();

        // Material
        _vrSphereMaterial = new Material(Shader.Find("Unlit/Color"));
        _vrSphereMaterial.color = new Color(_fadeColor.r, _fadeColor.g, _fadeColor.b, 0f);
        _vrSphereRenderer.material = _vrSphereMaterial;
        _vrSphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _vrSphereRenderer.receiveShadows = false;

        // Désactiver par défaut (alpha = 0)
        _vrSphere.SetActive(false);

        Debug.Log("[ScreenFader] VR sphere created");
    }

    Mesh CreateInvertedSphereMesh()
    {
        var tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var sourceMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;

        var mesh = new Mesh();
        mesh.vertices = sourceMesh.vertices;
        mesh.uv = sourceMesh.uv;

        // Inverser normales
        var normals = sourceMesh.normals;
        for (int i = 0; i < normals.Length; i++)
            normals[i] = -normals[i];
        mesh.normals = normals;

        // Inverser triangles
        var triangles = sourceMesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int temp = triangles[i + 1];
            triangles[i + 1] = triangles[i + 2];
            triangles[i + 2] = temp;
        }
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        DestroyImmediate(tempSphere);
        return mesh;
    }

    Camera FindVRCamera()
    {
        var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null && xrOrigin.Camera != null)
            return xrOrigin.Camera;

        if (Camera.main != null)
            return Camera.main;

        return null;
    }

    void CleanupVRSphere()
    {
        if (_vrSphere != null)
            Destroy(_vrSphere);
        if (_vrSphereMaterial != null)
            Destroy(_vrSphereMaterial);
    }

    /// <summary>
    /// Définit l'intensité du fade (0 = transparent, 1 = opaque).
    /// </summary>
    void SetIntensity(float intensity)
    {
        _intensity = Mathf.Clamp01(intensity);
        Color color = new Color(_fadeColor.r, _fadeColor.g, _fadeColor.b, _intensity);

        // UI Image
        if (_fadeImage != null)
        {
            _fadeImage.color = color;
            _fadeImage.gameObject.SetActive(_intensity > 0.01f);
        }

        // VR Sphere
        if (_vrSphereRenderer != null && _vrSphereMaterial != null)
        {
            _vrSphereMaterial.color = new Color(_fadeColor.r, _fadeColor.g, _fadeColor.b, _intensity);
            _vrSphere.SetActive(_intensity > 0.01f);
        }
    }

    /// <summary>
    /// Fade vers le noir (écran opaque).
    /// </summary>
    public Coroutine StartFadeIn()
    {
        StopAllCoroutines();

        // S'assurer que la sphère VR existe si nécessaire
        if (_isVRMode && _useVRSphere && _vrSphere == null)
        {
            CreateVRSphere();
        }

        return StartCoroutine(FadeInCoroutine());
    }

    IEnumerator FadeInCoroutine()
    {
        IsFading = true;

        while (_intensity < 1f)
        {
            _intensity += _fadeSpeed * Time.deltaTime;
            SetIntensity(_intensity);
            yield return null;
        }

        SetIntensity(1f);
        IsFading = false;
    }

    /// <summary>
    /// Fade vers transparent (écran visible).
    /// </summary>
    public Coroutine StartFadeOut()
    {
        StopAllCoroutines();
        return StartCoroutine(FadeOutCoroutine());
    }

    IEnumerator FadeOutCoroutine()
    {
        IsFading = true;

        while (_intensity > 0f)
        {
            _intensity -= _fadeSpeed * Time.deltaTime;
            SetIntensity(_intensity);
            yield return null;
        }

        SetIntensity(0f);
        IsFading = false;
    }

    /// <summary>
    /// Fade immédiat (sans animation).
    /// </summary>
    public void SetFadeImmediate(bool fadeIn)
    {
        StopAllCoroutines();
        SetIntensity(fadeIn ? 1f : 0f);
    }
}
