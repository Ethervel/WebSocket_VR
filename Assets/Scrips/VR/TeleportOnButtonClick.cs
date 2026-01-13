using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// Téléporte le joueur quand un bouton UI est cliqué.
/// Fonctionne en mode VR (XR Origin) et Desktop (CharacterController).
/// Attacher ce script au même GameObject que le Button, ou assigner le Button manuellement.
/// </summary>
public class TeleportOnButtonClick : MonoBehaviour
{
    [Header("Button")]
    [Tooltip("Button UI à écouter (auto-détecté si sur le même GameObject)")]
    public Button targetButton;

    [Header("Destination")]
    [Tooltip("Point de destination pour la téléportation")]
    public Transform destinationPoint;

    [Tooltip("Utiliser la position de ce GameObject comme destination")]
    public bool useOwnPositionAsDestination = false;

    [Header("Options")]
    [Tooltip("Appliquer la rotation du point de destination")]
    public bool applyRotation = true;

    [Tooltip("Délai avant téléportation (secondes)")]
    public float teleportDelay = 0f;

    [Header("Room Change (Optionnel)")]
    [Tooltip("Changer de zone/room après téléportation")]
    public bool changeRoomType = false;

    [Tooltip("Type de room destination")]
    public RoomType targetRoomType = RoomType.Lobby;

    [Header("Debug")]
    public bool showDebugLogs = true;

    void Start()
    {
        // Auto-detect button if not assigned
        if (targetButton == null)
        {
            targetButton = GetComponent<Button>();
        }

        if (targetButton == null)
        {
            Debug.LogError("[TeleportOnButtonClick] Aucun Button trouvé ! Assignez-en un ou attachez ce script à un Button.");
            enabled = false;
            return;
        }

        // Subscribe to button click
        targetButton.onClick.AddListener(OnButtonClicked);

        if (showDebugLogs)
            Debug.Log($"[TeleportOnButtonClick] Initialisé sur '{gameObject.name}'");
    }

    void OnButtonClicked()
    {
        if (teleportDelay > 0)
        {
            StartCoroutine(TeleportAfterDelay());
        }
        else
        {
            ExecuteTeleport();
        }
    }

    System.Collections.IEnumerator TeleportAfterDelay()
    {
        yield return new WaitForSeconds(teleportDelay);
        ExecuteTeleport();
    }

    void ExecuteTeleport()
    {
        // Determine destination
        Vector3 destination;
        Transform destTransform;

        if (useOwnPositionAsDestination)
        {
            destination = transform.position;
            destTransform = transform;
        }
        else if (destinationPoint != null)
        {
            destination = destinationPoint.position;
            destTransform = destinationPoint;
        }
        else
        {
            Debug.LogError("[TeleportOnButtonClick] Pas de destination définie !");
            return;
        }

        // Check if in Desktop or VR mode
        bool isDesktopMode = VRGameManager.Instance != null && VRGameManager.Instance.IsDesktopMode;

        if (isDesktopMode)
        {
            TeleportDesktopPlayer(destination, destTransform);
        }
        else
        {
            TeleportVRPlayer(destination, destTransform);
        }

        // Change room type if configured
        if (changeRoomType && VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.TeleportToRoomType(targetRoomType);
            if (showDebugLogs)
                Debug.Log($"[TeleportOnButtonClick] Room changée vers {targetRoomType}");
        }
    }

    void TeleportDesktopPlayer(Vector3 destination, Transform destTransform)
    {
        // Find the local Desktop player
        GameObject localPlayer = VRGameManager.Instance?.GetLocalPlayer();
        if (localPlayer == null)
        {
            Debug.LogError("[TeleportOnButtonClick] Joueur local non trouvé !");
            return;
        }

        var charController = localPlayer.GetComponent<CharacterController>();

        // Disable CharacterController temporarily for teleport
        if (charController != null)
        {
            charController.enabled = false;
        }

        // Apply rotation
        if (applyRotation && destTransform != null)
        {
            localPlayer.transform.rotation = Quaternion.Euler(0, destTransform.eulerAngles.y, 0);
        }

        // Apply position
        localPlayer.transform.position = destination;

        // Re-enable CharacterController
        if (charController != null)
        {
            charController.enabled = true;
        }

        if (showDebugLogs)
            Debug.Log($"[TeleportOnButtonClick] Desktop téléporté vers {destination}");
    }

    void TeleportVRPlayer(Vector3 destination, Transform destTransform)
    {
        // Find XR Origin
        var origin = FindFirstObjectByType<XROrigin>();
        if (origin == null)
        {
            // Fallback: try to find local player via VRGameManager
            GameObject localPlayer = VRGameManager.Instance?.GetLocalPlayer();
            if (localPlayer != null)
            {
                origin = localPlayer.GetComponent<XROrigin>();
                if (origin == null)
                    origin = localPlayer.GetComponentInChildren<XROrigin>();
            }
        }

        if (origin == null)
        {
            Debug.LogError("[TeleportOnButtonClick] XR Origin non trouvé !");
            return;
        }

        // Disable CharacterController if present
        var charController = origin.GetComponent<CharacterController>();
        if (charController != null)
        {
            charController.enabled = false;
        }

        Camera cam = origin.Camera;
        if (cam == null)
        {
            cam = Camera.main;
        }

        // Apply rotation
        if (applyRotation && destTransform != null && cam != null)
        {
            // Calculate camera offset from XR Origin
            float cameraYaw = cam.transform.eulerAngles.y;
            float originYaw = origin.transform.eulerAngles.y;
            float cameraOffsetYaw = cameraYaw - originYaw;

            // Target rotation = destination rotation - camera offset
            float targetYaw = destTransform.eulerAngles.y - cameraOffsetYaw;
            origin.transform.rotation = Quaternion.Euler(0, targetYaw, 0);
        }

        // Apply position (accounting for camera offset)
        if (cam != null)
        {
            Vector3 cameraOffset = cam.transform.position - origin.transform.position;
            cameraOffset.y = 0;
            origin.transform.position = destination - cameraOffset;
        }
        else
        {
            origin.transform.position = destination;
        }

        // Re-enable CharacterController
        if (charController != null)
        {
            charController.enabled = true;
        }

        if (showDebugLogs)
            Debug.Log($"[TeleportOnButtonClick] VR téléporté vers {destination}");
    }

    void OnDestroy()
    {
        if (targetButton != null)
        {
            targetButton.onClick.RemoveListener(OnButtonClicked);
        }
    }

    // Editor helper: visualize destination in Scene view
    void OnDrawGizmosSelected()
    {
        Vector3 dest = useOwnPositionAsDestination ? transform.position :
                       (destinationPoint != null ? destinationPoint.position : Vector3.zero);

        if (dest != Vector3.zero)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(dest, 0.5f);
            Gizmos.DrawLine(transform.position, dest);

            // Draw forward direction
            Transform destT = useOwnPositionAsDestination ? transform : destinationPoint;
            if (destT != null && applyRotation)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(dest, destT.forward * 1.5f);
            }
        }
    }
}
