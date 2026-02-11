using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Recording page - Controls for recording meetings.
/// Only host can start/stop, all participants can add markers.
/// </summary>
public class VRMenuPageRecording : MonoBehaviour
{
    [Header("Recording Controls")]
    public Button recordButton;
    public TextMeshProUGUI recordButtonText;
    public GameObject recIndicator;
    public TextMeshProUGUI recTimeText;
    public TextMeshProUGUI recStatusText;

    [Header("Markers")]
    public GameObject markersSection;
    public Button markerImportantButton;
    public Button markerQuestionButton;
    public Button markerTodoButton;
    public Button markerIdeaButton;

    [Header("Info")]
    public TextMeshProUGUI infoText;

    private bool _isSubscribed = false;
    private bool _uiCreated = false;

    void Awake()
    {
        SubscribeToEvents();
    }

    void OnEnable()
    {
        SubscribeToEvents();
        RefreshUI();
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    void SubscribeToEvents()
    {
        if (_isSubscribed) return;

        RecordingManager.OnRecordingStarted += OnRecordingStarted;
        RecordingManager.OnRecordingStopped += OnRecordingStopped;
        RecordingManager.OnRemoteRecordingChanged += OnRemoteRecordingChanged;
        RecordingManager.OnMarkerAdded += OnMarkerAdded;

        VRRoomManager.OnRoomJoined += OnRoomChanged;
        VRRoomManager.OnRoomLeft += OnRoomLeft;

        _isSubscribed = true;
    }

    void UnsubscribeFromEvents()
    {
        if (!_isSubscribed) return;

        RecordingManager.OnRecordingStarted -= OnRecordingStarted;
        RecordingManager.OnRecordingStopped -= OnRecordingStopped;
        RecordingManager.OnRemoteRecordingChanged -= OnRemoteRecordingChanged;
        RecordingManager.OnMarkerAdded -= OnMarkerAdded;

        VRRoomManager.OnRoomJoined -= OnRoomChanged;
        VRRoomManager.OnRoomLeft -= OnRoomLeft;

        _isSubscribed = false;
    }

    void Start()
    {
        CreateUI();
        RefreshUI();
    }

    void Update()
    {
        UpdateRecordingTime();
    }

    void UpdateRecordingTime()
    {
        if (recTimeText == null) return;

        var recordingManager = RecordingManager.Instance;
        if (recordingManager != null && recordingManager.State == RecordingState.Recording)
        {
            recTimeText.text = FormatTime(recordingManager.ElapsedTime);
        }
    }

    void CreateUI()
    {
        if (_uiCreated) return;

        // Get or create content container
        RectTransform content = GetComponent<RectTransform>();
        if (content == null) return;

        // Clear existing children (except templates)
        foreach (Transform child in transform)
        {
            if (!child.name.Contains("Template"))
            {
                Destroy(child.gameObject);
            }
        }

        // Main container with vertical layout
        GameObject container = new GameObject("Container");
        container.transform.SetParent(transform, false);

        RectTransform containerRect = container.AddComponent<RectTransform>();
        containerRect.anchorMin = Vector2.zero;
        containerRect.anchorMax = Vector2.one;
        containerRect.offsetMin = new Vector2(15, 15);
        containerRect.offsetMax = new Vector2(-15, -15);

        VerticalLayoutGroup vlg = container.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = false;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.spacing = 15;
        vlg.padding = new RectOffset(10, 10, 10, 10);

        // Title
        CreateLabel(container.transform, "Title", "Recording", 24, FontStyles.Bold);

        // Status text
        GameObject statusObj = CreateLabel(container.transform, "StatusText", "Not in a room", 16, FontStyles.Normal);
        recStatusText = statusObj.GetComponent<TextMeshProUGUI>();

        // Record button row
        GameObject recordRow = CreateRow(container.transform, "RecordRow", 50);

        // Record button
        GameObject recordBtnObj = CreateButton(recordRow.transform, "RecordButton", "Start Recording", new Color(0.8f, 0.2f, 0.2f, 0.9f), 180);
        recordButton = recordBtnObj.GetComponent<Button>();
        recordButton.onClick.AddListener(OnRecordButtonClicked);
        recordButtonText = recordBtnObj.GetComponentInChildren<TextMeshProUGUI>();

        // REC indicator
        GameObject recIndObj = new GameObject("RecIndicator");
        recIndObj.transform.SetParent(recordRow.transform, false);

        Image recBg = recIndObj.AddComponent<Image>();
        recBg.color = new Color(0.9f, 0.1f, 0.1f, 0.95f);

        LayoutElement recLayout = recIndObj.AddComponent<LayoutElement>();
        recLayout.minWidth = 100;
        recLayout.preferredWidth = 100;
        recLayout.minHeight = 40;

        // REC time text
        GameObject recTextObj = new GameObject("RecTimeText");
        recTextObj.transform.SetParent(recIndObj.transform, false);

        recTimeText = recTextObj.AddComponent<TextMeshProUGUI>();
        recTimeText.text = "00:00";
        recTimeText.fontSize = 20;
        recTimeText.color = Color.white;
        recTimeText.alignment = TextAlignmentOptions.Center;
        recTimeText.fontStyle = FontStyles.Bold;

        if (TMP_Settings.defaultFontAsset != null)
        {
            recTimeText.font = TMP_Settings.defaultFontAsset;
        }

        RectTransform recTextRect = recTextObj.GetComponent<RectTransform>();
        recTextRect.anchorMin = Vector2.zero;
        recTextRect.anchorMax = Vector2.one;
        recTextRect.offsetMin = Vector2.zero;
        recTextRect.offsetMax = Vector2.zero;

        recIndicator = recIndObj;
        recIndObj.SetActive(false);

        // Spacer
        CreateSpacer(container.transform, 10);

        // Markers section
        CreateLabel(container.transform, "MarkersTitle", "Markers", 18, FontStyles.Bold);

        markersSection = CreateRow(container.transform, "MarkersRow", 45);

        // Marker buttons
        GameObject impBtn = CreateButton(markersSection.transform, "MarkerImportant", "! Important", new Color(0.9f, 0.5f, 0.1f, 0.9f), 100);
        markerImportantButton = impBtn.GetComponent<Button>();
        markerImportantButton.onClick.AddListener(() => OnMarkerClicked(MarkerType.Important));

        GameObject qBtn = CreateButton(markersSection.transform, "MarkerQuestion", "? Question", new Color(0.2f, 0.5f, 0.9f, 0.9f), 100);
        markerQuestionButton = qBtn.GetComponent<Button>();
        markerQuestionButton.onClick.AddListener(() => OnMarkerClicked(MarkerType.Question));

        GameObject todoBtn = CreateButton(markersSection.transform, "MarkerTodo", "Todo", new Color(0.2f, 0.7f, 0.3f, 0.9f), 80);
        markerTodoButton = todoBtn.GetComponent<Button>();
        markerTodoButton.onClick.AddListener(() => OnMarkerClicked(MarkerType.Todo));

        GameObject ideaBtn = CreateButton(markersSection.transform, "MarkerIdea", "Idea", new Color(0.8f, 0.6f, 0.9f, 0.9f), 70);
        markerIdeaButton = ideaBtn.GetComponent<Button>();
        markerIdeaButton.onClick.AddListener(() => OnMarkerClicked(MarkerType.Idea));

        // Spacer
        CreateSpacer(container.transform, 15);

        // Info text
        GameObject infoObj = CreateLabel(container.transform, "InfoText", "", 14, FontStyles.Italic);
        infoText = infoObj.GetComponent<TextMeshProUGUI>();
        infoText.color = new Color(0.7f, 0.7f, 0.7f);

        _uiCreated = true;
        Debug.Log("[VRMenuPageRecording] UI created");
    }

    GameObject CreateRow(Transform parent, string name, float height)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);

        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(0, height);

        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlHeight = true;
        hlg.childControlWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.spacing = 15;

