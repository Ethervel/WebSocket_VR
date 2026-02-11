using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Helper temporaire pour tester l'enregistrement.
/// Raccourcis clavier pour demarrer/arreter l'enregistrement.
/// A SUPPRIMER apres les tests - sera remplace par l'UI.
/// </summary>
public class RecordingTestHelper : MonoBehaviour
{
    [Header("=== Status ===")]
    [SerializeField] private bool _isRecording = false;
    [SerializeField] private float _elapsedTime = 0f;
    [SerializeField] private string _currentState = "N/A";

    private Keyboard _keyboard;

    void Start()
    {
        _keyboard = Keyboard.current;
        if (_keyboard == null)
        {
            Debug.LogWarning("[RecordingTest] Pas de clavier detecte!");
        }
        else
        {
            Debug.Log("[RecordingTest] Helper actif - F9:Record, F10:Important, F11:Question, F12:Todo");
        }
    }

    void Update()
    {
        if (_keyboard == null)
        {
            _keyboard = Keyboard.current;
            if (_keyboard == null) return;
        }

        // Toggle enregistrement - F9
        if (_keyboard.f9Key.wasPressedThisFrame)
        {
            ToggleRecording();
        }

        // Marqueurs
        if (_keyboard.f10Key.wasPressedThisFrame)
        {
            AddMarker(MarkerType.Important);
        }
        if (_keyboard.f11Key.wasPressedThisFrame)
        {
            AddMarker(MarkerType.Question);
        }
        if (_keyboard.f12Key.wasPressedThisFrame)
        {
            AddMarker(MarkerType.Todo);
        }

        // Update status
        if (RecordingManager.Instance != null)
        {
            _isRecording = RecordingManager.Instance.State == RecordingState.Recording;
            _elapsedTime = RecordingManager.Instance.ElapsedTime;
            _currentState = RecordingManager.Instance.State.ToString();
        }
    }

    void ToggleRecording()
    {
        if (RecordingManager.Instance == null)
        {
            Debug.LogError("[RecordingTest] RecordingManager non trouve!");
            return;
        }

        if (RecordingManager.Instance.State == RecordingState.Idle)
        {
            Debug.Log("[RecordingTest] *** DEMARRAGE ENREGISTREMENT ***");
            RecordingManager.Instance.StartRecording();
        }
        else if (RecordingManager.Instance.State == RecordingState.Recording)
        {
            Debug.Log("[RecordingTest] *** ARRET ENREGISTREMENT ***");
            RecordingManager.Instance.StopRecording();
        }
        else
        {
            Debug.Log($"[RecordingTest] Etat actuel: {RecordingManager.Instance.State}");
        }
    }

    void AddMarker(MarkerType type)
    {
        if (RecordingManager.Instance == null)
        {
            Debug.LogError("[RecordingTest] RecordingManager non trouve!");
            return;
        }

        if (RecordingManager.Instance.State != RecordingState.Recording)
        {
            Debug.LogWarning("[RecordingTest] Pas d'enregistrement en cours pour ajouter un marqueur.");
            return;
        }

        Debug.Log($"[RecordingTest] Ajout marqueur: {type}");
        RecordingManager.Instance.AddMarker(type);
    }

    void OnGUI()
    {
        // Afficher un overlay simple pour le debug
        GUILayout.BeginArea(new Rect(10, 10, 300, 180));
        GUILayout.BeginVertical("box");

        GUILayout.Label("=== RECORDING TEST ===");
        GUILayout.Label($"Etat: {_currentState}");

        if (_isRecording)
        {
            GUI.color = Color.red;
            GUILayout.Label($"● REC {FormatTime(_elapsedTime)}");
            GUI.color = Color.white;
        }
        else
        {
            GUILayout.Label("○ Pret");
        }

        GUILayout.Space(10);
        GUILayout.Label("Raccourcis:");
        GUILayout.Label("  F9  = Start/Stop Recording");
        GUILayout.Label("  F10 = Marqueur Important");
        GUILayout.Label("  F11 = Marqueur Question");
        GUILayout.Label("  F12 = Marqueur Todo");

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    string FormatTime(float seconds)
    {
        int mins = (int)(seconds / 60);
        int secs = (int)(seconds % 60);
        return $"{mins:D2}:{secs:D2}";
    }
}
