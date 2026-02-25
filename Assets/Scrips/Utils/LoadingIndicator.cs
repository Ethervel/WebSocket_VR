using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Indicateur de chargement avec spinner et progression.
/// Compatible VR (World Space) et Desktop (Screen Space).
/// </summary>
public class LoadingIndicator : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _indicatorPanel;
    [SerializeField] private Image _spinnerImage;
    [SerializeField] private Image _progressFill;
    [SerializeField] private TextMeshProUGUI _progressText;
    [SerializeField] private TextMeshProUGUI _statusText;

    [Header("Spinner Settings")]
    [SerializeField] private float _spinSpeed = 200f;
    [SerializeField] private bool _autoRotate = true;

    [Header("Animation")]
    [SerializeField] private float _fadeSpeed = 3f;
    [SerializeField] private Animator _animator;

    // État
    private float _progress = 0f;
    private bool _isVisible = false;
    private CanvasGroup _canvasGroup;

    void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Auto-find references
        if (_indicatorPanel == null)
            _indicatorPanel = gameObject;

        if (_animator == null)
            _animator = GetComponent<Animator>();

        // Cacher au démarrage
        _canvasGroup.alpha = 0f;
        _indicatorPanel.SetActive(false);
    }

    void Update()
    {
        // Rotation du spinner
        if (_autoRotate && _spinnerImage != null && _isVisible)
        {
            _spinnerImage.rectTransform.Rotate(0f, 0f, -_spinSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Affiche l'indicateur avec animation.
    /// </summary>
    public void Show()
    {
        _indicatorPanel.SetActive(true);
        _isVisible = true;

        if (_animator != null)
        {
            _animator.SetBool("Show", true);
        }
        else
        {
            StartCoroutine(FadeIn());
        }

        Debug.Log("[LoadingIndicator] Show");
    }

    /// <summary>
    /// Cache l'indicateur avec animation.
    /// </summary>
    public void Hide()
    {
        _isVisible = false;

        if (_animator != null)
        {
            _animator.SetBool("Show", false);
            // L'animator appellera Deactivate() à la fin de l'animation
        }
        else
        {
            StartCoroutine(FadeOut());
        }

        Debug.Log("[LoadingIndicator] Hide");
    }

    /// <summary>
    /// Désactive complètement (appelé par l'animator ou après fade).
    /// </summary>
    public void Deactivate()
    {
        _indicatorPanel.SetActive(false);
        _canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// Définit la progression (0-1).
    /// </summary>
    public void SetProgress(float progress)
    {
        _progress = Mathf.Clamp01(progress);

        if (_progressFill != null)
        {
            _progressFill.fillAmount = _progress;
        }

        if (_progressText != null)
        {
            _progressText.text = $"{(_progress * 100):F0}%";
        }
    }

    /// <summary>
    /// Définit le texte de statut.
    /// </summary>
    public void SetStatus(string status)
    {
        if (_statusText != null)
        {
            _statusText.text = status;
        }
    }

    IEnumerator FadeIn()
    {
        while (_canvasGroup.alpha < 1f)
        {
            _canvasGroup.alpha += _fadeSpeed * Time.deltaTime;
            yield return null;
        }
        _canvasGroup.alpha = 1f;
    }

    IEnumerator FadeOut()
    {
        while (_canvasGroup.alpha > 0f)
        {
            _canvasGroup.alpha -= _fadeSpeed * Time.deltaTime;
            yield return null;
        }
        Deactivate();
    }
}
