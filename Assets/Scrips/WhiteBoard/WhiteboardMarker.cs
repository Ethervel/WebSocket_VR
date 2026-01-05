using UnityEngine;
using System.Linq;

public class WhiteboardMarker : MonoBehaviour
{
    [Header("Configuration")]
    public Transform tip;
    public int penSize = 5;
    public Color currentColor = Color.black;

    [Header("Network Sync")]
    public bool isNetworked = true;
    
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

        // Applique la couleur initiale
        ApplyColor(currentColor);

        // Hauteur de la pointe pour le Raycast
        _tipHeight = tip.localScale.y;
    }

    void Update()
    {
        Draw();
    }

    // =======================
    //  GESTION DES COULEURS
    // =======================

    public void SetColor(Color newColor)
    {
        currentColor = newColor;
        ApplyColor(newColor);
    }

    private void ApplyColor(Color color)
    {
        // Couleur visuelle du feutre
        _renderer.material.color = color;

        // Pré-calcul des pixels à dessiner
        _colors = Enumerable
            .Repeat(color, penSize * penSize)
            .ToArray();
    }

    // =======================
    // ✏️ DESSIN
    // =======================

    private void Draw()
    {
        if (Physics.Raycast(tip.position, transform.up, out _touch, _tipHeight))
        {
            if (_touch.transform.CompareTag("Whiteboard"))
            {
                if (_whiteboard == null)
                {
                    _whiteboard = _touch.transform.GetComponent<Whiteboard>();
                    
                }

                _touchPos = _touch.textureCoord;

                int x = (int)(_touchPos.x * _whiteboard.textureSize.x - penSize / 2);
                int y = (int)(_touchPos.y * _whiteboard.textureSize.y - penSize / 2);

                if (x < 0 || y < 0 ||
                    x + penSize > _whiteboard.textureSize.x ||
                    y + penSize > _whiteboard.textureSize.y)
                {
                    return;
                }

                if (_touchedLastFrame)
                {
                    // Dessiner localement
                    _whiteboard.texture.SetPixels(x, y, penSize, penSize, _colors);

                    // Interpolation pour des traits fluides
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

                    transform.rotation = _lastTouchRot;
                    _whiteboard.texture.Apply();
                }

                _lastTouchPos = new Vector2(x, y);
                _lastTouchRot = transform.rotation;
                _touchedLastFrame = true;
                return;
            }
        }

        _whiteboard = null;
        _touchedLastFrame = false;
    }
}