using System;
using UnityEngine;

/// <summary>
/// Manages user authentication via WebSocket.
/// Singleton that persists across scenes.
/// </summary>
public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    // Events
    public static event Action<AuthResult> OnLoginSuccess;
    public static event Action<AuthResult> OnRegisterSuccess;
    public static event Action<string> OnAuthError;
    public static event Action OnLogout;

    // State
    public bool IsAuthenticated { get; private set; }
    public string UserId { get; private set; }
    public string DisplayName { get; private set; }
    public string Token { get; private set; }
    public string AvatarConfig { get; private set; }

    private const string TOKEN_KEY = "auth_token";
    private const string USERID_KEY = "auth_userid";
    private const string DISPLAYNAME_KEY = "auth_displayname";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSavedAuth();
    }

    private void OnEnable()
    {
        VRNetworkManager.OnMessageReceived += HandleNetworkMessage;
        VRNetworkManager.OnConnected += OnNetworkConnected;
    }

    private void OnDisable()
    {
        VRNetworkManager.OnMessageReceived -= HandleNetworkMessage;
        VRNetworkManager.OnConnected -= OnNetworkConnected;
    }

    private void OnNetworkConnected()
    {
        // Auto-verify token if we have one saved
        if (!string.IsNullOrEmpty(Token))
        {
            VerifyToken();
        }
    }

    #region Public API

    public void Register(string email, string password, string displayName)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(displayName))
        {
            OnAuthError?.Invoke("All fields are required");
            return;
        }

        var data = new AuthRegisterData
        {
            email = email,
            password = password,
            displayName = displayName
        };

        SendAuthMessage("auth-register", JsonUtility.ToJson(data));
        Debug.Log("[Auth] Register request sent");
    }

    public void Login(string email, string password)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            OnAuthError?.Invoke("Email and password are required");
            return;
        }

        var data = new AuthLoginData
        {
            email = email,
            password = password
        };

        SendAuthMessage("auth-login", JsonUtility.ToJson(data));
        Debug.Log("[Auth] Login request sent");
    }

    public void Logout()
    {
        SendAuthMessage("auth-logout", "{}");

        ClearAuth();
        OnLogout?.Invoke();

        Debug.Log("[Auth] Logged out");
    }

    public void VerifyToken()
    {
        if (string.IsNullOrEmpty(Token))
        {
            IsAuthenticated = false;
            return;
        }

        var data = new AuthVerifyData { token = Token };
        SendAuthMessage("auth-verify", JsonUtility.ToJson(data));
        Debug.Log("[Auth] Verifying token...");
    }

    #endregion

    #region Message Handling

    private void HandleNetworkMessage(NetworkMessage message)
    {
        if (message.type != "auth-response") return;

        try
        {
            var response = JsonUtility.FromJson<AuthResponse>(message.data);
            HandleAuthResponse(response);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] Failed to parse response: {e.Message}");
        }
    }

    private void HandleAuthResponse(AuthResponse response)
    {
        switch (response.action)
        {
            case "register":
                if (response.success)
                {
                    SetAuth(response.userId, response.displayName, response.token, null);
                    OnRegisterSuccess?.Invoke(new AuthResult
                    {
                        UserId = response.userId,
                        DisplayName = response.displayName
                    });
                    Debug.Log($"[Auth] Registered successfully as {response.displayName}");
                }
                else
                {
                    OnAuthError?.Invoke(response.error ?? "Registration failed");
                    Debug.LogWarning($"[Auth] Register failed: {response.error}");
                }
                break;

            case "login":
                if (response.success)
                {
                    SetAuth(response.userId, response.displayName, response.token, response.avatarConfig);
                    OnLoginSuccess?.Invoke(new AuthResult
                    {
                        UserId = response.userId,
                        DisplayName = response.displayName,
                        AvatarConfig = response.avatarConfig
                    });
                    Debug.Log($"[Auth] Logged in as {response.displayName}");
                }
                else
                {
                    OnAuthError?.Invoke(response.error ?? "Login failed");
                    Debug.LogWarning($"[Auth] Login failed: {response.error}");
                }
                break;

            case "verify":
                if (response.success)
                {
                    IsAuthenticated = true;
                    UserId = response.userId;
                    Debug.Log("[Auth] Token verified");
                }
                else
                {
                    ClearAuth();
                    Debug.LogWarning("[Auth] Token invalid, cleared");
                }
                break;

            case "logout":
                ClearAuth();
                Debug.Log("[Auth] Logout confirmed");
                break;
        }
    }

    #endregion

    #region Helpers

    private void SendAuthMessage(string type, string data)
    {
        if (VRNetworkManager.Instance == null)
        {
            OnAuthError?.Invoke("Not connected to server");
            return;
        }

        VRNetworkManager.Instance.Send(type, data);
    }

    private void SetAuth(string userId, string displayName, string token, string avatarConfig)
    {
        IsAuthenticated = true;
        UserId = userId;
        DisplayName = displayName;
        Token = token;
        AvatarConfig = avatarConfig;

        // Save for auto-login
        PlayerPrefs.SetString(TOKEN_KEY, token ?? "");
        PlayerPrefs.SetString(USERID_KEY, userId ?? "");
        PlayerPrefs.SetString(DISPLAYNAME_KEY, displayName ?? "");
        PlayerPrefs.Save();
    }

    private void ClearAuth()
    {
        IsAuthenticated = false;
        UserId = null;
        DisplayName = null;
        Token = null;
        AvatarConfig = null;

        PlayerPrefs.DeleteKey(TOKEN_KEY);
        PlayerPrefs.DeleteKey(USERID_KEY);
        PlayerPrefs.DeleteKey(DISPLAYNAME_KEY);
        PlayerPrefs.Save();
    }

    private void LoadSavedAuth()
    {
        Token = PlayerPrefs.GetString(TOKEN_KEY, null);
        UserId = PlayerPrefs.GetString(USERID_KEY, null);
        DisplayName = PlayerPrefs.GetString(DISPLAYNAME_KEY, null);

        if (!string.IsNullOrEmpty(Token))
        {
            Debug.Log("[Auth] Found saved token, will verify on connect");
        }
    }

    #endregion
}

#region Data Classes

[Serializable]
public class AuthRegisterData
{
    public string email;
    public string password;
    public string displayName;
}

[Serializable]
public class AuthLoginData
{
    public string email;
    public string password;
}

[Serializable]
public class AuthVerifyData
{
    public string token;
}

[Serializable]
public class AuthResponse
{
    public string action;
    public bool success;
    public string userId;
    public string displayName;
    public string avatarConfig;
    public string token;
    public string error;
}

public class AuthResult
{
    public string UserId;
    public string DisplayName;
    public string AvatarConfig;
}

#endregion
