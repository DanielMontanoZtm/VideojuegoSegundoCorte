using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Random = UnityEngine.Random;

/// SERVIDOR (lo ejecuta el host). Es la autoridad de: objetos recogidos, puntajes,
/// power-ups, bloqueos y temporizador. Los hilos de red solo encolan mensajes;
/// todo se procesa en Update() (hilo principal de Unity).
///
/// Protocolo (texto, 1 mensaje por línea, campos separados por '|'):
///  Cliente -> Servidor: JOIN|nombre  POS|x|z|rotY  PICK|itemId  SPEED  BLOCK
///  Servidor -> Clientes: WELCOME|id  FULL  LOBBY|id:nombre;...  START|dur|id:x:z;...|i:x:z;...
///     POS|id|x|z|rotY  ITEM|itemId|playerId|score  PU|cargasVel|cargasBloq
///     SPEEDON|id|dur  BLOCKED|idObjetivo|dur|idAutor  TIME|seg  END|id:score;...|ganador  LEFT|id  DENY
public class GameServer : MonoBehaviour
{
    public static GameServer Instance { get; private set; }

    [Header("Red")]
    public int port = 7777;

    [Header("Partida")]
    public int minPlayers = 2;
    public int maxPlayers = 4;
    public float matchDuration = 120f;
    public int itemCount = 50;
    public float arenaHalf = 50f;        // terreno 100x100 -> de -50 a 50
    public float pickupRadius = 2f;      // el servidor acepta hasta 2.5x por la latencia

    [Header("Power-ups")]
    public float speedDuration = 4f;     // 3-6 s
    public float blockDuration = 3f;     // 2-4 s
    public float speedCooldown = 8f;
    public float blockCooldown = 8f;
    public float blockRange = 30f;       // distancia máxima para bloquear al rival más cercano
    public int itemsPerCharge = 5;       // cada N objetos recogidos = +1 carga de cada power-up
    public int startCharges = 1;

    class Conn
    {
        public int id; public TcpClient tcp; public StreamWriter writer;
        public string name = "Jugador"; public Vector2 pos; public int score;
        public int speedCharges, blockCharges;
        public float nextSpeedTime, nextBlockTime, blockedUntil;
    }
    struct Incoming { public int id; public string msg; public TcpClient tcp; }
    class ItemData { public Vector2 pos; public bool taken; }

    TcpListener listener;
    Thread acceptThread;
    volatile bool running;
    readonly ConcurrentQueue<Incoming> inbox = new ConcurrentQueue<Incoming>();
    readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
    readonly List<ItemData> items = new List<ItemData>();
    bool playing;
    float timeLeft, lastTimeSent;

    public bool IsRunning { get { return running; } }
    public int PlayerCount { get { return conns.Count; } }
    public bool IsPlaying { get { return playing; } }

    void Awake() { Instance = this; }
    void OnDestroy() { StopServer(); }
    void OnApplicationQuit() { StopServer(); }

