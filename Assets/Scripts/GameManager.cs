using System.Collections.Generic;
using UnityEngine;

public enum GameState { Menu, Lobby, Playing, Results }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Prefabs")]
    public GameObject cherryPrefab;     
    public GameObject playerPrefab;     
    public GameObject itemPrefab;       
    public Color[] playerColors = { new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.5f, 1f), new Color(0.2f, 0.8f, 0.3f), new Color(1f, 0.7f, 0.1f) };

    [Header("Escena")]
    public TopDownCamera topCamera;
    public float spawnY = 1f;
    public float pickupRadius = 2f;

    [Header("Efectos de Audio")]
    public AudioClip orbSfx;      // 'Consumir_Punto_Normal.wav'
    public AudioClip powerUpSfx;  // 'Power_Up.wav'

    // Estado expuesto a la UI
    public GameState State { get; private set; }
    public int MyId { get; private set; }
    public float TimeLeft { get; private set; }
    public int SpeedCharges { get; private set; }
    public int BlockCharges { get; private set; }
    public int WinnerId { get; private set; }
    public string Message { get; private set; }
    public float MessageUntil { get; private set; }
    public readonly int[] Scores = new int[4];
    public readonly string[] Names = new string[4];
    public readonly bool[] Connected = new bool[4];

    public bool IsHost { get { return GameServer.Instance != null && GameServer.Instance.IsRunning; } }
    public int ConnectedCount { get { int n = 0; foreach (var b in Connected) if (b) n++; return n; } }

    readonly Dictionary<int, PlayerController> players = new Dictionary<int, PlayerController>();
    readonly Dictionary<int, CollectibleItem> items = new Dictionary<int, CollectibleItem>();
    PlayerController local;
    GameServer server;
    GameClient client;

    void Awake() { Instance = this; MyId = -1; }

    void Start()
    {
        server = GameServer.Instance;
        client = GameClient.Instance;
        client.MessageReceived += Handle;
    }

    void OnDestroy() { if (client != null) client.MessageReceived -= Handle; }

    public bool Host(string name, out string error)
    {
        if (!server.StartServer(out error)) return false;
        if (!Join("127.0.0.1", name, out error)) { server.StopServer(); return false; }
        return true;
    }

    public bool Join(string ip, string name, out string error)
    {
        if (!client.Connect(ip, server.port, out error)) return false;
        client.Send("JOIN|" + NetUtil.Clean(name));
        System.Array.Clear(Connected, 0, 4);
        State = GameState.Lobby;
        return true;
    }

    public void Leave(string msg = "")
    {
        client.Disconnect();
        if (server.IsRunning) server.StopServer();
        ClearWorld();
        State = GameState.Menu;
        MyId = -1;
        SetMessage(msg, 5f);
    }

    public void RequestStart() { if (IsHost) server.StartMatch(); }
    public void UseSpeed() { if (State == GameState.Playing) client.Send("SPEED"); }
    public void UseBlock() { if (State == GameState.Playing) client.Send("BLOCK"); }
    public void SetMessage(string m, float dur = 2f) { Message = m; MessageUntil = Time.time + dur; }

    void Handle(string[] p)
    {
        try
        {
            switch (p[0])
            {
                case "WELCOME": MyId = int.Parse(p[1]); break;
                case "FULL": Leave("Sala llena o partida en curso."); break;
                case "CLOSED": if (State != GameState.Menu) Leave("Se perdió la conexión con el host."); break;
                case "LOBBY":
                    System.Array.Clear(Connected, 0, 4);
                    foreach (var e in p[1].Split(';'))
                    {
                        var kv = e.Split(':'); int id = int.Parse(kv[0]);
                        Connected[id] = true; Names[id] = kv[1];
                    }
                    break;
                case "START": StartMatch(p); break;
                case "POS":
                    PlayerController pc;
                    if (players.TryGetValue(int.Parse(p[1]), out pc)) pc.SetRemote(NetUtil.P(p[2]), NetUtil.P(p[3]), NetUtil.P(p[4]));
                    break;
                case "ITEM":
                    {
                        int itemId = int.Parse(p[1]), pid = int.Parse(p[2]);
                        if (pid >= 0 && pid < 4) Scores[pid] = int.Parse(p[3]);

                        CollectibleItem it;
                        if (itemId >= 0 && items.TryGetValue(itemId, out it))
                        {
                            // Reproduce el SFX mediante AudioManager
                            bool isCherry = it.gameObject.name.ToLower().Contains("fruta") || it.gameObject.name.ToLower().Contains("cherry");
                            if (AudioManager.Instance != null)
                            {
                                if (isCherry) AudioManager.Instance.PlayPowerUpPickup();
                                else AudioManager.Instance.PlayOrbPickup();
                            }

                            if (pid == MyId && local != null) local.TriggerVibration();

                            Destroy(it.gameObject); 
                            items.Remove(itemId);
                        }
                        break;
                    }
                case "PU": SpeedCharges = int.Parse(p[1]); BlockCharges = int.Parse(p[2]); break;
                case "SPEEDON":
                    {
                        PlayerController s;
                        if (players.TryGetValue(int.Parse(p[1]), out s)) s.ApplySpeed(NetUtil.P(p[2]));
                        if (int.Parse(p[1]) == MyId) 
                        {
                            SetMessage("¡Velocidad!");
                            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUpPickup();
                            if (local != null) local.TriggerVibration();
                        }
                        break;
                    }
                case "BLOCKED":
                    {
                        int target = int.Parse(p[1]);
                        PlayerController b;
                        if (players.TryGetValue(target, out b)) b.ApplyBlock(NetUtil.P(p[2]));
                        if (target == MyId) SetMessage("¡Te bloquearon!", 2.5f);
                        else if (int.Parse(p[3]) == MyId) 
                        {
                            SetMessage("¡Rival bloqueado!");
                            if (AudioManager.Instance != null) AudioManager.Instance.PlayPowerUpPickup();
                            if (local != null) local.TriggerVibration();
                        }
                        break;
                    }
                case "DENY": SetMessage("No disponible (sin cargas, enfriamiento o rival fuera de rango)"); break;
                case "TIME": TimeLeft = float.Parse(p[1]); break;
                case "LEFT":
                    {
                        int id = int.Parse(p[1]); Connected[id] = false;
                        PlayerController l;
                        if (players.TryGetValue(id, out l)) { Destroy(l.gameObject); players.Remove(id); }
                        break;
                    }
                case "END":
                    foreach (var e in p[1].Split(';')) { var kv = e.Split(':'); Scores[int.Parse(kv[0])] = int.Parse(kv[1]); }
                    WinnerId = int.Parse(p[2]);
                    State = GameState.Results;
                    break;
            }
        }
        catch (System.Exception e) { Debug.LogWarning("Error procesando " + string.Join("|", p) + ": " + e.Message); }
    }

    void StartMatch(string[] p)
    {
        ClearWorld();
        System.Array.Clear(Scores, 0, 4);
        TimeLeft = NetUtil.P(p[1]);
        WinnerId = -1;

        System.Array.Clear(Connected, 0, 4);
        foreach (var e in p[2].Split(';'))
        {
            var v = e.Split(':'); int id = int.Parse(v[0]);
            Connected[id] = true;
            var pos = new Vector3(NetUtil.P(v[1]), spawnY, NetUtil.P(v[2]));
            var go = Instantiate(playerPrefab, pos, Quaternion.identity);
            var pc = go.GetComponent<PlayerController>();
            if (pc == null) pc = go.AddComponent<PlayerController>();
            pc.Init(id, id == MyId, playerColors[id % playerColors.Length]);
            players[id] = pc;
            if (id == MyId) { local = pc; if (topCamera != null) topCamera.SetTarget(go.transform); }
        }

        foreach (var e in p[3].Split(';'))
        {
            var v = e.Split(':'); 
            int id = int.Parse(v[0]);
            float x = NetUtil.P(v[1]);
            float z = NetUtil.P(v[2]);
            int type = (v.Length > 3) ? int.Parse(v[3]) : 0; 

            GameObject prefabToUse = (type == 1 && cherryPrefab != null) ? cherryPrefab : itemPrefab;

            var go = Instantiate(prefabToUse, new Vector3(x, spawnY, z), prefabToUse.transform.rotation);
            var ci = go.GetComponent<CollectibleItem>();
            if (ci == null) ci = go.AddComponent<CollectibleItem>();
            ci.id = id;
            items[id] = ci;
        }
        State = GameState.Playing;
        SetMessage("¡Ya!", 1.5f);
    }

    void ClearWorld()
    {
        foreach (var pc in players.Values) if (pc != null) Destroy(pc.gameObject);
        foreach (var it in items.Values) if (it != null) Destroy(it.gameObject);
        players.Clear(); items.Clear(); local = null;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.H)) 
        { 
            string err; 
            if (Host("Prueba", out err)) Debug.Log("HOST CREADO. Presiona Enter para iniciar.");
            else Debug.LogError("ERROR AL CREAR HOST: " + err);
        }
        if (Input.GetKeyDown(KeyCode.Return)) 
        { 
            if (IsHost) GameServer.Instance.StartMatch();
        }

        if (State != GameState.Playing || local == null) return;
        Vector3 lp = local.Center;  
        foreach (var kv in items)
        {
            var it = kv.Value;
            if (it == null) continue;
            Vector3 d = it.transform.position - lp; d.y = 0;
            if (d.sqrMagnitude <= pickupRadius * pickupRadius)
            {
                client.Send("PICK|" + kv.Key);
            }
        }
    }
}