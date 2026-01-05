using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Feutre VR pour dessiner sur les tableaux blancs
/// Version avec debug amélioré
/// </summary>
public class WhiteboardMarker : MonoBehaviour
{
    [Header("Configuration")]
    public Transform tip;
    public int penSize = 10;
    public Color currentColor = Color.black;
    public LayerMask whiteboardLayer;

    [Header("Network Settings")]
    public float sendRate = 0.1f; // 10 fois par seconde (plus lent pour debug)
    public bool sendEmptyBatches = false;

    [Header("Debug")]
    public bool showDebugRay = true;
    public bool showDebugLogs = true;

    private Renderer _renderer;
    private Color[] _colors;
    private float _tipHeight;
    
    // État du dessin LOCAL
    private Whiteboard _currentWhiteboard;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private RaycastHit _touch;

    // Réseau - Buffer de points à envoyer
    private float _networkTimer;
    private List<float[]> _pendingPoints = new List<float[]>();
    private string _currentWhiteboardId;
    
    // XR Interaction
    private XRGrabInteractable _grabInteractable;
    private bool _isHeld = false;

    // Stats
    private int _totalPointsSent = 0;
    private int _totalBatchesSent = 0;

    void Start()
    {
        if (tip == null)
        {
            Debug.LogError("[WhiteboardMarker] Tip non assigné!");
            enabled = false;
            return;
        }

        _renderer = tip.GetComponent<Renderer>();
        if (_renderer == null)
        {
            Debug.LogWarning("[WhiteboardMarker] Aucun Renderer sur le Tip!");
        }

        _grabInteractable = GetComponent<XRGrabInteractable>();
        
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.AddListener(_ => OnGrabbed());
            _grabInteractable.selectExited.AddListener(_ => OnReleased());
        }
        else
        {
            Debug.LogWarning("[WhiteboardMarker] Pas de XRGrabInteractable - Test sans VR");
            _isHeld = true; // Pour tester sans VR
        }

        _tipHeight = tip.localScale.y;
        ApplyColor(currentColor);

