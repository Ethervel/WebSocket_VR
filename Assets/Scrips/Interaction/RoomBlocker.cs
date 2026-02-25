using UnityEngine;

/// <summary>
/// Bloque un passage (mur noir) jusqu'à ce que le joueur rejoigne une room.
/// Assigner ce script à l'objet bloquant (mur, porte, etc.)
/// L'objet disparaît quand le joueur rejoint une room et réapparaît quand il quitte.
/// </summary>
public class RoomBlocker : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Si true, l'objet réapparaît quand le joueur quitte la room")]
    [SerializeField] private bool _reappearOnLeave = true;
    
    [Tooltip("Si true, utilise un fade au lieu d'un toggle instantané")]
    [SerializeField] private bool _useFade = false;
    
    [Tooltip("Durée du fade en secondes")]
    [SerializeField] private float _fadeDuration = 0.5f;

    private Renderer[] _renderers;
    private Collider[] _colliders;
    private bool _isFading;
    private float _fadeProgress;
    private bool _fadeIn;

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _colliders = GetComponentsInChildren<Collider>();
    }

    private void OnEnable()
    {
        VRRoomManager.OnRoomCreated += HandleRoomCreated;
        VRRoomManager.OnRoomJoined += HandleRoomJoined;
        VRRoomManager.OnRoomLeft += HandleRoomLeft;
        
        // Si déjà dans une room, désactiver immédiatement
        if (VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom)
        {
            SetBlockerActive(false);
        }
    }

    private void OnDisable()
    {
        VRRoomManager.OnRoomCreated -= HandleRoomCreated;
        VRRoomManager.OnRoomJoined -= HandleRoomJoined;
        VRRoomManager.OnRoomLeft -= HandleRoomLeft;
    }

    private void Update()
    {
        if (_isFading)
        {
            _fadeProgress += Time.deltaTime / _fadeDuration;
            
            if (_fadeProgress >= 1f)
            {
                _fadeProgress = 1f;
                _isFading = false;
                
                // À la fin du fade out, désactiver complètement
                if (!_fadeIn)
                {
                    gameObject.SetActive(false);
                }
            }
            
            float alpha = _fadeIn ? _fadeProgress : (1f - _fadeProgress);
            SetRenderersAlpha(alpha);
        }
    }

    private void HandleRoomCreated(string roomId)
    {
        Debug.Log($"[RoomBlocker] Room created: {roomId}, hiding blocker");
        SetBlockerActive(false);
    }

    
private void HandleRoomJoined(string roomId)
    {
        Debug.Log($"[RoomBlocker] Room joined: {roomId}, hiding blocker");
        SetBlockerActive(false);
    }

    private void HandleRoomLeft()
    {
        if (_reappearOnLeave)
        {
            Debug.Log("[RoomBlocker] Room left, showing blocker");
            SetBlockerActive(true);
        }
    }

    private void SetBlockerActive(bool active)
    {
        if (_useFade)
        {
            if (active && !gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                SetRenderersAlpha(0f);
            }
            
            _isFading = true;
            _fadeProgress = 0f;
            _fadeIn = active;
        }
        else
        {
            gameObject.SetActive(active);
        }
    }

    private void SetRenderersAlpha(float alpha)
    {
        foreach (var renderer in _renderers)
        {
            if (renderer != null && renderer.material.HasProperty("_Color"))
            {
                Color color = renderer.material.color;
                color.a = alpha;
                renderer.material.color = color;
            }
        }
    }

    // Méthode publique pour forcer l'état
    public void ForceHide()
    {
        SetBlockerActive(false);
    }

    public void ForceShow()
    {
        SetBlockerActive(true);
    }
}
