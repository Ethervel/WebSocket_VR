using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using System.Threading.Tasks;

/// <summary>
/// Exit confirmation dialog - Choose to leave room or quit game.
/// </summary>
public class VRMenuExitDialog : MonoBehaviour
{
    [Header("Dialog Panel")]
    public GameObject dialogPanel;

    [Header("Buttons")]
    public Button exitButton;           // Main exit button in sidebar
    public Button leaveRoomButton;      // Leave room option
    public Button quitGameButton;       // Quit game option
    public Button cancelButton;         // Cancel/close dialog

    [Header("Texts")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI messageText;

    [Header("Appearance")]
    public Color leaveRoomColor = new Color(0.9f, 0.6f, 0.1f, 1f);
    public Color quitGameColor = new Color(0.8f, 0.2f, 0.2f, 1f);

    private bool _isDialogOpen = false;
    private Keyboard _keyboard;

    void Start()
    {
        // Auto-find references if not assigned
        AutoFindReferences();

        // Hide dialog initially
        if (dialogPanel != null)
        {
            dialogPanel.SetActive(false);
        }

        // Setup button listeners
        if (exitButton != null)
        {
            exitButton.onClick.AddListener(ShowDialog);
            Debug.Log("[VRMenuExitDialog] Exit button connected");
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.onClick.AddListener(OnLeaveRoom);
            var colors = leaveRoomButton.colors;
            colors.normalColor = leaveRoomColor;
            colors.highlightedColor = leaveRoomColor * 1.1f;
            colors.pressedColor = leaveRoomColor * 0.9f;
            leaveRoomButton.colors = colors;
            Debug.Log("[VRMenuExitDialog] Leave Room button connected");
        }

        if (quitGameButton != null)
        {
            quitGameButton.onClick.AddListener(OnQuitGame);
            var colors = quitGameButton.colors;
            colors.normalColor = quitGameColor;
            colors.highlightedColor = quitGameColor * 1.1f;
            colors.pressedColor = quitGameColor * 0.9f;
            quitGameButton.colors = colors;
            Debug.Log("[VRMenuExitDialog] Quit Game button connected");
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(HideDialog);
            Debug.Log("[VRMenuExitDialog] Cancel button connected");
        }
    }

    void AutoFindReferences()
    {
        // Find dialog panel
        if (dialogPanel == null)
        {
            dialogPanel = FindChildByName(transform, "ExitDialog")?.gameObject;
            if (dialogPanel == null)
            {
                // Try to find any panel with "Exit" or "Dialog" in name
                foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Contains("ExitDialog") || child.name.Contains("Dialog"))
                    {
                        if (child.GetComponent<Image>() != null)
                        {
                            dialogPanel = child.gameObject;
                            break;
                        }
                    }
                }
            }
        }

        // Find buttons by searching all buttons in hierarchy
        Button[] allButtons = GetComponentsInChildren<Button>(true);
        foreach (Button btn in allButtons)
        {
            string btnName = btn.name.ToLower();

            if (exitButton == null && (btnName.Contains("exit") && !btnName.Contains("leave") && !btnName.Contains("quit")))
            {
                exitButton = btn;
            }
            else if (leaveRoomButton == null && (btnName.Contains("leave") || btnName.Contains("leaveroom")))
            {
                leaveRoomButton = btn;
            }
            else if (quitGameButton == null && (btnName.Contains("quit") || btnName.Contains("quitgame")))
            {
                quitGameButton = btn;
            }
            else if (cancelButton == null && btnName.Contains("cancel"))
            {
                cancelButton = btn;
            }
        }

        // Find texts
        if (dialogPanel != null)
        {
            TMPro.TextMeshProUGUI[] texts = dialogPanel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            foreach (var txt in texts)
            {
                if (titleText == null && txt.name.ToLower().Contains("title"))
                    titleText = txt;
                else if (messageText == null && txt.name.ToLower().Contains("message"))
                    messageText = txt;
            }
        }

        Debug.Log($"[VRMenuExitDialog] AutoFind: dialog={dialogPanel != null}, exit={exitButton != null}, leave={leaveRoomButton != null}, quit={quitGameButton != null}, cancel={cancelButton != null}");
    }

    Transform FindChildByName(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name) return child;
        }
        return null;
    }

    void Update()
    {
        // Close dialog with Escape key (using new Input System)
        if (_keyboard == null)
        {
            _keyboard = Keyboard.current;
        }

        if (_isDialogOpen && _keyboard != null && _keyboard.escapeKey.wasPressedThisFrame)
        {
            HideDialog();
        }
    }

    public void ShowDialog()
    {
        if (dialogPanel == null) return;

        dialogPanel.SetActive(true);
        _isDialogOpen = true;

        // Update message based on room state
        UpdateDialogContent();

        Debug.Log("[VRMenuExitDialog] Dialog opened");
    }

    public void HideDialog()
    {
        if (dialogPanel == null) return;

        dialogPanel.SetActive(false);
        _isDialogOpen = false;

        Debug.Log("[VRMenuExitDialog] Dialog closed");
    }

    void UpdateDialogContent()
    {
        var roomManager = VRRoomManager.Instance;
        bool inRoom = roomManager != null && !string.IsNullOrEmpty(roomManager.CurrentRoomId);

        if (titleText != null)
        {
            titleText.text = "Exit";
        }

        if (messageText != null)
        {
            if (inRoom)
            {
                messageText.text = "What would you like to do?";
            }
            else
            {
                messageText.text = "Are you sure you want to quit?";
            }
        }

        // Disable leave room button if not in a room
        if (leaveRoomButton != null)
        {
            leaveRoomButton.interactable = inRoom;

            var buttonText = leaveRoomButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = inRoom ? "Leave Room" : "Not in Room";
            }
        }
    }

    void OnLeaveRoom()
    {
        Debug.Log("[VRMenuExitDialog] Leaving room...");

        var roomManager = VRRoomManager.Instance;
        if (roomManager != null)
        {
            roomManager.LeaveRoom();
        }

        // Teleport player to lobby spawn point
        var gameManager = VRGameManager.Instance;
        if (gameManager != null)
        {
            gameManager.TeleportLocalPlayer(RoomType.Lobby);
            Debug.Log("[VRMenuExitDialog] Teleported to lobby");
        }

        HideDialog();

        // Also hide the menu
        var menuToggle = FindFirstObjectByType<VRMenuToggle>();
        if (menuToggle != null)
        {
            menuToggle.HideMenu();
        }
    }

    async void OnQuitGame()
    {
        Debug.Log("[VRMenuExitDialog] Quitting game...");

        // Disconnect from server first
        var networkManager = VRNetworkManager.Instance;
        if (networkManager != null)
        {
            await networkManager.Disconnect();
        }

        // Quit application
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public bool IsDialogOpen => _isDialogOpen;
}
