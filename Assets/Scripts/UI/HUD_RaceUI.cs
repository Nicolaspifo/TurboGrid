using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// HUD de carrera:
    ///  - Puntaje propio (y barra de progreso hacia 1000).
    ///  - Tiempo restante (3:00) y cuenta regresiva 3-2-1-¡YA!
    ///  - Tabla de posiciones en vivo de todos los pilotos.
    ///  - Posicion propia (1°, 2°...).
    ///  - Avisos: "¡Pinchazo de X a Y!", "¡DRS!", etc.
    /// Todos los campos son opcionales: si alguno no esta asignado, se ignora.
    /// </summary>
    public class HUD_RaceUI : MonoBehaviour
    {
        [Header("Puntaje propio")]
        [SerializeField] private TMP_Text fuelCountText;   // muestra los puntos propios
        [SerializeField] private Image scoreProgressFill;  // Image Filled Horizontal (opcional)
        [SerializeField] private TMP_Text positionText;    // "1°"

        [Header("Tiempo")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private GameObject countdownPanel;

        [Header("Tabla de posiciones")]
        [SerializeField] private TMP_Text leaderboardText;

        [Header("Avisos")]
        [SerializeField] private TMP_Text feedText;

        public static HUD_RaceUI Instance { get; private set; }

        private readonly List<PlayerNetworkData> _sorted = new List<PlayerNetworkData>();
        private readonly StringBuilder _sb = new StringBuilder();

        // Avisos apilados (maximo 3 a la vez, el mas nuevo abajo).
        private const int MAX_FEED_LINES = 3;
        private readonly List<(string text, float hideAt)> _feed = new List<(string, float)>();
        private bool _feedDirty;
        private float _goHideAt;
        private int _lastShownSecond = -1;
        private bool _lastStunned;

        public void Configure(TMP_Text points, Image progress, TMP_Text position, TMP_Text timer,
            TMP_Text countdown, GameObject countdownRoot, TMP_Text leaderboard, TMP_Text feed)
        {
            fuelCountText = points;
            scoreProgressFill = progress;
            positionText = position;
            timerText = timer;
            countdownText = countdown;
            countdownPanel = countdownRoot;
            leaderboardText = leaderboard;
            feedText = feed;
        }

        private void Awake()
        {
            Instance = this;
            if (feedText != null) feedText.text = string.Empty;

            // La tabla ahora muestra etiquetas de power-ups: que el texto se achique si no cabe.
            if (leaderboardText != null && !leaderboardText.enableAutoSizing)
            {
                float max = leaderboardText.fontSize;
                leaderboardText.enableAutoSizing = true;
                leaderboardText.fontSizeMax = max;
                leaderboardText.fontSizeMin = Mathf.Max(12f, max * 0.55f);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            PlayerNetworkData.OnPinchazoFeed += HandlePinchazoFeed;
            PlayerNetworkData.OnPowerUpFeed += HandlePowerUpFeed;
        }

        private void OnDisable()
        {
            PlayerNetworkData.OnPinchazoFeed -= HandlePinchazoFeed;
            PlayerNetworkData.OnPowerUpFeed -= HandlePowerUpFeed;
        }

        private void HandlePowerUpFeed(PlayerNetworkData.PowerUpFeed feed, ulong clientId, string playerName)
        {
            bool isMe = Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.LocalClientId == clientId;
            switch (feed)
            {
                case PlayerNetworkData.PowerUpFeed.RecogioDrs:
                    ShowFeed(isMe
                        ? "<color=#4FC3F7>¡Recogiste DRS! Toca el boton azul</color>"
                        : $"<color=#4FC3F7>{playerName} tiene DRS</color>", 2.5f);
                    break;
                case PlayerNetworkData.PowerUpFeed.RecogioPinchazo:
                    ShowFeed(isMe
                        ? "<color=#C77DFF>¡Recogiste Pinchazo! Toca el boton morado</color>"
                        : $"<color=#C77DFF>¡Cuidado! {playerName} tiene PINCHAZO</color>", 3f);
                    break;
                case PlayerNetworkData.PowerUpFeed.ActivoDrs:
                    ShowFeed(isMe
                        ? "<color=#4FC3F7>¡DRS ACTIVADO!</color>"
                        : $"<color=#4FC3F7>¡{playerName} activo el DRS!</color>", 2.5f);
                    break;
                case PlayerNetworkData.PowerUpFeed.RecogioEscudo:
                    ShowFeed(isMe
                        ? "<color=#4DD9AC>¡ESCUDO! Bloquea el proximo Pinchazo</color>"
                        : $"<color=#4DD9AC>{playerName} tiene ESCUDO</color>", 2.5f);
                    break;
                case PlayerNetworkData.PowerUpFeed.RecogioIman:
                    ShowFeed(isMe
                        ? "<color=#FF6B6B>¡IMAN! Atraes la gasolina cercana</color>"
                        : $"<color=#FF6B6B>{playerName} activo el IMAN</color>", 2.5f);
                    break;
                case PlayerNetworkData.PowerUpFeed.RecogioDoblePuntos:
                    ShowFeed(isMe
                        ? "<color=#FFD54F>¡PUNTOS x2! Cada bidon vale el doble</color>"
                        : $"<color=#FFD54F>{playerName} tiene PUNTOS x2</color>", 2.5f);
                    break;
                case PlayerNetworkData.PowerUpFeed.EscudoBloqueo:
                    ShowFeed(isMe
                        ? "<color=#4DD9AC>¡Tu escudo bloqueo el Pinchazo!</color>"
                        : $"<color=#4DD9AC>El escudo de {playerName} bloqueo el Pinchazo</color>", 2.5f);
                    break;
            }
        }

        private void HandlePinchazoFeed(string attacker, string victim)
        {
            var local = PlayerNetworkData.Local;
            if (local != null && local.DisplayName == victim)
                ShowFeed($"<color=#C77DFF>¡{attacker} te pincho!</color>", 2.5f);
            else if (local != null && local.DisplayName == attacker)
                ShowFeed($"<color=#C77DFF>¡Pinchaste a {victim}!</color>", 2.5f);
            else
                ShowFeed($"<color=#C77DFF>{attacker} pincho a {victim}</color>", 2.5f);
        }

        public void ShowFeed(string message, float seconds)
        {
            if (feedText == null) return;
            _feed.Add((message, Time.time + seconds));
            while (_feed.Count > MAX_FEED_LINES) _feed.RemoveAt(0);
            _feedDirty = true;
        }

        private void UpdateFeed()
        {
            if (feedText == null) return;
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                if (Time.time > _feed[i].hideAt)
                {
                    _feed.RemoveAt(i);
                    _feedDirty = true;
                }
            }
            if (!_feedDirty) return;
            _feedDirty = false;

            _sb.Clear();
            for (int i = 0; i < _feed.Count; i++)
            {
                if (i > 0) _sb.Append('\n');
                _sb.Append(_feed[i].text);
            }
            feedText.text = _sb.ToString();
        }

        private void Update()
        {
            var race = RaceManager.Instance;
            var local = PlayerNetworkData.Local;

            UpdateTimer(race);
            UpdateCountdown(race);
            UpdateOwnScore(local);
            UpdateLeaderboard(local);
            UpdateStunFeed(local);
            UpdateFeed();
        }

        private void UpdateTimer(RaceManager race)
        {
            if (timerText == null) return;
            float remaining = race != null ? race.TimeRemaining : GameConstants.RACE_DURATION_SECONDS;
            int total = Mathf.CeilToInt(remaining);
            if (total == _lastShownSecond) return;
            _lastShownSecond = total;
            int minutes = total / 60;
            int seconds = total % 60;
            timerText.text = $"{minutes:00}:{seconds:00}";
            // Ultimos 10 segundos en rojo.
            timerText.color = race != null && race.IsRacing && total <= 10 ? new Color(1f, 0.35f, 0.3f) : Color.white;
        }

        private void UpdateCountdown(RaceManager race)
        {
            if (countdownText == null && countdownPanel == null) return;

            bool counting = race != null && race.IsSpawned && race.CurrentState.Value == RaceManager.RaceState.Countdown;
            bool waiting = race == null || !race.IsSpawned || race.CurrentState.Value == RaceManager.RaceState.WaitingForPlayers;

            if (counting)
            {
                float remaining = race.CountdownRemaining;
                if (countdownText != null) countdownText.text = Mathf.CeilToInt(Mathf.Max(0.01f, remaining)).ToString();
                _goHideAt = Time.time + 0.9f; // al terminar la cuenta se muestra "¡YA!" un momento
                SetCountdownVisible(true);
            }
            else if (waiting)
            {
                if (countdownText != null) countdownText.text = "Esperando pilotos...";
                SetCountdownVisible(true);
            }
            else if (race.IsRacing && Time.time < _goHideAt)
            {
                if (countdownText != null) countdownText.text = "¡YA!";
                SetCountdownVisible(true);
            }
            else
            {
                SetCountdownVisible(false);
            }
        }

        private void SetCountdownVisible(bool visible)
        {
            if (countdownPanel != null)
            {
                if (countdownPanel.activeSelf != visible) countdownPanel.SetActive(visible);
            }
            else if (countdownText != null && countdownText.gameObject.activeSelf != visible)
            {
                countdownText.gameObject.SetActive(visible);
            }
        }

        private void UpdateOwnScore(PlayerNetworkData local)
        {
            int points = local != null ? local.Points.Value : 0;
            if (fuelCountText != null) fuelCountText.text = $"{points} pts";
            if (scoreProgressFill != null) scoreProgressFill.fillAmount = Mathf.Clamp01(points / (float)GameConstants.WIN_SCORE);
        }

        private void UpdateLeaderboard(PlayerNetworkData local)
        {
            _sorted.Clear();
            foreach (var p in PlayerNetworkData.All)
                if (p != null && p.IsSpawned) _sorted.Add(p);
            _sorted.Sort((a, b) =>
            {
                int cmp = b.Points.Value.CompareTo(a.Points.Value);
                return cmp != 0 ? cmp : a.SlotIndex.Value.CompareTo(b.SlotIndex.Value);
            });

            if (positionText != null)
            {
                int pos = local != null ? _sorted.IndexOf(local) + 1 : 0;
                positionText.text = pos > 0 ? $"{pos}°" : "-";
            }

            if (leaderboardText == null) return;
            _sb.Clear();
            for (int i = 0; i < _sorted.Count; i++)
            {
                var p = _sorted[i];
                string hex = PlayerColors.Hex(p.SlotIndex.Value);
                string name = p.DisplayName;
                if (p == local) name = $"<b>{name}</b>";
                _sb.Append($"{i + 1}. <color=#{hex}>{name}</color>  {p.Points.Value}");
                // Que todos vean quien tiene power-ups guardados o activos.
                if (p.IsDrsActive.Value) _sb.Append("  <color=#4FC3F7><size=80%>DRS!</size></color>");
                else if (p.HasDrs.Value) _sb.Append("  <color=#4FC3F7><size=80%>[DRS]</size></color>");
                if (p.HasPinchazo.Value) _sb.Append("  <color=#C77DFF><size=80%>[PINCH]</size></color>");
                if (p.HasShield.Value) _sb.Append("  <color=#4DD9AC><size=80%>[ESC]</size></color>");
                if (p.IsMagnetActive) _sb.Append("  <color=#FF6B6B><size=80%>IMAN</size></color>");
                if (p.IsDoublePointsActive) _sb.Append("  <color=#FFD54F><size=80%>x2</size></color>");
                if (i < _sorted.Count - 1) _sb.Append('\n');
            }
            leaderboardText.text = _sb.ToString();
        }

        private void UpdateStunFeed(PlayerNetworkData local)
        {
            if (local == null) return;
            bool stunned = local.IsStunned.Value && (RaceManager.Instance == null || !RaceManager.Instance.IsFinished);
            // El aviso "X te pincho" llega por OnPinchazoFeed; aqui solo se limpia el estado.
            _lastStunned = stunned;
        }
    }
}
