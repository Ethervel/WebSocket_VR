using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages the sidebar navigation with icons.
/// Each icon switches to a different page/panel.
/// </summary>
public class VRMenuSidebar : MonoBehaviour
{
    [Serializable]
    public class MenuPage
    {
        public string pageName;
        public Sprite icon;
        public GameObject panel;
        [HideInInspector] public Button button;
    }

    [Header("Pages")]
    public List<MenuPage> pages = new List<MenuPage>();

    [Header("UI References")]
    [Tooltip("Container for sidebar buttons (should have VerticalLayoutGroup)")]
    public Transform sidebarContainer;

    [Tooltip("Prefab for sidebar icon button")]
    public GameObject iconButtonPrefab;

    [Header("Appearance")]
    public Color normalColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
    public Color selectedColor = new Color(0.3f, 0.5f, 0.8f, 1f);
    public Color hoverColor = new Color(0.4f, 0.4f, 0.4f, 0.9f);

    [Header("Settings")]
    [Tooltip("Index of page to show on start")]
    public int defaultPageIndex = 0;

    // Events
    public static event Action<int, string> OnPageChanged;

    // State
    private int _currentPageIndex = -1;

    void Start()
    {
        AutoFindReferences();
        Debug.Log($"[VRMenuSidebar] Starting with {pages.Count} pages");
        CreateSidebarButtons();
        ShowPage(defaultPageIndex);
    }

