using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI pour contrôler le Screen Share.
/// Assigne les références UI depuis l'Inspector.
/// </summary>
public class ScreenShareUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Bouton pour démarrer le partage")]
    public Button shareButton;

    [Tooltip("Bouton pour arrêter le partage")]
    public Button stopButton;

    [Tooltip("Texte de statut")]
    public TextMeshProUGUI statusText;

    [Tooltip("Panel racine (optionnel, pour hide/show)")]
    public GameObject panelRoot;

    [Header("Settings")]
    [Tooltip("Cacher le panel quand pas dans une room")]
    public bool hideWhenNotInRoom = false;

    void Start()
    {
        // Setup button listeners
        if (shareButton != null)
            shareButton.onClick.AddListener(OnShareClick);

        if (stopButton != null)
            stopButton.onClick.AddListener(OnStopClick);

        UpdateUI();
    }

    void OnEnable()
    {
        ScreenShareManager.OnScreenShareStarted += OnShareStarted;
        ScreenShareManager.OnScreenShareStopped += OnShareStopped;
    }

    void OnDisable()
    {
        ScreenShareManager.OnScreenShareStarted -= OnShareStarted;
        ScreenShareManager.OnScreenShareStopped -= OnShareStopped;
    }

    void Update()
    {
        UpdateUI();
    }

    void OnShareClick()
    {
        if (ScreenShareManager.Instance != null)
        {
            ScreenShareManager.Instance.StartSharing();
        }
        else
        {
            Debug.LogError("[ScreenShareUI] ScreenShareManager.Instance is null! Use menu: VR Meeting > Setup Sharing System");
            if (statusText != null)
                statusText.text = "Error: Manager missing";
        }
    }

    void OnStopClick()
    {
        if (ScreenShareManager.Instance != null)
        {
            ScreenShareManager.Instance.StopSharing();
        }
    }

    void OnShareStarted(string sharerId, string sharerName)
    {
        UpdateUI();
    }

    void OnShareStopped(string sharerId)
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        var manager = ScreenShareManager.Instance;
        bool isInRoom = VRRoomManager.Instance != null && VRRoomManager.Instance.IsInRoom;
        bool isDesktop = VRGameManager.Instance == null || VRGameManager.Instance.IsDesktopMode;

        // Hide panel if not in room
        if (hideWhenNotInRoom && panelRoot != null)
        {
            panelRoot.SetActive(isInRoom);
        }

        // Update status text
        if (statusText != null)
        {
            if (manager == null)
            {
                statusText.text = "Not initialized";
                statusText.color = Color.red;
            }
            else if (manager.IsSharing)
            {
                statusText.text = "Sharing...";
                statusText.color = Color.green;
            }
            else if (manager.IsReceiving)
            {
                statusText.text = "Receiving";
                statusText.color = Color.cyan;
            }
            else if (!isInRoom)
            {
                statusText.text = "Join a room";
                statusText.color = Color.yellow;
            }
            else if (!isDesktop)
            {
                statusText.text = "Desktop only";
                statusText.color = Color.gray;
            }
            else
            {
                statusText.text = "Ready";
                statusText.color = Color.white;
            }
        }

        // Update buttons visibility
        bool canShare = manager != null && isInRoom && isDesktop && !manager.IsSharing && !manager.IsReceiving;
        bool canStop = manager != null && manager.IsSharing;

        if (shareButton != null)
        {
            shareButton.gameObject.SetActive(!canStop);
            shareButton.interactable = canShare;
        }

        if (stopButton != null)
        {
            stopButton.gameObject.SetActive(canStop);
        }
    }
}
