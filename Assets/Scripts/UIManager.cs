using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Paneles (menú, lobby, HUD, resultados) y botones. Se actualiza según GameManager.State.
public class UIManager : MonoBehaviour
{
    [Header("Paneles")]
    public GameObject menuPanel, lobbyPanel, gamePanel, resultsPanel;

    [Header("Menú")]
    public TMP_InputField nameInput, ipInput;
    public Button hostButton, joinButton, quitButton;
    public TMP_Text menuMessage;

    [Header("Lobby")]
    public TMP_Text lobbyText;
    public Button startButton, leaveButton;

    [Header("HUD")]
    public TMP_Text timerText, scoreText, rankingText, messageText, speedChargesText, blockChargesText;
    public Button speedButton, blockButton;

    [Header("Resultados")]
    public TMP_Text resultsText;
    public Button retryButton, menuButton;

    GameManager gm;
    GameState shown = (GameState)(-1);
    float refresh;
    string hostIp = "";

    void Start()
    {
        gm = GameManager.Instance;
        hostButton.onClick.AddListener(OnHost);
        joinButton.onClick.AddListener(OnJoin);
        quitButton.onClick.AddListener(Application.Quit);
        startButton.onClick.AddListener(gm.RequestStart);
        leaveButton.onClick.AddListener(() => gm.Leave());
        speedButton.onClick.AddListener(gm.UseSpeed);
        blockButton.onClick.AddListener(gm.UseBlock);
        retryButton.onClick.AddListener(gm.RequestStart);
        menuButton.onClick.AddListener(() => gm.Leave());
        if (ipInput != null && string.IsNullOrEmpty(ipInput.text)) ipInput.text = "192.168.1.";
    }

    void OnHost()
    {
        string err;
        if (gm.Host(nameInput.text, out err)) hostIp = NetUtil.LocalIP();
        else gm.SetMessage(err, 5f);
    }

    void OnJoin()
    {
        string err;
        if (!gm.Join(ipInput.text.Trim(), nameInput.text, out err)) gm.SetMessage(err, 5f);
    }

    void Update()
    {
        if (gm.State != shown)
        {
            shown = gm.State;
            menuPanel.SetActive(shown == GameState.Menu);
            lobbyPanel.SetActive(shown == GameState.Lobby);
            gamePanel.SetActive(shown == GameState.Playing);
            resultsPanel.SetActive(shown == GameState.Results);
        }

        refresh -= Time.deltaTime;
        if (refresh > 0f) return;
        refresh = 0.15f;

        string msg = Time.time < gm.MessageUntil ? gm.Message : "";
        switch (gm.State)
        {
            case GameState.Menu: menuMessage.text = msg; break;
            case GameState.Lobby: RefreshLobby(); break;
            case GameState.Playing: RefreshHud(msg); break;
            case GameState.Results: RefreshResults(); break;
        }
    }

    void RefreshLobby()
    {
        var sb = new StringBuilder();
        if (gm.IsHost) sb.AppendLine("IP para que se unan: " + hostIp);
        sb.AppendLine("Jugadores (" + gm.ConnectedCount + "/4):");
        for (int i = 0; i < 4; i++)
            if (gm.Connected[i]) sb.AppendLine("• " + gm.Names[i] + (i == gm.MyId ? " (tú)" : ""));
        if (!gm.IsHost) sb.AppendLine("\nEsperando a que el host inicie...");
        else if (gm.ConnectedCount < 2) sb.AppendLine("\nSe necesitan mínimo 2 jugadores.");
        lobbyText.text = sb.ToString();

        startButton.gameObject.SetActive(gm.IsHost);
        startButton.interactable = gm.IsHost && gm.ConnectedCount >= 2;
    }

    void RefreshHud(string msg)
    {
        int t = Mathf.Max(0, Mathf.CeilToInt(gm.TimeLeft));
        timerText.text = (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
        int me = Mathf.Clamp(gm.MyId, 0, 3);
        scoreText.text = gm.Names[me] + "\nObjetos: " + gm.Scores[me];
        rankingText.text = Ranking();
        messageText.text = msg;
        speedChargesText.text = "x" + gm.SpeedCharges;
        blockChargesText.text = "x" + gm.BlockCharges;
        speedButton.interactable = gm.SpeedCharges > 0;
        blockButton.interactable = gm.BlockCharges > 0;
    }

    void RefreshResults()
    {
        var sb = new StringBuilder();
        sb.AppendLine(gm.WinnerId < 0 ? "¡EMPATE!" : "GANADOR: " + gm.Names[gm.WinnerId]);
        sb.AppendLine();
        sb.Append(Ranking());
        resultsText.text = sb.ToString();
        retryButton.gameObject.SetActive(gm.IsHost);
    }

    string Ranking()
    {
        var ids = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 4; i++) if (gm.Connected[i]) ids.Add(i);
        ids.Sort((a, b) => gm.Scores[b].CompareTo(gm.Scores[a]));
        var sb = new StringBuilder();
        for (int i = 0; i < ids.Count; i++)
            sb.AppendLine((i + 1) + ". " + gm.Names[ids[i]] + "  " + gm.Scores[ids[i]]);
        return sb.ToString();
    }
}
