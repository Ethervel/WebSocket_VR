using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Permet de dessiner sur les whiteboards avec les controllers VR
/// Attach sur le Right Hand Controller (ou les deux mains)
/// </summary>
public class VRWhiteboardDrawer : MonoBehaviour
{
    [Header("VR Controller")]
    [Tooltip("Quel controller utiliser")]
    public XRNode controllerNode = XRNode.RightHand;
    
    [Tooltip("Transform du rayon (si null, utilise transform du script)")]
    public Transform rayOrigin;
    
    [Tooltip("Distance maximale de dessin")]
    public float maxDrawDistance = 3f;
    
    [Header("Input Buttons - CommonUsages")]
    [Tooltip("Mapping automatique: Trigger=Dessiner | Primary(A/X)=Gomme | Secondary(B/Y)=Couleur | Grip+Primary=Effacer")]
    public bool showInputInfo = true; 
    
    [Header("Visual Feedback")]
    [Tooltip("LineRenderer pour afficher le rayon")]
    public LineRenderer rayLine;
    
    [Tooltip("Couleur du rayon en mode dessin")]
    public Color drawRayColor = Color.blue;
    
    [Tooltip("Couleur du rayon en mode gomme")]
    public Color eraseRayColor = Color.red;
    
    [Tooltip("Épaisseur du rayon")]
    public float rayWidth = 0.005f;
    
    [Tooltip("Particules au point de contact (optionnel)")]
    public ParticleSystem drawParticles;
    
    [Header("Haptic Feedback")]
    [Tooltip("Activer les vibrations")]
    public bool enableHaptics = true;
    
    [Tooltip("Intensité des vibrations")]
    [Range(0f, 1f)]
    public float hapticIntensity = 0.1f;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    // État
    private InputDevice _controller;
    private bool _isDrawing = false;
    private bool _isErasing = false;
    private WhiteboardSurface _currentWhiteboard = null;
    private Vector2 _lastUV = Vector2.zero;
    private Vector3 _lastHitPoint = Vector3.zero;
    
    // Cooldown pour les actions
    private float _lastColorChangeTime = 0f;
    private float _lastClearTime = 0f;
    private const float ActionCooldown = 0.3f;
    
    void Start()
    {
        UpdateInputDevice();
        
        if (rayOrigin == null)
        {
            rayOrigin = transform;
        }
        
        SetupRayLine();
    }
    
    void Update()
    {
        if (!_controller.isValid)
        {
            UpdateInputDevice();
        }
        
        HandleInput();
        UpdateRaycast();
        UpdateVisuals();
    }
    
    #region Initialization
    
    void UpdateInputDevice()
    {
        _controller = InputDevices.GetDeviceAtXRNode(controllerNode);
        
        if (_controller.isValid)
        {
            LogDebug($"[WhiteboardDrawer] Controller found: {_controller.name}");
        }
    }
    
    void SetupRayLine()
    {
        if (rayLine == null)
        {
            // Créer un LineRenderer si absent
            GameObject rayObj = new GameObject("WhiteboardRay");
            rayObj.transform.SetParent(rayOrigin);
            rayObj.transform.localPosition = Vector3.zero;
            
            rayLine = rayObj.AddComponent<LineRenderer>();
        }
        
        rayLine.startWidth = rayWidth;
        rayLine.endWidth = rayWidth;
        rayLine.positionCount = 2;
        rayLine.enabled = false;
        rayLine.material = new Material(Shader.Find("Sprites/Default"));
    }
    
    #endregion
    
    #region Input Handling
    
    void HandleInput()
    {
        if (!_controller.isValid) return;
        if (VRWhiteboardManager.Instance == null) return;
        
        // Récupérer l'état des boutons avec TryGetFeatureValue (pas obsolète)
        float triggerValue = 0f;
        bool primaryButton = false;
        bool secondaryButton = false;
        bool gripButton = false;
        
        _controller.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
        _controller.TryGetFeatureValue(CommonUsages.primaryButton, out primaryButton);
        _controller.TryGetFeatureValue(CommonUsages.secondaryButton, out secondaryButton);
        _controller.TryGetFeatureValue(CommonUsages.gripButton, out gripButton);
        
        // Convertir trigger en booléen
        bool drawPressed = triggerValue > 0.5f;
        bool eraserPressed = primaryButton;
        bool colorPressed = secondaryButton;
        bool clearPressed = gripButton;
        
        // Activer/désactiver la gomme
        if (eraserPressed && !_isErasing)
        {
            _isErasing = true;
            VRWhiteboardManager.Instance.SetEraser(true);
            TriggerHaptic(0.2f);
        }
        else if (!eraserPressed && _isErasing)
        {
            _isErasing = false;
            VRWhiteboardManager.Instance.SetEraser(false);
        }
        
        // Changer de couleur (avec cooldown)
        if (colorPressed && Time.time - _lastColorChangeTime > ActionCooldown)
        {
            VRWhiteboardManager.Instance.NextColor();
            _lastColorChangeTime = Time.time;
            TriggerHaptic(0.15f);
        }
        
        // Effacer le whiteboard (Grip + Primary avec cooldown)
        if (clearPressed && colorPressed && _currentWhiteboard != null)
        {
            if (Time.time - _lastClearTime > ActionCooldown)
            {
                VRWhiteboardManager.Instance.ClearWhiteboard(_currentWhiteboard.whiteboardId);
                _lastClearTime = Time.time;
                TriggerHaptic(0.5f);
            }
        }
        
        // Gérer le dessin
        if (drawPressed || eraserPressed)
        {
            if (!_isDrawing && _currentWhiteboard != null)
            {
                BeginDraw();
            }
            else if (_isDrawing && _currentWhiteboard != null)
            {
                ContinueDraw();
            }
        }
        else
        {
            if (_isDrawing)
            {
                EndDraw();
            }
        }
    }
    
