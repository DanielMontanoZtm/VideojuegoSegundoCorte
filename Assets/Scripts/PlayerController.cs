using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public int id;
    public bool isLocal;

    [Header("Movimiento")]
    public float baseSpeed = 14f;
    public float speedBoost = 2.2f;
    public float turnSpeed = 720f;
    public float arenaHalf = 49f;
    public float sendRate = 15f;

    [Header("Animación (opcional)")]
    public Animator animator;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int BoostHash = Animator.StringToHash("Boost");
    static readonly int BlockedHash = Animator.StringToHash("Blocked");

    [Header("Efectos Temáticos")]
    public TrailRenderer speedTrail;

    [Header("Configuración de Apariencia")]
    public Renderer bodyRenderer;

    [Header("Audio y Vibración")]
    public AudioSource powerUpLoopSource; // AudioSource con 'Duracion_Power Up.wav' (Loop = true)
    public AudioClip collisionSfx;        // 'Sonido_Colision.wav'
    public float collisionCooldown = 0.3f;
    float lastCollisionTime;

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
        if (speedTrail == null) speedTrail = GetComponentInChildren<TrailRenderer>();
        if (powerUpLoopSource == null) powerUpLoopSource = GetComponent<AudioSource>();
    }

    public void Init(int playerId, bool local, Color color)
    {
        id = playerId; isLocal = local; baseColor = color;
        cc.enabled = local;
        remoteTarget = transform.position; lastPos = transform.position;
        lastVisualState = -1;

        transform.rotation = Quaternion.Euler(0f, 180f, 0f);
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
        HandleAudioAndVibration();
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
            Vector3 moveVelocity = dir * speed;
            cc.Move(moveVelocity * Time.deltaTime);
        }
    }

    /// Detecta colisiones físicas con paredes
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!isLocal) return;

        // Si choca contra una pared o muro del escenario
        if (hit.gameObject.name.ToLower().Contains("wall") || hit.gameObject.name.ToLower().Contains("muro") || hit.gameObject.CompareTag("Untagged"))
        {
            if (Time.time - lastCollisionTime >= collisionCooldown)
            {
                lastCollisionTime = Time.time;
                if (collisionSfx != null) AudioSource.PlayClipAtPoint(collisionSfx, transform.position, 0.7f);

                // Activa la respuesta háptica/vibración en el smartphone
                TriggerVibration();
            }
        }
    }

    void HandleAudioAndVibration()
    {
        // Sonido en bucle mientras dura el power-up activo
        if (powerUpLoopSource != null)
        {
            if (IsBoosted && !powerUpLoopSource.isPlaying)
            {
                powerUpLoopSource.Play();
            }
            else if (!IsBoosted && powerUpLoopSource.isPlaying)
            {
                powerUpLoopSource.Stop();
            }
        }
    }

    public void TriggerVibration()
    {
        #if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
        #endif
    }

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

    void ApplyColor(Color c)
    {
        if (bodyRenderer != null)
        {
            Material mat = bodyRenderer.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else if (mat.HasProperty("_Color")) mat.color = c;
        }
    }
}