    // ------------------------------------------------------------ arranque / parada
    public bool StartServer(out string error)
    {
        error = null;
        if (running) return true;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
        }
        catch (System.Exception e) { error = e.Message; return false; }
        running = true;
        acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        acceptThread.Start();
        return true;
    }

    public void StopServer()
    {
        running = false;
        try { if (listener != null) listener.Stop(); } catch { }
        foreach (var c in conns.Values) { try { c.tcp.Close(); } catch { } }
        conns.Clear(); items.Clear();
        playing = false;
        Incoming dump; while (inbox.TryDequeue(out dump)) { }
    }

    void AcceptLoop()
    {
        while (running)
        {
            try { var t = listener.AcceptTcpClient(); inbox.Enqueue(new Incoming { id = -1, tcp = t }); }
            catch { if (!running) break; }
        }
    }

    void ReadLoop(int id, TcpClient tcp)
    {
        try
        {
            var r = new StreamReader(tcp.GetStream(), Encoding.UTF8);
            string line;
            while (running && (line = r.ReadLine()) != null)
                inbox.Enqueue(new Incoming { id = id, msg = line });
        }
        catch { }
        inbox.Enqueue(new Incoming { id = id, msg = "DISCONNECT" });
    }

    // ------------------------------------------------------------ bucle principal
    void Update()
    {
        Incoming inc;
        while (inbox.TryDequeue(out inc))
        {
            if (inc.tcp != null) OnNewClient(inc.tcp);
            else
            {
                Conn c;
                if (conns.TryGetValue(inc.id, out c))
                {
                    try { OnMessage(c, inc.msg); } catch (System.Exception e) { Debug.LogWarning("Msg inválido: " + inc.msg + " " + e.Message); }
                }
            }
        }

        if (playing)
        {
            timeLeft -= Time.deltaTime;
            if (Time.time - lastTimeSent >= 1f)
            {
                lastTimeSent = Time.time;
                Broadcast("TIME|" + Mathf.CeilToInt(Mathf.Max(0, timeLeft)));
            }
            if (timeLeft <= 0f) EndMatch();
        }
    }

    void OnNewClient(TcpClient tcp)
    {
        if (playing || conns.Count >= maxPlayers)
        {
            try { var w = new StreamWriter(tcp.GetStream()) { AutoFlush = true }; w.WriteLine("FULL"); } catch { }
            try { tcp.Close(); } catch { }
            return;
        }
        int id = 0; while (conns.ContainsKey(id)) id++;
        tcp.NoDelay = true;
        var c = new Conn { id = id, tcp = tcp, writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true } };
        conns[id] = c;
        var t = new Thread(() => ReadLoop(id, tcp)) { IsBackground = true };
        t.Start();
        Send(c, "WELCOME|" + id);
        BroadcastLobby();
    }

    void OnMessage(Conn c, string msg)
    {
        string[] p = msg.Split('|');
        switch (p[0])
        {
            case "JOIN": c.name = NetUtil.Clean(p.Length > 1 ? p[1] : ""); BroadcastLobby(); break;
            case "POS":
                if (!playing) return;
                c.pos = new Vector2(NetUtil.P(p[1]), NetUtil.P(p[2]));
                BroadcastExcept(c.id, "POS|" + c.id + "|" + p[1] + "|" + p[2] + "|" + p[3]);
                break;
            case "PICK": TryPick(c, int.Parse(p[1])); break;
            case "SPEED": UseSpeed(c); break;
            case "BLOCK": UseBlock(c); break;
            case "DISCONNECT": RemoveConn(c); break;
        }
    }

    // ------------------------------------------------------------ partida
    /// Lo llama el host desde la UI (botón Iniciar / Reintentar).
    public bool StartMatch()
    {
        if (playing || conns.Count < minPlayers) return false;

        var spawns = new List<Vector2>();
        var sbP = new StringBuilder();
        foreach (var c in conns.Values)
        {
            Vector2 s; int tries = 0;
            do { s = RandomPoint(arenaHalf - 5f); tries++; }
            while (tries < 100 && spawns.Exists(o => Vector2.Distance(o, s) < 15f)); // sin superposición
            spawns.Add(s);
            c.pos = s; c.score = 0; c.blockedUntil = 0;
            c.speedCharges = startCharges; c.blockCharges = startCharges;
            c.nextSpeedTime = c.nextBlockTime = 0;
            if (sbP.Length > 0) sbP.Append(';');
            sbP.Append(c.id).Append(':').Append(NetUtil.F(s.x)).Append(':').Append(NetUtil.F(s.y));
        }

        items.Clear();
        var sbI = new StringBuilder();
        for (int i = 0; i < itemCount; i++)
        {
            Vector2 s; int tries = 0; bool bad;
            do
            {
                s = RandomPoint(arenaHalf - 3f); tries++;
                bad = spawns.Exists(o => Vector2.Distance(o, s) < 4f) || items.Exists(o => Vector2.Distance(o.pos, s) < 3f);
            } while (bad && tries < 100);
            items.Add(new ItemData { pos = s });
            if (i > 0) sbI.Append(';');
            sbI.Append(i).Append(':').Append(NetUtil.F(s.x)).Append(':').Append(NetUtil.F(s.y));
        }

        playing = true; timeLeft = matchDuration; lastTimeSent = Time.time;
        Broadcast("START|" + NetUtil.F(matchDuration) + "|" + sbP + "|" + sbI);
        foreach (var c in conns.Values) SendCharges(c);
        return true;
    }

    void TryPick(Conn c, int itemId)
    {
        if (!playing || itemId < 0 || itemId >= items.Count) return;
        var it = items[itemId];
        if (it.taken) return;                                   // conflicto: gana el primero en llegar
        if (Vector2.Distance(c.pos, it.pos) > pickupRadius * 2.5f) return; // validación del servidor
        it.taken = true;
        c.score++;
        Broadcast("ITEM|" + itemId + "|" + c.id + "|" + c.score);
        if (c.score % itemsPerCharge == 0)
        {
            c.speedCharges++; c.blockCharges++;
            SendCharges(c);
        }
        if (items.TrueForAll(i => i.taken)) EndMatch();
    }

    void UseSpeed(Conn c)
    {
        if (!playing || c.speedCharges <= 0 || Time.time < c.nextSpeedTime) { Send(c, "DENY"); return; }
        c.speedCharges--; c.nextSpeedTime = Time.time + speedCooldown;
        Broadcast("SPEEDON|" + c.id + "|" + NetUtil.F(speedDuration));
        SendCharges(c);
    }

    void UseBlock(Conn c)
    {
        if (!playing || c.blockCharges <= 0 || Time.time < c.nextBlockTime) { Send(c, "DENY"); return; }
        Conn best = null; float bestD = blockRange;
        foreach (var o in conns.Values)
        {
            if (o.id == c.id || Time.time < o.blockedUntil) continue;
            float d = Vector2.Distance(o.pos, c.pos);
            if (d <= bestD) { bestD = d; best = o; }
        }
        if (best == null) { Send(c, "DENY"); return; }         // no se gasta la carga
        c.blockCharges--; c.nextBlockTime = Time.time + blockCooldown;
        best.blockedUntil = Time.time + blockDuration;
        Broadcast("BLOCKED|" + best.id + "|" + NetUtil.F(blockDuration) + "|" + c.id);
        SendCharges(c);
    }

    void EndMatch()
    {
        if (!playing) return;
        playing = false;
        var sb = new StringBuilder(); int best = -1, winner = -1; bool tie = false;
        foreach (var c in conns.Values)
        {
            if (sb.Length > 0) sb.Append(';');
            sb.Append(c.id).Append(':').Append(c.score);
            if (c.score > best) { best = c.score; winner = c.id; tie = false; }
            else if (c.score == best) tie = true;
        }
        Broadcast("END|" + sb + "|" + (tie ? -1 : winner));
    }

    void RemoveConn(Conn c)
    {
        if (!conns.ContainsKey(c.id)) return;
        conns.Remove(c.id);
        try { c.tcp.Close(); } catch { }
        Broadcast("LEFT|" + c.id);
        if (!playing) BroadcastLobby();
    }

    // ------------------------------------------------------------ envío
    void SendCharges(Conn c) { Send(c, "PU|" + c.speedCharges + "|" + c.blockCharges); }

    void BroadcastLobby()
    {
        var sb = new StringBuilder("LOBBY|");
        bool first = true;
        foreach (var c in conns.Values)
        {
            if (!first) sb.Append(';'); first = false;
            sb.Append(c.id).Append(':').Append(c.name);
        }
        Broadcast(sb.ToString());
    }

    void Send(Conn c, string msg) { try { c.writer.WriteLine(msg); } catch { } }
    void Broadcast(string msg) { foreach (var c in conns.Values) Send(c, msg); }
    void BroadcastExcept(int id, string msg) { foreach (var c in conns.Values) if (c.id != id) Send(c, msg); }

    Vector2 RandomPoint(float half) { return new Vector2(Random.Range(-half, half), Random.Range(-half, half)); }
}
