using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // Pour Unity 6 / XRI 3.x
// using UnityEngine.XR.Interaction.Toolkit; // Décommentez si vous utilisez XRI 2.x

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
public class VRNetworkedInteractable : MonoBehaviour
{
    [Header("Network Settings")]
    [Tooltip("ID unique pour cet objet (ex: Cube_01). Doit être identique sur tous les clients.")]
    public string objectId = "SharedObject_01";
    
    public float syncRate = 20f; // Mises à jour par seconde
    public float interpolationSpeed = 15f;

    private XRGrabInteractable _interactable;
    private Rigidbody _rb;
    private bool _isOwner = false; // Suis-je celui qui tient l'objet ?
    
    // Sync vars
    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private float _lastSyncTime;
    private bool _hasReceivedData = false;

    // Pour éviter les conflits
    private string _currentOwnerId = "";

    void Awake()
    {
        _interactable = GetComponent<XRGrabInteractable>();
        _rb = GetComponent<Rigidbody>();
        
        // Initialisation des targets
        _targetPos = transform.position;
        _targetRot = transform.rotation;
    }

    void OnEnable()
    {
        // S'abonner aux événements du XR Toolkit
        // Note: La syntaxe peut varier légèrement selon la version de XRI (2.x vs 3.x)
        _interactable.selectEntered.AddListener(OnGrab);
        _interactable.selectExited.AddListener(OnRelease);
        
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
    }

    void OnDisable()
    {
        _interactable.selectEntered.RemoveListener(OnGrab);
        _interactable.selectExited.RemoveListener(OnRelease);
        
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
    }

    void Update()
    {
        // Si je suis le propriétaire (je le tiens), j'envoie les données
        if (_isOwner)
        {
            if (Time.time - _lastSyncTime > 1f / syncRate)
            {
                SendTransformUpdate();
                _lastSyncTime = Time.time;
            }
        }
        // Si je ne suis pas le propriétaire et qu'on a reçu des données, j'interpole
        else if (_hasReceivedData)
        {
            // Interpolation fluide vers la position cible
            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * interpolationSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, Time.deltaTime * interpolationSpeed);
        }
    }

    // --- Événements XR (Local) ---

    private void OnGrab(SelectEnterEventArgs args)
    {
        RequestOwnership();
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        // Quand je lâche, je reste propriétaire jusqu'à ce que quelqu'un d'autre le prenne,
        // mais j'envoie un dernier message pour dire "Physics Released" (pour le lancer)
        SendStateUpdate(false, _rb.linearVelocity, _rb.angularVelocity);
    }

    // --- Logique Réseau ---

    void RequestOwnership()
    {
        _isOwner = true;
        _currentOwnerId = VRNetworkManager.LocalId;
        
        // Je le rends physique pour moi (si nécessaire) ou Kinematic pour ne pas trembler dans la main
        // XRI gère généralement le IsKinematic quand on grab, donc on laisse faire XRI.
        
        // J'annonce à tout le monde que j'ai pris l'objet
        SendStateUpdate(true, Vector3.zero, Vector3.zero);
    }

    void SendTransformUpdate()
    {
        if (!VRNetworkManager.IsConnected) return;

        var data = new ObjectSyncData
        {
            objId = objectId,
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
        if (!VRNetworkManager.IsConnected) return;

        var data = new ObjectStateData
        {
            objId = objectId,
            ownerId = VRNetworkManager.LocalId,
            isGrabbed = isGrabbed,
            velX = velocity.x, velY = velocity.y, velZ = velocity.z,
            angX = angularVel.x, angY = angularVel.y, angZ = angularVel.z
        };

        VRNetworkManager.Instance.Send("obj-state", data);
    }

    // --- Réception des Messages ---

    void HandleNetworkMessage(NetworkMessage msg)
    {
        // 1. Mise à jour de position (Fréquent)
        if (msg.type == "obj-sync")
        {
            var data = JsonUtility.FromJson<ObjectSyncData>(msg.data);
            if (data.objId != objectId) return; // Ce n'est pas cet objet

            // Si je suis le propriétaire, j'ignore les positions des autres (conflit)
            if (_isOwner) return;

            // Mise à jour des cibles pour interpolation
            _targetPos = new Vector3(data.posX, data.posY, data.posZ);
            _targetRot = new Quaternion(data.rotX, data.rotY, data.rotZ, data.rotW);
            _hasReceivedData = true;
        }
        // 2. Changement d'état / Propriété (Ponctuel)
        else if (msg.type == "obj-state")
        {
            var data = JsonUtility.FromJson<ObjectStateData>(msg.data);
            if (data.objId != objectId) return;

            // Si quelqu'un d'autre prend l'objet
            if (data.ownerId != VRNetworkManager.LocalId)
            {
                _currentOwnerId = data.ownerId;
                _isOwner = false; // Je perds la propriété

                // Si l'autre l'a attrapé
                if (data.isGrabbed)
                {
                    // Important : On rend l'objet Kinematic chez nous pour éviter que la physique locale
                    // ne se batte avec la position reçue du réseau.
                    _rb.isKinematic = true;
                    
                    // Force XRI à lâcher l'objet si JE le tenais aussi
                    _interactable.interactionManager.CancelInteractableSelection((IXRSelectInteractable)_interactable);
                }
                else
                {
                    // L'autre l'a relâché (lancer)
                    _rb.isKinematic = false; // On réactive la physique
                    
                    // On applique la vélocité pour simuler le lancer synchronisé
                    _rb.linearVelocity = new Vector3(data.velX, data.velY, data.velZ);
                    _rb.angularVelocity = new Vector3(data.angX, data.angY, data.angZ);
                }
            }
        }
    }
}

// Structures de données pour JSON
[System.Serializable]
public class ObjectSyncData
{
    public string objId;
    public float posX, posY, posZ;
    public float rotX, rotY, rotZ, rotW;
}

[System.Serializable]
public class ObjectStateData
{
    public string objId;
    public string ownerId;
    public bool isGrabbed;
    // Vélocité pour le lancer
    public float velX, velY, velZ;
    public float angX, angY, angZ;
}