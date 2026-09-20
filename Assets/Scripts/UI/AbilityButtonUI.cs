using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Boton de habilidad con relleno radial mostrando el cooldown restante.
    /// Asignalo a un Button + Image (Fill Amount, tipo Radial 360) y
    /// enlazalo con la AbilityBase correspondiente (DRS o Pinchazo) del
    /// auto local cuando este spawnea.
    /// </summary>
    public class AbilityButtonUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image cooldownFillImage; // Image Type = Filled, Fill Method = Radial 360

        private AbilityBase _ability;

        private void Awake()
        {
            button.onClick.AddListener(HandleClick);
        }

        public void Bind(AbilityBase ability)
        {
            _ability = ability;
        }

        private void HandleClick()
        {
            _ability?.TryActivate();
        }

        private void Update()
        {
            if (_ability == null) return;

            cooldownFillImage.fillAmount = _ability.NormalizedCooldown;
            button.interactable = _ability.CanUseLocally;
        }
    }
}