    void AutoFindReferences()
    {
        // Find sidebar container if not assigned
        if (sidebarContainer == null)
        {
            VerticalLayoutGroup vLayout = GetComponentInChildren<VerticalLayoutGroup>(true);
            if (vLayout != null && vLayout.name.Contains("Button"))
            {
                sidebarContainer = vLayout.transform;
            }
        }

        // Find icon button prefab (inactive template)
        if (iconButtonPrefab == null && sidebarContainer != null)
        {
            foreach (Transform child in sidebarContainer)
            {
                if (!child.gameObject.activeSelf && child.name.Contains("Template"))
                {
                    iconButtonPrefab = child.gameObject;
                    break;
                }
            }
        }

        // Find content area for pages
        Transform content = null;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Content" && child.parent != null &&
                (child.parent.name == "Background" || child.parent.name.Contains("Background")))
            {
                content = child;
                break;
            }
        }

        // Auto-find pages if list is empty OR if pages have null panels
        bool needsPageRefresh = pages.Count == 0;
        if (!needsPageRefresh)
        {
            foreach (var page in pages)
            {
                if (page.panel == null)
                {
                    needsPageRefresh = true;
                    break;
                }
            }
        }

        if (needsPageRefresh && content != null)
        {
            pages.Clear();
            foreach (Transform child in content)
            {
                if (child.name.StartsWith("Page_"))
                {
                    string pageName = child.name.Replace("Page_", "");
                    pages.Add(new MenuPage
                    {
                        pageName = pageName,
                        panel = child.gameObject,
                        icon = null
                    });
                    Debug.Log($"[VRMenuSidebar] AutoFind: Found page {pageName}");
                }
            }
        }

        // Verify all pages have valid panels
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i].panel == null && content != null)
            {
                // Try to find the panel by name
                Transform panel = content.Find($"Page_{pages[i].pageName}");
                if (panel != null)
                {
                    pages[i].panel = panel.gameObject;
                    Debug.Log($"[VRMenuSidebar] AutoFind: Reconnected panel for {pages[i].pageName}");
                }
            }
        }

        Debug.Log($"[VRMenuSidebar] AutoFind: container={sidebarContainer != null}, prefab={iconButtonPrefab != null}, pages={pages.Count}");
    }

    void CreateSidebarButtons()
    {
        if (sidebarContainer == null)
        {
            Debug.LogWarning("[VRMenuSidebar] Missing sidebarContainer!");
            return;
        }

        // First, try to find existing buttons in the sidebar
        List<Button> existingButtons = new List<Button>();
        foreach (Transform child in sidebarContainer)
        {
            if (child.gameObject.activeSelf)
            {
                Button btn = child.GetComponent<Button>();
                if (btn != null && !child.name.Contains("Template"))
                {
                    existingButtons.Add(btn);
                }
            }
        }

        Debug.Log($"[VRMenuSidebar] Found {existingButtons.Count} existing buttons, need {pages.Count} pages");

        // If we have existing buttons, use them
        if (existingButtons.Count >= pages.Count)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                int pageIndex = i;
                MenuPage page = pages[i];
                Button btn = existingButtons[i];

                // Clear any existing listeners and add our handler
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => ShowPage(pageIndex));
                page.button = btn;

                Debug.Log($"[VRMenuSidebar] Connected existing button '{btn.name}' to page: {page.pageName}");
            }
        }
        // Otherwise create new buttons from prefab
        else if (iconButtonPrefab != null)
        {
            // Destroy existing buttons first
            foreach (Button btn in existingButtons)
            {
                Destroy(btn.gameObject);
            }

            for (int i = 0; i < pages.Count; i++)
            {
                int pageIndex = i;
                MenuPage page = pages[i];

                GameObject buttonObj = Instantiate(iconButtonPrefab, sidebarContainer);
                buttonObj.name = $"Btn_{page.pageName}";
                buttonObj.SetActive(true);

                Button btn = buttonObj.GetComponent<Button>();
                if (btn == null) btn = buttonObj.AddComponent<Button>();
                page.button = btn;

                // Setup icon if provided
                if (page.icon != null)
                {
                    var childImages = buttonObj.GetComponentsInChildren<Image>();
                    if (childImages.Length > 1)
                    {
                        childImages[1].sprite = page.icon;
                    }
                }

                btn.onClick.AddListener(() => ShowPage(pageIndex));
                Debug.Log($"[VRMenuSidebar] Created button for page: {page.pageName}");

                ColorBlock colors = btn.colors;
                colors.normalColor = normalColor;
                colors.highlightedColor = hoverColor;
                colors.selectedColor = selectedColor;
                colors.pressedColor = selectedColor;
                btn.colors = colors;
            }
        }
        else
        {
            Debug.LogError("[VRMenuSidebar] No existing buttons and no prefab to create them!");
        }
    }

    public void ShowPage(int index)
    {
        if (index < 0 || index >= pages.Count) return;
        if (index == _currentPageIndex) return;

        // Hide all panels
        foreach (var page in pages)
        {
            if (page.panel != null)
            {
                page.panel.SetActive(false);
            }

            // Reset button color
            if (page.button != null)
            {
                SetButtonSelected(page.button, false);
            }
        }

        // Show selected panel
        MenuPage selectedPage = pages[index];
        if (selectedPage.panel != null)
        {
            selectedPage.panel.SetActive(true);
        }

        // Highlight selected button
        if (selectedPage.button != null)
        {
            SetButtonSelected(selectedPage.button, true);
        }

        _currentPageIndex = index;

        Debug.Log($"[VRMenuSidebar] Showing page: {selectedPage.pageName}");
        OnPageChanged?.Invoke(index, selectedPage.pageName);
    }

    public void ShowPage(string pageName)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            if (pages[i].pageName == pageName)
            {
                ShowPage(i);
                return;
            }
        }
        Debug.LogWarning($"[VRMenuSidebar] Page not found: {pageName}");
    }

    void SetButtonSelected(Button btn, bool selected)
    {
        if (btn == null) return;

        // Change background color to show selection
        Image bgImage = btn.GetComponent<Image>();
        if (bgImage != null)
        {
            bgImage.color = selected ? selectedColor : normalColor;
        }
    }

    public void NextPage()
    {
        int next = (_currentPageIndex + 1) % pages.Count;
        ShowPage(next);
    }

    public void PreviousPage()
    {
        int prev = _currentPageIndex - 1;
        if (prev < 0) prev = pages.Count - 1;
        ShowPage(prev);
    }

    public int CurrentPageIndex => _currentPageIndex;
    public string CurrentPageName => _currentPageIndex >= 0 ? pages[_currentPageIndex].pageName : "";
}
