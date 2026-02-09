using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Ajoute automatiquement les sons de clic et hover à un bouton UI.
/// Attacher ce script à n'importe quel bouton.
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonSounds : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public enum ButtonSoundType
    {
        Default,    // Click normal
        Back,       // Retour/Annuler
        Success,    // Confirmation positive
        Error       // Action interdite
    }

    [Header("Sound Type")]
    public ButtonSoundType soundType = ButtonSoundType.Default;

    [Header("Options")]
    public bool playHoverSound = true;
    public bool playClickSound = true;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (playHoverSound && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayHover();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!playClickSound || SoundManager.Instance == null) return;

        switch (soundType)
        {
            case ButtonSoundType.Default:
                SoundManager.Instance.PlayClick();
                break;
            case ButtonSoundType.Back:
                SoundManager.Instance.PlayBack();
                break;
            case ButtonSoundType.Success:
                SoundManager.Instance.PlaySuccess();
                break;
            case ButtonSoundType.Error:
                SoundManager.Instance.PlayError();
                break;
        }
    }
}
