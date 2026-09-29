using System;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using CombatPrep.Audio;
using CombatPrep.Core;
using CombatPrep.Net;
using CombatPrep.Player;

namespace CombatPrep.UI
{
    /// <summary>
    /// Online lobby: host a session (and get a join code) or join one by code, then wait for
    /// players. Two panels on one canvas - Connect, then Lobby - over a slow orbiting shot of
    /// the arena, so the wait shows the map everyone is about to fight on.
    ///
    /// Owns no networking itself: SessionService does sessions, MatchManager holds match
    /// state, and NetPlayer.All is the live player list. This only presents them.
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        public event Action OnBack;     // connect panel -> main menu
        public event Action OnLeave;    // lobby panel -> leave the session

        Font _font;
        RectTransform _root;
        GameObject _connectPanel, _lobbyPanel;
        InputField _name, _code;
        Button _host, _join, _back, _start, _leave;
        Text _status, _codeText, _players, _waiting;
        Camera _cam;
        bool _busy;
        float _orbit;

        public Camera Camera => _cam;

        void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            MainMenu.EnsureEventSystem();
            BuildCamera();
            BuildUi();
            ShowConnect();
            GameInput.LockCursor(false);
        }

        void OnDestroy()
        {
            if (_cam != null) Destroy(_cam.gameObject);
        }

        // ---------------------------------------------------------------------- camera

        void BuildCamera()
        {
            var go = new GameObject("LobbyCamera");
            _cam = go.AddComponent<Camera>();
            _cam.fieldOfView = 52f;
            _cam.farClipPlane = 600f;
            Bootstrap.ConfigureCamera(_cam);
            if (ListenerRig.I != null) ListenerRig.I.Follow = go.transform;
        }

        void Update()
        {
            // Slow orbit around the middle of the arena.
            _orbit += Time.deltaTime * 4f;
            float a = _orbit * Mathf.Deg2Rad;
            var centre = new Vector3(0f, 0f, 42f);
            _cam.transform.position = centre + new Vector3(Mathf.Sin(a) * 62f, 34f, -Mathf.Cos(a) * 62f);
            _cam.transform.LookAt(centre + Vector3.up * 2f);

            if (_lobbyPanel.activeSelf) RefreshLobby();
        }

        // -------------------------------------------------------------------- actions

        async void Host()
        {
            if (_busy) return;
            SetBusy(true, "Signing in and creating a lobby...");
            try
            {
                NetPlayer.PendingName = _name.text;
                NetworkManager.Singleton.ConnectionApprovalCallback = MatchManager.Approve;

                await SessionService.HostAsync();

                // This machine is now the server: create the shared match state.
                var match = Instantiate(NetworkPrefabRefs.I.Match);
                match.GetComponent<NetworkObject>().Spawn();

                ShowLobby();
                SetBusy(false, "");
            }
            catch (Exception e)
            {
                SetBusy(false, Friendly(e, joining: false));
                await SessionService.LeaveAsync();
                ShutdownNetwork();
            }
        }

        async void Join()
        {
            if (_busy) return;
            string code = _code.text.Trim();
            if (code.Length < 4) { SetStatus("Enter the lobby code your host shared."); return; }

            SetBusy(true, $"Joining {code.ToUpperInvariant()}...");
            try
            {
                NetPlayer.PendingName = _name.text;
                await SessionService.JoinAsync(code);
                ShowLobby();
                SetBusy(false, "");
            }
            catch (Exception e)
            {
                SetBusy(false, Friendly(e, joining: true));
                await SessionService.LeaveAsync();
                ShutdownNetwork();
            }
        }

        void StartMatch()
        {
            if (MatchManager.I != null) MatchManager.I.StartMatch();
        }

        static void ShutdownNetwork()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();
        }

        /// <summary>Turns service exceptions into something a player can act on.</summary>
        static string Friendly(Exception e, bool joining)
        {
            string m = (e.Message ?? "").ToLowerInvariant();

            if (m.Contains("project") || m.Contains("cloud") || m.Contains("environment") || m.Contains("unauthorized"))
                return "Online play isn't set up yet: link this project to Unity Cloud " +
                       "(Edit > Project Settings > Services).";
            if (joining && (m.Contains("not found") || m.Contains("404") || m.Contains("invalid")))
                return "No lobby with that code. Check it with your host.";
            if (m.Contains("full"))
                return "That lobby is full (4 players max).";
            if (m.Contains("network") || m.Contains("timeout") || m.Contains("connect"))
                return "Couldn't reach the online service. Check your internet connection.";

            return (joining ? "Couldn't join: " : "Couldn't create a lobby: ") + e.Message;
        }

        // ------------------------------------------------------------------ presentation

        void ShowConnect()
        {
            _connectPanel.SetActive(true);
            _lobbyPanel.SetActive(false);
        }

        void ShowLobby()
        {
            _connectPanel.SetActive(false);
            _lobbyPanel.SetActive(true);
            _codeText.text = SessionService.Code ?? "------";
        }

        void RefreshLobby()
        {
            var players = NetPlayer.All.Where(p => p != null && p.IsSpawned)
                                       .OrderBy(p => p.OwnerClientId).ToList();

            var sb = new StringBuilder();
            for (int i = 0; i < SessionService.MaxPlayers; i++)
            {
                if (i < players.Count)
                {
                    var p = players[i];
                    sb.Append(i + 1).Append(".   ").Append(p.PlayerName)
                      .Append("    <color=#ffffff88>").Append(p.Weapon.Def.DisplayName).Append("</color>");
                    if (p.OwnerClientId == NetworkManager.ServerClientId) sb.Append("   <color=#ff9a3c>HOST</color>");
                    if (p.IsOwner) sb.Append("   <color=#9be15d>YOU</color>");
                }
                else
                {
                    sb.Append("<color=#ffffff40>").Append(i + 1).Append(".   waiting for player...</color>");
                }
                sb.Append('\n');
            }
            _players.text = sb.ToString();

            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            _start.gameObject.SetActive(isHost);
            _start.interactable = MatchManager.I != null && MatchManager.I.CanStart;
            _waiting.text = isHost
                ? (players.Count < MatchManager.MinPlayersToStart
                    ? $"Share the code - need at least {MatchManager.MinPlayersToStart} players"
                    : $"{players.Count}/{SessionService.MaxPlayers} players - start when ready")
                : "Waiting for the host to start the match...";
        }

        void SetBusy(bool busy, string status)
        {
            _busy = busy;
            _host.interactable = !busy;
            _join.interactable = !busy;
            _back.interactable = !busy;
            SetStatus(status);
        }

        void SetStatus(string s) => _status.text = s;

        // --------------------------------------------------------------------------- UI

        void BuildUi()
        {
            var canvasGo = new GameObject("LobbyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _root = canvasGo.GetComponent<RectTransform>();

            // A dark band down the middle keeps text legible over the moving arena.
            var band = Img(_root, "Band", new Color(0.04f, 0.05f, 0.07f, 0.78f));
            Place(band.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 1080f));

            Label(_root, "COMBATPREP", 54, new Vector2(0f, 430f), Color.white);
            Label(_root, "PLAY ONLINE", 22, new Vector2(0f, 378f), new Color(1f, 0.62f, 0.2f));

            BuildConnectPanel();
            BuildLobbyPanel();

            _status = Label(_root, "", 20, new Vector2(0f, -420f), new Color(1f, 0.55f, 0.45f));
            _status.rectTransform.sizeDelta = new Vector2(720f, 80f);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void BuildConnectPanel()
        {
            _connectPanel = Panel("Connect");
            var p = _connectPanel.transform;

            Label(p, "YOUR NAME", 18, new Vector2(0f, 250f), new Color(1f, 1f, 1f, 0.5f));
            _name = Field(p, new Vector2(0f, 200f), "Your name", 16);
            _name.text = "Player" + UnityEngine.Random.Range(100, 1000);

            _host = Btn(p, "HOST A LOBBY", new Vector2(0f, 96f), new Vector2(460f, 70f), Host,
                        new Color(0.82f, 0.36f, 0.08f, 0.95f));

            Label(p, "or join a friend's", 18, new Vector2(0f, 10f), new Color(1f, 1f, 1f, 0.45f));
            _code = Field(p, new Vector2(-80f, -52f), "LOBBY CODE", 10, 300f);
            _code.onValidateInput = (_, __, c) => char.ToUpperInvariant(c);
            _join = Btn(p, "JOIN", new Vector2(160f, -52f), new Vector2(150f, 60f), Join,
                        new Color(1f, 1f, 1f, 0.18f));

            _back = Btn(p, "BACK", new Vector2(0f, -300f), new Vector2(240f, 56f),
                        () => OnBack?.Invoke(), new Color(1f, 1f, 1f, 0.10f));
        }

        void BuildLobbyPanel()
        {
            _lobbyPanel = Panel("Lobby");
            var p = _lobbyPanel.transform;

            Label(p, "LOBBY CODE", 18, new Vector2(0f, 270f), new Color(1f, 1f, 1f, 0.5f));
            _codeText = Label(p, "------", 64, new Vector2(0f, 214f), new Color(1f, 0.72f, 0.3f));
            Label(p, "Share it with up to 3 friends", 17, new Vector2(0f, 162f), new Color(1f, 1f, 1f, 0.42f));

            _players = Label(p, "", 24, new Vector2(0f, 20f), Color.white);
            _players.alignment = TextAnchor.UpperLeft;
            _players.supportRichText = true;
            _players.lineSpacing = 1.5f;
            _players.rectTransform.sizeDelta = new Vector2(600f, 220f);

            _waiting = Label(p, "", 19, new Vector2(0f, -130f), new Color(1f, 1f, 1f, 0.55f));

            _start = Btn(p, "START MATCH", new Vector2(0f, -210f), new Vector2(420f, 70f), StartMatch,
                         new Color(0.82f, 0.36f, 0.08f, 0.95f));
            _leave = Btn(p, "LEAVE", new Vector2(0f, -300f), new Vector2(240f, 56f),
                         () => OnLeave?.Invoke(), new Color(1f, 1f, 1f, 0.10f));
        }

        // -------------------------------------------------------------- UI factories

        GameObject Panel(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        Image Img(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        Text Label(Transform parent, string content, int size, Vector2 pos, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.text = content;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), pos, new Vector2(700f, size * 1.6f));
            return t;
        }

        Button Btn(Transform parent, string label, Vector2 pos, Vector2 size, Action onClick, Color color)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), pos, size);

            var img = go.GetComponent<Image>();
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            var t = Label(go.transform, label, 24, Vector2.zero, Color.white);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            return btn;
        }

        InputField Field(Transform parent, Vector2 pos, string placeholder, int limit, float width = 460f)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            Place(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), pos, new Vector2(width, 60f));
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.10f);

            var text = Label(go.transform, "", 26, Vector2.zero, Color.white);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(text.rectTransform, 18f);

            var ph = Label(go.transform, placeholder, 24, Vector2.zero, new Color(1f, 1f, 1f, 0.3f));
            Stretch(ph.rectTransform, 18f);

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = limit;
            return field;
        }

        static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, 0f);
            rt.offsetMax = new Vector2(-pad, 0f);
        }
    }
}
