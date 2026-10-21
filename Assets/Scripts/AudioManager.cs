using UnityEngine;

/// Gestiona la música de fondo y los efectos de sonido globales del juego.
/// Colocar en un GameObject vacío de la escena (ej: "AudioManager").
///
/// Asignar en el Inspector:
///   - backgroundMusic  → Audio/Music.wav
///   - orbPickupSfx     → Audio/Consumir_Punto_Normal.wav
///   - powerUpPickupSfx → Audio/Power_Up.wav
///   - powerUpLoopSfx   → Audio/Duracion_Power Up.wav
///   - collisionSfx     → Audio/Sonido_Colision.wav
///
/// El AudioManager reproduce automáticamente la música al iniciar la partida,
/// la pausa en el lobby/resultados y la reanuda al volver a jugar.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Música de Fondo")]
    public AudioClip backgroundMusic;
    [Range(0f, 1f)] public float musicVolume = 0.35f;
    public bool fadeInMusic = true;
    public float fadeDuration = 2f;

    [Header("Efectos de Sonido")]
    public AudioClip orbPickupSfx;       // Consumir_Punto_Normal.wav
    public AudioClip powerUpPickupSfx;   // Power_Up.wav
    public AudioClip powerUpLoopSfx;     // Duracion_Power Up.wav
    public AudioClip collisionSfx;       // Sonido_Colision.wav
    public AudioClip warpSfx;            // (Opcional) sonido al teletransportarse por un túnel
    [Range(0f, 1f)] public float sfxVolume = 0.8f;

    [Header("Configuración")]
    public bool playMusicOnMenu = true;

    AudioSource musicSource;
    float targetMusicVolume;
    bool isFading;
    GameState lastState = (GameState)(-1);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Crear AudioSource dedicado para la música de fondo
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f; // 2D
        musicSource.priority = 0;      // máxima prioridad
    }

    void Start()
    {
        if (playMusicOnMenu && backgroundMusic != null)
        {
            PlayMusic();
        }
    }

    void Update()
    {
        // Fade in suave de la música
        if (isFading && musicSource.isPlaying)
        {
            musicSource.volume = Mathf.MoveTowards(musicSource.volume, targetMusicVolume, (musicVolume / fadeDuration) * Time.deltaTime);
            if (Mathf.Approximately(musicSource.volume, targetMusicVolume)) isFading = false;
        }

        // Control automático según el estado del juego
        if (GameManager.Instance != null)
        {
            GameState state = GameManager.Instance.State;
            if (state != lastState)
            {
                lastState = state;
                OnStateChanged(state);
            }
        }
    }

    void OnStateChanged(GameState state)
    {
        switch (state)
        {
            case GameState.Menu:
                if (playMusicOnMenu) PlayMusic();
                else PauseMusic();
                break;
            case GameState.Lobby:
                // Mantiene la música del menú a volumen reducido
                SetMusicVolume(musicVolume * 0.5f);
                break;
            case GameState.Playing:
                PlayMusic();
                SetMusicVolume(musicVolume);
                break;
            case GameState.Results:
                SetMusicVolume(musicVolume * 0.3f);
                break;
        }
    }

    // ─────────────────────── Música ───────────────────────

    public void PlayMusic()
    {
        if (backgroundMusic == null) return;
        if (musicSource.clip != backgroundMusic) musicSource.clip = backgroundMusic;
        if (!musicSource.isPlaying)
        {
            if (fadeInMusic)
            {
                musicSource.volume = 0f;
                targetMusicVolume = musicVolume;
                isFading = true;
            }
            else
            {
                musicSource.volume = musicVolume;
            }
            musicSource.Play();
        }
    }

    public void PauseMusic() { if (musicSource.isPlaying) musicSource.Pause(); }
    public void ResumeMusic() { musicSource.UnPause(); }
    public void StopMusic() { musicSource.Stop(); }

    public void SetMusicVolume(float vol)
    {
        targetMusicVolume = vol;
        isFading = true;
    }

    // ─────────────────────── Efectos de Sonido ───────────────────────

    /// Reproduce un SFX en 2D (volumen directo, sin atenuación por distancia).
    public void PlaySfx2D(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        // Reproduce en la posición de la cámara para que siempre se escuche al 100%
        Vector3 listenerPos = Camera.main != null ? Camera.main.transform.position : transform.position;
        AudioSource.PlayClipAtPoint(clip, listenerPos, sfxVolume * volumeScale);
    }

    /// Reproduce un SFX en 3D (atenuación por distancia respecto al listener).
    public void PlaySfx3D(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, sfxVolume * volumeScale);
    }

    // ─── Métodos de conveniencia para cada evento del juego ───

    public void PlayOrbPickup()      { PlaySfx2D(orbPickupSfx); }
    public void PlayPowerUpPickup()  { PlaySfx2D(powerUpPickupSfx); }
    public void PlayCollision(Vector3 pos) { PlaySfx3D(collisionSfx, pos, 0.7f); }
    public void PlayWarp(Vector3 pos)
    {
        if (warpSfx != null) PlaySfx3D(warpSfx, pos, 0.9f);
        else PlaySfx2D(powerUpPickupSfx, 0.5f); // fallback: usa power-up atenuado
    }
}
