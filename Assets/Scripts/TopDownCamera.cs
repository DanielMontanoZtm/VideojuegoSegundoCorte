using UnityEngine;

/// Cámara superior inclinada que sigue al jugador local (como en la imagen de referencia).
public class TopDownCamera : MonoBehaviour
{
    public Transform target;
    public float height = 28f;
    public float pitch = 65f;      // 90 = totalmente vertical
    public float smooth = 8f;

    public void SetTarget(Transform t)
    {
        target = t;
        if (t != null) transform.position = Desired();
    }

    Vector3 Desired()
    {
        float back = height / Mathf.Tan(pitch * Mathf.Deg2Rad);
        return target.position + new Vector3(0f, height, -back);
    }

    void LateUpdate()
    {
        transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
        if (target == null) return;
        transform.position = Vector3.Lerp(transform.position, Desired(), 1f - Mathf.Exp(-smooth * Time.deltaTime));
    }
}
