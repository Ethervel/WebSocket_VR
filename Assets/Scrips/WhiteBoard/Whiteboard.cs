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

    // Mode présentation (screen share / fichiers)
    private bool _isPresentationMode = false;
    private Texture2D _savedDrawingTexture;
    private string _currentPresenterId;
    private string _currentPresentationTitle;

    // Events pour le mode présentation
    public static event Action<Whiteboard, bool> OnPresentationModeChanged;  // whiteboard, isPresenting
    public static event Action<Whiteboard, Texture> OnPresentationTextureUpdated;

    void Start()
    {
        InitializeTexture();
        SubscribeToNetwork();

        // 🔧 FIX: Si on est déjà dans une room (scène chargée après join), demander l'état
        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            string roomId = VRRoomManager.Instance.CurrentRoomId;
            Debug.Log($"[Whiteboard:{id}] Already in room {roomId} at Start, requesting state...");
            _hasRequestedState = false;
            StartCoroutine(RequestWhiteboardStateDelayed());
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

    void OnDestroy()
    {
        UnsubscribeFromNetwork();
    }

    // Retourne le roomId actuel ou null si pas dans une room
    string GetCurrentRoomId()
    {
        if (VRRoomManager.Instance == null || !VRRoomManager.Instance.IsInRoom)
            return null;
        return VRRoomManager.Instance.CurrentRoomId;
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
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;

        // S'abonner aux événements de room pour sync whiteboard
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
        Debug.Log($"[Whiteboard:{id}] Joined room {roomId}, requesting state...");

        // Reset le flag pour permettre une nouvelle demande
        _hasRequestedState = false;

        // Demander l'état du whiteboard aux autres joueurs de la room
        StartCoroutine(RequestWhiteboardStateDelayed());
    }

    void OnRoomLeft()
    {
        Debug.Log($"[Whiteboard:{id}] Left room, clearing whiteboard");

        // Effacer le whiteboard quand on quitte la room
        ClearTextureLocal();

        // Reset le flag pour la prochaine room
        _hasRequestedState = false;
    }

    IEnumerator RequestWhiteboardStateDelayed()
    {
        // Attendre un peu que les autres joueurs soient prêts
        yield return new WaitForSeconds(1.5f);

        if (VRNetworkManager.IsConnected && VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            if (!_hasRequestedState)
            {
                RequestWhiteboardState();
                _hasRequestedState = true;
            }
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

        // 🔧 FIX: Vérifier que le batch vient de la même room
        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(batchData.roomId) && batchData.roomId != currentRoom)
        {
            // Ignorer les dessins d'autres rooms
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

        // 🔧 FIX: Vérifier que le clear vient de la même room
        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(clearData.roomId) && clearData.roomId != currentRoom)
        {
            return; // Ignorer les clears d'autres rooms
        }

        ClearTextureLocal();
        Debug.Log($"[Whiteboard:{id}] Cleared by {senderId} (room: {currentRoom})");
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

        string currentRoom = GetCurrentRoomId();

        WhiteboardClearData data = new WhiteboardClearData
        {
            whiteboardId = id,
            roomId = currentRoom,
            senderId = VRNetworkManager.LocalId
        };

        VRNetworkManager.Instance.Send("whiteboard-clear", data);
    }

    void RequestWhiteboardState()
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
        Debug.Log($"[Whiteboard:{id}] Requesting state for room {currentRoom}");
    }

    void HandleStateRequest(string dataJson, string requesterId)
    {
        WhiteboardRequestData request = JsonUtility.FromJson<WhiteboardRequestData>(dataJson);

        if (request.whiteboardId != id) return;
        if (request.requesterId == VRNetworkManager.LocalId) return;

        // 🔧 FIX: Vérifier que la requête vient de la même room
        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(request.roomId) && request.roomId != currentRoom)
        {
            return; // Ignorer les requêtes d'autres rooms
        }

        // 🔧 FIX: Toujours envoyer l'état de la texture (pas seulement si _drawHistory > 0)
        // Car les dessins locaux ne sont pas dans _drawHistory
        if (texture != null)
        {
            Debug.Log($"[Whiteboard:{id}] Sending state to {request.requesterId} (room: {currentRoom})");
            SendWhiteboardState(request.requesterId);
        }
    }

    void SendWhiteboardState(string targetId)
    {
        if (texture == null) return;

        string currentRoom = GetCurrentRoomId();
        if (string.IsNullOrEmpty(currentRoom)) return;

        byte[] pngData = texture.EncodeToPNG();
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

        // 🔧 FIX: Vérifier que l'état vient de la même room
        string currentRoom = GetCurrentRoomId();
        if (!string.IsNullOrEmpty(state.roomId) && state.roomId != currentRoom)
        {
            return; // Ignorer les états d'autres rooms
        }

        try
        {
            byte[] pngData = Convert.FromBase64String(state.textureData);

            Texture2D receivedTexture = new Texture2D(state.width, state.height);
            receivedTexture.LoadImage(pngData);

            texture.SetPixels(receivedTexture.GetPixels());
            texture.Apply();

            Destroy(receivedTexture);
            Debug.Log($"[Whiteboard:{id}] State received from {senderId} (room: {currentRoom})");
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

    #region Presentation Mode (Screen Share / File Display)

    /// <summary>
    /// Est-ce que le whiteboard est en mode présentation?
    /// </summary>
    public bool IsPresentationMode => _isPresentationMode;

    /// <summary>
    /// ID du présentateur actuel
    /// </summary>
    public string CurrentPresenterId => _currentPresenterId;

    /// <summary>
    /// Titre de la présentation actuelle (nom fichier ou "Screen Share")
    /// </summary>
    public string CurrentPresentationTitle => _currentPresentationTitle;

    /// <summary>
    /// Démarre le mode présentation - sauvegarde le dessin actuel
    /// </summary>
    public void StartPresentationMode(string presenterId, string title)
    {
        if (_isPresentationMode)
        {
            Debug.LogWarning($"[Whiteboard:{id}] Already in presentation mode");
            return;
        }

        if (texture == null)
        {
            Debug.LogError($"[Whiteboard:{id}] Cannot start presentation: texture not initialized");
            return;
        }

        // Sauvegarder le dessin actuel
        _savedDrawingTexture = new Texture2D((int)textureSize.x, (int)textureSize.y);
        _savedDrawingTexture.SetPixels(texture.GetPixels());
        _savedDrawingTexture.Apply();

        _isPresentationMode = true;
        _currentPresenterId = presenterId;
        _currentPresentationTitle = title;

        Debug.Log($"[Whiteboard:{id}] Presentation mode started: {title} by {presenterId}");
        OnPresentationModeChanged?.Invoke(this, true);
    }

    /// <summary>
    /// Arrête le mode présentation - restaure le dessin
    /// </summary>
    public void StopPresentationMode()
    {
        if (!_isPresentationMode)
        {
            return;
        }

        // Restaurer le dessin sauvegardé
        if (_savedDrawingTexture != null && texture != null)
        {
            texture.SetPixels(_savedDrawingTexture.GetPixels());
            texture.Apply();

            Destroy(_savedDrawingTexture);
            _savedDrawingTexture = null;
        }

        _isPresentationMode = false;
        _currentPresenterId = null;
        _currentPresentationTitle = null;

        // Remettre la texture de dessin sur le renderer
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = texture;
            // Reset texture scale/offset
            targetRenderer.material.mainTextureScale = new Vector2(1, 1);
            targetRenderer.material.mainTextureOffset = new Vector2(0, 0);
        }

        Debug.Log($"[Whiteboard:{id}] Presentation mode stopped, drawing restored");
        OnPresentationModeChanged?.Invoke(this, false);
    }

    /// <summary>
    /// Met à jour la texture de présentation (pour screen share)
    /// </summary>
    public void UpdatePresentationTexture(Texture newTexture, bool flipY = true)
    {
        if (!_isPresentationMode)
        {
            Debug.LogWarning($"[Whiteboard:{id}] Not in presentation mode");
            return;
        }

        if (targetRenderer != null && newTexture != null)
        {
            targetRenderer.material.mainTexture = newTexture;

            // Flip Y pour corriger l'inversion du screen capture
            if (flipY)
            {
                targetRenderer.material.mainTextureScale = new Vector2(1, -1);
                targetRenderer.material.mainTextureOffset = new Vector2(0, 1);
            }

            OnPresentationTextureUpdated?.Invoke(this, newTexture);
        }
    }

    /// <summary>
    /// Affiche une image sur le whiteboard (pour fichiers partagés)
    /// </summary>
    public void DisplayImage(Texture2D image, string presenterId, string fileName)
    {
        if (image == null)
        {
            Debug.LogError($"[Whiteboard:{id}] Cannot display null image");
            return;
        }

        // Démarrer le mode présentation si pas déjà actif
        if (!_isPresentationMode)
        {
            StartPresentationMode(presenterId, fileName);
        }
        else
        {
            _currentPresentationTitle = fileName;
        }

        // Afficher l'image
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = image;
        }

        Debug.Log($"[Whiteboard:{id}] Displaying image: {fileName}");
        OnPresentationTextureUpdated?.Invoke(this, image);
    }

    /// <summary>
    /// Affiche une texture de screen share sur le whiteboard
    /// </summary>
    public void DisplayScreenShare(Texture screenTexture, string presenterId, string presenterName)
    {
        if (screenTexture == null)
        {
            Debug.LogError($"[Whiteboard:{id}] Cannot display null screen texture");
            return;
        }

        // Démarrer le mode présentation si pas déjà actif
        if (!_isPresentationMode)
        {
            StartPresentationMode(presenterId, $"Screen: {presenterName}");
        }

        // Afficher le screen share
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = screenTexture;
        }

        OnPresentationTextureUpdated?.Invoke(this, screenTexture);
    }

    #endregion
}