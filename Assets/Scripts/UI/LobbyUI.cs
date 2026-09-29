using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Menu principal + Sala de carrera.
    ///
    /// Pantalla principal:
    ///  - El jugador DEBE escribir su nombre antes de "Crear partida" o "Unirse".
    ///  - "Crear partida": el celular se vuelve host y pasa a la Sala.
    ///  - "Unirse": escribe el codigo de 6 letras y pasa a la Sala.
    ///
    /// Sala de carrera (todos la ven igual):
    ///  - Codigo de la sala en grande.
    ///  - 4 puestos: auto, nombre y estado (Anfitrion / Conectando... / En la sala / ¡Listo! / Esperando...).
    ///  - Invitados: boton "¡ESTOY LISTO!" (se puede cancelar).
    ///  - Host: boton "COMENZAR", activo solo con 2+ pilotos y todos listos.
    ///  - Opciones: controles (cada uno el suyo) y circuito (solo host).
    ///  - Salir de la sala.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [Header("Pantalla principal")]
        [SerializeField] private TMP_InputField playerNameInput;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TMP_Text statusText;

        [Header("Sala de carrera")]
        [SerializeField] private GameObject salaPanel;
        [SerializeField] private TMP_Text joinCodeDisplayText;   // codigo en la sala
        [SerializeField] private TMP_Text salaStatusText;
        [SerializeField] private Button startRaceButton;         // COMENZAR (solo host)
        [SerializeField] private Button readyButton;             // ¡ESTOY LISTO! (invitados)
        [SerializeField] private TMP_Text readyButtonText;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button salaOptionsButton;
        [SerializeField] private GameObject[] slotRoots = new GameObject[4];
        [SerializeField] private Image[] slotCars = new Image[4];
        [SerializeField] private TMP_Text[] slotNames = new TMP_Text[4];
        [SerializeField] private TMP_Text[] slotStatus = new TMP_Text[4];

        [Header("Opciones")]
        [SerializeField] private Button optionsButton;
        [SerializeField] private GameObject optionsPanel;
        [SerializeField] private Button closeOptionsButton;
        [SerializeField] private TMP_Text controlSchemeText;
        [SerializeField] private Button controlPrevButton;
        [SerializeField] private Button controlNextButton;
        [SerializeField] private GameObject mapRow;            // solo el host elige circuito
        [SerializeField] private TMP_Text mapText;
        [SerializeField] private Button mapPrevButton;
        [SerializeField] private Button mapNextButton;

        private enum LobbyStage { Idle, Busy, Hosting, Joined }
        private LobbyStage _stage = LobbyStage.Idle;
        private bool _starting;
        private Coroutine _shake;
        private Vector3 _nameBasePos;

        private static readonly Color ColorHost = new Color(1f, 0.82f, 0.25f);
        private static readonly Color ColorConnecting = new Color(1f, 0.6f, 0.15f);
        private static readonly Color ColorInRoom = new Color(0.55f, 0.85f, 1f);
        private static readonly Color ColorReady = new Color(0.35f, 0.95f, 0.35f);
        private static readonly Color ColorEmpty = new Color(0.95f, 0.3f, 0.3f);

        private void Awake()
        {
            if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
            if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
            if (startRaceButton != null) startRaceButton.onClick.AddListener(OnStartRaceClicked);
            if (readyButton != null) readyButton.onClick.AddListener(OnReadyClicked);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveClicked);

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

            if (playerNameInput != null)
            {
                _nameBasePos = playerNameInput.transform.localPosition;
                playerNameInput.characterLimit = LocalPlayerSettings.MAX_NAME_LENGTH;
                playerNameInput.text = LocalPlayerSettings.PlayerName;
                playerNameInput.onEndEdit.AddListener(v => LocalPlayerSettings.PlayerName = v);
            }

            if (optionsButton != null) optionsButton.onClick.AddListener(() => SetOptionsVisible(true));
            if (salaOptionsButton != null) salaOptionsButton.onClick.AddListener(() => SetOptionsVisible(true));
            if (closeOptionsButton != null) closeOptionsButton.onClick.AddListener(() => SetOptionsVisible(false));
            if (controlPrevButton != null) controlPrevButton.onClick.AddListener(() => CycleScheme(-1));
            if (controlNextButton != null) controlNextButton.onClick.AddListener(() => CycleScheme(1));
            if (mapPrevButton != null) mapPrevButton.onClick.AddListener(() => CycleMap(-1));
            if (mapNextButton != null) mapNextButton.onClick.AddListener(() => CycleMap(1));

            SetOptionsVisible(false);
            ShowSala(false);
        }

        private async void Start()
        {
            // Mensaje pendiente de una partida anterior (host se fue, sala llena, etc.)
            string reason = GameNetworkManager.LastDisconnectReason;
            GameNetworkManager.LastDisconnectReason = null;
            if (!string.IsNullOrEmpty(reason)) SetStatus(reason);
            else if (string.IsNullOrEmpty(CurrentName())) SetStatus("Escribe tu nombre de piloto para empezar");
            else SetStatus("Crea una partida o unete con un codigo");

            RefreshOptionsTexts();

            if (RelayManager.Instance != null)
            {
                try { await RelayManager.Instance.EnsureSignedInAsync(); }
                catch (System.Exception e) { Debug.LogWarning($"[LobbyUI] No se pudo iniciar sesion todavia: {e.Message}"); }
            }
        }

        private void OnEnable()
        {
            SessionState.OnPlayersChanged += RefreshSala;
            LocalPlayerSettings.OnChanged += RefreshOptionsTexts;
        }

        private void OnDisable()
        {
            SessionState.OnPlayersChanged -= RefreshSala;
            LocalPlayerSettings.OnChanged -= RefreshOptionsTexts;
        }

        // =====================================================================
        // Pantalla principal
        // =====================================================================

        private string CurrentName()
        {
            string raw = playerNameInput != null ? playerNameInput.text : LocalPlayerSettings.PlayerName;
            return LocalPlayerSettings.SanitizeName(raw);
        }

        /// <summary>Obliga a tener nombre. Devuelve false (y avisa) si esta vacio.</summary>
        private bool RequireName()
        {
            string name = CurrentName();
            if (string.IsNullOrEmpty(name))
            {
                SetStatus("¡Primero escribe tu nombre de piloto!");
                if (playerNameInput != null)
                {
                    if (_shake != null) StopCoroutine(_shake);
                    _shake = StartCoroutine(Shake(playerNameInput.transform));
                    playerNameInput.ActivateInputField();
                }
                return false;
            }
            LocalPlayerSettings.PlayerName = name;
            if (playerNameInput != null) playerNameInput.SetTextWithoutNotify(name);
            return true;
        }

        private async void OnHostClicked()
        {
            if (!RequireName()) return;
            if (RelayManager.Instance == null) { SetStatus("Falta RelayManager en la escena."); return; }

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

            if (joinCodeDisplayText != null) joinCodeDisplayText.text = $"CODIGO: <color=#FFD54F>{joinCode}</color>";
            SetStage(LobbyStage.Hosting);
            ShowSala(true);
        }

        private async void OnJoinClicked()
        {
            if (!RequireName()) return;
            if (RelayManager.Instance == null) { SetStatus("Falta RelayManager en la escena."); return; }

            string code = joinCodeInputField != null ? joinCodeInputField.text.Trim().ToUpperInvariant() : string.Empty;
            if (code.Length != 6)
            {
                SetStatus("Escribe el codigo de 6 letras que te paso el host.");
                return;
            }

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

            if (joinCodeDisplayText != null) joinCodeDisplayText.text = $"CODIGO: <color=#FFD54F>{code}</color>";
            SetStage(LobbyStage.Joined);
            ShowSala(true);
        }

        private void SetStage(LobbyStage stage)
        {
            _stage = stage;
            bool idle = stage == LobbyStage.Idle;
            if (hostButton != null) hostButton.interactable = idle;
            if (joinButton != null) joinButton.interactable = idle;
            if (joinCodeInputField != null) joinCodeInputField.interactable = idle;
            if (playerNameInput != null) playerNameInput.interactable = idle;
            if (mapRow != null) mapRow.SetActive(stage != LobbyStage.Joined);
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        // =====================================================================
        // Sala de carrera
        // =====================================================================

        private void ShowSala(bool visible)
        {
            if (salaPanel != null) salaPanel.SetActive(visible);
            _starting = false;

            bool host = _stage == LobbyStage.Hosting;
            if (startRaceButton != null) startRaceButton.gameObject.SetActive(visible && host);
            if (readyButton != null) readyButton.gameObject.SetActive(visible && _stage == LobbyStage.Joined);
            if (visible) RefreshSala();
        }

        private void OnReadyClicked()
        {
            var session = SessionState.Instance;
            var nm = NetworkManager.Singleton;
            if (session == null || nm == null) return;
            if (!session.TryGetPlayer(nm.LocalClientId, out var me) || me.IsConnecting) return;
            session.RequestSetReady(!me.IsReady);
        }

        private void OnStartRaceClicked()
        {
            var nm = NetworkManager.Singleton;
            var session = SessionState.Instance;
            if (nm == null || !nm.IsHost || GameNetworkManager.Instance == null || session == null || _starting) return;

            if (!session.CanStart(out string reason))
            {
                SetSalaStatus(reason);
                return;
            }

            _starting = true;
            if (startRaceButton != null) startRaceButton.interactable = false;
            SetSalaStatus("¡Arrancando la carrera!");
            if (!GameNetworkManager.Instance.HostStartRace(LocalPlayerSettings.MapIndex))
            {
                _starting = false;
                SetSalaStatus("No se pudo cargar el circuito. Revisa la consola.");
            }
        }

        private void OnLeaveClicked()
        {
            if (GameNetworkManager.Instance != null)
                GameNetworkManager.Instance.ReturnToMainMenu(_stage == LobbyStage.Hosting ? null : "Saliste de la sala.");
            else
                ShowSala(false);
        }

        private void Update()
        {
            if (salaPanel == null || !salaPanel.activeSelf) return;
            // Refresco continuo: animacion de "Conectando..." y estado del boton COMENZAR.
            RefreshSala();
        }

        private void RefreshSala()
        {
            if (salaPanel == null || !salaPanel.activeSelf) return;

            var session = SessionState.Instance;
            var nm = NetworkManager.Singleton;
            ulong localId = nm != null ? nm.LocalClientId : ulong.MaxValue;

            // Puestos
            for (int s = 0; s < GameConstants.MAX_PLAYERS; s++)
            {
                PlayerSlot player = default;
                bool occupied = false;
                if (session != null)
                {
                    foreach (var p in session.Players)
                        if (p.SlotIndex == s) { player = p; occupied = true; break; }
                }
                DrawSlot(s, occupied, player, localId);
            }

            if (session == null)
            {
                SetSalaStatus("Conectando con la sala...");
                if (readyButton != null) readyButton.interactable = false;
                return;
            }

            if (_stage == LobbyStage.Hosting)
            {
                bool can = session.CanStart(out string reason);
                if (!_starting)
                {
                    if (startRaceButton != null) startRaceButton.interactable = can;
                    SetSalaStatus(reason);
                }
            }
            else if (_stage == LobbyStage.Joined)
            {
                bool found = session.TryGetPlayer(localId, out var me);
                bool connecting = !found || me.IsConnecting;
                if (readyButton != null)
                {
                    readyButton.interactable = !connecting;
                    var img = readyButton.targetGraphic as Image;
                    if (img != null) img.color = found && me.IsReady ? new Color(1f, 0.55f, 0.2f) : new Color(0.35f, 0.85f, 0.3f);
                }
                if (readyButtonText != null)
                    readyButtonText.text = found && me.IsReady ? "CANCELAR" : "¡ESTOY LISTO!";

                if (connecting) SetSalaStatus("Conectando con la sala...");
                else if (!me.IsReady) SetSalaStatus("Toca ¡ESTOY LISTO! cuando estes preparado");
                else SetSalaStatus("¡Listo! Esperando que el host comience...");
            }
        }

        private void DrawSlot(int s, bool occupied, PlayerSlot player, ulong localId)
        {
            var car = s < slotCars.Length ? slotCars[s] : null;
            var nameText = s < slotNames.Length ? slotNames[s] : null;
            var status = s < slotStatus.Length ? slotStatus[s] : null;
            float t = Time.unscaledTime;

            if (!occupied)
            {
                if (car != null) car.color = new Color(1f, 1f, 1f, 0.18f);
                if (nameText != null) { nameText.text = $"PILOTO {s + 1}"; nameText.color = new Color(1f, 1f, 1f, 0.35f); }
                if (status != null) { status.text = "ESPERANDO" + Dots(t); status.color = ColorEmpty; }
                if (car != null) car.rectTransform.localScale = Vector3.one;
                return;
            }

            string you = player.ClientId == localId ? " (tu)" : string.Empty;
            if (nameText != null)
            {
                nameText.text = $"{player.Name}{you}";
                nameText.color = PlayerColors.Get(player.SlotIndex);
            }

            if (player.IsConnecting)
            {
                if (car != null)
                {
                    car.color = new Color(1f, 1f, 1f, 0.35f + 0.25f * Mathf.Sin(t * 5f));
                    car.rectTransform.localScale = Vector3.one;
                }
                if (status != null) { status.text = "CONECTANDO" + Dots(t); status.color = ColorConnecting; }
                return;
            }

            if (car != null) car.color = Color.white;

            if (player.IsHost)
            {
                if (status != null) { status.text = "ANFITRION"; status.color = ColorHost; }
                if (car != null) car.rectTransform.localScale = Vector3.one;
            }
            else if (player.IsReady)
            {
                if (status != null) { status.text = "¡LISTO!"; status.color = ColorReady; }
                // Pequeño salto del auto de quien esta listo.
                if (car != null) car.rectTransform.localScale = Vector3.one * (1f + Mathf.Abs(Mathf.Sin(t * 4f)) * 0.06f);
            }
            else
            {
                if (status != null) { status.text = "EN LA SALA"; status.color = ColorInRoom; }
                if (car != null) car.rectTransform.localScale = Vector3.one;
            }
        }

        private static string Dots(float t)
        {
            int n = (int)(t * 2.5f) % 4;
            return new string('.', n);
        }

        private void SetSalaStatus(string message)
        {
            if (salaStatusText != null) salaStatusText.text = message;
            else SetStatus(message);
        }

        // =====================================================================
        // Opciones
        // =====================================================================

        private void SetOptionsVisible(bool visible)
        {
            if (optionsPanel != null) optionsPanel.SetActive(visible);
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

        // =====================================================================

        private IEnumerator Shake(Transform target)
        {
            Vector3 basePos = _nameBasePos;
            for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime)
            {
                float offset = Mathf.Sin(t * 60f) * 10f * (1f - t / 0.4f);
                target.localPosition = basePos + new Vector3(offset, 0f, 0f);
                yield return null;
            }
            target.localPosition = basePos;
            _shake = null;
        }
    }
}
