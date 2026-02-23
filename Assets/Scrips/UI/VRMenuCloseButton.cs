using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Closes the VR Menu when the assigned button is clicked.
/// Assign the button in the Inspector.
/// </summary>
public class VRMenuCloseButton : MonoBehaviour
{
    [Header("Button to Close Menu")]
    [Tooltip("Assign the button that will close the menu")]
    public Button closeButton;

    [Header("References (Auto-found if empty)")]
    [Tooltip("The VRMenuToggle component. Auto-found if not assigned.")]
    public VRMenuToggle menuToggle;

    [Tooltip("The menu GameObject to hide. Used as fallback if VRMenuToggle not found.")]
    public GameObject menuCanvas;

    void Start()
    {
        // Auto-find VRMenuToggle if not assigned
        if (menuToggle == null)
        {
            menuToggle = FindFirstObjectByType<VRMenuToggle>();
        }

        // Setup button listener
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseClicked);
            Debug.Log("[VRMenuCloseButton] Close button connected");
        }
        else
        {
            Debug.LogWarning("[VRMenuCloseButton] No close button assigned!");
        }
    }

    void OnDestroy()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnCloseClicked);
        }
    }

    void OnCloseClicked()
    {
        CloseMenu();
    }

    public void CloseMenu()
    {
        // Method 1: Use VRMenuToggle
        if (menuToggle != null)
        {
            menuToggle.HideMenu();
            Debug.Log("[VRMenuCloseButton] Menu closed via VRMenuToggle");
            return;
        }

        // Method 2: Disable menu canvas directly
        if (menuCanvas != null)
        {
            menuCanvas.SetActive(false);
            Debug.Log("[VRMenuCloseButton] Menu closed via menuCanvas");
            return;
        }

        Debug.LogWarning("[VRMenuCloseButton] Cannot close menu - no reference found!");
    }
}
