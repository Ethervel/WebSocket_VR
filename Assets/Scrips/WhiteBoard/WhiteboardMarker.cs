using UnityEngine;
using System.Linq; // Nécessaire pour Enumerable.Repeat

public class WhiteboardMarker : MonoBehaviour
{
    [Header("Configuration")]
    public Transform tip;
    public int penSize = 5;

    private Renderer _renderer;
    private Color[] _colors;           
    private float _tipHeight;

    // Variables pour le dessin
    private RaycastHit _touch;
    private Whiteboard _whiteboard;
    private Vector2 _touchPos;
    private Vector2 _lastTouchPos;
    private bool _touchedLastFrame;
    private Quaternion _lastTouchRot;

    void Start()
    {
        _renderer = tip.GetComponent<Renderer>();

        // Pré-calcul des pixels du feutre
        _colors = Enumerable
            .Repeat(_renderer.material.color, penSize * penSize)
            .ToArray();

        // Hauteur de la pointe pour le Raycast
        _tipHeight = tip.localScale.y;
    }

    void Update()
    {
        Draw();
    }

    private void Draw()
    {
        // Raycast depuis la pointe
        if (Physics.Raycast(tip.position, transform.up, out _touch, _tipHeight))
        {
            if (_touch.transform.CompareTag("Whiteboard"))
            {
                if (_whiteboard == null)
                {
                    _whiteboard = _touch.transform.GetComponent<Whiteboard>();
                }

                // Coordonnées UV → pixels
                _touchPos = _touch.textureCoord;

                int x = (int)(_touchPos.x * _whiteboard.textureSize.x - penSize / 2);
                int y = (int)(_touchPos.y * _whiteboard.textureSize.y - penSize / 2);

                // 🔒 Vérification des limites (CORRIGÉE)
                if (x < 0 || y < 0 ||
                    x + penSize > _whiteboard.textureSize.x ||
                    y + penSize > _whiteboard.textureSize.y)
                {
                    return;
                }

                if (_touchedLastFrame)
                {
                    // Point actuel
                    _whiteboard.texture.SetPixels(x, y, penSize, penSize, _colors);

                    // Interpolation pour éviter les trous
                    for (float t = 0.01f; t < 1.0f; t += 0.01f)
                    {
                        int lerpX = (int)Mathf.Lerp(_lastTouchPos.x, x, t);
                        int lerpY = (int)Mathf.Lerp(_lastTouchPos.y, y, t);

                        _whiteboard.texture.SetPixels(
                            lerpX,
                            lerpY,
                            penSize,
                            penSize,
                            _colors
                        );
                    }

                    // Verrouillage rotation
                    transform.rotation = _lastTouchRot;

                    // Upload GPU (1 fois par frame)
                    _whiteboard.texture.Apply();
                }

                // Sauvegarde état précédent
                _lastTouchPos = new Vector2(x, y);
                _lastTouchRot = transform.rotation;
                _touchedLastFrame = true;
                return;
            }
        }

        // Reset si on ne touche plus le tableau
        _whiteboard = null;
        _touchedLastFrame = false;
    }
}