    #endregion
    
    #region Raycasting
    
    void UpdateRaycast()
    {
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, maxDrawDistance))
        {
            var whiteboard = hit.collider.GetComponent<WhiteboardSurface>();
            
            if (whiteboard != null)
            {
                _currentWhiteboard = whiteboard;
                _lastHitPoint = hit.point;
                
                // Convertir en UV
                if (whiteboard.HitToUV(hit, out Vector2 uv))
                {
                    _lastUV = uv;
                }
                
                return;
            }
        }
        
        // Pas de whiteboard trouvé
        _currentWhiteboard = null;
    }
    
    #endregion
    
    #region Drawing Actions
    
    void BeginDraw()
    {
        _isDrawing = true;
        
        if (VRWhiteboardManager.Instance != null && _currentWhiteboard != null)
        {
            VRWhiteboardManager.Instance.BeginStroke(_currentWhiteboard.whiteboardId, _lastUV);
            TriggerHaptic(0.05f);
            
            LogDebug($"[WhiteboardDrawer] Begin draw on {_currentWhiteboard.whiteboardId}");
        }
    }
    
    void ContinueDraw()
    {
        if (VRWhiteboardManager.Instance != null && _currentWhiteboard != null)
        {
            VRWhiteboardManager.Instance.AddStrokePoint(_currentWhiteboard.whiteboardId, _lastUV);
            
            // Haptic léger continu
            if (enableHaptics && Time.frameCount % 5 == 0)
            {
                TriggerHaptic(hapticIntensity * 0.5f);
            }
        }
    }
    
    void EndDraw()
    {
        _isDrawing = false;
        
        if (VRWhiteboardManager.Instance != null)
        {
            VRWhiteboardManager.Instance.EndStroke();
            TriggerHaptic(0.05f);
            
            LogDebug("[WhiteboardDrawer] End draw");
        }
    }
    
    #endregion
    
    #region Visual Feedback
    
    void UpdateVisuals()
    {
        if (rayLine == null) return;
        
        // Afficher le rayon seulement quand on pointe vers un whiteboard
        if (_currentWhiteboard != null)
        {
            rayLine.enabled = true;
            
            // Positions
            rayLine.SetPosition(0, rayOrigin.position);
            rayLine.SetPosition(1, _lastHitPoint);
            
            // Couleur selon le mode
            Color rayColor = _isErasing ? eraseRayColor : drawRayColor;
            
            // Si on dessine, rendre le rayon plus brillant
            if (_isDrawing)
            {
                rayColor = Color.Lerp(rayColor, Color.white, 0.5f);
            }
            
            rayLine.startColor = rayColor;
            rayLine.endColor = rayColor;
            
            // Particules au point de contact
            if (drawParticles != null)
            {
                drawParticles.transform.position = _lastHitPoint;
                
                if (_isDrawing && !drawParticles.isPlaying)
                {
                    drawParticles.Play();
                }
                else if (!_isDrawing && drawParticles.isPlaying)
                {
                    drawParticles.Stop();
                }
            }
        }
        else
        {
            rayLine.enabled = false;
            
            if (drawParticles != null && drawParticles.isPlaying)
            {
                drawParticles.Stop();
            }
        }
    }
    
    #endregion
    
    #region Haptic Feedback
    
    void TriggerHaptic(float intensity)
    {
        if (!enableHaptics) return;
        if (!_controller.isValid) return;
        
        HapticCapabilities capabilities;
        if (_controller.TryGetHapticCapabilities(out capabilities))
        {
            if (capabilities.supportsImpulse)
            {
                _controller.SendHapticImpulse(0, intensity, 0.1f);
            }
        }
    }
    
    #endregion
    
    #region Debug
    
    void LogDebug(string message)
    {
        if (showDebugInfo)
            Debug.Log(message);
    }
    
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 150));
        GUILayout.BeginVertical("box");
        
        GUILayout.Label("=== Whiteboard Drawer ===");
        GUILayout.Label($"Controller: {(controllerNode == XRNode.RightHand ? "Right" : "Left")}");
        GUILayout.Label($"Valid: {_controller.isValid}");
        GUILayout.Label($"Drawing: {_isDrawing}");
        GUILayout.Label($"Erasing: {_isErasing}");
        GUILayout.Label($"Whiteboard: {(_currentWhiteboard != null ? _currentWhiteboard.whiteboardId : "None")}");
        GUILayout.Label($"UV: {_lastUV}");
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
    
    #endregion
}