using System;
using UnityEngine;

/// <summary>
/// Authentication Manager - handles user registration, login, and profile updates
/// Communicates with the Node.js server via WebSocket
/// </summary>
public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    // User data after successful login
    public static bool IsLoggedIn { get; private set; }
    public static int UserId { get; private set; }
    public static string Username { get; private set; }
    public static string Email { get; private set; }
    public static string DisplayName { get; private set; }
    public static string AvatarColor { get; private set; }

    // Events
    public static event Action<AuthResult> OnRegisterResponse;
    public static event Action<AuthResult> OnLoginResponse;
    public static event Action<AuthResult> OnUpdateProfileResponse;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        VRNetworkManager.OnMessageReceived += HandleMessage;
    }

    void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleMessage;
    }

    // ========================================
    // PUBLIC API
    // ========================================

    /// <summary>
    /// Register a new user account
    /// </summary>
    public void Register(string username, string email, string password, string displayName = null)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            Debug.LogError("[Auth] Register: Missing required fields");
            OnRegisterResponse?.Invoke(new AuthResult { success = false, error = "Missing required fields" });
            return;
        }

        var data = new AuthRegisterData
        {
            username = username,
            email = email,
            password = password,
            displayName = displayName ?? username
        };

        Debug.Log($"[Auth] Registering user: {username}");
        VRNetworkManager.Instance.Send("auth-register", data);
    }

    /// <summary>
    /// Login with username/email and password
    /// </summary>
    public void Login(string usernameOrEmail, string password)
    {
        if (string.IsNullOrEmpty(usernameOrEmail) || string.IsNullOrEmpty(password))
        {
            Debug.LogError("[Auth] Login: Missing credentials");
            OnLoginResponse?.Invoke(new AuthResult { success = false, error = "Missing credentials" });
            return;
        }

        var data = new AuthLoginData
        {
            username = usernameOrEmail,
            password = password
        };

        Debug.Log($"[Auth] Logging in: {usernameOrEmail}");
        VRNetworkManager.Instance.Send("auth-login", data);
    }

    /// <summary>
    /// Update the current user's profile
    /// </summary>
    public void UpdateProfile(string newDisplayName, string newAvatarColor = null)
    {
        if (!IsLoggedIn)
        {
            Debug.LogError("[Auth] UpdateProfile: Not logged in");
            OnUpdateProfileResponse?.Invoke(new AuthResult { success = false, error = "Not logged in" });
            return;
        }

        var data = new AuthUpdateProfileData
        {
            displayName = newDisplayName,
            avatarColor = newAvatarColor ?? AvatarColor
        };

        Debug.Log($"[Auth] Updating profile: {newDisplayName}");
        VRNetworkManager.Instance.Send("auth-update-profile", data);
    }

    /// <summary>
    /// Logout the current user (local only, no server call needed)
    /// </summary>
    public void Logout()
    {
        IsLoggedIn = false;
        UserId = 0;
        Username = null;
        Email = null;
        DisplayName = null;
        AvatarColor = null;

        // Clear saved credentials
        PlayerPrefs.DeleteKey("SavedUsername");
        PlayerPrefs.DeleteKey("SavedPassword");
        PlayerPrefs.Save();

        Debug.Log("[Auth] User logged out");
    }

    /// <summary>
    /// Try auto-login with saved credentials
    /// </summary>
    public void TryAutoLogin()
    {
        string savedUsername = PlayerPrefs.GetString("SavedUsername", "");
        string savedPassword = PlayerPrefs.GetString("SavedPassword", "");

        if (!string.IsNullOrEmpty(savedUsername) && !string.IsNullOrEmpty(savedPassword))
        {
            Debug.Log("[Auth] Attempting auto-login...");
            Login(savedUsername, savedPassword);
        }
    }

    // ========================================
    // MESSAGE HANDLING
    // ========================================

    private void HandleMessage(NetworkMessage message)
    {
        switch (message.type)
        {
            case "auth-register-response":
                HandleRegisterResponse(message.data);
                break;

            case "auth-login-response":
                HandleLoginResponse(message.data);
                break;

            case "auth-update-response":
                HandleUpdateProfileResponse(message.data);
                break;
        }
    }

    private void HandleRegisterResponse(string dataJson)
    {
        try
        {
            var result = JsonUtility.FromJson<AuthResult>(dataJson);

            if (result.success)
            {
                Debug.Log($"[Auth] Registration successful: {result.username}");

                // Auto-login after registration
                IsLoggedIn = true;
                UserId = result.userId;
                Username = result.username;
                DisplayName = result.displayName;

                // Update PlayerPrefs for PlayerName display
                PlayerPrefs.SetString("PlayerName", result.displayName);
                PlayerPrefs.Save();
            }
            else
            {
                Debug.LogWarning($"[Auth] Registration failed: {result.error}");
            }

            OnRegisterResponse?.Invoke(result);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] HandleRegisterResponse error: {e.Message}");
        }
    }

    private void HandleLoginResponse(string dataJson)
    {
        try
        {
            var result = JsonUtility.FromJson<AuthResult>(dataJson);

            if (result.success)
            {
                Debug.Log($"[Auth] Login successful: {result.username}");

                IsLoggedIn = true;
                UserId = result.userId;
                Username = result.username;
                Email = result.email;
                DisplayName = result.displayName;
                AvatarColor = result.avatarColor;

                // Update PlayerPrefs for PlayerName display
                PlayerPrefs.SetString("PlayerName", result.displayName);
                PlayerPrefs.Save();
            }
            else
            {
                Debug.LogWarning($"[Auth] Login failed: {result.error}");
            }

            OnLoginResponse?.Invoke(result);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] HandleLoginResponse error: {e.Message}");
        }
    }

    private void HandleUpdateProfileResponse(string dataJson)
    {
        try
        {
            var result = JsonUtility.FromJson<AuthResult>(dataJson);

            if (result.success)
            {
                Debug.Log("[Auth] Profile updated successfully");
            }
            else
            {
                Debug.LogWarning($"[Auth] Profile update failed: {result.error}");
            }

            OnUpdateProfileResponse?.Invoke(result);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] HandleUpdateProfileResponse error: {e.Message}");
        }
    }
}

// ========================================
// DATA CLASSES
// ========================================

[Serializable]
public class AuthRegisterData
{
    public string username;
    public string email;
    public string password;
    public string displayName;
}

[Serializable]
public class AuthLoginData
{
    public string username;
    public string password;
}

[Serializable]
public class AuthUpdateProfileData
{
    public string displayName;
    public string avatarColor;
}

[Serializable]
public class AuthResult
{
    public bool success;
    public string error;
    public int userId;
    public string username;
    public string email;
    public string displayName;
    public string avatarColor;
}