        if (showDebugLogs)
            Debug.Log($"[WhiteboardMarker] Initialisé - Tip height: {_tipHeight}");
    }

    void OnGrabbed()
    {
        _isHeld = true;
        if (showDebugLogs)
            Debug.Log("[WhiteboardMarker] ATTRAPÉ");
    }

    void OnReleased()
    {
        _isHeld = false;
        _touchedLastFrame = false;
        _currentWhiteboard = null;
        
        // Vider le buffer restant
        if (_pendingPoints.Count > 0)
        {
            SendBatchToNetwork();
        }
        
        if (showDebugLogs)
            Debug.Log("[WhiteboardMarker] RELÂCHÉ");
    }

    void Update()
    {
        // Dessiner seulement si on tient le feutre
        if (_isHeld)
        {
            Draw();
        }
        else
        {
            // Reset quand on lâche
            if (_touchedLastFrame)
            {
                _touchedLastFrame = false;
                _currentWhiteboard = null;
                
                // Envoyer les derniers points
                if (_pendingPoints.Count > 0)
                {
                    SendBatchToNetwork();
                }
            }
        }

        // Gestion réseau
        NetworkUpdate();
    }

    // ========================================
    // DESSIN LOCAL
    // ========================================

    void Draw()
    {
        // Raycast vers le bas du feutre
        bool hit = Physics.Raycast(tip.position, transform.up, out _touch, _tipHeight, whiteboardLayer);

        if (showDebugRay)
            Debug.DrawRay(tip.position, transform.up * _tipHeight, hit ? Color.green : Color.red);

        if (!hit)
        {
            if (_touchedLastFrame && showDebugLogs)
            {
                Debug.Log("[WhiteboardMarker] Perdu le contact avec le tableau");
            }
            
            _currentWhiteboard = null;
            _touchedLastFrame = false;
            return;
        }

        // Vérifier qu'on a bien touché un Whiteboard
        Whiteboard wb = _touch.transform.GetComponent<Whiteboard>();
        if (wb == null)
        {
            if (showDebugLogs && Time.frameCount % 60 == 0) // Tous les 60 frames
            {
                Debug.LogWarning($"[WhiteboardMarker] Touché {_touch.transform.name} mais pas de Whiteboard!");
            }
            _touchedLastFrame = false;
            return;
        }

        // Premier contact avec ce tableau
        if (_currentWhiteboard != wb)
        {
            _currentWhiteboard = wb;
            _currentWhiteboardId = wb.id;
            
            if (showDebugLogs)
                Debug.Log($"[WhiteboardMarker] Contact avec tableau: {_currentWhiteboardId}");
        }

        // Coordonnées UV (0-1)
        Vector2 uv = _touch.textureCoord;
        
        // Calcul pixels pour dessin LOCAL avec CLAMPING
        int maxX = (int)wb.textureSize.x - penSize;
        int maxY = (int)wb.textureSize.y - penSize;
        
        int x = Mathf.Clamp((int)(uv.x * wb.textureSize.x - penSize / 2), 0, maxX);
        int y = Mathf.Clamp((int)(uv.y * wb.textureSize.y - penSize / 2), 0, maxY);

        // 1. DESSIN LOCAL IMMÉDIAT
        if (_touchedLastFrame)
        {
            // Interpolation locale pour un trait continu
            Vector2 start = _lastTouchPos;
            Vector2 end = new Vector2(x, y);
            float dist = Vector2.Distance(start, end);
            
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0;
                int lerpX = Mathf.Clamp((int)Mathf.Lerp(start.x, end.x, t), 0, maxX);
                int lerpY = Mathf.Clamp((int)Mathf.Lerp(start.y, end.y, t), 0, maxY);
                wb.texture.SetPixels(lerpX, lerpY, penSize, penSize, _colors);
            }
            wb.texture.Apply();
        }
        else
        {
            // Premier point du trait
            wb.texture.SetPixels(x, y, penSize, penSize, _colors);
            wb.texture.Apply();
            
            if (showDebugLogs)
                Debug.Log($"[WhiteboardMarker] Premier point du trait à UV({uv.x:F2}, {uv.y:F2})");
        }
        
        // 2. AJOUT AU BUFFER RÉSEAU
        _pendingPoints.Add(new float[] { uv.x, uv.y });

        _lastTouchPos = new Vector2(x, y);
        _touchedLastFrame = true;
    }

    // ========================================
    // RÉSEAU - ENVOI PAR BATCH
    // ========================================

    void NetworkUpdate()
    {
        _networkTimer += Time.deltaTime;

        if (_networkTimer >= sendRate)
        {
            if (_pendingPoints.Count > 0)
            {
                SendBatchToNetwork();
            }
            
            _networkTimer = 0f;
        }
    }

    void SendBatchToNetwork()
    {
        // VÉRIFICATIONS CRITIQUES
        if (!VRNetworkManager.IsConnected)
        {
            if (showDebugLogs && Time.frameCount % 120 == 0)
                Debug.LogWarning("[WhiteboardMarker] Pas connecté au réseau!");
            return;
        }

        if (_pendingPoints.Count == 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(_currentWhiteboardId))
        {
            Debug.LogError("[WhiteboardMarker] ERREUR: _currentWhiteboardId est vide!");
            _pendingPoints.Clear();
            return;
        }

        // Créer le packet
        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentWhiteboardId,
            r = currentColor.r,
            g = currentColor.g,
            b = currentColor.b,
            a = currentColor.a,
            penSize = penSize,
            points = new List<float[]>(_pendingPoints)
        };

        // Créer le batch
        WhiteboardBatchData batch = new WhiteboardBatchData
        {
            whiteboardId = packet.whiteboardId,
            draws = new List<WhiteboardPacket> { packet }
        };

        // ENVOI
        try
        {
            VRNetworkManager.Instance.Send("whiteboard-batch", batch);
            
            _totalPointsSent += _pendingPoints.Count;
            _totalBatchesSent++;
            
            if (showDebugLogs)
            {
                Debug.Log($"[WhiteboardMarker] ✅ ENVOYÉ batch #{_totalBatchesSent} : " +
                          $"{_pendingPoints.Count} points pour {_currentWhiteboardId}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WhiteboardMarker] Erreur envoi: {e.Message}");
        }

        // Vider le buffer
        _pendingPoints.Clear();
    }

    // ========================================
    // COULEUR
    // ========================================

    public void SetColor(Color newColor)
    {
        currentColor = newColor;
        ApplyColor(newColor);
    }

    void ApplyColor(Color color)
    {
        if (_renderer != null)
            _renderer.material.color = color;

        _colors = Enumerable.Repeat(color, penSize * penSize).ToArray();
    }

    // ========================================
    // DEBUG
    // ========================================

    public bool IsDrawing()
    {
        return _touchedLastFrame && _currentWhiteboard != null;
    }

    void OnDestroy()
    {
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.RemoveAllListeners();
            _grabInteractable.selectExited.RemoveAllListeners();
        }
    }

    void OnGUI()
    {
        if (!showDebugRay) return;

        GUILayout.BeginArea(new Rect(10, 350, 400, 180));
        GUILayout.Box("=== WHITEBOARD MARKER DEBUG ===");
        GUILayout.Label($"Tenu: {(_isHeld ? "OUI " : "NON")}");
        GUILayout.Label($"Dessin actif: {(IsDrawing() ? "OUI " : "NON")}");
        GUILayout.Label($"Tableau: {_currentWhiteboardId ?? "Aucun"}");
        GUILayout.Label($"Buffer: {_pendingPoints.Count} points");
        GUILayout.Label($"Connecté: {(VRNetworkManager.IsConnected ? "OUI " : "NON ")}");
        GUILayout.Label($"Total envoyé: {_totalBatchesSent} batchs ({_totalPointsSent} points)");
        GUILayout.Label($"Layer Whiteboard: {LayerMask.LayerToName(whiteboardLayer)}");
        GUILayout.EndArea();
    }
}