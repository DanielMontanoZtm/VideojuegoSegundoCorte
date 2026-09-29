using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// CLIENTE de sockets. Todos los jugadores (incluido el host) usan esta clase.
/// El hilo de lectura encola líneas y Update() las entrega en el hilo principal.
public class GameClient : MonoBehaviour
{
    public static GameClient Instance { get; private set; }
    public event Action<string[]> MessageReceived;
    public bool IsConnected { get; private set; }

    TcpClient tcp;
    StreamWriter writer;
    int generation;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();

    void Awake() { Instance = this; }
    void OnDestroy() { Disconnect(); }
    void OnApplicationQuit() { Disconnect(); }

    public bool Connect(string ip, int port, out string error)
    {
        error = null;
        Disconnect();
        try
        {
            tcp = new TcpClient();
            var ar = tcp.BeginConnect(ip, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(3000)) { tcp.Close(); error = "Tiempo de espera agotado. Revisa la IP y que estén en la misma red."; return false; }
            tcp.EndConnect(ar);
            tcp.NoDelay = true;
            writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (Exception e) { error = "No se pudo conectar: " + e.Message; return false; }

        IsConnected = true;
        int g = ++generation;
        var t = tcp;
        new Thread(() => ReadLoop(t, g)) { IsBackground = true }.Start();
        return true;
    }

    void ReadLoop(TcpClient t, int g)
    {
        try
        {
            var r = new StreamReader(t.GetStream(), Encoding.UTF8);
            string line;
            while ((line = r.ReadLine()) != null) { if (g == generation) inbox.Enqueue(line); }
        }
        catch { }
        if (g == generation) inbox.Enqueue("CLOSED");
    }

    void Update()
    {
        string line;
        while (inbox.TryDequeue(out line))
        {
            var h = MessageReceived;
            if (h != null) h(line.Split('|'));
        }
    }

    public void Send(string msg)
    {
        if (!IsConnected) return;
        try { writer.WriteLine(msg); } catch { }
    }

    public void Disconnect()
    {
        IsConnected = false;
        generation++;
        try { if (tcp != null) tcp.Close(); } catch { }
        string dump; while (inbox.TryDequeue(out dump)) { }
    }
}
