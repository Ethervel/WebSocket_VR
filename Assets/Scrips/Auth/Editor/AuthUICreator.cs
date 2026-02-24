#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor tool to create Auth UI automatically.
/// Menu: Tools > VR Meeting > Create Auth UI
/// </summary>
public class AuthUICreator : EditorWindow
{
    private Canvas targetCanvas;
    private bool createAsPrefab = true;
    private string prefabPath = "Assets/Prefabs/UI/AuthPanel.prefab";

    [MenuItem("Tools/VR Meeting/Create Auth UI")]
    public static void ShowWindow()
    {
        GetWindow<AuthUICreator>("Auth UI Creator");
    }

    void OnGUI()
    {
        GUILayout.Label("Create Authentication UI", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        targetCanvas = (Canvas)EditorGUILayout.ObjectField("Target Canvas", targetCanvas, typeof(Canvas), true);

        EditorGUILayout.Space();
        createAsPrefab = EditorGUILayout.Toggle("Save as Prefab", createAsPrefab);

        if (createAsPrefab)
        {
            prefabPath = EditorGUILayout.TextField("Prefab Path", prefabPath);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This will create:\n" +
            "- AuthPanel with LoginPanel and RegisterPanel\n" +
            "- All input fields, buttons, and error texts\n" +
            "- AuthUI script with all references assigned\n\n" +
            "You can modify the design after creation.",
            MessageType.Info);

        EditorGUILayout.Space();

        GUI.enabled = targetCanvas != null;
        if (GUILayout.Button("Create Auth UI", GUILayout.Height(40)))
        {
            CreateAuthUI();
        }
        GUI.enabled = true;

        EditorGUILayout.Space();

        if (GUILayout.Button("Find Canvas in Scene"))
        {
            targetCanvas = FindFirstObjectByType<Canvas>();
            if (targetCanvas != null)
            {
                Debug.Log($"[AuthUICreator] Found canvas: {targetCanvas.name}");
            }
        }
    }

    void CreateAuthUI()
    {
        Undo.SetCurrentGroupName("Create Auth UI");
        int undoGroup = Undo.GetCurrentGroup();

        // Create main AuthPanel
        GameObject authPanel = CreatePanel("AuthPanel", targetCanvas.transform);
        SetFullStretch(authPanel.GetComponent<RectTransform>());

        // Add semi-transparent background
        Image authBg = authPanel.GetComponent<Image>();
        authBg.color = new Color(0, 0, 0, 0.8f);

        // Create container for centering
        GameObject container = CreatePanel("Container", authPanel.transform);
        RectTransform containerRect = container.GetComponent<RectTransform>();
        containerRect.sizeDelta = new Vector2(400, 500);
        container.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);

        // Add rounded corners effect (optional - via outline)
        Outline containerOutline = Undo.AddComponent<Outline>(container);
        containerOutline.effectColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        containerOutline.effectDistance = new Vector2(2, 2);

        // ===== LOGIN PANEL =====
        GameObject loginPanel = CreatePanel("LoginPanel", container.transform);
        SetFullStretch(loginPanel.GetComponent<RectTransform>());
        loginPanel.GetComponent<Image>().color = Color.clear;

        // Add vertical layout
        VerticalLayoutGroup loginLayout = Undo.AddComponent<VerticalLayoutGroup>(loginPanel);
        loginLayout.padding = new RectOffset(30, 30, 40, 30);
        loginLayout.spacing = 15;
        loginLayout.childAlignment = TextAnchor.UpperCenter;
        loginLayout.childControlWidth = true;
        loginLayout.childControlHeight = false;
        loginLayout.childForceExpandWidth = true;
        loginLayout.childForceExpandHeight = false;

        // Login Title
        GameObject loginTitle = CreateText("Title", loginPanel.transform, "Login", 32, FontStyles.Bold);
        SetLayoutHeight(loginTitle, 50);

        // Login Email
        GameObject loginEmail = CreateInputField("EmailField", loginPanel.transform, "Email", TMP_InputField.ContentType.EmailAddress);
        SetLayoutHeight(loginEmail, 50);

        // Login Password
        GameObject loginPassword = CreateInputField("PasswordField", loginPanel.transform, "Password", TMP_InputField.ContentType.Password);
        SetLayoutHeight(loginPassword, 50);

        // Login Error Text
        GameObject loginError = CreateText("ErrorText", loginPanel.transform, "", 14, FontStyles.Normal);
        TextMeshProUGUI loginErrorTmp = loginError.GetComponent<TextMeshProUGUI>();
        loginErrorTmp.color = new Color(1f, 0.3f, 0.3f, 1f);
        loginErrorTmp.alignment = TextAlignmentOptions.Center;
        SetLayoutHeight(loginError, 25);
        loginError.SetActive(false);

        // Spacer
        GameObject loginSpacer = new GameObject("Spacer");
        loginSpacer.transform.SetParent(loginPanel.transform, false);
        Undo.RegisterCreatedObjectUndo(loginSpacer, "Create Spacer");
        LayoutElement loginSpacerLayout = Undo.AddComponent<LayoutElement>(loginSpacer);
        loginSpacerLayout.preferredHeight = 10;

        // Login Button
        GameObject loginBtn = CreateButton("LoginButton", loginPanel.transform, "Login", new Color(0.2f, 0.6f, 0.2f, 1f));
        SetLayoutHeight(loginBtn, 50);

        // Guest Button
        GameObject guestBtn = CreateButton("GuestButton", loginPanel.transform, "Play as Guest", new Color(0.4f, 0.4f, 0.4f, 1f));
        SetLayoutHeight(guestBtn, 45);

        // Go to Register Button
        GameObject goToRegisterBtn = CreateButton("GoToRegisterButton", loginPanel.transform, "Create Account", Color.clear);
        TextMeshProUGUI goToRegisterText = goToRegisterBtn.GetComponentInChildren<TextMeshProUGUI>();
        goToRegisterText.color = new Color(0.5f, 0.7f, 1f, 1f);
        goToRegisterText.fontSize = 16;
        SetLayoutHeight(goToRegisterBtn, 35);

        // ===== REGISTER PANEL =====
        GameObject registerPanel = CreatePanel("RegisterPanel", container.transform);
        SetFullStretch(registerPanel.GetComponent<RectTransform>());
        registerPanel.GetComponent<Image>().color = Color.clear;
        registerPanel.SetActive(false); // Hidden by default

        // Add vertical layout
        VerticalLayoutGroup registerLayout = Undo.AddComponent<VerticalLayoutGroup>(registerPanel);
        registerLayout.padding = new RectOffset(30, 30, 30, 30);
        registerLayout.spacing = 12;
        registerLayout.childAlignment = TextAnchor.UpperCenter;
        registerLayout.childControlWidth = true;
        registerLayout.childControlHeight = false;
        registerLayout.childForceExpandWidth = true;
        registerLayout.childForceExpandHeight = false;

        // Register Title
        GameObject registerTitle = CreateText("Title", registerPanel.transform, "Create Account", 28, FontStyles.Bold);
        SetLayoutHeight(registerTitle, 45);

        // Register Display Name
        GameObject registerDisplayName = CreateInputField("DisplayNameField", registerPanel.transform, "Display Name", TMP_InputField.ContentType.Standard);
        SetLayoutHeight(registerDisplayName, 45);

        // Register Email
        GameObject registerEmail = CreateInputField("EmailField", registerPanel.transform, "Email", TMP_InputField.ContentType.EmailAddress);
        SetLayoutHeight(registerEmail, 45);

        // Register Password
        GameObject registerPassword = CreateInputField("PasswordField", registerPanel.transform, "Password", TMP_InputField.ContentType.Password);
        SetLayoutHeight(registerPassword, 45);

        // Register Confirm Password
        GameObject registerConfirmPassword = CreateInputField("ConfirmPasswordField", registerPanel.transform, "Confirm Password", TMP_InputField.ContentType.Password);
        SetLayoutHeight(registerConfirmPassword, 45);

        // Register Error Text
        GameObject registerError = CreateText("ErrorText", registerPanel.transform, "", 14, FontStyles.Normal);
        TextMeshProUGUI registerErrorTmp = registerError.GetComponent<TextMeshProUGUI>();
        registerErrorTmp.color = new Color(1f, 0.3f, 0.3f, 1f);
        registerErrorTmp.alignment = TextAlignmentOptions.Center;
        SetLayoutHeight(registerError, 25);
        registerError.SetActive(false);

        // Register Button
        GameObject registerBtn = CreateButton("RegisterButton", registerPanel.transform, "Create Account", new Color(0.2f, 0.5f, 0.7f, 1f));
        SetLayoutHeight(registerBtn, 50);

        // Go to Login Button
        GameObject goToLoginBtn = CreateButton("GoToLoginButton", registerPanel.transform, "Back to Login", Color.clear);
        TextMeshProUGUI goToLoginText = goToLoginBtn.GetComponentInChildren<TextMeshProUGUI>();
        goToLoginText.color = new Color(0.5f, 0.7f, 1f, 1f);
        goToLoginText.fontSize = 16;
        SetLayoutHeight(goToLoginBtn, 35);

        // ===== ADD AUTH UI SCRIPT =====
        AuthUI authUI = Undo.AddComponent<AuthUI>(authPanel);

        // Assign references
        authUI.authPanel = authPanel;
        authUI.loginPanel = loginPanel;
        authUI.registerPanel = registerPanel;

        authUI.loginEmailField = loginEmail.GetComponent<TMP_InputField>();
        authUI.loginPasswordField = loginPassword.GetComponent<TMP_InputField>();
        authUI.loginButton = loginBtn.GetComponent<Button>();
        authUI.goToRegisterButton = goToRegisterBtn.GetComponent<Button>();
        authUI.loginErrorText = loginErrorTmp;

        authUI.registerEmailField = registerEmail.GetComponent<TMP_InputField>();
        authUI.registerPasswordField = registerPassword.GetComponent<TMP_InputField>();
        authUI.registerConfirmPasswordField = registerConfirmPassword.GetComponent<TMP_InputField>();
        authUI.registerDisplayNameField = registerDisplayName.GetComponent<TMP_InputField>();
        authUI.registerButton = registerBtn.GetComponent<Button>();
        authUI.goToLoginButton = goToLoginBtn.GetComponent<Button>();
        authUI.registerErrorText = registerErrorTmp;

        authUI.guestButton = guestBtn.GetComponent<Button>();

        // Save as prefab if requested
        if (createAsPrefab)
        {
            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(prefabPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            PrefabUtility.SaveAsPrefabAsset(authPanel, prefabPath);
            Debug.Log($"[AuthUICreator] Prefab saved to: {prefabPath}");
        }

        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = authPanel;
        Debug.Log("[AuthUICreator] Auth UI created successfully!");
        EditorUtility.DisplayDialog("Success", "Auth UI created!\n\nYou can now modify the design as needed.", "OK");
    }

    #region UI Creation Helpers

    GameObject CreatePanel(string name, Transform parent)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        Image image = panel.AddComponent<Image>();
        image.color = Color.white;

        Undo.RegisterCreatedObjectUndo(panel, "Create Panel");
        return panel;
    }

