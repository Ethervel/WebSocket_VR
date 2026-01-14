using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class TeleportOnGrab : MonoBehaviour
{
    [Header("Destination")]
    [Tooltip("Point de destination (laisser vide = position du pad)")]
    public Transform destinationPoint;

    [Header("Options")]
    public bool useOwnPositionAsDestination = true;
    public float teleportDelay = 0.1f;

    [Header("Rotation")]
    [Tooltip("Appliquer la rotation du point de destination")]
    public bool applyRotation = true;

    private XRGrabInteractable grabInteractable;

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        if (grabInteractable == null)
        {
            Debug.LogError("[TeleportOnGrab] XRGrabInteractable manquant!");
            return;
        }

        grabInteractable.selectEntered.AddListener(OnGrab);
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        StartCoroutine(TeleportAfterDelay(args));
    }

    System.Collections.IEnumerator TeleportAfterDelay(SelectEnterEventArgs args)
    {
        yield return new WaitForSeconds(teleportDelay);

        Vector3 destination;
        Transform destTransform = null;

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
            yield break;
        }

        TeleportPlayer(destination, destTransform);

        var interactor = args.interactorObject as IXRSelectInteractor;
        if (interactor != null && grabInteractable.isSelected &&
            grabInteractable.interactorsSelecting.Contains(interactor))
        {
            grabInteractable.interactionManager.SelectExit(interactor, grabInteractable);
        }
    }

    void TeleportPlayer(Vector3 destination, Transform destTransform)
    {
        var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin == null)
        {
            Debug.LogError("[TeleportOnGrab] XR Origin non trouvé!");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[TeleportOnGrab] Camera non trouvée!");
            origin.transform.position = destination;
            return;
        }

        if (applyRotation && destTransform != null)
        {
            float cameraYaw = cam.transform.eulerAngles.y;
            float originYaw = origin.transform.eulerAngles.y;
            float cameraOffsetYaw = cameraYaw - originYaw;

            float targetYaw = destTransform.eulerAngles.y - cameraOffsetYaw;
            origin.transform.rotation = Quaternion.Euler(0, targetYaw, 0);
        }

        Vector3 cameraOffset = cam.transform.position - origin.transform.position;
        cameraOffset.y = 0;

        origin.transform.position = destination - cameraOffset;
    }

    void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrab);
        }
    }
}
