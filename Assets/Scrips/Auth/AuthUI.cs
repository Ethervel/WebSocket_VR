using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI for login/register. Integrates with MainMenuManager.
/// Shows login panel before main menu if not authenticated.
/// </summary>
public class AuthUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject authPanel;
    public GameObject loginPanel;
    public GameObject registerPanel;

    [Header("Login Fields")]
    public TMP_InputField loginEmailField;
    public TMP_InputField loginPasswordField;
    public Button loginButton;
    public Button goToRegisterButton;
    public TextMeshProUGUI loginErrorText;

    [Header("Register Fields")]
    public TMP_InputField registerEmailField;
    public TMP_InputField registerPasswordField;
    public TMP_InputField registerConfirmPasswordField;
    public TMP_InputField registerDisplayNameField;
    public Button registerButton;
    public Button goToLoginButton;
    public TextMeshProUGUI registerErrorText;

    [Header("Loading")]
    public GameObject loadingIndicator;

    [Header("Settings")]
    public bool skipAuthInEditor = false;
    public bool allowGuestMode = true;

    [Header("Guest Mode")]
    public Button guestButton;

    private bool _isProcessing = false;

    void Awake()
    {
        SetupButtons();
        HideAllErrors();
    }

    void OnEnable()
    {
        AuthManager.OnLoginSuccess += OnLoginSuccess;
        AuthManager.OnRegisterSuccess += OnRegisterSuccess;
        AuthManager.OnAuthError += OnAuthError;
    }

    void OnDisable()
    {
        AuthManager.OnLoginSuccess -= OnLoginSuccess;
        AuthManager.OnRegisterSuccess -= OnRegisterSuccess;
        AuthManager.OnAuthError -= OnAuthError;
    }

    void Start()
    {
        // Skip auth in editor if configured
#if UNITY_EDITOR
        if (skipAuthInEditor)
        {
            Debug.Log("[AuthUI] Skipping auth in editor");
            HideAuthPanel();
            return;
        }
#endif

        // Check if already authenticated
        if (AuthManager.Instance != null && AuthManager.Instance.IsAuthenticated)
        {
            Debug.Log("[AuthUI] Already authenticated");
            HideAuthPanel();
            return;
        }

        // Show login panel
        ShowLoginPanel();
    }

    void SetupButtons()
    {
        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginClicked);

        if (registerButton != null)
            registerButton.onClick.AddListener(OnRegisterClicked);

        if (goToRegisterButton != null)
            goToRegisterButton.onClick.AddListener(ShowRegisterPanel);

        if (goToLoginButton != null)
            goToLoginButton.onClick.AddListener(ShowLoginPanel);

        if (guestButton != null)
        {
            guestButton.gameObject.SetActive(allowGuestMode);
            guestButton.onClick.AddListener(OnGuestClicked);
        }
    }

    #region Panel Navigation

    public void ShowLoginPanel()
    {
        if (authPanel != null) authPanel.SetActive(true);
        if (loginPanel != null) loginPanel.SetActive(true);
        if (registerPanel != null) registerPanel.SetActive(false);
        HideAllErrors();
        HideLoading();

        // Hide main menu panel while showing auth
        if (MainMenuManager.Instance != null && MainMenuManager.Instance.mainPanel != null)
        {
            MainMenuManager.Instance.mainPanel.SetActive(false);
        }
    }

    public void ShowRegisterPanel()
    {
        if (authPanel != null) authPanel.SetActive(true);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(true);
        HideAllErrors();
        HideLoading();

        // Setup XR keyboard for register fields (they were inactive at start)
        SetupXRKeyboardForRegisterFields();
    }

    void SetupXRKeyboardForRegisterFields()
    {
        var keyboardBinder = FindFirstObjectByType<GlobalKeyboardAutoBind>();
        if (keyboardBinder == null) return;

        if (registerEmailField != null)
            keyboardBinder.SetupInputField(registerEmailField);
        if (registerPasswordField != null)
            keyboardBinder.SetupInputField(registerPasswordField);
        if (registerConfirmPasswordField != null)
            keyboardBinder.SetupInputField(registerConfirmPasswordField);
        if (registerDisplayNameField != null)
            keyboardBinder.SetupInputField(registerDisplayNameField);
    }

    void HideAuthPanel()
    {
        if (authPanel != null) authPanel.SetActive(false);

        // Show main menu panel
        if (MainMenuManager.Instance != null && MainMenuManager.Instance.mainPanel != null)
        {
            MainMenuManager.Instance.mainPanel.SetActive(true);
        }
    }

    void HideAllErrors()
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = "";
            loginErrorText.gameObject.SetActive(false);
        }
        if (registerErrorText != null)
        {
            registerErrorText.text = "";
            registerErrorText.gameObject.SetActive(false);
        }
    }

    void ShowLoading()
    {
        if (loadingIndicator != null)
            loadingIndicator.SetActive(true);

        if (loginButton != null) loginButton.interactable = false;
        if (registerButton != null) registerButton.interactable = false;
    }

    void HideLoading()
    {
        if (loadingIndicator != null)
            loadingIndicator.SetActive(false);

        if (loginButton != null) loginButton.interactable = true;
        if (registerButton != null) registerButton.interactable = true;
    }

    void ShowLoginError(string message)
    {
        if (loginErrorText != null)
        {
            loginErrorText.text = message;
            loginErrorText.gameObject.SetActive(true);
        }
    }

    void ShowRegisterError(string message)
    {
        if (registerErrorText != null)
        {
            registerErrorText.text = message;
            registerErrorText.gameObject.SetActive(true);
        }
    }

    #endregion

    #region Button Handlers

    void OnLoginClicked()
    {
        if (_isProcessing) return;

        string email = loginEmailField != null ? loginEmailField.text.Trim() : "";
        string password = loginPasswordField != null ? loginPasswordField.text : "";

        // Validation
        if (string.IsNullOrEmpty(email))
        {
            ShowLoginError("Email is required");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowLoginError("Password is required");
            return;
        }

        if (!IsValidEmail(email))
        {
            ShowLoginError("Invalid email format");
            return;
        }

        _isProcessing = true;
        HideAllErrors();
        ShowLoading();

        AuthManager.Instance?.Login(email, password);

        // Timeout after 10 seconds
        StartCoroutine(AuthTimeout());
    }

    void OnRegisterClicked()
    {
        if (_isProcessing) return;

        string email = registerEmailField != null ? registerEmailField.text.Trim() : "";
        string password = registerPasswordField != null ? registerPasswordField.text : "";
        string confirmPassword = registerConfirmPasswordField != null ? registerConfirmPasswordField.text : "";
        string displayName = registerDisplayNameField != null ? registerDisplayNameField.text.Trim() : "";

        // Validation
        if (string.IsNullOrEmpty(email))
        {
            ShowRegisterError("Email is required");
            return;
        }

        if (!IsValidEmail(email))
        {
            ShowRegisterError("Invalid email format");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowRegisterError("Password is required");
            return;
        }

        if (password.Length < 8)
        {
            ShowRegisterError("Password must be at least 8 characters");
            return;
        }

        if (password != confirmPassword)
        {
            ShowRegisterError("Passwords do not match");
            return;
        }

        if (string.IsNullOrEmpty(displayName))
        {
            ShowRegisterError("Display name is required");
            return;
        }

        if (displayName.Length < 2 || displayName.Length > 50)
        {
            ShowRegisterError("Display name must be 2-50 characters");
            return;
        }

        _isProcessing = true;
        HideAllErrors();
        ShowLoading();

        AuthManager.Instance?.Register(email, password, displayName);

        StartCoroutine(AuthTimeout());
    }

    void OnGuestClicked()
    {
        Debug.Log("[AuthUI] Guest mode selected");
        HideAuthPanel();
    }

    IEnumerator AuthTimeout()
    {
        yield return new WaitForSeconds(10f);

        if (_isProcessing)
        {
            _isProcessing = false;
            HideLoading();

            if (loginPanel != null && loginPanel.activeSelf)
            {
                ShowLoginError("Connection timeout. Please try again.");
            }
            else if (registerPanel != null && registerPanel.activeSelf)
            {
                ShowRegisterError("Connection timeout. Please try again.");
            }
        }
    }

    #endregion

    #region Auth Callbacks

    void OnLoginSuccess(AuthResult result)
    {
        _isProcessing = false;
        HideLoading();
        Debug.Log($"[AuthUI] Login success: {result.DisplayName}");

        // Update player name if in a room
        if (VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.SetPlayerName(result.DisplayName);
        }

        HideAuthPanel();
    }

    void OnRegisterSuccess(AuthResult result)
    {
        _isProcessing = false;
        HideLoading();
        Debug.Log($"[AuthUI] Register success: {result.DisplayName}");

        // Update player name
        if (VRRoomManager.Instance != null)
        {
            VRRoomManager.Instance.SetPlayerName(result.DisplayName);
        }

        HideAuthPanel();
    }

    void OnAuthError(string error)
    {
        _isProcessing = false;
        HideLoading();

        if (loginPanel != null && loginPanel.activeSelf)
        {
            ShowLoginError(error);
        }
        else if (registerPanel != null && registerPanel.activeSelf)
        {
            ShowRegisterError(error);
        }

        Debug.LogWarning($"[AuthUI] Auth error: {error}");
    }

    #endregion

    #region Helpers

    bool IsValidEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return false;

        int atIndex = email.IndexOf('@');
        if (atIndex <= 0) return false;

        int dotIndex = email.LastIndexOf('.');
        if (dotIndex <= atIndex + 1) return false;

        if (dotIndex >= email.Length - 1) return false;

        return true;
    }

    #endregion
}
