using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Fin de carrera con celebracion:
    ///  1. La camara hace zoom al auto ganador, aparece un letrero gigante
    ///     ("¡GANASTE!" / "¡X GANA!" / "¡EMPATE!") y cae confeti.
    ///  2. Se muestra la tabla de resultados animada: el titulo rebota, el trofeo cae,
    ///     las filas entran una por una y aparecen los botones Revancha / Salir.
    /// </summary>
    public class EndRaceUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform resultsListContainer;
        [SerializeField] private GameObject resultRowPrefab; // fila con 2 TMP_Text: nombre y puntos
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private Button rematchButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private TMP_Text waitingHostText;

        [Header("Celebracion")]
        [Tooltip("Segundos de celebracion (zoom al ganador + letrero) antes de mostrar la tabla.")]
        [SerializeField] private float celebrationSeconds = 2.8f;
        [SerializeField] private RectTransform trophy; // opcional: se busca "Trofeo" dentro del panel

        private RaceManager _subscribed;
        private Coroutine _sequence;
        private readonly List<Coroutine> _loops = new List<Coroutine>();
        private TextMeshProUGUI _banner;
        private CanvasGroup _bannerGroup;
        private Vector2 _trophyBasePos;

        public void Configure(GameObject root, Transform container, GameObject rowPrefab, TMP_Text title,
            TMP_Text subtitle, Button rematch, Button exit, TMP_Text waitingHost)
        {
            panelRoot = root;
            resultsListContainer = container;
            resultRowPrefab = rowPrefab;
            titleText = title;
            subtitleText = subtitle;
            rematchButton = rematch;
            exitButton = exit;
            waitingHostText = waitingHost;
        }

        private void Awake()
        {
            if (rematchButton != null) rematchButton.onClick.AddListener(OnRematchClicked);
            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);
            if (panelRoot != null) panelRoot.SetActive(false);
            if (trophy == null && panelRoot != null) trophy = UIAnim.FindDeep(panelRoot.transform, "Trofeo") as RectTransform;
            if (trophy != null) _trophyBasePos = trophy.anchoredPosition;
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            // RaceManager puede aparecer despues que esta UI (spawn de red): suscribirse cuando exista.
            var race = RaceManager.Instance;
            if (race != _subscribed)
            {
                Unsubscribe();
                if (race != null)
                {
                    race.OnRaceFinishedClientSide += HandleRaceFinished;
                    race.OnRaceRestartedClientSide += HandleRaceRestarted;
                    _subscribed = race;
                    if (race.LastResults != null) HandleRaceFinished(race.LastResults, race.LastWinByScore);
                }
            }

            if (panelRoot != null && panelRoot.activeSelf)
            {
                bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
                int players = NetworkManager.Singleton != null && isHost ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;
                if (rematchButton != null)
                {
                    rematchButton.gameObject.SetActive(isHost);
                    rematchButton.interactable = players >= GameConstants.MIN_PLAYERS;
                }
                if (waitingHostText != null)
                {
                    waitingHostText.gameObject.SetActive(true);
                    waitingHostText.text = isHost
                        ? (players >= GameConstants.MIN_PLAYERS ? "Toca Revancha para correr otra vez" : "No hay suficientes pilotos para la revancha")
                        : "Esperando que el host elija revancha...";
                }
            }
        }

        private void Unsubscribe()
        {
            if (_subscribed == null) return;
            _subscribed.OnRaceFinishedClientSide -= HandleRaceFinished;
            _subscribed.OnRaceRestartedClientSide -= HandleRaceRestarted;
            _subscribed = null;
        }

        private void HandleRaceFinished(List<RaceResult> results, bool winByScore)
        {
            StopAnimations();
            _sequence = StartCoroutine(PlaySequence(results, winByScore));
        }

        private IEnumerator PlaySequence(List<RaceResult> results, bool winByScore)
        {
            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            bool tie = results.Count > 1 && results[0].Points == results[1].Points;
            bool iWon = !tie && results.Count > 0 && results[0].ClientId == localId;

            string headline;
            if (results.Count == 0) headline = "¡FIN DE LA CARRERA!";
            else if (tie) headline = "¡EMPATE!";
            else if (iWon) headline = "¡GANASTE!";
            else headline = $"¡{results[0].Name} GANA!";

            // ---- 1. Celebracion: zoom al ganador + letrero + confeti ----
            if (!tie && results.Count > 0)
                CameraFollow.FocusOn(results[0].ClientId);

            var canvasRect = transform as RectTransform;
            ShowBanner(headline, results.Count > 0 && !tie ? PlayerColors.Get(results[0].SlotIndex) : Color.white,
                winByScore ? $"¡Llego a {GameConstants.WIN_SCORE} puntos!" : "¡Se acabo el tiempo!");
            ConfettiBurst.Play(canvasRect, iWon ? 160 : 90, fromTop: false);
            yield return new WaitForSecondsRealtime(0.5f);
            ConfettiBurst.Play(canvasRect, iWon ? 140 : 60, fromTop: true);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, celebrationSeconds - 0.5f));

            if (_bannerGroup != null) yield return UIAnim.Fade(_bannerGroup, 1f, 0f, 0.35f);
            if (_banner != null) _banner.gameObject.SetActive(false);

            // ---- 2. Tabla de resultados animada ----
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
                StartCoroutine(UIAnim.Fade(UIAnim.EnsureGroup(panelRoot), 0f, 1f, 0.35f));
            }

            FillTexts(results, winByScore, headline);
            var rows = FillRows(results, localId);

            if (titleText != null) StartCoroutine(UIAnim.PopIn(titleText.transform.parent != null ? titleText.transform.parent : titleText.transform, 0.5f, 0.1f));
            if (subtitleText != null) StartCoroutine(UIAnim.PopIn(subtitleText.transform, 0.4f, 0.3f));
            if (trophy != null)
            {
                StartCoroutine(UIAnim.DropIn(trophy, 700f, 0.9f, 0.2f));
                _loops.Add(StartCoroutine(TrophyIdle(trophy, 1.2f)));
            }

            for (int i = 0; i < rows.Count; i++)
                StartCoroutine(UIAnim.PopIn(rows[i].transform, 0.45f, 0.45f + i * 0.18f));

            if (rows.Count > 0 && !tie)
                _loops.Add(StartCoroutine(Pulse(rows[0].transform, 0.045f, 0.45f + rows.Count * 0.18f + 0.4f)));

            float buttonsDelay = 0.6f + rows.Count * 0.18f;
            if (rematchButton != null) StartCoroutine(UIAnim.PopIn(rematchButton.transform, 0.4f, buttonsDelay));
            if (exitButton != null) StartCoroutine(UIAnim.PopIn(exitButton.transform, 0.4f, buttonsDelay + 0.1f));

            // Un poco mas de confeti cayendo encima de la tabla si ganaste.
            if (iWon)
            {
                yield return new WaitForSecondsRealtime(0.8f);
                ConfettiBurst.Play(canvasRect, 80, fromTop: true);
            }
        }

        private void FillTexts(List<RaceResult> results, bool winByScore, string headline)
        {
            if (titleText != null) titleText.text = headline;
            if (subtitleText != null)
                subtitleText.text = winByScore ? $"Llego a {GameConstants.WIN_SCORE} puntos" : "Se acabo el tiempo";
        }

        private List<GameObject> FillRows(List<RaceResult> results, ulong localId)
        {
            var rows = new List<GameObject>();
            if (resultsListContainer == null) return rows;

            foreach (Transform child in resultsListContainer)
                Destroy(child.gameObject);

            if (resultRowPrefab == null) return rows;

            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var row = Instantiate(resultRowPrefab, resultsListContainer);
                var texts = row.GetComponentsInChildren<TMP_Text>(true);
                string hex = PlayerColors.Hex(r.SlotIndex);
                string you = r.ClientId == localId ? " (tu)" : string.Empty;

                // texts[0] = posicion + nombre, texts[1] = puntos
                if (texts.Length > 0) texts[0].text = $"{i + 1}. <color=#{hex}>{r.Name}</color>{you}";
                if (texts.Length > 1) texts[1].text = $"{r.Points} pts ({r.Fuel} gasolina)";
                rows.Add(row);
            }
            return rows;
        }

        private void ShowBanner(string headline, Color color, string subline)
        {
            if (_banner == null)
            {
                var go = new GameObject("TG_Letrero", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(0f, 400f);
                rt.anchoredPosition = new Vector2(0f, 60f);
                _banner = go.AddComponent<TextMeshProUGUI>();
                if (titleText != null) _banner.font = titleText.font;
                _banner.alignment = TextAlignmentOptions.Center;
                _banner.enableAutoSizing = true;
                _banner.fontSizeMin = 50;
                _banner.fontSizeMax = 170;
                _banner.textWrappingMode = TextWrappingModes.Normal;
                _banner.outlineWidth = 0.25f;
                _banner.outlineColor = new Color32(10, 20, 50, 255);
                _banner.raycastTarget = false;
                _bannerGroup = go.AddComponent<CanvasGroup>();
                _bannerGroup.blocksRaycasts = false;
            }

            _banner.gameObject.SetActive(true);
            _banner.transform.SetAsLastSibling();
            _banner.color = Color.white;
            string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(color, Color.white, 0.15f));
            _banner.text = $"<color=#{hex}>{headline}</color>\n<size=45%><color=#FFD54F>{subline}</color></size>";
            _bannerGroup.alpha = 1f;
            _banner.transform.localScale = Vector3.one;
            StartCoroutine(UIAnim.PopIn(_banner.transform, 0.6f));
            _loops.Add(StartCoroutine(Pulse(_banner.transform, 0.05f, 0.7f)));
        }

        private IEnumerator Pulse(Transform target, float amount, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Vector3 baseScale = target != null ? target.localScale : Vector3.one;
            while (target != null)
            {
                target.localScale = baseScale * (1f + Mathf.Sin(Time.unscaledTime * 5f) * amount);
                yield return null;
            }
        }

        private IEnumerator TrophyIdle(RectTransform target, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            while (target != null)
            {
                target.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 2.2f) * 6f);
                yield return null;
            }
        }

        private void StopAnimations()
        {
            if (_sequence != null) StopCoroutine(_sequence);
            _sequence = null;
            foreach (var c in _loops)
                if (c != null) StopCoroutine(c);
            _loops.Clear();
            StopAllCoroutines();

            // Si una animacion se corto a la mitad, dejar todo en su estado original.
            if (trophy != null)
            {
                trophy.localRotation = Quaternion.identity;
                trophy.anchoredPosition = _trophyBasePos;
            }
            ResetScale(titleText != null ? (titleText.transform.parent != null ? titleText.transform.parent : titleText.transform) : null);
            ResetScale(subtitleText != null ? subtitleText.transform : null);
            ResetScale(rematchButton != null ? rematchButton.transform : null);
            ResetScale(exitButton != null ? exitButton.transform : null);
            ResetScale(_banner != null ? _banner.transform : null);
        }

        private static void ResetScale(Transform t)
        {
            if (t != null) t.localScale = Vector3.one;
        }

        private void HandleRaceRestarted()
        {
            StopAnimations();
            CameraFollow.FocusOn(null);
            if (_banner != null) _banner.gameObject.SetActive(false);
            if (panelRoot != null) panelRoot.SetActive(false);

            // Quitar confeti que haya quedado.
            foreach (var c in GetComponentsInChildren<ConfettiBurst>(true))
                Destroy(c.gameObject);
        }

        private void OnRematchClicked()
        {
            RaceManager.Instance?.RequestRematch();
        }

        private void OnExitClicked()
        {
            if (GameNetworkManager.Instance != null)
                GameNetworkManager.Instance.ReturnToMainMenu();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.MAIN_MENU_SCENE);
        }
    }
}
