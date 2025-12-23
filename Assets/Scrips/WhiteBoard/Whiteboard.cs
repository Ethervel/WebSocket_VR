using UnityEngine;

public class Whiteboard : MonoBehaviour
{
    public Texture2D texture;
    public Vector2 textureSize = new Vector2(2048, 2048); // Haute résolution pour un trait net

    void Start()
    {
        var r = GetComponent<Renderer>();
        
        // 1. Création d'une nouvelle texture vierge en mémoire
        texture = new Texture2D((int)textureSize.x, (int)textureSize.y);
        
        // 2. Assigner cette texture au matériau de l'objet
        r.material.mainTexture = texture;
    }
}