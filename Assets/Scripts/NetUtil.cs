using System.Globalization;
using System.Net;
using System.Net.Sockets;

/// Utilidades de red compartidas (formato de números y obtención de IP local).
public static class NetUtil
{
    public static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string F(float v) { return v.ToString("0.00", C); }
    public static float P(string s) { return float.Parse(s, C); }

    /// Quita caracteres que rompen el protocolo (| : ; ,) y limita el largo.
    public static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "Jugador";
        foreach (char ch in "|:;,") s = s.Replace(ch, ' ');
        s = s.Trim();
        return s.Length > 12 ? s.Substring(0, 12) : s;
    }

    /// IP local del dispositivo (para mostrarla en el lobby del host).
    public static string LocalIP()
    {
        try
        {
            using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                s.Connect("8.8.8.8", 65530);
                return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
            }
        }
        catch { }
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }
}
