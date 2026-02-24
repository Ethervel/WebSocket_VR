using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// Adapte un Canvas pour fonctionner en mode VR et Desktop.
/// Screen Space - Overlay ne fonctionne pas en VR, ce script:
/// - VR: Convertit en World Space et positionne devant la camera
/// - Desktop: Garde Screen Space - Overlay
/// </summary>
[RequireComponent(typeof(Canvas))]
public class VRCanvasAdapter : MonoBehaviour
{
    [Header("=== VR Settings ===")]
    [Tooltip("Distance du Canvas devant la camera VR (metres)")]
    public float vrDistance = 2f;

    [Tooltip("Taille du Canvas en World Space (largeur en metres)")]
    public float vrCanvasWidth = 2f;

    [Tooltip("Suivre la camera VR chaque frame")]
    public bool followVRCamera = true;

    [Tooltip("Vitesse de suivi (lerp)")]
    public float followSmoothness = 5f;

    [Header("=== References ===")]
    [Tooltip("Camera a suivre (auto-detectee si null)")]
    public Camera targetCamera;

    [Header("=== Debug ===")]
    [SerializeField] private bool _isVRMode = false;
    public bool IsVRMode => _isVRMode;

    private Canvas _canvas;
    private CanvasScaler _canvasScaler;
    private RenderMode _originalRenderMode;
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _canvasScaler = GetComponent<CanvasScaler>();

        // Sauvegarder le mode original
        _originalRenderMode = _canvas.renderMode;

        // Detecter le mode VR
        CheckVRMode();
    }

    void Start()
    {
        // Re-verifier apres l'initialisation complete
        CheckVRMode();
    }

    void OnEnable()
    {
        // Re-verifier quand le Canvas est active
        CheckVRMode();
    }

    /// <summary>
    /// Detecte si le mode VR est actif et adapte le Canvas.
    /// </summary>
    public void CheckVRMode()
    {
        _isVRMode = UnityEngine.XR.XRSettings.isDeviceActive;

        if (_isVRMode)
        {
            ConvertToVRMode();
        }
        else
        {
            ConvertToDesktopMode();
        }
    }

    /// <summary>
    /// Convertit le Canvas en mode World Space pour VR.
    /// </summary>
    private void ConvertToVRMode()
    {
        if (_canvas == null) return;

        Debug.Log($"[VRCanvasAdapter] Converting '{gameObject.name}' to VR World Space mode");

        // Trouver la camera VR
        if (targetCamera == null)
        {
            targetCamera = FindVRCamera();
        }

        if (targetCamera == null)
        {
            Debug.LogWarning($"[VRCanvasAdapter] No VR camera found for '{gameObject.name}'");
            return;
        }

        // Convertir en World Space
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = targetCamera;

        // Configurer la taille du Canvas
        RectTransform rt = _canvas.GetComponent<RectTransform>();
        if (rt != null)
        {
            // Calculer la hauteur basee sur le ratio original
            float aspectRatio = rt.rect.width / rt.rect.height;
            float canvasHeight = vrCanvasWidth / aspectRatio;

            // Definir la taille en pixels (pour le layout)
            rt.sizeDelta = new Vector2(1920, 1080);

            // Scale pour obtenir la taille physique voulue
            float scale = vrCanvasWidth / rt.sizeDelta.x;
            rt.localScale = new Vector3(scale, scale, scale);
        }

        // Configurer le CanvasScaler pour World Space
        if (_canvasScaler != null)
        {
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        }

        // Positionner immediatement devant la camera
        UpdateVRPosition(true);
    }

    /// <summary>
    /// Restaure le Canvas en mode Desktop (Screen Space - Overlay).
    /// </summary>
    private void ConvertToDesktopMode()
    {
        if (_canvas == null) return;

        Debug.Log($"[VRCanvasAdapter] Converting '{gameObject.name}' to Desktop mode");

        // Restaurer le mode original
        _canvas.renderMode = _originalRenderMode;
        _canvas.worldCamera = null;

        // Restaurer le CanvasScaler
        if (_canvasScaler != null)
        {
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        }

        // Restaurer la position/scale par defaut
        RectTransform rt = _canvas.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
        }
    }

    void LateUpdate()
    {
        if (_isVRMode && followVRCamera && targetCamera != null && gameObject.activeInHierarchy)
        {
            UpdateVRPosition(false);
        }
    }

    /// <summary>
    /// Met a jour la position du Canvas devant la camera VR.
    /// </summary>
    private void UpdateVRPosition(bool immediate)
    {
        if (targetCamera == null) return;

        // Calculer la position cible devant la camera
        _targetPosition = targetCamera.transform.position + targetCamera.transform.forward * vrDistance;
        _targetRotation = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);

        if (immediate)
        {
            transform.position = _targetPosition;
            transform.rotation = _targetRotation;
        }
        else
        {
            // Smooth follow
            transform.position = Vector3.Lerp(transform.position, _targetPosition, Time.deltaTime * followSmoothness);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, Time.deltaTime * followSmoothness);
        }
    }

    /// <summary>
    /// Trouve la camera VR principale.
    /// </summary>
    private Camera FindVRCamera()
    {
        // 1. Chercher dans XR Origin (Unity XR Interaction Toolkit)
        var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            var xrCamera = xrOrigin.Camera;
            if (xrCamera != null)
            {
                Debug.Log($"[VRCanvasAdapter] Found XR Origin camera: {xrCamera.name}");
                return xrCamera;
            }
        }

        // 2. Chercher par tag "MainCamera"
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Debug.Log($"[VRCanvasAdapter] Found Camera.main: {mainCam.name}");
            return mainCam;
        }

        // 3. Chercher dans les cameras actives (preferer VR/stereo)
        Camera bestCandidate = null;
        foreach (var cam in Camera.allCameras)
        {
            // Ignorer les cameras de preview/spectateur
            if (cam.gameObject.name.Contains("Spectator") || cam.gameObject.name.Contains("Preview"))
                continue;

            // Preferer les cameras stereo (VR)
            if (cam.stereoTargetEye != StereoTargetEyeMask.None)
            {
                Debug.Log($"[VRCanvasAdapter] Found stereo camera: {cam.name}");
                return cam;
            }

            // Garder la premiere camera valide comme fallback
            if (bestCandidate == null)
            {
                bestCandidate = cam;
            }
        }

        if (bestCandidate != null)
        {
            Debug.Log($"[VRCanvasAdapter] Using fallback camera: {bestCandidate.name}");
            return bestCandidate;
        }

        Debug.LogWarning("[VRCanvasAdapter] No camera found!");
        return null;
    }

    /// <summary>
    /// Force le recalcul du mode VR (utile apres un changement de scene).
    /// </summary>
    public void Refresh()
    {
        targetCamera = null; // Forcer la re-detection
        CheckVRMode();
    }
}
