using UnityEngine;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Gère le tableau blanc synchronisé en réseau
/// Version corrigée avec meilleure désérialisation
/// </summary>
public class Whiteboard : MonoBehaviour
{
    [Header("Network Identity")]
    [Tooltip("ID unique pour ce tableau - doit être identique sur tous les clients")]
    public string id = "Whiteboard_01";

    [Header("Texture Settings")]
    public Vector2 textureSize = new Vector2(2048, 2048);
    public Color defaultColor = Color.white;
    
    [Header("References")]
    public Renderer targetRenderer;
    [HideInInspector] public Texture2D texture;
    
    private List<WhiteboardPacket> _drawHistory = new List<WhiteboardPacket>();
    private const int MAX_HISTORY_SIZE = 100;

    private bool _isInitialized = false;
    private bool _hasRequestedState = false;
    
    private int _receivedBatches = 0;
    private int _receivedDraws = 0;
    private int _receivedPoints = 0;

    void Start()
    {
        InitializeTexture();
        SubscribeToNetwork();
    }

    void OnEnable()
    {
        SubscribeToNetwork();
    }

    void OnDisable()
    {
        UnsubscribeFromNetwork();
    }

    void OnDestroy()
    {
        UnsubscribeFromNetwork();
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

        texture = new Texture2D((int)textureSize.x, (int)textureSize.y);
        ClearTextureLocal();
        
        targetRenderer.material.mainTexture = texture;
        _isInitialized = true;

        Debug.Log($"[Whiteboard:{id}] Initialisé ({textureSize.x}x{textureSize.y})");
    }

    void SubscribeToNetwork()
    {
        if (VRNetworkManager.Instance == null) return;

        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        
        VRNetworkManager.OnConnected -= OnNetworkConnected;
        VRNetworkManager.OnConnected += OnNetworkConnected;
    }

    void UnsubscribeFromNetwork()
    {
        if (VRNetworkManager.Instance == null) return;

        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRNetworkManager.OnConnected -= OnNetworkConnected;
    }

    void OnNetworkConnected()
    {
        if (!_hasRequestedState)
        {
            StartCoroutine(RequestWhiteboardStateDelayed());
        }
    }

    IEnumerator RequestWhiteboardStateDelayed()
    {
        yield return new WaitForSeconds(1f);
        
        if (VRNetworkManager.IsConnected)
        {
            RequestWhiteboardState();
            _hasRequestedState = true;
        }
    }

