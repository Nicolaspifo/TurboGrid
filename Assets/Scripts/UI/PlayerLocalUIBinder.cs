using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboGrid
{
    public class PlayerLocalUIBinder : MonoBehaviour
    {
        [SerializeField] private CarController carController;
        [SerializeField] private PlayerNetworkData playerData;
        [SerializeField] private DrsTurboAbility drsAbility;
        [SerializeField] private PinchazoAbility pinchazoAbility;

        private bool _isBound;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Start()
        {
            TryBindToRaceUI();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TryBindToRaceUI();
        }

        private void TryBindToRaceUI()
        {
            if (_isBound) return;
            if (!carController.IsOwner) return;
            if (VirtualJoystick.Instance == null || HUD_RaceUI.Instance == null) return;

            VirtualJoystick.Instance.BindLocalCar(carController);

            if (AbilityButtonUI.AllButtons.Count > 0) AbilityButtonUI.AllButtons[0].Bind(drsAbility);
            if (AbilityButtonUI.AllButtons.Count > 1) AbilityButtonUI.AllButtons[1].Bind(pinchazoAbility);

            HUD_RaceUI.Instance.BindLocalPlayer(playerData);

            _isBound = true;
        }
    }
}