        LayoutElement layout = row.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;

        return row;
    }

    GameObject CreateButton(Transform parent, string name, string label, Color bgColor, float width)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);

        Image btnBg = btnObj.AddComponent<Image>();
        btnBg.color = bgColor;

        Button btn = btnObj.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        LayoutElement btnLayout = btnObj.AddComponent<LayoutElement>();
        btnLayout.minWidth = width;
        btnLayout.preferredWidth = width;
        btnLayout.minHeight = 40;

        // Label
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;

        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
        }

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(5, 0);
        textRect.offsetMax = new Vector2(-5, 0);

        return btnObj;
    }

    GameObject CreateLabel(Transform parent, string name, string text, int fontSize, FontStyles style)
    {
        GameObject labelObj = new GameObject(name);
        labelObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = labelObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = style;

        if (TMP_Settings.defaultFontAsset != null)
        {
            tmp.font = TMP_Settings.defaultFontAsset;
        }

        LayoutElement layout = labelObj.AddComponent<LayoutElement>();
        layout.minHeight = fontSize + 10;
        layout.preferredHeight = fontSize + 10;

        return labelObj;
    }

    void CreateSpacer(Transform parent, float height)
    {
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(parent, false);

        LayoutElement layout = spacer.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    #region UI Updates

    void RefreshUI()
    {
        var roomManager = VRRoomManager.Instance;
        var recordingManager = RecordingManager.Instance;

        bool inRoom = roomManager != null && roomManager.IsInRoom;
        bool isHost = roomManager != null && roomManager.IsHost;
        bool isRecording = recordingManager != null && recordingManager.State == RecordingState.Recording;
        bool isRemoteRecording = recordingManager != null && recordingManager.IsRemoteRecording;
        bool anyRecording = isRecording || isRemoteRecording;

        // Status text
        if (recStatusText != null)
        {
            if (!inRoom)
            {
                recStatusText.text = "Join a room to record";
                recStatusText.color = new Color(0.7f, 0.7f, 0.7f);
            }
            else if (isRecording)
            {
                recStatusText.text = "Recording in progress...";
                recStatusText.color = new Color(1f, 0.3f, 0.3f);
            }
            else if (isRemoteRecording)
            {
                string hostName = recordingManager?.RemoteRecordingHostName ?? "Host";
                recStatusText.text = $"Recording by {hostName}";
                recStatusText.color = new Color(1f, 0.6f, 0.3f);
            }
            else if (isHost)
            {
                recStatusText.text = "Ready to record (Host)";
                recStatusText.color = new Color(0.3f, 1f, 0.3f);
            }
            else
            {
                recStatusText.text = "Only the host can start recording";
                recStatusText.color = new Color(0.7f, 0.7f, 0.7f);
            }
        }

        // Record button
        if (recordButton != null)
        {
            bool canRecord = inRoom && isHost && !isRemoteRecording;
            recordButton.gameObject.SetActive(canRecord || isRecording);
            recordButton.interactable = canRecord || isRecording;

            if (recordButtonText != null)
            {
                recordButtonText.text = isRecording ? "Stop Recording" : "Start Recording";
            }

            // Change button color based on state
            Image btnImage = recordButton.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = isRecording ? new Color(0.3f, 0.3f, 0.3f, 0.9f) : new Color(0.8f, 0.2f, 0.2f, 0.9f);
            }
        }

        // REC indicator
        if (recIndicator != null)
        {
            recIndicator.SetActive(anyRecording);
        }

        // Markers section
        if (markersSection != null)
        {
            markersSection.SetActive(anyRecording);
        }

        // Update marker buttons interactability
        bool canAddMarker = anyRecording;
        if (markerImportantButton != null) markerImportantButton.interactable = canAddMarker;
        if (markerQuestionButton != null) markerQuestionButton.interactable = canAddMarker;
        if (markerTodoButton != null) markerTodoButton.interactable = canAddMarker;
        if (markerIdeaButton != null) markerIdeaButton.interactable = canAddMarker;

        // Info text
        if (infoText != null)
        {
            if (anyRecording)
            {
                infoText.text = "Add markers to highlight important moments";
            }
            else if (inRoom && isHost)
            {
                infoText.text = "Recording will capture video and audio of the meeting";
            }
            else if (inRoom)
            {
                infoText.text = "You can add markers when recording starts";
            }
            else
            {
                infoText.text = "";
            }
        }
    }

    #endregion

    #region Button Handlers

    void OnRecordButtonClicked()
    {
        var recordingManager = RecordingManager.Instance;
        if (recordingManager == null) return;

        if (recordingManager.State == RecordingState.Idle)
        {
            recordingManager.StartRecording();
        }
        else if (recordingManager.State == RecordingState.Recording)
        {
            recordingManager.StopRecording();
        }
    }

    void OnMarkerClicked(MarkerType type)
    {
        var recordingManager = RecordingManager.Instance;
        if (recordingManager == null) return;

        // Can add marker if local recording or remote recording
        if (recordingManager.State == RecordingState.Recording || recordingManager.IsRemoteRecording)
        {
            recordingManager.AddMarker(type);
            Debug.Log($"[VRMenuPageRecording] Marker added: {type}");

            // Brief visual feedback
            StartCoroutine(MarkerFeedback(type));
        }
    }

    System.Collections.IEnumerator MarkerFeedback(MarkerType type)
    {
        if (infoText != null)
        {
            string originalText = infoText.text;
            Color originalColor = infoText.color;

            infoText.text = $"Marker '{type}' added!";
            infoText.color = Color.green;

            yield return new WaitForSeconds(1.5f);

            infoText.text = originalText;
            infoText.color = originalColor;
        }
    }

    #endregion

    #region Event Handlers

    void OnRecordingStarted()
    {
        RefreshUI();
    }

    void OnRecordingStopped()
    {
        RefreshUI();
    }

    void OnRemoteRecordingChanged(bool isRecording, string hostName)
    {
        RefreshUI();
    }

    void OnMarkerAdded(RecordingMarker marker)
    {
        // Could show marker feedback here
    }

    void OnRoomChanged(string roomId)
    {
        RefreshUI();
    }

    void OnRoomLeft()
    {
        RefreshUI();
    }

    #endregion

    string FormatTime(float seconds)
    {
        int mins = (int)(seconds / 60);
        int secs = (int)(seconds % 60);
        return $"{mins:D2}:{secs:D2}";
    }
}
