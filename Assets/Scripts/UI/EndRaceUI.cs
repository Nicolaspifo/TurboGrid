using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Pantalla de resultados: ganador, ranking con puntos y gasolina recogida, y botones
    /// "Revancha" (solo el host; reinicia en el mismo circuito) y "Salir" (vuelve al menu).
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

        private RaceManager _subscribed;

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
            if (panelRoot != null) panelRoot.SetActive(true);

            ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            bool tie = results.Count > 1 && results[0].Points == results[1].Points;

            if (titleText != null)
            {
                if (results.Count == 0) titleText.text = "Fin de la carrera";
                else if (tie) titleText.text = "¡Empate!";
                else if (results[0].ClientId == localId) titleText.text = "¡GANASTE!";
                else titleText.text = $"Gana {results[0].Name}";
            }

            if (subtitleText != null)
            {
                subtitleText.text = winByScore
                    ? $"Llego a {GameConstants.WIN_SCORE} puntos"
                    : "Se acabo el tiempo";
            }

            if (resultsListContainer == null) return;

            foreach (Transform child in resultsListContainer)
                Destroy(child.gameObject);

            if (resultRowPrefab == null) return;

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
            }
        }

        private void HandleRaceRestarted()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnRematchClicked()
        {
            RaceManager.Instance?.RequestRematch();
        }

        private void OnExitClicked()
        {
            if (GameNetworkManager.Instance != null)
            {
                GameNetworkManager.Instance.ReturnToMainMenu();
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.MAIN_MENU_SCENE);
            }
        }
    }
}
