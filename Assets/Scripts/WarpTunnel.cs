using UnityEngine;

/// Túnel de teletransporte estilo Pac-Man.
/// Coloca DOS GameObjects vacíos en los bordes del laberinto (las aberturas)
/// y asigna cada uno como el "exitPoint" del otro.
///
/// Configuración en Unity:
///   1. Crea un Empty GameObject en la abertura izquierda del muro → llámalo "TunnelA"
///   2. Crea un Empty GameObject en la abertura derecha del muro → llámalo "TunnelB"
///   3. Agrega este script a ambos.
///   4. En TunnelA: asigna "TunnelB" como exitPoint.
///      En TunnelB: asigna "TunnelA" como exitPoint.
///   5. Agrega un BoxCollider a cada uno, marca "Is Trigger", y dimensiónalo
///      para cubrir la abertura del muro (ej: 3x4x3).
///   6. En ambos GameObjects, establece el Layer o Tag si lo deseas (no es obligatorio).
///
/// El jugador al entrar en el trigger es teletransportado instantáneamente
/// al exitPoint + un pequeño offset para que no active el trigger de salida.
[RequireComponent(typeof(BoxCollider))]
public class WarpTunnel : MonoBehaviour
{
    [Header("Destino")]
    [Tooltip("El túnel opuesto al que viajará el jugador.")]
    public Transform exitPoint;

    [Tooltip("Opcional: Si quieres un punto exacto donde aparezca el jugador, crea un Empty hijo dentro del pasillo y asígnalo aquí. Si está vacío, se calcula automáticamente.")]
    public Transform spawnPoint;

    [Header("Ajuste de Posición")]
    [Tooltip("Distancia hacia el interior del pasillo donde aparecerá el jugador.")]
    public float inwardOffset = 3.5f;

    [Header("Configuración")]
    [Tooltip("Tiempo mínimo entre teletransportes para evitar bucles (segundos).")]
    public float cooldown = 1.0f;

    [Header("Efectos Visuales (Opcional)")]
    [Tooltip("Partículas que se activan al entrar al túnel.")]
    public ParticleSystem warpVfx;

    // Control de cooldown
    static float lastWarpTime;

    void Awake()
    {
        var col = GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;
    }

    /// Obtiene la dirección cardinal recta hacia el interior del pasillo
    public Vector3 GetCardinalDirection()
    {
        if (exitPoint == null) return transform.forward;

        // Vector directo desde el túnel de salida hacia este túnel (camino hacia adentro)
        Vector3 fromExitToHere = transform.position - exitPoint.position;
        fromExitToHere.y = 0f;

        // Alinear estrictamente al eje dominante (evita desviaciones diagonales)
        if (Mathf.Abs(fromExitToHere.x) >= Mathf.Abs(fromExitToHere.z))
        {
            return new Vector3(Mathf.Sign(fromExitToHere.x), 0f, 0f);
        }
        else
        {
            return new Vector3(0f, 0f, Mathf.Sign(fromExitToHere.z));
        }
    }

    public Vector3 GetDestinationPosition()
    {
        if (spawnPoint != null) return spawnPoint.position;
        if (exitPoint == null) return transform.position;

        var targetTunnel = exitPoint.GetComponent<WarpTunnel>();
        if (targetTunnel != null && targetTunnel.spawnPoint != null)
            return targetTunnel.spawnPoint.position;

        Vector3 cardinalDir = GetCardinalDirection();
        Vector3 basePos = exitPoint.position;

        // Desplazar únicamente sobre el eje del pasillo, manteniendo centrada la otra coordenada
        if (cardinalDir.x != 0f)
        {
            return new Vector3(basePos.x + cardinalDir.x * inwardOffset, basePos.y, basePos.z);
        }
        else
        {
            return new Vector3(basePos.x, basePos.y, basePos.z + cardinalDir.z * inwardOffset);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (exitPoint == null && spawnPoint == null) return;

        // Solo teletransportar al jugador local
        var player = other.GetComponent<PlayerController>();
        if (player == null || !player.isLocal) return;

        // Cooldown para evitar rebote inmediato
        if (Time.time - lastWarpTime < cooldown) return;
        lastWarpTime = Time.time;

        Vector3 destination = GetDestinationPosition();
        destination.y = other.transform.position.y; // Mantener la altura del suelo del jugador

        // Teletransportar al jugador (desactiva CC, mueve y sincroniza por red)
        // NOTA: NO cambiar player.transform.rotation porque el modelo 3D del jugador
        // tiene el pivote descentrado y cualquier cambio de rotación desplaza el collider contra el muro.
        player.Teleport(destination);

        // Efectos
        if (warpVfx != null) warpVfx.Play();

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayWarp(destination);
        }

        // Vibración en móvil
        player.TriggerVibration();
    }

    void OnDrawGizmos()
    {
        // Dibujar el área del trigger
        var col = GetComponent<BoxCollider>();
        if (col != null)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(col.center, col.size);
            Gizmos.matrix = Matrix4x4.identity;
        }

        // Línea y esfera de destino
        if (exitPoint != null || spawnPoint != null)
        {
            Vector3 dest = GetDestinationPosition();
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, dest);

            // Esfera verde en el punto exacto de aparición
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(dest, 0.6f);

            // Flecha indicando la dirección de salida recta
            Vector3 dir = GetCardinalDirection();
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(dest, dir * 1.5f);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (exitPoint == null && spawnPoint == null) return;

        Vector3 dest = GetDestinationPosition();
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, dest);

        Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
        Gizmos.DrawSphere(dest, 1.0f);
    }
}
