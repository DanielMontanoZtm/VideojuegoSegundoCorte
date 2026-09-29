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

    CharacterController cc;
    Renderer[] rends;
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
        rends = GetComponentsInChildren<Renderer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    public void Init(int playerId, bool local, Color color)
    {
        id = playerId; isLocal = local; baseColor = color;
        cc.enabled = local;                 // los remotos no usan CharacterController
        remoteTarget = transform.position; lastPos = transform.position;
        lastVisualState = -1;
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

        Vector3 dir = new Vector3(input.x, 0f, input.y);   // cámara fija: arriba de la pantalla = +Z
        float speed = baseSpeed * (IsBoosted ? speedBoost : 1f);
        if (dir.sqrMagnitude > 0.001f)
        {
            cc.Move(dir * speed * Time.deltaTime);
            Quaternion look = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
        // Seguridad: no salir del terreno
        Vector3 p = transform.position;
        Vector3 clamped = new Vector3(Mathf.Clamp(p.x, -arenaHalf, arenaHalf), p.y, Mathf.Clamp(p.z, -arenaHalf, arenaHalf));
        if (clamped != p) cc.Move(clamped - p);
    }

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

        int state = IsBlocked ? 2 : (IsBoosted ? 1 : 0);
        if (state != lastVisualState)
        {
            lastVisualState = state;
            Color c = state == 2 ? new Color(0.4f, 0.9f, 1f) : (state == 1 ? new Color(1f, 0.95f, 0.2f) : baseColor);
            foreach (var r in rends) if (r != null && r.material.HasProperty("_Color")) r.material.color = c;
            foreach (var r in rends) if (r != null && r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
        }
    }
}