    GameObject CreateText(string name, Transform parent, string text, int fontSize, FontStyles style)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        RectTransform rect = textObj.AddComponent<RectTransform>();
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();

        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        Undo.RegisterCreatedObjectUndo(textObj, "Create Text");
        return textObj;
    }

    GameObject CreateInputField(string name, Transform parent, string placeholder, TMP_InputField.ContentType contentType)
    {
        GameObject inputObj = new GameObject(name);
        inputObj.transform.SetParent(parent, false);

        RectTransform rect = inputObj.AddComponent<RectTransform>();
        Image bg = inputObj.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 1f);

        TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();
        inputField.contentType = contentType;

        // Text Area
        GameObject textArea = new GameObject("Text Area");
        textArea.transform.SetParent(inputObj.transform, false);
        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        SetFullStretch(textAreaRect);
        textAreaRect.offsetMin = new Vector2(10, 5);
        textAreaRect.offsetMax = new Vector2(-10, -5);
        RectMask2D mask = textArea.AddComponent<RectMask2D>();

        // Placeholder
        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        RectTransform placeholderRect = placeholderObj.AddComponent<RectTransform>();
        SetFullStretch(placeholderRect);
        TextMeshProUGUI placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 16;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        placeholderText.alignment = TextAlignmentOptions.Left;
        placeholderText.verticalAlignment = VerticalAlignmentOptions.Middle;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        SetFullStretch(textRect);
        TextMeshProUGUI inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 16;
        inputText.color = Color.white;
        inputText.alignment = TextAlignmentOptions.Left;
        inputText.verticalAlignment = VerticalAlignmentOptions.Middle;

        // Assign to input field
        inputField.textViewport = textAreaRect;
        inputField.textComponent = inputText;
        inputField.placeholder = placeholderText;

        Undo.RegisterCreatedObjectUndo(inputObj, "Create Input Field");
        return inputObj;
    }

    GameObject CreateButton(string name, Transform parent, string text, Color bgColor)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        RectTransform rect = btnObj.AddComponent<RectTransform>();
        Image bg = btnObj.AddComponent<Image>();
        bg.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.highlightedColor = new Color(bgColor.r + 0.1f, bgColor.g + 0.1f, bgColor.b + 0.1f, bgColor.a);
        colors.pressedColor = new Color(bgColor.r - 0.1f, bgColor.g - 0.1f, bgColor.b - 0.1f, bgColor.a);
        btn.colors = colors;

        // Button text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        SetFullStretch(textRect);
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 18;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        Undo.RegisterCreatedObjectUndo(btnObj, "Create Button");
        return btnObj;
    }

    void SetFullStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void SetLayoutHeight(GameObject obj, float height)
    {
        LayoutElement layout = obj.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    #endregion
}
#endif
