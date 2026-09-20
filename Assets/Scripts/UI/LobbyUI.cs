using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Pantalla de lobby: un boton "Crear partida" (el movil se vuelve host)
    /// y un boton "Unirse" con un campo de texto para el codigo de 6 caracteres
    /// que le paso el host (por voz, chat, lo que sea -- fuera del alcance del juego).
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_InputField joinCodeInputField;
        [SerializeField] private TMP_Text joinCodeDisplayText; // muestra el codigo al host para compartirlo
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button startRaceButton; // solo visible/interactivo para el host
        [SerializeField] private string raceSceneName = "Race_Oval";

        private void Awake()
        {
            hostButton.onClick.AddListener(OnHostClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
            startRaceButton.onClick.AddListener(OnStartRaceClicked);
            startRaceButton.gameObject.SetActive(false);
            joinCodeDisplayText.text = string.Empty;
        }

        private async void Start()
        {
            await RelayManager.Instance.EnsureSignedInAsync();
        }

        private async void OnHostClicked()
        {
            SetInteractable(false);
            statusText.text = "Creando partida...";

            string joinCode = await RelayManager.Instance.StartHostWithRelayAsync();

            if (string.IsNullOrEmpty(joinCode))
            {
                statusText.text = "No se pudo crear la partida. Reintenta.";
                SetInteractable(true);
                return;
            }

            joinCodeDisplayText.text = $"Codigo: {joinCode}";
            statusText.text = "Esperando jugadores (2-4)...";
            startRaceButton.gameObject.SetActive(true);
        }

        private async void OnJoinClicked()
        {
            string code = joinCodeInputField.text.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
            {
                statusText.text = "Ingresa un codigo valido.";
                return;
            }

            SetInteractable(false);
            statusText.text = "Uniendose...";

            bool success = await RelayManager.Instance.JoinWithRelayAsync(code);

            if (!success)
            {
                statusText.text = "No se pudo unir. Revisa el codigo.";
                SetInteractable(true);
                return;
            }

            statusText.text = "Conectado. Esperando que el host arranque la carrera...";
        }

        private void OnStartRaceClicked()
        {
            if (!NetworkManager.Singleton.IsHost) return;
            GameNetworkManager.Instance.HostStartRace(raceSceneName);
        }

        private void SetInteractable(bool value)
        {
            hostButton.interactable = value;
            joinButton.interactable = value;
        }
    }
}
