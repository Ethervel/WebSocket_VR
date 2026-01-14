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
    [HideInInspector] public Texture2D drawingTexture;

    private Renderer _renderer;
    private bool _isInitialized = false;
    private bool _hasRequestedState = false;

    // Historique pour sync réseau
    private List<WhiteboardPacket> _drawHistory = new List<WhiteboardPacket>();
    private const int MAX_HISTORY_SIZE = 100;

    // Continuité entre batches réseau (pour éviter les coupures)
    private Vector2? _lastReceivedPoint = null;
    private string _lastSenderId = null;


    void Start()
    {
        InitializeTexture();
        SubscribeToNetwork();

        // Si déjà dans une room, demander l'état
        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            _hasRequestedState = false;
            StartCoroutine(RequestStateDelayed());
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

        // Utiliser Sprites/Default shader - parfait pour overlay transparent
        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            _renderer.material = new Material(spriteShader);
            _renderer.material.mainTexture = drawingTexture;
            _renderer.material.renderQueue = 3001;
        }
        else
        {
            _renderer.material.mainTexture = drawingTexture;
            if (_renderer.material.HasProperty("_BaseMap"))
            {
                _renderer.material.SetTexture("_BaseMap", drawingTexture);
            }
            if (_renderer.material.HasProperty("_BaseColor"))
            {
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

        Color[] clearPixels = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < clearPixels.Length; i++)
            clearPixels[i] = new Color(0, 0, 0, 0); // Transparent

        drawingTexture.SetPixels(clearPixels);
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
        StartCoroutine(RequestStateDelayed());
    }

    void OnRoomLeft()
    {
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
                RequestState();
                _hasRequestedState = true;
            }
        }
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
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[DrawingSurface:{id}] Erreur: {e.Message}");
        }
    }

    void HandleBatchReceived(string dataJson, string senderId)
    {
        WhiteboardBatchData batchData = JsonUtility.FromJson<WhiteboardBatchData>(dataJson);

        if (batchData.whiteboardId != id) return;

        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(batchData.roomId) && batchData.roomId != currentRoom) return;

        if (batchData.draws == null || batchData.draws.Count == 0) return;

        // Reset continuité si nouveau sender
        if (_lastSenderId != senderId)
        {
            _lastReceivedPoint = null;
            _lastSenderId = senderId;
        }

        foreach (var packet in batchData.draws)
        {
            if (packet.pointsFlat == null || packet.pointsFlat.Length == 0) continue;

            // Passer le dernier point reçu pour continuité entre batches
            ApplyPacket(packet, false, _lastReceivedPoint);
            AddToHistory(packet);

            // Sauvegarder le dernier point du batch pour le prochain
            if (packet.pointsFlat.Length >= 2)
            {
                float lastU = packet.pointsFlat[packet.pointsFlat.Length - 2];
                float lastV = packet.pointsFlat[packet.pointsFlat.Length - 1];
                int lastX = Mathf.Clamp((int)(lastU * textureSize.x - packet.penSize / 2), 0, (int)textureSize.x - packet.penSize);
                int lastY = Mathf.Clamp((int)(lastV * textureSize.y - packet.penSize / 2), 0, (int)textureSize.y - packet.penSize);
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

        int pixelCount = packet.penSize * packet.penSize;
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

            int x = Mathf.Clamp((int)(u * textureSize.x - packet.penSize / 2), 0, (int)textureSize.x - packet.penSize);
            int y = Mathf.Clamp((int)(v * textureSize.y - packet.penSize / 2), 0, (int)textureSize.y - packet.penSize);

            Vector2 currentPoint = new Vector2(x, y);

            if (lastPoint.HasValue)
            {
                float dist = Vector2.Distance(lastPoint.Value, currentPoint);

                // Ne pas interpoler si les points sont trop éloignés (nouveau trait)
                if (dist <= maxInterpolationDistance)
                {
                    InterpolatePoints(lastPoint.Value, currentPoint, paintPixels, packet.penSize);
                }
                else
                {
                    // Points trop éloignés = nouveau trait
                    _lastReceivedPoint = null;
                    drawingTexture.SetPixels(x, y, packet.penSize, packet.penSize, paintPixels);
                }
            }
            else
            {
                drawingTexture.SetPixels(x, y, packet.penSize, packet.penSize, paintPixels);
            }

            lastPoint = currentPoint;
        }

        if (apply)
            drawingTexture.Apply();
    }

    void InterpolatePoints(Vector2 start, Vector2 end, Color[] paintPixels, int penSize)
    {
        float dist = Vector2.Distance(start, end);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist));

        for (int i = 0; i <= steps; i++)
        {
            float t = steps > 0 ? (float)i / steps : 0;
            int lerpX = Mathf.Clamp((int)Mathf.Lerp(start.x, end.x, t), 0, (int)textureSize.x - penSize);
            int lerpY = Mathf.Clamp((int)Mathf.Lerp(start.y, end.y, t), 0, (int)textureSize.y - penSize);
            drawingTexture.SetPixels(lerpX, lerpY, penSize, penSize, paintPixels);
        }
    }

    void HandleClearReceived(string dataJson, string senderId)
    {
        WhiteboardClearData clearData = JsonUtility.FromJson<WhiteboardClearData>(dataJson);

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
        WhiteboardRequestData request = JsonUtility.FromJson<WhiteboardRequestData>(dataJson);

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
        WhiteboardStateData state = JsonUtility.FromJson<WhiteboardStateData>(dataJson);

        if (state.whiteboardId != id) return;
        if (senderId == VRNetworkManager.LocalId) return;

        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(state.roomId) && state.roomId != currentRoom) return;

        try
        {
            byte[] pngData = Convert.FromBase64String(state.textureData);
            Texture2D receivedTexture = new Texture2D(state.width, state.height);
            receivedTexture.LoadImage(pngData);

            drawingTexture.SetPixels(receivedTexture.GetPixels());
            drawingTexture.Apply();

            Destroy(receivedTexture);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DrawingSurface:{id}] Erreur réception état: {e.Message}");
        }
    }

    void AddToHistory(WhiteboardPacket packet)
    {
        _drawHistory.Add(packet);
        if (_drawHistory.Count > MAX_HISTORY_SIZE)
            _drawHistory.RemoveAt(0);
    }

    #endregion
}
