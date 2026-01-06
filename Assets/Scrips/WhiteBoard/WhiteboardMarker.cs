using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Feutre VR pour dessiner sur les tableaux blancs
/// Version corrigée avec sérialisation JSON fiable
/// </summary>
public class WhiteboardMarker : MonoBehaviour
{
    [Header("Configuration")]
    public Transform tip;
    public int penSize = 10;
    public Color currentColor = Color.black;
    public LayerMask whiteboardLayer;

    [Header("Network Settings")]
    public float sendRate = 0.05f;
    public int minPointsBeforeSend = 3;

    [Header("Debug")]
    private Renderer _renderer;
    private Color[] _colors;
    private float _tipHeight;
    
    private Whiteboard _currentWhiteboard;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private RaycastHit _touch;

    // 🔧 FIX: Stocker les points en format plat (u1,v1,u2,v2,...)
    private float _networkTimer;
    private List<float> _pendingPointsFlat = new List<float>();
    private string _currentWhiteboardId;
    
    private XRGrabInteractable _grabInteractable;
    private bool _isHeld = false;

    private int _totalPointsSent = 0;
    private int _totalBatchesSent = 0;
    private int _failedSends = 0;

    void Start()
    {
        if (tip == null)
        {
            Debug.LogError("[WhiteboardMarker] Tip non assigné!");
            enabled = false;
            return;
        }

        _renderer = tip.GetComponent<Renderer>();
        _grabInteractable = GetComponent<XRGrabInteractable>();
        
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.AddListener(_ => OnGrabbed());
            _grabInteractable.selectExited.AddListener(_ => OnReleased());
        }
        else
        {
            Debug.LogWarning("[WhiteboardMarker] Pas de XRGrabInteractable");
            _isHeld = true;
        }

        _tipHeight = tip.localScale.y;
        ApplyColor(currentColor);

        
    }

    void OnGrabbed()
    {
        _isHeld = true;
        
    }

    void OnReleased()
    {
        _isHeld = false;
        _touchedLastFrame = false;
        
        if (_pendingPointsFlat.Count > 0 && _currentWhiteboard != null)
        {
            SendBatchToNetwork();
        }
        
        _currentWhiteboard = null;
        _currentWhiteboardId = null;
        

    }

    void Update()
    {
        if (_isHeld)
        {
            Draw();
            NetworkUpdate();
        }
    }

    void Draw()
    {
        bool hit = Physics.Raycast(tip.position, transform.up, out _touch, _tipHeight, whiteboardLayer);

        

        if (!hit)
        {
            if (_touchedLastFrame)
            {
                Debug.Log("[WhiteboardMarker] Perdu contact avec tableau");
                if (_pendingPointsFlat.Count > 0)
                {
                    SendBatchToNetwork();
                }
            }
            
            _currentWhiteboard = null;
            _touchedLastFrame = false;
            return;
        }

        Whiteboard wb = _touch.transform.GetComponent<Whiteboard>();
        if (wb == null)
        {
            _touchedLastFrame = false;
            return;
        }

        if (_currentWhiteboard != wb)
        {
            if (_pendingPointsFlat.Count > 0 && _currentWhiteboard != null)
            {
                SendBatchToNetwork();
            }
            
            _currentWhiteboard = wb;
            _currentWhiteboardId = wb.id;
            _pendingPointsFlat.Clear();
            
            
        }

        Vector2 uv = _touch.textureCoord;
        
        int maxX = (int)wb.textureSize.x - penSize;
        int maxY = (int)wb.textureSize.y - penSize;
        
        int x = Mathf.Clamp((int)(uv.x * wb.textureSize.x - penSize / 2), 0, maxX);
        int y = Mathf.Clamp((int)(uv.y * wb.textureSize.y - penSize / 2), 0, maxY);

        // Dessin local immédiat
        if (_touchedLastFrame)
        {
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
            wb.texture.SetPixels(x, y, penSize, penSize, _colors);
            wb.texture.Apply();
            
        }
        
        // 🔧 FIX: Ajouter au buffer PLAT (u,v,u,v,u,v...)
        _pendingPointsFlat.Add(uv.x);
        _pendingPointsFlat.Add(uv.y);

        _lastTouchPos = new Vector2(x, y);
        _touchedLastFrame = true;
    }

    void NetworkUpdate()
    {
        if (!_isHeld || _currentWhiteboard == null)
            return;

        _networkTimer += Time.deltaTime;

        // 🔧 FIX: Diviser par 2 car on stocke u,v séparément
        int pointCount = _pendingPointsFlat.Count / 2;
        if (_networkTimer >= sendRate && pointCount >= minPointsBeforeSend)
        {
            SendBatchToNetwork();
            _networkTimer = 0f;
        }
    }

    void SendBatchToNetwork()
    {
        if (_pendingPointsFlat.Count == 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(_currentWhiteboardId))
        {
            Debug.LogError("[WhiteboardMarker] ERREUR: _currentWhiteboardId vide!");
            _pendingPointsFlat.Clear();
            _failedSends++;
            return;
        }

        if (!VRNetworkManager.IsConnected)
        {
            
            _failedSends++;
            return;
        }

        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            
            _failedSends++;
            return;
        }

        // 🔧 FIX: Créer packet avec liste plate de floats
        WhiteboardPacket packet = new WhiteboardPacket
        {
            whiteboardId = _currentWhiteboardId,
            r = currentColor.r,
            g = currentColor.g,
            b = currentColor.b,
            a = currentColor.a,
            penSize = penSize,
            pointsFlat = _pendingPointsFlat.ToArray() // Array pour meilleure sérialisation
        };

        WhiteboardBatchData batch = new WhiteboardBatchData
        {
            whiteboardId = _currentWhiteboardId,
            draws = new List<WhiteboardPacket> { packet }
        };

        try
        {
            VRNetworkManager.Instance.Send("whiteboard-batch", batch);
            
            int pointCount = _pendingPointsFlat.Count / 2;
            _totalPointsSent += pointCount;
            _totalBatchesSent++;
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WhiteboardMarker] ❌ Erreur envoi: {e.Message}\n{e.StackTrace}");
            _failedSends++;
        }

        _pendingPointsFlat.Clear();
    }

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

    void OnDestroy()
    {
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.RemoveAllListeners();
            _grabInteractable.selectExited.RemoveAllListeners();
        }
    }

}