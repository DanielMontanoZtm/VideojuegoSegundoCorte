using UnityEngine;

/// Objeto recolectable: solo animación (rotación + flotación). La recolección la valida el servidor.
public class CollectibleItem : MonoBehaviour
{
    public int id;
    [HideInInspector] public float nextRequest;
    public float rotationSpeed = 120f;
    public float bobHeight = 0.3f;
    public float bobSpeed = 2f;

    Vector3 basePos;
    float phase;

    void Start() { basePos = transform.position; phase = Random.value * 6.28f; }

    void Update()
    {
        transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = basePos + Vector3.up * Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight;
    }
}
