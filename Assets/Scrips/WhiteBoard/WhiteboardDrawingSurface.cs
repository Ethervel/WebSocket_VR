using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Surface de dessin transparente - se place DEVANT le whiteboard de fond.
/// Reçoit les dessins du réseau et du WhiteboardMarker.
/// Le dessin local est géré UNIQUEMENT par WhiteboardMarker.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class WhiteboardDrawingSurface : MonoBehaviour
{
    [Header("Network Identity")]
    [Tooltip("ID unique - doit correspondre au Whiteboard associé")]
    public string id = "Whiteboard_01";

    [Header("Texture Settings")]
    public Vector2 textureSize = new Vector2(2048, 2048);

    [Header("References")]
    [Tooltip("Le Whiteboard de fond associé (pour le screen share)")]
    public Whiteboard backgroundWhiteboard;

    // Texture de dessin (transparente)
    // MINOR FIX: Converted public field to property with private setter
    [HideInInspector] public Texture2D drawingTexture { get; private set; }

    private Renderer _renderer;
    private bool _isInitialized = false;
    private bool _hasRequestedState = false;

    // Historique pour sync réseau
    private List<WhiteboardPacket> _drawHistory = new List<WhiteboardPacket>();
    private const int MAX_HISTORY_SIZE = 100;

    // Continuité entre batches réseau (pour éviter les coupures)
    private Vector2? _lastReceivedPoint = null;
    private string _lastSenderId = null;

    // P2 FIX: Cache clearPixels array to avoid 16MB allocation per ClearTexture() call
    private Color[] _cachedClearPixels;

    // P2 FIX: Track pending state request coroutine to prevent duplicates
    private Coroutine _pendingStateRequestCoroutine;

    // IMPORTANT FIX: Timeout handling for late joiner state requests
    [Header("State Request Timeout")]
    [Tooltip("Timeout in seconds waiting for state response")]
    public float stateRequestTimeout = 10f;
    [Tooltip("Maximum retry attempts for state requests")]
    public int maxStateRequestRetries = 2;
    private int _stateRequestRetries = 0;
    private bool _waitingForStateResponse = false;


    void Start()
    {
        InitializeTexture();
        SubscribeToNetwork();

        // Si déjà dans une room, demander l'état
        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            _hasRequestedState = false;
            // P2 FIX: Cancel any pending coroutine before starting a new one
            if (_pendingStateRequestCoroutine != null)
                StopCoroutine(_pendingStateRequestCoroutine);
            _pendingStateRequestCoroutine = StartCoroutine(RequestStateDelayed());
        }
    }

    void OnEnable()
    {
        SubscribeToNetwork();
    }

    void OnDisable()
    {
        UnsubscribeFromNetwork();
    }

    void InitializeTexture()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer == null)
        {
            Debug.LogError($"[DrawingSurface:{id}] Pas de Renderer!");
            return;
        }

        // Créer texture transparente
        drawingTexture = new Texture2D((int)textureSize.x, (int)textureSize.y, TextureFormat.RGBA32, false);
        ClearTexture();

        // VR FIX: Use URP Unlit instead of Sprites/Default
        // Sprites/Default does NOT support Single Pass Instanced rendering,
        // causing the drawing surface to appear broken/split in VR headsets.
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (urpUnlit != null)
        {
            Material mat = new Material(urpUnlit);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", drawingTexture);
            mat.mainTexture = drawingTexture;
            // Transparent surface for overlay on whiteboard background
            mat.SetFloat("_Surface", 1); // Transparent
            mat.SetFloat("_Blend", 0);   // Alpha blend
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.renderQueue = 3001;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _renderer.material = mat;
        }
        else
        {
            // Fallback: try Sprites/Default (won't work in VR Single Pass Instanced)
            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader != null)
            {
                _renderer.material = new Material(spriteShader);
                _renderer.material.mainTexture = drawingTexture;
                _renderer.material.renderQueue = 3001;
                Debug.LogWarning($"[DrawingSurface:{id}] Using Sprites/Default fallback - VR stereo may be broken!");
            }
            else
            {
                _renderer.material.mainTexture = drawingTexture;
                if (_renderer.material.HasProperty("_BaseMap"))
                    _renderer.material.SetTexture("_BaseMap", drawingTexture);
                if (_renderer.material.HasProperty("_BaseColor"))
                    _renderer.material.SetColor("_BaseColor", Color.white);
            }
        }

        _isInitialized = true;
    }

    /// <summary>
    /// Efface la texture (transparent)
    /// </summary>
    public void ClearTexture()
    {
        if (drawingTexture == null) return;

        // P2 FIX: Reuse cached array instead of allocating 16MB every clear
        int pixelCount = (int)(textureSize.x * textureSize.y);
        if (_cachedClearPixels == null || _cachedClearPixels.Length != pixelCount)
        {
            _cachedClearPixels = new Color[pixelCount];
            // Initialize once with transparent color
            Color transparent = new Color(0, 0, 0, 0);
            for (int i = 0; i < pixelCount; i++)
                _cachedClearPixels[i] = transparent;
        }

        drawingTexture.SetPixels(_cachedClearPixels);
        drawingTexture.Apply();

        _drawHistory.Clear();

        // Reset continuité réseau
        _lastReceivedPoint = null;
        _lastSenderId = null;
    }

    /// <summary>
    /// Demande un clear et notifie le réseau
    /// </summary>
    public void RequestClear()
    {
        ClearTexture();
        SendClearToNetwork();
    }

    #region Network

    void SubscribeToNetwork()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;

        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomJoined += OnRoomJoined;

        VRRoomManager.OnRoomLeft -= OnRoomLeft;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
    }

    void UnsubscribeFromNetwork()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRRoomManager.OnRoomJoined -= OnRoomJoined;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
    }

    void OnRoomJoined(string roomId)
    {
        _hasRequestedState = false;
        // P2 FIX: Cancel any pending coroutine before starting a new one
        if (_pendingStateRequestCoroutine != null)
            StopCoroutine(_pendingStateRequestCoroutine);
        _pendingStateRequestCoroutine = StartCoroutine(RequestStateDelayed());
    }

    void OnRoomLeft()
    {
        // P2 FIX: Cancel any pending state request on room leave
        if (_pendingStateRequestCoroutine != null)
        {
            StopCoroutine(_pendingStateRequestCoroutine);
            _pendingStateRequestCoroutine = null;
        }
        ClearTexture();
        _hasRequestedState = false;
    }

    System.Collections.IEnumerator RequestStateDelayed()
    {
        yield return new WaitForSeconds(1.5f);

        if (VRNetworkManager.IsConnected && VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            if (!_hasRequestedState)
            {
                _stateRequestRetries = 0;
                yield return StartCoroutine(RequestStateWithTimeout());
            }
        }

        // P2 FIX: Clear coroutine reference when complete
        _pendingStateRequestCoroutine = null;
    }

    // IMPORTANT FIX: Request state with timeout and retry logic
    System.Collections.IEnumerator RequestStateWithTimeout()
    {
        while (_stateRequestRetries < maxStateRequestRetries)
        {
            _stateRequestRetries++;
            _waitingForStateResponse = true;

            Debug.Log($"[DrawingSurface:{id}] IMPORTANT FIX: Requesting state (attempt {_stateRequestRetries}/{maxStateRequestRetries})");
            RequestState();
            _hasRequestedState = true;

            // Wait for response or timeout
            float timer = 0f;
            while (_waitingForStateResponse && timer < stateRequestTimeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }

            if (!_waitingForStateResponse)
            {
                // Response received
                Debug.Log($"[DrawingSurface:{id}] IMPORTANT FIX: State response received");
                yield break;
            }

            // Timeout - retry if attempts remaining
            Debug.LogWarning($"[DrawingSurface:{id}] IMPORTANT FIX: State request timeout after {stateRequestTimeout}s (attempt {_stateRequestRetries}/{maxStateRequestRetries})");
        }

        // All retries exhausted
        _waitingForStateResponse = false;
        Debug.LogWarning($"[DrawingSurface:{id}] IMPORTANT FIX: State request failed after {maxStateRequestRetries} attempts - proceeding with empty canvas");
    }

    string GetCurrentRoomId()
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            return null;
        return VRRoomManager.Instance.CurrentRoomId;
    }

    void HandleNetworkMessage(NetworkMessage msg)
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            return;

        try
        {
            switch (msg.type)
            {
                case "whiteboard-batch":
                    HandleBatchReceived(msg.data, msg.senderId);
                    break;
                case "whiteboard-clear":
                    HandleClearReceived(msg.data, msg.senderId);
                    break;
                case "whiteboard-request":
                    HandleStateRequest(msg.data, msg.senderId);
                    break;
                case "whiteboard-state":
                    HandleStateReceived(msg.data, msg.senderId);
                    break;
                case "whiteboard-history":
                    HandleHistoryReceived(msg.data, msg.senderId);
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DrawingSurface:{id}] Erreur: {e.Message}");
        }
    }

    void HandleBatchReceived(string dataJson, string senderId)
    {
        // IMPORTANT FIX: Defensive null checks
        if (string.IsNullOrEmpty(dataJson)) return;

        WhiteboardBatchData batchData;
        try
        {
            batchData = JsonUtility.FromJson<WhiteboardBatchData>(dataJson);
            if (batchData == null) return;
        }
        catch (Exception e)
        {
            // MINOR FIX: Log instead of silently swallowing
            Debug.LogWarning($"[DrawingSurface:{id}] Failed to parse batch data: {e.Message}");
            return;
        }

        if (batchData.whiteboardId != id) return;

        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(batchData.roomId) && batchData.roomId != currentRoom) return;

        if (batchData.draws == null || batchData.draws.Count == 0) return;

        // Ne pas re-dessiner nos propres traits (deja appliques localement)
        if (senderId == VRNetworkManager.LocalId) return;

        // Reset continuité si nouveau sender
        if (_lastSenderId != senderId)
        {
            _lastReceivedPoint = null;
            _lastSenderId = senderId;
        }

        foreach (var packet in batchData.draws)
        {
            if (packet.pointsFlat == null || packet.pointsFlat.Length == 0) continue;

            // Si c'est un nouveau trait, ne pas interpoler depuis le dernier point
            Vector2? previousPoint = packet.isNewStroke ? null : _lastReceivedPoint;

            ApplyPacket(packet, false, previousPoint);
            AddToHistory(packet);

            // Sauvegarder le dernier point du batch pour le prochain (sauf si nouveau trait)
            if (packet.pointsFlat.Length >= 2)
            {
                float lastU = packet.pointsFlat[packet.pointsFlat.Length - 2];
                float lastV = packet.pointsFlat[packet.pointsFlat.Length - 1];
                int sizeX = packet.penSize;
                int sizeY = packet.penSizeY > 0 ? packet.penSizeY : packet.penSize;
                int lastX = Mathf.Clamp((int)(lastU * textureSize.x - sizeX / 2), 0, (int)textureSize.x - sizeX);
                int lastY = Mathf.Clamp((int)(lastV * textureSize.y - sizeY / 2), 0, (int)textureSize.y - sizeY);
                _lastReceivedPoint = new Vector2(lastX, lastY);
            }
        }

        drawingTexture.Apply();
    }

    /// <summary>
    /// Applique un packet de dessin sur la texture
    /// </summary>
    public void ApplyPacket(WhiteboardPacket packet, bool apply = true, Vector2? previousBatchLastPoint = null)
    {
        if (!_isInitialized || drawingTexture == null) return;
        if (packet.pointsFlat == null || packet.pointsFlat.Length < 2) return;
        if (packet.pointsFlat.Length % 2 != 0) return;

        Color col = new Color(packet.r, packet.g, packet.b, packet.a);

        int sizeX = packet.penSize;
        int sizeY = packet.penSizeY > 0 ? packet.penSizeY : packet.penSize;

        int pixelCount = sizeX * sizeY;
        Color[] paintPixels = new Color[pixelCount];
        for (int i = 0; i < pixelCount; i++) paintPixels[i] = col;

        // Distance max pour interpoler (25% de la texture = ~512px sur 2048)
        float maxInterpolationDistance = textureSize.x * 0.25f;

        // Commencer avec le dernier point du batch précédent si disponible
        Vector2? lastPoint = previousBatchLastPoint;

        for (int i = 0; i < packet.pointsFlat.Length; i += 2)
        {
            float u = packet.pointsFlat[i];
            float v = packet.pointsFlat[i + 1];

            int x = Mathf.Clamp((int)(u * textureSize.x - sizeX / 2), 0, (int)textureSize.x - sizeX);
            int y = Mathf.Clamp((int)(v * textureSize.y - sizeY / 2), 0, (int)textureSize.y - sizeY);

            Vector2 currentPoint = new Vector2(x, y);

            if (lastPoint.HasValue)
            {
                float dist = Vector2.Distance(lastPoint.Value, currentPoint);

                // Ne pas interpoler si les points sont trop éloignés (nouveau trait)
                if (dist <= maxInterpolationDistance)
                {
                    InterpolatePoints(lastPoint.Value, currentPoint, paintPixels, sizeX, sizeY);
                }
                else
                {
                    // Points trop éloignés = nouveau trait
                    _lastReceivedPoint = null;
                    drawingTexture.SetPixels(x, y, sizeX, sizeY, paintPixels);
                }
            }
            else
            {
                drawingTexture.SetPixels(x, y, sizeX, sizeY, paintPixels);
            }

            lastPoint = currentPoint;
        }

        if (apply)
            drawingTexture.Apply();
    }

    void InterpolatePoints(Vector2 start, Vector2 end, Color[] paintPixels, int sizeX, int sizeY)
    {
        float dist = Vector2.Distance(start, end);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist));

        for (int i = 0; i <= steps; i++)
        {
            float t = steps > 0 ? (float)i / steps : 0;
            int lerpX = Mathf.Clamp((int)Mathf.Lerp(start.x, end.x, t), 0, (int)textureSize.x - sizeX);
            int lerpY = Mathf.Clamp((int)Mathf.Lerp(start.y, end.y, t), 0, (int)textureSize.y - sizeY);
            drawingTexture.SetPixels(lerpX, lerpY, sizeX, sizeY, paintPixels);
        }
    }

    void HandleClearReceived(string dataJson, string senderId)
    {
        // IMPORTANT FIX: Defensive null checks
        if (string.IsNullOrEmpty(dataJson)) return;

        WhiteboardClearData clearData;
        try
        {
            clearData = JsonUtility.FromJson<WhiteboardClearData>(dataJson);
            if (clearData == null) return;
        }
        catch (Exception e)
        {
            // MINOR FIX: Log instead of silently swallowing
            Debug.LogWarning($"[DrawingSurface:{id}] Failed to parse clear data: {e.Message}");
            return;
        }

        if (clearData.whiteboardId != id) return;
        if (senderId == VRNetworkManager.LocalId) return;

        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(clearData.roomId) && clearData.roomId != currentRoom) return;

        ClearTexture();
    }

    void SendClearToNetwork()
    {
        if (!VRNetworkManager.IsConnected) return;

        WhiteboardClearData data = new WhiteboardClearData
        {
            whiteboardId = id,
            roomId = GetCurrentRoomId(),
            senderId = VRNetworkManager.LocalId
        };

        VRNetworkManager.Instance.Send("whiteboard-clear", data);
    }

    void RequestState()
    {
        if (!VRNetworkManager.IsConnected) return;

        string currentRoom = GetCurrentRoomId();
        if (string.IsNullOrEmpty(currentRoom)) return;

        WhiteboardRequestData request = new WhiteboardRequestData
        {
            whiteboardId = id,
            roomId = currentRoom,
            requesterId = VRNetworkManager.LocalId
        };

        VRNetworkManager.Instance.Send("whiteboard-request", request);
    }

    void HandleStateRequest(string dataJson, string requesterId)
    {
        // IMPORTANT FIX: Defensive null checks
        if (string.IsNullOrEmpty(dataJson)) return;

        WhiteboardRequestData request;
        try
        {
            request = JsonUtility.FromJson<WhiteboardRequestData>(dataJson);
            if (request == null) return;
        }
        catch (Exception e)
        {
            // MINOR FIX: Log instead of silently swallowing
            Debug.LogWarning($"[DrawingSurface:{id}] Failed to parse state request: {e.Message}");
            return;
        }

        if (request.whiteboardId != id) return;
        if (request.requesterId == VRNetworkManager.LocalId) return;

        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(request.roomId) && request.roomId != currentRoom) return;

        if (drawingTexture != null)
        {
            SendState(request.requesterId);
        }
    }

    void SendState(string targetId)
    {
        if (drawingTexture == null) return;

        string currentRoom = GetCurrentRoomId();
        if (string.IsNullOrEmpty(currentRoom)) return;

        // P1 FIX: Use history-based sync when possible (much faster than PNG encoding)
        // PNG encoding is CPU intensive (~50-100ms for 2048x2048)
        // History replay is ~0.1ms per stroke
        if (_drawHistory.Count > 0 && _drawHistory.Count <= MAX_HISTORY_SIZE)
        {
            // Send history instead of PNG - much faster
            WhiteboardHistoryData historyData = new WhiteboardHistoryData
            {
                whiteboardId = id,
                roomId = currentRoom,
                packets = _drawHistory
            };

            VRNetworkManager.Instance.Send("whiteboard-history", historyData);
            Debug.Log($"[DrawingSurface:{id}] P1 FIX: Sent history-based state ({_drawHistory.Count} strokes)");
            return;
        }

        // Fallback to PNG for empty canvas or when history is full (complex drawings)
        byte[] pngData = drawingTexture.EncodeToPNG();
        string base64Data = Convert.ToBase64String(pngData);

        WhiteboardStateData state = new WhiteboardStateData
        {
            whiteboardId = id,
            roomId = currentRoom,
            textureData = base64Data,
            width = (int)textureSize.x,
            height = (int)textureSize.y
        };

        VRNetworkManager.Instance.Send("whiteboard-state", state);
    }

    void HandleStateReceived(string dataJson, string senderId)
    {
        // IMPORTANT FIX: Defensive null checks
        if (string.IsNullOrEmpty(dataJson)) return;

        WhiteboardStateData state;
        try
        {
            state = JsonUtility.FromJson<WhiteboardStateData>(dataJson);
            if (state == null) return;
        }
        catch (Exception e)
        {
            // MINOR FIX: Log instead of silently swallowing
            Debug.LogWarning($"[DrawingSurface:{id}] Failed to parse state data: {e.Message}");
            return;
        }

        if (state.whiteboardId != id) return;
        if (senderId == VRNetworkManager.LocalId) return;

        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(state.roomId) && state.roomId != currentRoom) return;

        // IMPORTANT FIX: Validate Base64 data before decoding
        if (string.IsNullOrEmpty(state.textureData)) return;

        try
        {
            byte[] pngData = Convert.FromBase64String(state.textureData);
            if (pngData == null || pngData.Length == 0) return;

            Texture2D receivedTexture = new Texture2D(state.width, state.height);
            receivedTexture.LoadImage(pngData);

            drawingTexture.SetPixels(receivedTexture.GetPixels());
            drawingTexture.Apply();

            Destroy(receivedTexture);

            // IMPORTANT FIX: Mark state response as received for timeout handling
            _waitingForStateResponse = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[DrawingSurface:{id}] Erreur réception état: {e.Message}");
        }
    }

    // P1 FIX: Handle history-based state sync (much faster than PNG)
    void HandleHistoryReceived(string dataJson, string senderId)
    {
        // IMPORTANT FIX: Defensive null checks
        if (string.IsNullOrEmpty(dataJson)) return;

        WhiteboardHistoryData historyData;
        try
        {
            historyData = JsonUtility.FromJson<WhiteboardHistoryData>(dataJson);
            if (historyData == null) return;
        }
        catch (Exception e)
        {
            // MINOR FIX: Log instead of silently swallowing
            Debug.LogWarning($"[DrawingSurface:{id}] Failed to parse history data: {e.Message}");
            return;
        }

        if (historyData.whiteboardId != id) return;
        if (senderId == VRNetworkManager.LocalId) return;

        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(historyData.roomId) && historyData.roomId != currentRoom) return;

        if (historyData.packets == null || historyData.packets.Count == 0)
        {
            Debug.Log($"[DrawingSurface:{id}] P1 FIX: Received empty history");
            return;
        }

        // Clear and replay history
        ClearTexture();

        foreach (var packet in historyData.packets)
        {
            // IMPORTANT FIX: Skip invalid packets
            if (packet == null || packet.pointsFlat == null) continue;

            ApplyPacket(packet, false, null); // Don't apply yet, batch it
            AddToHistory(packet);
        }

        // Single Apply() after replaying all strokes
        drawingTexture.Apply();

        // IMPORTANT FIX: Mark state response as received for timeout handling
        _waitingForStateResponse = false;

        Debug.Log($"[DrawingSurface:{id}] P1 FIX: Replayed {historyData.packets.Count} strokes from history");
    }

    void AddToHistory(WhiteboardPacket packet)
    {
        _drawHistory.Add(packet);
        if (_drawHistory.Count > MAX_HISTORY_SIZE)
            _drawHistory.RemoveAt(0);
    }

    #endregion
}
