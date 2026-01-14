using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// Téléporte le joueur quand un bouton UI est cliqué.
/// Fonctionne en mode VR (XR Origin) et Desktop (CharacterController).
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

    void Start()
    {
        if (targetButton == null)
        {
            targetButton = GetComponent<Button>();
        }

        if (targetButton == null)
        {
            Debug.LogError("[TeleportOnButtonClick] Aucun Button trouvé!");
            enabled = false;
            return;
        }

        targetButton.onClick.AddListener(OnButtonClicked);
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
            Debug.LogError("[TeleportOnButtonClick] Pas de destination définie!");
            return;
        }

        bool isDesktopMode = VRGameManager.Instance != null && VRGameManager.Instance.IsDesktopMode;

        if (isDesktopMode)
        {
            TeleportDesktopPlayer(destination, destTransform);
        }
        else
        {
            TeleportVRPlayer(destination, destTransform);
        }

        if (changeRoomType && VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.TeleportToRoomType(targetRoomType);
        }
    }

    void TeleportDesktopPlayer(Vector3 destination, Transform destTransform)
    {
        GameObject localPlayer = VRGameManager.Instance?.GetLocalPlayer();
        if (localPlayer == null)
        {
            Debug.LogError("[TeleportOnButtonClick] Joueur local non trouvé!");
            return;
        }

        var charController = localPlayer.GetComponent<CharacterController>();

        if (charController != null)
        {
            charController.enabled = false;
        }

        if (applyRotation && destTransform != null)
        {
            localPlayer.transform.rotation = Quaternion.Euler(0, destTransform.eulerAngles.y, 0);
        }

        localPlayer.transform.position = destination;

        if (charController != null)
        {
            charController.enabled = true;
        }
    }

    void TeleportVRPlayer(Vector3 destination, Transform destTransform)
    {
        var origin = FindFirstObjectByType<XROrigin>();
        if (origin == null)
        {
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
            Debug.LogError("[TeleportOnButtonClick] XR Origin non trouvé!");
            return;
        }

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

        if (applyRotation && destTransform != null && cam != null)
        {
            float cameraYaw = cam.transform.eulerAngles.y;
            float originYaw = origin.transform.eulerAngles.y;
            float cameraOffsetYaw = cameraYaw - originYaw;

            float targetYaw = destTransform.eulerAngles.y - cameraOffsetYaw;
            origin.transform.rotation = Quaternion.Euler(0, targetYaw, 0);
        }

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

        if (charController != null)
        {
            charController.enabled = true;
        }
    }

    void OnDestroy()
    {
        if (targetButton != null)
        {
            targetButton.onClick.RemoveListener(OnButtonClicked);
        }
    }

    void OnDrawGizmosSelected()
    {
        Vector3 dest = useOwnPositionAsDestination ? transform.position :
                       (destinationPoint != null ? destinationPoint.position : Vector3.zero);

        if (dest != Vector3.zero)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(dest, 0.5f);
            Gizmos.DrawLine(transform.position, dest);

            Transform destT = useOwnPositionAsDestination ? transform : destinationPoint;
            if (destT != null && applyRotation)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(dest, destT.forward * 1.5f);
            }
        }
    }
}
