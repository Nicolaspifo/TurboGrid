using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Pantalla de lobby:
    ///  - "Crear partida": el celular se vuelve host y muestra el codigo de 6 letras.
    ///  - "Unirse": escribe el codigo que le pasaron.
    ///  - Lista de pilotos conectados (2-4) con su color.
    ///  - Panel de Opciones: nombre del piloto, esquema de control (flechas / timon /
    ///    joystick, cada jugador el suyo) y circuito (solo el host: aleatorio o manual).
    ///  - "Jugar": solo el host, con al menos 2 pilotos.
    /// Los campos marcados como opcionales pueden quedar vacios.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [Header("Conexion")]
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TMP_Text joinCodeDisplayText; // muestra el codigo al host para compartirlo
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button startRaceButton;       // solo visible para el host

        [Header("Sala (opcional)")]
        [SerializeField] private TMP_Text playersText;

        [Header("Opciones (opcional)")]
        [SerializeField] private Button optionsButton;
        [SerializeField] private GameObject optionsPanel;
        [SerializeField] private Button closeOptionsButton;
        [SerializeField] private TMP_InputField playerNameInput;
        [SerializeField] private TMP_Text controlSchemeText;
        [SerializeField] private Button controlPrevButton;
        [SerializeField] private Button controlNextButton;
        [SerializeField] private GameObject mapRow;            // se oculta a los clientes
        [SerializeField] private TMP_Text mapText;
        [SerializeField] private Button mapPrevButton;
        [SerializeField] private Button mapNextButton;

        [Tooltip("Obsoleto: el circuito ahora se elige en Opciones (MapCatalog).")]
        [SerializeField, HideInInspector] private string raceSceneName = "Game";

        private enum LobbyStage { Idle, Busy, Hosting, Joined }
        private LobbyStage _stage = LobbyStage.Idle;
        private readonly StringBuilder _sb = new StringBuilder();

        private void Awake()
        {
            if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
            if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
            if (startRaceButton != null)
            {
                startRaceButton.onClick.AddListener(OnStartRaceClicked);
                startRaceButton.gameObject.SetActive(false);
            }
            if (joinCodeDisplayText != null) joinCodeDisplayText.text = string.Empty;

            if (joinCodeInputField != null)
            {
                joinCodeInputField.characterLimit = 6;
                joinCodeInputField.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
                joinCodeInputField.onValueChanged.AddListener(v =>
                {
                    string upper = v.ToUpperInvariant();
                    if (upper != v) joinCodeInputField.SetTextWithoutNotify(upper);
                });
            }

            if (optionsButton != null) optionsButton.onClick.AddListener(() => SetOptionsVisible(true));
            if (closeOptionsButton != null) closeOptionsButton.onClick.AddListener(() => SetOptionsVisible(false));
            if (controlPrevButton != null) controlPrevButton.onClick.AddListener(() => CycleScheme(-1));
            if (controlNextButton != null) controlNextButton.onClick.AddListener(() => CycleScheme(1));
            if (mapPrevButton != null) mapPrevButton.onClick.AddListener(() => CycleMap(-1));
            if (mapNextButton != null) mapNextButton.onClick.AddListener(() => CycleMap(1));

            if (playerNameInput != null)
            {
                playerNameInput.characterLimit = LocalPlayerSettings.MAX_NAME_LENGTH;
                playerNameInput.text = LocalPlayerSettings.PlayerName;
                playerNameInput.onEndEdit.AddListener(v => LocalPlayerSettings.PlayerName = v);
            }

            SetOptionsVisible(false);
        }

        private async void Start()
        {
            // Mensaje pendiente de una partida anterior (host se fue, sala llena, etc.)
            string reason = GameNetworkManager.LastDisconnectReason;
            GameNetworkManager.LastDisconnectReason = null;
            SetStatus(string.IsNullOrEmpty(reason) ? "Crea una partida o unete con un codigo." : reason);

            RefreshOptionsTexts();
            RefreshPlayers();

            if (RelayManager.Instance != null)
            {
                try { await RelayManager.Instance.EnsureSignedInAsync(); }
                catch (System.Exception e) { Debug.LogWarning($"[LobbyUI] No se pudo iniciar sesion todavia: {e.Message}"); }
            }
        }

        private void OnEnable()
        {
            SessionState.OnPlayersChanged += RefreshPlayers;
            LocalPlayerSettings.OnChanged += RefreshOptionsTexts;
        }

        private void OnDisable()
        {
            SessionState.OnPlayersChanged -= RefreshPlayers;
            LocalPlayerSettings.OnChanged -= RefreshOptionsTexts;
        }

        // ---------------- Conexion ----------------

        private async void OnHostClicked()
        {
            if (RelayManager.Instance == null) { SetStatus("Falta RelayManager en la escena."); return; }
            CommitName();
            SetStage(LobbyStage.Busy);
            SetStatus("Creando partida...");

            string joinCode = await RelayManager.Instance.StartHostWithRelayAsync();
            if (this == null) return; // la escena cambio mientras esperabamos

            if (string.IsNullOrEmpty(joinCode))
            {
                SetStatus(RelayManager.Instance.LastError ?? "No se pudo crear la partida. Reintenta.");
                SetStage(LobbyStage.Idle);
                return;
            }

            if (joinCodeDisplayText != null) joinCodeDisplayText.text = $"Codigo: {joinCode}";
            SetStage(LobbyStage.Hosting);
            RefreshPlayers();
        }

        private async void OnJoinClicked()
        {
            if (RelayManager.Instance == null) { SetStatus("Falta RelayManager en la escena."); return; }
            string code = joinCodeInputField != null ? joinCodeInputField.text.Trim().ToUpperInvariant() : string.Empty;
            if (code.Length != 6)
            {
                SetStatus("Escribe el codigo de 6 letras que te paso el host.");
                return;
            }

            CommitName();
            SetStage(LobbyStage.Busy);
            SetStatus("Uniendose...");

            bool success = await RelayManager.Instance.JoinWithRelayAsync(code);
            if (this == null) return;

            if (!success)
            {
                SetStatus(RelayManager.Instance.LastError ?? "No se pudo unir. Revisa el codigo.");
                SetStage(LobbyStage.Idle);
                return;
            }

            if (joinCodeDisplayText != null) joinCodeDisplayText.text = $"Sala: {code}";
            SetStage(LobbyStage.Joined);
            SetStatus("Conectando con el host...");
        }

        private void OnStartRaceClicked()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsHost || GameNetworkManager.Instance == null) return;

            int players = GameNetworkManager.Instance.ConnectedClientIds.Count;
            if (players < GameConstants.MIN_PLAYERS)
            {
                SetStatus($"Se necesitan al menos {GameConstants.MIN_PLAYERS} pilotos para empezar.");
                return;
            }

            if (startRaceButton != null) startRaceButton.interactable = false;
            SetStatus("¡Arrancando la carrera!");
            if (!GameNetworkManager.Instance.HostStartRace(LocalPlayerSettings.MapIndex))
            {
                SetStatus("No se pudo cargar el circuito. Revisa la consola.");
                if (startRaceButton != null) startRaceButton.interactable = true;
            }
        }

        private void CommitName()
        {
            if (playerNameInput != null) LocalPlayerSettings.PlayerName = playerNameInput.text;
        }

        private void SetStage(LobbyStage stage)
        {
            _stage = stage;
            bool idle = stage == LobbyStage.Idle;
            if (hostButton != null) hostButton.interactable = idle;
            if (joinButton != null) joinButton.interactable = idle;
            if (joinCodeInputField != null) joinCodeInputField.interactable = idle;
            if (playerNameInput != null) playerNameInput.interactable = idle;
            if (startRaceButton != null) startRaceButton.gameObject.SetActive(stage == LobbyStage.Hosting);
            if (mapRow != null) mapRow.SetActive(stage != LobbyStage.Joined);
        }

        private void Update()
        {
            // El cliente pasa de "conectando" a "conectado" cuando aparece SessionState.
            if (_stage == LobbyStage.Joined && SessionState.Instance != null && statusText != null &&
                statusText.text == "Conectando con el host...")
            {
                RefreshPlayers();
            }

            if (_stage == LobbyStage.Hosting && startRaceButton != null && GameNetworkManager.Instance != null)
            {
                bool enough = GameNetworkManager.Instance.ConnectedClientIds.Count >= GameConstants.MIN_PLAYERS;
                startRaceButton.interactable = enough;
            }
        }

        // ---------------- Sala ----------------

        private void RefreshPlayers()
        {
            var session = SessionState.Instance;
            int count = session != null ? session.PlayerCount : 0;

            if (playersText != null)
            {
                _sb.Clear();
                if (session != null)
                {
                    foreach (var p in session.Players)
                    {
                        if (_sb.Length > 0) _sb.Append("   ");
                        _sb.Append($"<color=#{PlayerColors.Hex(p.SlotIndex)}>{p.Name}</color>");
                    }
                }
                playersText.text = _sb.ToString();
            }

            if (session == null) return;

            if (_stage == LobbyStage.Hosting)
            {
                SetStatus(count >= GameConstants.MIN_PLAYERS
                    ? $"Pilotos: {count}/{GameConstants.MAX_PLAYERS}. ¡Toca JUGAR!"
                    : $"Esperando pilotos ({count}/{GameConstants.MAX_PLAYERS})... Comparte el codigo.");
            }
            else if (_stage == LobbyStage.Joined)
            {
                SetStatus($"Conectado ({count}/{GameConstants.MAX_PLAYERS}). Esperando al host...");
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        // ---------------- Opciones ----------------

        private void SetOptionsVisible(bool visible)
        {
            if (optionsPanel != null) optionsPanel.SetActive(visible);
            if (!visible) CommitName();
            RefreshOptionsTexts();
        }

        private void CycleScheme(int direction)
        {
            int count = System.Enum.GetValues(typeof(ControlScheme)).Length;
            int next = ((int)LocalPlayerSettings.Scheme + direction + count) % count;
            LocalPlayerSettings.Scheme = (ControlScheme)next;
        }

        private void CycleMap(int direction)
        {
            // Opciones: -1 (aleatorio), 0..n-1
            int total = MapCatalog.Available.Count + 1;
            int current = LocalPlayerSettings.MapIndex + 1;
            int next = ((current + direction) % total + total) % total;
            LocalPlayerSettings.MapIndex = next - 1;
        }

        private void RefreshOptionsTexts()
        {
            if (controlSchemeText != null)
                controlSchemeText.text = LocalPlayerSettings.SchemeDisplayName(LocalPlayerSettings.Scheme);

            int mapIndex = LocalPlayerSettings.MapIndex;
            if (mapIndex >= MapCatalog.Available.Count) mapIndex = -1;
            if (mapText != null) mapText.text = MapCatalog.DisplayNameFor(mapIndex);
        }
    }
}
