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
            Debug.LogError("[TeleportOnGrab] XRGrabInteractable manquant !");
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
            Debug.LogWarning("[TeleportOnGrab] Pas de destination définie!");
            yield break;
        }
        
        TeleportPlayer(destination, destTransform);
        
        // Forcer le lâcher
        var interactor = args.interactorObject as IXRSelectInteractor;
        if (interactor != null && grabInteractable.isSelected && 
            grabInteractable.interactorsSelecting.Contains(interactor))
        {
            grabInteractable.interactionManager.SelectExit(interactor, grabInteractable);
        }
    }
    
    void TeleportPlayer(Vector3 destination, Transform destTransform)
    {
        // Chercher le XR Origin
        var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin == null)
        {
            Debug.LogError("[TeleportOnGrab] XR Origin non trouvé !");
            return;
        }
        
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[TeleportOnGrab] Camera non trouvée !");
            origin.transform.position = destination;
            return;
        }
        
        // === ROTATION ===
        if (applyRotation && destTransform != null)
        {
            // Rotation actuelle de la caméra par rapport au XR Origin
            float cameraYaw = cam.transform.eulerAngles.y;
            float originYaw = origin.transform.eulerAngles.y;
            float cameraOffsetYaw = cameraYaw - originYaw;
            
            // Rotation cible = rotation du destination point - offset de la caméra
            float targetYaw = destTransform.eulerAngles.y - cameraOffsetYaw;
            origin.transform.rotation = Quaternion.Euler(0, targetYaw, 0);
        }
        
        // === POSITION ===
        // Recalculer l'offset après la rotation
        Vector3 cameraOffset = cam.transform.position - origin.transform.position;
        cameraOffset.y = 0;
        
        origin.transform.position = destination - cameraOffset;
        
        Debug.Log($"[TeleportOnGrab] Téléporté vers {destination}, rotation: {destTransform?.eulerAngles.y ?? 0}°");
    }
    
    void OnDestroy()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrab);
        }
    }
}