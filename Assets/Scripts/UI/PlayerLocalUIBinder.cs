using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Cuelga este script del PREFAB del auto (Player_Car). Cuando el auto
    /// spawnea y es el del jugador LOCAL (IsOwner), busca los elementos de UI
    /// ya presentes en la escena de carrera (Canvas persistente) y los enlaza
    /// a este auto: joystick, botones de habilidad y HUD.
    ///
    /// Asi evitas instanciar un Canvas por jugador: hay un solo Canvas en la
    /// escena y este script simplemente lo "conecta" al auto correcto.
    /// </summary>
    public class PlayerLocalUIBinder : MonoBehaviour
    {
        [SerializeField] private CarController carController;
        [SerializeField] private PlayerNetworkData playerData;
        [SerializeField] private DrsTurboAbility drsAbility;
        [SerializeField] private PinchazoAbility pinchazoAbility;

        private void Start()
        {
            if (!carController.IsOwner) return;

            var joystick = FindObjectOfType<VirtualJoystick>();
            joystick?.BindLocalCar(carController);

            var abilityButtons = FindObjectsOfType<AbilityButtonUI>();
            // Convencion: coloca el boton de DRS primero en la jerarquia y el de Pinchazo segundo,
            // o mejor aun, expon dos referencias serializadas separadas en un HUD manager propio.
            if (abilityButtons.Length > 0) abilityButtons[0].Bind(drsAbility);
            if (abilityButtons.Length > 1) abilityButtons[1].Bind(pinchazoAbility);

            var hud = FindObjectOfType<HUD_RaceUI>();
            hud?.BindLocalPlayer(playerData);
        }
    }
}
