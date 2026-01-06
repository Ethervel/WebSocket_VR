using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class VRNetworkedInteractable : MonoBehaviour
{
    [Header("Network Settings")]
    [Tooltip("ID unique pour cet objet (ex: Cube_01). Doit être identique sur tous les clients.")]
    public string objectId = "SharedObject_01";
    
    public float syncRate = 20f;
    public float interpolationSpeed = 15f;

    [Header("Debug")]
    public bool showDebugLogs = false;

    private XRGrabInteractable _interactable;
    private Rigidbody _rb;
    private bool _isOwner = false;
    
    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private float _lastSyncTime;
    private bool _hasReceivedData = false;

    private string _currentOwnerId = "";
    private string _currentRoomId = "";

    void Awake()
    {
        _interactable = GetComponent<XRGrabInteractable>();
        _rb = GetComponent<Rigidbody>();
        
        _targetPos = transform.position;
        _targetRot = transform.rotation;
    }

    void OnEnable()
    {
        _interactable.selectEntered.AddListener(OnGrab);
        _interactable.selectExited.AddListener(OnRelease);
        
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        
        // 🔥 IMPORTANT: Écouter les changements de room
        VRRoomManager.OnRoomJoined += OnRoomChanged;
        VRRoomManager.OnRoomCreated += OnRoomChanged;
        VRRoomManager.OnRoomLeft += OnRoomLeft;
    }

    void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(OnGrab);
        _interactable.selectExited.RemoveListener(OnRelease);
        
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        
        VRRoomManager.OnRoomJoined -= OnRoomChanged;
        VRRoomManager.OnRoomCreated -= OnRoomChanged;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;
    }

    void Update()
    {
        // Ne sync que si on est dans une room
        if (!IsInRoom())
        {
            return;
        }

        if (_isOwner)
        {
            if (Time.time - _lastSyncTime > 1f / syncRate)
            {
                SendTransformUpdate();
                _lastSyncTime = Time.time;
            }
        }
        else if (_hasReceivedData)
        {
            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * interpolationSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, Time.deltaTime * interpolationSpeed);
        }
    }

    // ========================================
    // ROOM MANAGEMENT
    // ========================================

    void OnRoomChanged(string roomId)
    {
        _currentRoomId = roomId;
        
        if (showDebugLogs)
            Debug.Log($"[NetObj:{objectId}] Room changed to: {roomId}");
    }

    void OnRoomLeft()
    {
        _currentRoomId = "";
        _isOwner = false;
        _hasReceivedData = false;
        
        if (showDebugLogs)
            Debug.Log($"[NetObj:{objectId}] Left room");
    }

    bool IsInRoom()
    {
        return VRRoomManager.Instance != null && 
               VRRoomManager.Instance.IsInRoom &&
               !string.IsNullOrEmpty(_currentRoomId);
    }

    // ========================================
    // XR EVENTS
    // ========================================

    private void OnGrab(SelectEnterEventArgs args)
    {
        RequestOwnership();
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        SendStateUpdate(false, _rb.linearVelocity, _rb.angularVelocity);
    }

    // ========================================
    // NETWORK SYNC
    // ========================================

    void RequestOwnership()
    {
        if (!IsInRoom()) return;

        _isOwner = true;
        _currentOwnerId = VRNetworkManager.LocalId;
        
        SendStateUpdate(true, Vector3.zero, Vector3.zero);
        
        if (showDebugLogs)
            Debug.Log($"[NetObj:{objectId}] Ownership taken in room {_currentRoomId}");
    }

    void SendTransformUpdate()
    {
        if (!VRNetworkManager.IsConnected || !IsInRoom()) return;

        var data = new ObjectSyncData
        {
            objId = objectId,
            roomId = _currentRoomId, // 🔥 AJOUT DU ROOM ID
            posX = transform.position.x,
            posY = transform.position.y,
            posZ = transform.position.z,
            rotX = transform.rotation.x,
            rotY = transform.rotation.y,
            rotZ = transform.rotation.z,
            rotW = transform.rotation.w
        };

        VRNetworkManager.Instance.Send("obj-sync", data);
    }

    void SendStateUpdate(bool isGrabbed, Vector3 velocity, Vector3 angularVel)
    {
        if (!VRNetworkManager.IsConnected || !IsInRoom()) return;

        var data = new ObjectStateData
        {
            objId = objectId,
            roomId = _currentRoomId, // 🔥 AJOUT DU ROOM ID
            ownerId = VRNetworkManager.LocalId,
            isGrabbed = isGrabbed,
            velX = velocity.x, velY = velocity.y, velZ = velocity.z,
            angX = angularVel.x, angY = angularVel.y, angZ = angularVel.z
        };

        VRNetworkManager.Instance.Send("obj-state", data);
    }

    // ========================================
    // NETWORK RECEIVE
    // ========================================

    void HandleNetworkMessage(NetworkMessage msg)
    {
        // 🔥 FILTRAGE 1: Vérifier qu'on est dans une room
        if (!IsInRoom()) return;

        if (msg.type == "obj-sync")
        {
            var data = JsonUtility.FromJson<ObjectSyncData>(msg.data);
            
            // 🔥 FILTRAGE 2: Vérifier que c'est notre objet
            if (data.objId != objectId) return;
            
            // 🔥 FILTRAGE 3: Vérifier que c'est la même room
            if (data.roomId != _currentRoomId)
            {
                if (showDebugLogs)
                    Debug.Log($"[NetObj:{objectId}] Ignored sync from different room: {data.roomId}");
                return;
            }

            // 🔥 FILTRAGE 4: Ignorer si on est le proprio
            if (_isOwner) return;

            _targetPos = new Vector3(data.posX, data.posY, data.posZ);
            _targetRot = new Quaternion(data.rotX, data.rotY, data.rotZ, data.rotW);
            _hasReceivedData = true;
        }
        else if (msg.type == "obj-state")
        {
            var data = JsonUtility.FromJson<ObjectStateData>(msg.data);
            
            if (data.objId != objectId) return;
            
            // 🔥 FILTRAGE 3: Vérifier la room
            if (data.roomId != _currentRoomId)
            {
                if (showDebugLogs)
                    Debug.Log($"[NetObj:{objectId}] Ignored state from different room: {data.roomId}");
                return;
            }

            if (data.ownerId != VRNetworkManager.LocalId)
            {
                _currentOwnerId = data.ownerId;
                _isOwner = false;

                if (data.isGrabbed)
                {
                    _rb.isKinematic = true;
                    _interactable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)_interactable);
                }
                else
                {
                    _rb.isKinematic = false;
                    _rb.linearVelocity = new Vector3(data.velX, data.velY, data.velZ);
                    _rb.angularVelocity = new Vector3(data.angX, data.angY, data.angZ);
                }
            }
        }
    }
}

// ========================================
// DATA STRUCTURES
// ========================================

[System.Serializable]
public class ObjectSyncData
{
    public string objId;
    public string roomId; // 🔥 NOUVEAU CHAMP
    public float posX, posY, posZ;
    public float rotX, rotY, rotZ, rotW;
}

[System.Serializable]
public class ObjectStateData
{
    public string objId;
    public string roomId; // 🔥 NOUVEAU CHAMP
    public string ownerId;
    public bool isGrabbed;
    public float velX, velY, velZ;
    public float angX, angY, angZ;
}