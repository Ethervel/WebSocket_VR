using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI pour les controles de presentation de fichiers.
/// Affiche les boutons prev/next/stop et les infos de page.
/// </summary>
public class FilePresentationUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Le whiteboard cible pour les presentations")]
    public Whiteboard targetWhiteboard;

    [Header("Presenter Controls")]
    [Tooltip("Panel affiche quand on est le presentateur")]
    public GameObject presenterPanel;
    public Button prevButton;
    public Button nextButton;
    public Button stopButton;
    public TextMeshProUGUI pageText;
    public TextMeshProUGUI fileNameText;

    [Header("Viewer Info")]
    [Tooltip("Panel affiche quand on regarde une presentation")]
    public GameObject viewerPanel;
    public TextMeshProUGUI presenterNameText;
    public TextMeshProUGUI viewerPageText;

    private bool _isPresenter = false;
    private string _currentWhiteboardId;

    void OnEnable()
    {
        // S'abonner aux events dans OnEnable pour capturer les events des managers DontDestroyOnLoad
        FilePresentationManager.OnPresentationStarted += OnPresentationStarted;
        FilePresentationManager.OnPresentationStopped += OnPresentationStopped;
        FilePresentationManager.OnPageChanged += OnPageChanged;
        FilePresentationManager.OnPresentationError += OnPresentationError;
    }

    void OnDisable()
    {
        // Se desabonner dans OnDisable
        FilePresentationManager.OnPresentationStarted -= OnPresentationStarted;
        FilePresentationManager.OnPresentationStopped -= OnPresentationStopped;
        FilePresentationManager.OnPageChanged -= OnPageChanged;
        FilePresentationManager.OnPresentationError -= OnPresentationError;
    }

    void Start()
    {
        // Connecter les boutons
        if (prevButton != null)
            prevButton.onClick.AddListener(OnPrevClicked);
        if (nextButton != null)
            nextButton.onClick.AddListener(OnNextClicked);
        if (stopButton != null)
            stopButton.onClick.AddListener(OnStopClicked);

        // Etat initial: tout cache
        if (presenterPanel != null)
            presenterPanel.SetActive(false);
        if (viewerPanel != null)
            viewerPanel.SetActive(false);

        // Trouver le whiteboard si pas assigne
        if (targetWhiteboard == null)
        {
            targetWhiteboard = GetComponentInParent<Whiteboard>();
            if (targetWhiteboard == null)
            {
                targetWhiteboard = FindAnyObjectByType<Whiteboard>();
            }
        }

        if (targetWhiteboard != null)
        {
            _currentWhiteboardId = targetWhiteboard.id;
        }

        // Synchroniser avec l'etat actuel (si presentation deja en cours)
        SyncWithCurrentPresentationState();
    }

    void OnDestroy()
    {
        // Cleanup button listeners
        if (prevButton != null)
            prevButton.onClick.RemoveAllListeners();
        if (nextButton != null)
            nextButton.onClick.RemoveAllListeners();
        if (stopButton != null)
            stopButton.onClick.RemoveAllListeners();
    }

    /// <summary>
    /// Synchronise l'UI avec l'etat de presentation actuel.
    /// </summary>
    void SyncWithCurrentPresentationState()
    {
        if (FilePresentationManager.Instance == null) return;
        if (string.IsNullOrEmpty(_currentWhiteboardId)) return;

        // Verifier si le local player presente
        if (FilePresentationManager.Instance.IsPresenting)
        {
            _isPresenter = true;

            if (presenterPanel != null)
                presenterPanel.SetActive(true);
            if (viewerPanel != null)
                viewerPanel.SetActive(false);

            string fileId = FilePresentationManager.Instance.PresentingFileId;
            if (fileNameText != null && FileShareManager.Instance != null)
            {
                var metadata = FileShareManager.Instance.GetFileMetadata(fileId);
                if (metadata != null)
                {
                    fileNameText.text = metadata.fileName;
                }
            }

            UpdatePageDisplay();
        }
        // Verifier si on recoit une presentation sur ce whiteboard
        else if (FilePresentationManager.Instance.IsWhiteboardReceiving(_currentWhiteboardId))
        {
            _isPresenter = false;
            string presenterName = FilePresentationManager.Instance.GetPresenterName(_currentWhiteboardId);

            if (presenterPanel != null)
                presenterPanel.SetActive(false);
            if (viewerPanel != null)
                viewerPanel.SetActive(true);

            if (presenterNameText != null)
            {
                presenterNameText.text = $"Presenting: {presenterName ?? "Someone"}";
            }

            UpdatePageDisplay();
        }
    }

    /// <summary>
    /// Demarre la presentation d'un fichier.
    /// Appele depuis FileSharingUI quand on clique sur "Presenter".
    /// </summary>
    public void PresentFile(string fileId)
    {
        if (targetWhiteboard == null)
        {
            targetWhiteboard = FindAnyObjectByType<Whiteboard>();
        }

        if (FilePresentationManager.Instance != null && targetWhiteboard != null)
        {
            FilePresentationManager.Instance.StartPresentation(fileId, targetWhiteboard);
        }
        else
        {
            Debug.LogError("[FilePresentUI] Cannot present: manager or whiteboard is null");
        }
    }

    void OnPrevClicked()
    {
        FilePresentationManager.Instance?.PreviousPage();
    }

    void OnNextClicked()
    {
        FilePresentationManager.Instance?.NextPage();
    }

    void OnStopClicked()
    {
        FilePresentationManager.Instance?.StopPresentation();
    }

    void OnPresentationStarted(string whiteboardId, string fileId, string presenterId, string presenterName)
    {
        // Verifier si c'est notre whiteboard
        if (!string.IsNullOrEmpty(_currentWhiteboardId) && whiteboardId != _currentWhiteboardId)
            return;

        _isPresenter = (presenterId == VRNetworkManager.LocalId);

        if (presenterPanel != null)
            presenterPanel.SetActive(_isPresenter);
        if (viewerPanel != null)
            viewerPanel.SetActive(!_isPresenter);

        if (!_isPresenter && presenterNameText != null)
        {
            presenterNameText.text = $"Presenting: {presenterName}";
        }

        if (_isPresenter && fileNameText != null)
        {
            var metadata = FileShareManager.Instance?.GetFileMetadata(fileId);
            if (metadata != null)
            {
                fileNameText.text = metadata.fileName;
            }
        }

        UpdatePageDisplay();
    }

    void OnPresentationStopped(string whiteboardId, string presenterId)
    {
        if (!string.IsNullOrEmpty(_currentWhiteboardId) && whiteboardId != _currentWhiteboardId)
            return;

        _isPresenter = false;

        if (presenterPanel != null)
            presenterPanel.SetActive(false);
        if (viewerPanel != null)
            viewerPanel.SetActive(false);
    }

    void OnPageChanged(string fileId, int currentPage, int totalPages)
    {
        UpdatePageDisplay();
    }

    void OnPresentationError(string context, string error)
    {
        Debug.LogWarning($"[FilePresentUI] Error ({context}): {error}");
    }

    void UpdatePageDisplay()
    {
        var mgr = FilePresentationManager.Instance;
        if (mgr == null) return;

        int current = mgr.CurrentPage + 1;  // 1-indexed pour l'affichage
        int total = mgr.TotalPages;

        string pageStr = $"{current} / {total}";

        if (pageText != null)
            pageText.text = pageStr;
        if (viewerPageText != null)
            viewerPageText.text = pageStr;

        // Activer/desactiver les boutons
        if (prevButton != null)
            prevButton.interactable = mgr.CurrentPage > 0;
        if (nextButton != null)
            nextButton.interactable = mgr.CurrentPage < mgr.TotalPages - 1;
    }
}
