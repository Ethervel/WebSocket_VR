using UnityEngine;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Gère le tableau blanc synchronisé en réseau
/// - Applique les dessins reçus du réseau
/// - Envoie l'état complet aux nouveaux joueurs
/// - Gère le clear synchronisé
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

    [Header("Debug")]
    public bool showDebugInfo = true;

    // Texture principale
    [HideInInspector] public Texture2D texture;
    
    // Historique des dessins (pour nouveaux joueurs)
    private List<WhiteboardPacket> _drawHistory = new List<WhiteboardPacket>();
    private const int MAX_HISTORY_SIZE = 100; // Limiter la mémoire

    // État de synchronisation
    private bool _isInitialized = false;
    private bool _hasRequestedState = false;

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

    // ========================================
    // INITIALISATION
    // ========================================

    void InitializeTexture()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null)
        {
            Debug.LogError($"[Whiteboard:{id}] Aucun Renderer trouvé!");
            return;
        }

        // Créer texture vierge
        texture = new Texture2D((int)textureSize.x, (int)textureSize.y);
        ClearTextureLocal();
        
        targetRenderer.material.mainTexture = texture;
        _isInitialized = true;

        Debug.Log($"[Whiteboard:{id}] Initialisé ({textureSize.x}x{textureSize.y})");
    }

    // ========================================
    // NETWORK SUBSCRIPTION
    // ========================================

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
        // Quand on se connecte/rejoint une room, demander l'état actuel
        if (!_hasRequestedState)
        {
            StartCoroutine(RequestWhiteboardStateDelayed());
        }
    }

    IEnumerator RequestWhiteboardStateDelayed()
    {
        // Attendre 1 seconde pour que la room soit bien jointe
        yield return new WaitForSeconds(1f);
        
        if (VRNetworkManager.IsConnected)
        {
            RequestWhiteboardState();
            _hasRequestedState = true;
        }
    }

    // ========================================
    // NETWORK HANDLERS
    // ========================================

    void HandleNetworkMessage(NetworkMessage msg)
    {
        try
        {
            switch (msg.type)
            {
                case "whiteboard-batch":
                    HandleBatchReceived(msg.data);
                    break;

                case "whiteboard-clear":
                    HandleClearReceived(msg.data);
                    break;

                case "whiteboard-request":
                    HandleStateRequest(msg.data, msg.senderId);
                    break;

                case "whiteboard-state":
                    HandleStateReceived(msg.data);
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Whiteboard:{id}] Erreur message: {e.Message}");
        }
    }

    // ========================================
    // DESSIN RÉSEAU (Batch)
    // ========================================

    void HandleBatchReceived(string dataJson)
    {
        WhiteboardBatchData batchData = JsonUtility.FromJson<WhiteboardBatchData>(dataJson);
        
        if (batchData.whiteboardId != id) return;

        foreach (var packet in batchData.draws)
        {
            ApplyReceivedPacket(packet);
            
            // Ajouter à l'historique
            AddToHistory(packet);
        }

        if (showDebugInfo)
            Debug.Log($"[Whiteboard:{id}] Reçu batch de {batchData.draws.Count} dessins");
    }

    public void ApplyReceivedPacket(WhiteboardPacket packet)
    {
        if (!_isInitialized || packet.points == null || packet.points.Count == 0)
            return;

        Color col = new Color(packet.r, packet.g, packet.b, packet.a);
        Color[] paintPixels = Enumerable.Repeat(col, packet.penSize * packet.penSize).ToArray();

        Vector2? lastPoint = null;

        foreach (var p in packet.points)
        {
            // Convertir UV (0-1) en pixels
            int x = (int)(p[0] * textureSize.x - packet.penSize / 2);
            int y = (int)(p[1] * textureSize.y - packet.penSize / 2);

            // Interpolation pour éviter les trous
            if (lastPoint.HasValue)
            {
                InterpolatePoints(lastPoint.Value, new Vector2(x, y), paintPixels, packet.penSize);
            }
            else
            {
                // Premier point
                texture.SetPixels(x, y, packet.penSize, packet.penSize, paintPixels);
            }

            lastPoint = new Vector2(x, y);
        }

        texture.Apply();
    }

    void InterpolatePoints(Vector2 start, Vector2 end, Color[] paintPixels, int penSize)
    {
        float dist = Vector2.Distance(start, end);
        int steps = Mathf.CeilToInt(dist);

        for (int i = 0; i <= steps; i++)
        {
            float t = steps > 0 ? (float)i / steps : 0;
            int lerpX = (int)Mathf.Lerp(start.x, end.x, t);
            int lerpY = (int)Mathf.Lerp(start.y, end.y, t);
            
            texture.SetPixels(lerpX, lerpY, penSize, penSize, paintPixels);
        }
    }

    // ========================================
    // CLEAR SYNCHRONISÉ
    // ========================================

    /// <summary>
    /// Appeler cette fonction depuis un bouton UI pour effacer le tableau
    /// </summary>
    public void RequestClear()
    {
        // 1. Effacer localement
        ClearTextureLocal();
        
        // 2. Envoyer au réseau
        SendClearToNetwork();
    }

    void HandleClearReceived(string dataJson)
    {
        WhiteboardClearData clearData = JsonUtility.FromJson<WhiteboardClearData>(dataJson);
        
        if (clearData.whiteboardId != id) return;

        ClearTextureLocal();
        
        if (showDebugInfo)
            Debug.Log($"[Whiteboard:{id}] Tableau effacé par {clearData.senderId}");
    }

    void ClearTextureLocal()
    {
        if (texture == null) return;

        Color[] pixels = new Color[(int)(textureSize.x * textureSize.y)];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = defaultColor;

        texture.SetPixels(pixels);
        texture.Apply();

        // Vider l'historique
        _drawHistory.Clear();
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

    // ========================================
    // SYNCHRONISATION ÉTAT COMPLET (New Player)
    // ========================================

    /// <summary>
    /// Demande l'état actuel du tableau (pour nouveaux joueurs)
    /// </summary>
    void RequestWhiteboardState()
    {
        if (!VRNetworkManager.IsConnected) return;

        WhiteboardRequestData request = new WhiteboardRequestData
        {
            whiteboardId = id,
            requesterId = VRNetworkManager.LocalId
        };

        VRNetworkManager.Instance.Send("whiteboard-request", request);
        
        if (showDebugInfo)
            Debug.Log($"[Whiteboard:{id}] Demande d'état envoyée");
    }

    /// <summary>
    /// Répond à une demande d'état en envoyant la texture complète
    /// </summary>
    void HandleStateRequest(string dataJson, string requesterId)
    {
        WhiteboardRequestData request = JsonUtility.FromJson<WhiteboardRequestData>(dataJson);
        
        if (request.whiteboardId != id) return;
        if (request.requesterId == VRNetworkManager.LocalId) return; // Pas à nous-mêmes

        // Envoyer seulement si on a un historique (on est déjà dans la room)
        if (_drawHistory.Count > 0)
        {
            SendWhiteboardState(requesterId);
        }
    }

    /// <summary>
    /// Envoie l'état complet du tableau (texture en PNG base64)
    /// </summary>
    void SendWhiteboardState(string targetId)
    {
        if (texture == null) return;

        // Encoder la texture en PNG
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

        if (showDebugInfo)
            Debug.Log($"[Whiteboard:{id}] État envoyé ({pngData.Length / 1024}KB)");
    }

    /// <summary>
    /// Reçoit et applique l'état complet du tableau
    /// </summary>
    void HandleStateReceived(string dataJson)
    {
        WhiteboardStateData state = JsonUtility.FromJson<WhiteboardStateData>(dataJson);
        
        if (state.whiteboardId != id) return;

        try
        {
            // Décoder le PNG
            byte[] pngData = Convert.FromBase64String(state.textureData);
            
            // Créer une nouvelle texture et charger le PNG
            Texture2D receivedTexture = new Texture2D(state.width, state.height);
            receivedTexture.LoadImage(pngData);

            // Appliquer à notre texture
            texture.SetPixels(receivedTexture.GetPixels());
            texture.Apply();

            Destroy(receivedTexture); // Nettoyer

            if (showDebugInfo)
                Debug.Log($"[Whiteboard:{id}] État reçu et appliqué ({pngData.Length / 1024}KB)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Whiteboard:{id}] Erreur lors de la réception de l'état: {e.Message}");
        }
    }

    // ========================================
    // HISTORIQUE (pour optimisation future)
    // ========================================

    void AddToHistory(WhiteboardPacket packet)
    {
        _drawHistory.Add(packet);

        // Limiter la taille de l'historique
        if (_drawHistory.Count > MAX_HISTORY_SIZE)
        {
            _drawHistory.RemoveAt(0);
        }
    }

    // ========================================
    // DEBUG
    // ========================================

    void OnGUI()
    {
        if (!showDebugInfo) return;

        GUILayout.BeginArea(new Rect(10, 200, 300, 150));
        GUILayout.Label($"Whiteboard: {id}");
        GUILayout.Label($"Historique: {_drawHistory.Count} packets");
        GUILayout.Label($"Initialisé: {_isInitialized}");
        GUILayout.Label($"Connecté: {VRNetworkManager.IsConnected}");
        GUILayout.EndArea();
    }
}