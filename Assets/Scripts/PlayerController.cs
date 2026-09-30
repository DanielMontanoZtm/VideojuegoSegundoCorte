using UnityEngine;

/// Personaje. El jugador local se mueve con el joystick (o WASD en el editor) y envía su posición;
/// los remotos se interpolan hacia la última posición recibida.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public int id;
    public bool isLocal;

    [Header("Movimiento")]
    public float baseSpeed = 8f;
    public float speedBoost = 1.8f;
    public float turnSpeed = 720f;
    public float arenaHalf = 49f;
    public float sendRate = 15f;

    [Header("Animación (opcional)")]
    public Animator animator;   // parámetros: Speed(float), Boost(bool), Blocked(bool)

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int BoostHash = Animator.StringToHash("Boost");
    static readonly int BlockedHash = Animator.StringToHash("Blocked");

    [Header("Efectos Temáticos")]
    public TrailRenderer speedTrail;

    CharacterController cc;
    Renderer[] rends;
    Color[] originalColors;   // colores originales de cada renderer
    Color baseColor = Color.white;
    float speedUntil, blockUntil, sendTimer;
    Vector3 remoteTarget, lastPos;
    float remoteRot;
    int lastVisualState = -1;

    public bool IsBlocked { get { return Time.time < blockUntil; } }
    public bool IsBoosted { get { return Time.time < speedUntil; } }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        rends = GetComponents<Renderer>();  // SOLO el objeto raíz, NO los hijos (ojos)
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (speedTrail == null) speedTrail = GetComponentInChildren<TrailRenderer>();
    }

    public void Init(int playerId, bool local, Color color)
    {
        id = playerId; isLocal = local; baseColor = color;
        cc.enabled = local;
        remoteTarget = transform.position; lastPos = transform.position;
        lastVisualState = -1;

        // Fijar rotación: el fantasma siempre mira hacia la derecha y queda derecho
        transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        // Guardar colores originales y pintar solo las partes "neutras" (cuerpo)
        originalColors = new Color[rends.Length];
        for (int i = 0; i < rends.Length; i++)
        {
            Material mat = rends[i].material;
            if (mat.HasProperty("_BaseColor")) originalColors[i] = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color")) originalColors[i] = mat.color;
            else originalColors[i] = Color.white;
        }
        ApplyColor(baseColor);
    }

    public void SetRemote(float x, float z, float rotY) { remoteTarget = new Vector3(x, transform.position.y, z); remoteRot = rotY; }
    public void ApplySpeed(float dur) { speedUntil = Time.time + dur; }
    public void ApplyBlock(float dur) { blockUntil = Time.time + dur; }

    void Update()
    {
        bool playing = GameManager.Instance != null && GameManager.Instance.State == GameState.Playing;
        if (isLocal) { if (playing) LocalMove(); SendPosition(playing); }
        else RemoteMove();
        UpdateVisuals();
    }

    void LocalMove()
    {
        if (IsBlocked) return;
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (VirtualJoystick.Instance != null) input += VirtualJoystick.Instance.Value;
        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 dir = new Vector3(input.x, 0f, input.y);
        float speed = baseSpeed * (IsBoosted ? speedBoost : 1f);
        if (dir.sqrMagnitude > 0.001f)
        {
            cc.Move(dir * speed * Time.deltaTime);
            // Los fantasmas de Pac-Man no rotan, siempre miran al frente
        }
    }

    /// Centro real del fantasma (para detección de pickup precisa)
    public Vector3 Center { get { return cc != null ? cc.bounds.center : transform.position; } }

    void SendPosition(bool playing)
    {
        if (!playing) return;
        sendTimer += Time.deltaTime;
        if (sendTimer < 1f / sendRate) return;
        sendTimer = 0f;
        Vector3 p = transform.position;
        GameClient.Instance.Send("POS|" + NetUtil.F(p.x) + "|" + NetUtil.F(p.z) + "|" + NetUtil.F(transform.eulerAngles.y));
    }

    void RemoteMove()
    {
        float t = 1f - Mathf.Exp(-15f * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, remoteTarget, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, remoteRot, 0), t);
    }

    void UpdateVisuals()
    {
        float v = Time.deltaTime > 0 ? ((transform.position - lastPos).magnitude / Time.deltaTime) / baseSpeed : 0f;
        lastPos = transform.position;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(SpeedHash, v);
            animator.SetBool(BoostHash, IsBoosted);
            animator.SetBool(BlockedHash, IsBlocked);
        }

        if (speedTrail != null) speedTrail.emitting = IsBoosted;

        int state = IsBlocked ? 2 : (IsBoosted ? 1 : 0);
        if (state != lastVisualState)
        {
            lastVisualState = state;
            Color c = state == 2 ? new Color(0.4f, 0.9f, 1f) : (state == 1 ? new Color(1f, 0.95f, 0.2f) : baseColor);
            ApplyColor(c);
        }
    }

    /// Solo pinta renderers cuyo color original es cercano a blanco/gris (el cuerpo).
    /// Deja intactos los renderers con colores únicos (ojos, pupilas, etc.).
    void ApplyColor(Color c)
    {
        if (originalColors == null) return;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            // Si el color original era muy saturado o muy oscuro, es un detalle (ojos) → no tocar
            float sat, val, hue;
            Color.RGBToHSV(originalColors[i], out hue, out sat, out val);
            if (sat > 0.3f || val < 0.3f) continue;  // es un detalle con color propio, no pintar

            if (rends[i].material.HasProperty("_BaseColor")) rends[i].material.SetColor("_BaseColor", c);
            else if (rends[i].material.HasProperty("_Color")) rends[i].material.color = c;
        }
    }
}