    void HandleNetworkMessage(NetworkMessage msg)
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
        {
            return;
        }

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
            Debug.LogError($"[Whiteboard:{id}] Erreur message '{msg.type}': {e.Message}\n{e.StackTrace}");
        }
    }

    void HandleBatchReceived(string dataJson, string senderId)
    {
        WhiteboardBatchData batchData = JsonUtility.FromJson<WhiteboardBatchData>(dataJson);
        
        if (batchData.whiteboardId != id)
        {
            return;
        }

        if (batchData.draws == null || batchData.draws.Count == 0)
        {
            Debug.LogWarning($"[Whiteboard:{id}] Batch vide reçu de {senderId}");
            return;
        }

        int totalPoints = 0;
        foreach (var packet in batchData.draws)
        {
            // 🔧 FIX: Vérifier pointsFlat au lieu de points
            if (packet.pointsFlat == null || packet.pointsFlat.Length == 0)
            {
                Debug.LogWarning($"[Whiteboard:{id}] Packet sans points dans batch de {senderId}");
                continue;
            }

            // Appliquer le packet SANS upload GPU immédiat (optimisation)
            ApplyReceivedPacket(packet, false);
            AddToHistory(packet);
            
            totalPoints += packet.pointsFlat.Length / 2;
        }

        // ✅ OPTIMIZATION: Un seul upload GPU pour tout le batch
        if (texture != null)
            texture.Apply();

        _receivedBatches++;
        _receivedDraws += batchData.draws.Count;
        _receivedPoints += totalPoints;
    }

    // ✅ OPTIMIZATION: Paramètre 'apply' pour contrôler l'upload GPU
    public void ApplyReceivedPacket(WhiteboardPacket packet, bool apply = true)
    {
        if (!_isInitialized)
        {
            Debug.LogWarning($"[Whiteboard:{id}] Cannot apply packet: not initialized");
            return;
        }

        // 🔧 FIX: Supporter les deux formats (ancien et nouveau)
        if (packet.pointsFlat == null || packet.pointsFlat.Length == 0)
        {
            Debug.LogWarning($"[Whiteboard:{id}] Packet sans pointsFlat");
            return;
        }

        // Vérifier format valide (paires u,v)
        if (packet.pointsFlat.Length % 2 != 0)
        {
            Debug.LogError($"[Whiteboard:{id}] pointsFlat invalide (longueur impaire: {packet.pointsFlat.Length})");
            return;
        }

        Color col = new Color(packet.r, packet.g, packet.b, packet.a);
        
        // ✅ OPTIMIZATION: Remplacer Enumerable.Repeat par allocation simple
        int pixelCount = packet.penSize * packet.penSize;
        Color[] paintPixels = new Color[pixelCount];
        for (int i = 0; i < pixelCount; i++) paintPixels[i] = col;

        Vector2? lastPoint = null;

        // 🔧 FIX: Lire les points par paires (u,v)
        for (int i = 0; i < packet.pointsFlat.Length; i += 2)
        {
            float u = packet.pointsFlat[i];
            float v = packet.pointsFlat[i + 1];

            // Convertir UV en pixels avec clamping
            int x = Mathf.Clamp(
                (int)(u * textureSize.x - packet.penSize / 2),
                0,
                (int)textureSize.x - packet.penSize
            );
            
            int y = Mathf.Clamp(
                (int)(v * textureSize.y - packet.penSize / 2),
                0,
                (int)textureSize.y - packet.penSize
            );

            // Interpolation pour trait continu
            if (lastPoint.HasValue)
            {
                InterpolatePoints(lastPoint.Value, new Vector2(x, y), paintPixels, packet.penSize);
            }
            else
            {
                texture.SetPixels(x, y, packet.penSize, packet.penSize, paintPixels);
            }

            lastPoint = new Vector2(x, y);
        }

        if (apply)
            texture.Apply();
    }

    void InterpolatePoints(Vector2 start, Vector2 end, Color[] paintPixels, int penSize)
    {
        float dist = Vector2.Distance(start, end);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist));

        for (int i = 0; i <= steps; i++)
        {
            float t = steps > 0 ? (float)i / steps : 0;
            
            int lerpX = Mathf.Clamp(
                (int)Mathf.Lerp(start.x, end.x, t),
                0,
                (int)textureSize.x - penSize
            );
            
            int lerpY = Mathf.Clamp(
                (int)Mathf.Lerp(start.y, end.y, t),
                0,
                (int)textureSize.y - penSize
            );
            
            texture.SetPixels(lerpX, lerpY, penSize, penSize, paintPixels);
        }
    }

    public void RequestClear()
    {
        ClearTextureLocal();
        SendClearToNetwork();
    }

    void HandleClearReceived(string dataJson, string senderId)
    {
        WhiteboardClearData clearData = JsonUtility.FromJson<WhiteboardClearData>(dataJson);
        
        if (clearData.whiteboardId != id) return;
        if (senderId == VRNetworkManager.LocalId) return;

        ClearTextureLocal();
        
    }

    void ClearTextureLocal()
    {
        if (texture == null) return;

        Color[] pixels = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = defaultColor;

        texture.SetPixels(pixels);
        texture.Apply();

        _drawHistory.Clear();
        
        // Reset stats
        _receivedBatches = 0;
        _receivedDraws = 0;
        _receivedPoints = 0;
    }

    void SendClearToNetwork()
    {
        if (!VRNetworkManager.IsConnected) return;

        WhiteboardClearData data = new WhiteboardClearData
        {
            whiteboardId = id,
            senderId = VRNetworkManager.LocalId
        };

        VRNetworkManager.Instance.Send("whiteboard-clear", data);
    }

    void RequestWhiteboardState()
    {
        if (!VRNetworkManager.IsConnected) return;

        WhiteboardRequestData request = new WhiteboardRequestData
        {
            whiteboardId = id,
            requesterId = VRNetworkManager.LocalId
        };

        VRNetworkManager.Instance.Send("whiteboard-request", request);
        
    
    }

    void HandleStateRequest(string dataJson, string requesterId)
    {
        WhiteboardRequestData request = JsonUtility.FromJson<WhiteboardRequestData>(dataJson);
        
        if (request.whiteboardId != id) return;
        if (request.requesterId == VRNetworkManager.LocalId) return;

        if (_drawHistory.Count > 0)
        {
            SendWhiteboardState(requesterId);
        }
    }

    void SendWhiteboardState(string targetId)
    {
        if (texture == null) return;

        byte[] pngData = texture.EncodeToPNG();
        string base64Data = Convert.ToBase64String(pngData);

        WhiteboardStateData state = new WhiteboardStateData
        {
            whiteboardId = id,
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

        try
        {
            byte[] pngData = Convert.FromBase64String(state.textureData);
            
            Texture2D receivedTexture = new Texture2D(state.width, state.height);
            receivedTexture.LoadImage(pngData);

            texture.SetPixels(receivedTexture.GetPixels());
            texture.Apply();

            Destroy(receivedTexture);

        }
        catch (Exception e)
        {
            Debug.LogError($"[Whiteboard:{id}] Erreur réception état: {e.Message}");
        }
    }

    void AddToHistory(WhiteboardPacket packet)
    {
        _drawHistory.Add(packet);

        if (_drawHistory.Count > MAX_HISTORY_SIZE)
        {
            _drawHistory.RemoveAt(0);
        }
    }

    
}