using TMPro;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// HUD de carrera: fichas propias recolectadas, tiempo restante y
    /// mensaje de cuenta regresiva. Se suscribe a las NetworkVariables
    /// de RaceManager y del PlayerNetworkData local -- no hace polling
    /// innecesario del estado de red.
    /// </summary>
    public class HUD_RaceUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text fuelCountText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private GameObject countdownPanel;


        public static HUD_RaceUI Instance { get; private set; }

        private PlayerNetworkData _localPlayerData;

        private void Awake()
        {
            Instance = this;
        }

        public void BindLocalPlayer(PlayerNetworkData playerData)
        {
            _localPlayerData = playerData;
            _localPlayerData.FuelCount.OnValueChanged += HandleFuelChanged;
            UpdateFuelText(_localPlayerData.FuelCount.Value);
        }

        private void OnEnable()
        {
            if (RaceManager.Instance != null)
            {
                RaceManager.Instance.TimeRemaining.OnValueChanged += HandleTimeChanged;
                RaceManager.Instance.CurrentState.OnValueChanged += HandleRaceStateChanged;
            }
        }

        private void OnDisable()
        {
            if (RaceManager.Instance != null)
            {
                RaceManager.Instance.TimeRemaining.OnValueChanged -= HandleTimeChanged;
                RaceManager.Instance.CurrentState.OnValueChanged -= HandleRaceStateChanged;
            }

            if (_localPlayerData != null)
                _localPlayerData.FuelCount.OnValueChanged -= HandleFuelChanged;
        }

        private void HandleFuelChanged(int previous, int current) => UpdateFuelText(current);

        private void UpdateFuelText(int value)
        {
            fuelCountText.text = $"{value} / {GameConstants.TOTAL_FUEL_TOKENS}";
        }

        private void HandleTimeChanged(float previous, float current)
        {
            int minutes = Mathf.FloorToInt(current / 60f);
            int seconds = Mathf.FloorToInt(current % 60f);
            timerText.text = $"{minutes:00}:{seconds:00}";
        }

        private void HandleRaceStateChanged(RaceManager.RaceState previous, RaceManager.RaceState current)
        {
            bool showCountdown = current == RaceManager.RaceState.Countdown;
            countdownPanel.SetActive(showCountdown);
        }

        private void Update()
        {
            if (RaceManager.Instance != null &&
                RaceManager.Instance.CurrentState.Value == RaceManager.RaceState.Countdown)
            {
                float remaining = RaceManager.Instance.CountdownRemaining.Value;
                countdownText.text = remaining > 0.5f ? Mathf.CeilToInt(remaining).ToString() : "GO!";
            }
        }
    }
}
