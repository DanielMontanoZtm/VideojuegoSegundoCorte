using UnityEngine;

/// Construye en tiempo de ejecución el terreno de 100x100 con muros perimetrales.
public class ArenaBuilder : MonoBehaviour
{
    public float size = 100f;
    public float wallHeight = 4f;
    public float wallThickness = 2f;
    public Material floorMaterial;
    public Material wallMaterial;

    void Awake()
    {
        var root = new GameObject("Arena").transform;
        Make("Floor", new Vector3(0, -0.5f, 0), new Vector3(size, 1f, size), floorMaterial, new Color(0.25f, 0.25f, 0.28f), root);
        float o = size / 2f + wallThickness / 2f;
        Make("WallN", new Vector3(0, wallHeight / 2, o), new Vector3(size + wallThickness * 2, wallHeight, wallThickness), wallMaterial, Color.white, root);
        Make("WallS", new Vector3(0, wallHeight / 2, -o), new Vector3(size + wallThickness * 2, wallHeight, wallThickness), wallMaterial, Color.white, root);
        Make("WallE", new Vector3(o, wallHeight / 2, 0), new Vector3(wallThickness, wallHeight, size), wallMaterial, Color.white, root);
        Make("WallW", new Vector3(-o, wallHeight / 2, 0), new Vector3(wallThickness, wallHeight, size), wallMaterial, Color.white, root);
    }

    void Make(string n, Vector3 pos, Vector3 scale, Material mat, Color fallback, Transform parent)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = n; g.transform.SetParent(parent);
        g.transform.position = pos; g.transform.localScale = scale;
        var r = g.GetComponent<Renderer>();
        if (mat != null) r.sharedMaterial = mat; else r.material.color = fallback;
    }
}
