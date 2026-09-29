using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Boton de power-up (DRS o Pinchazo) del jugador local.
    ///  - Apagado (gris) mientras no lo haya recogido en pista.
    ///  - Encendido y latiendo cuando lo tiene guardado: tocar para usarlo.
    ///  - El relleno radial muestra el tiempo que le queda al DRS activo.
    /// Se enlaza solo con el auto local cuando este spawnea.
    /// </summary>
    public class AbilityButtonUI : MonoBehaviour
    {
        [SerializeField] private PowerUpType type = PowerUpType.Drs;
        [SerializeField] private Button button;
        [SerializeField] private Image iconImage;          // imagen del boton (se agrisa sin carga)
        [SerializeField] private Image cooldownFillImage;  // Image Type = Filled, Radial 360 (opcional)

        private AbilityBase _ability;
        private Vector3 _baseScale;

        public PowerUpType Type => type;

        public void Configure(PowerUpType newType, Button newButton, Image icon, Image fill)
        {
            type = newType;
            button = newButton;
            iconImage = icon;
            cooldownFillImage = fill;
        }

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (iconImage == null && button != null) iconImage = button.targetGraphic as Image;
            if (button != null) button.onClick.AddListener(HandleClick);
            _baseScale = transform.localScale;

            // Compatibilidad con la escena anterior: el boton "AbilityPinchazo" no tenia tipo.
            if (name.ToLowerInvariant().Contains("pinchazo")) type = PowerUpType.Pinchazo;
        }

        private void OnEnable()
        {
            PlayerNetworkData.OnLocalPlayerSpawned += Bind;
            if (PlayerNetworkData.Local != null) Bind(PlayerNetworkData.Local);
        }

        private void OnDisable()
        {
            PlayerNetworkData.OnLocalPlayerSpawned -= Bind;
        }

        private void Bind(PlayerNetworkData player)
        {
            _ability = null;
            foreach (var a in player.GetComponents<AbilityBase>())
            {
                if (a.Type == type) { _ability = a; break; }
            }
        }

        private void HandleClick()
        {
            _ability?.TryActivate();
        }

        private void Update()
        {
            if (_ability == null && PlayerNetworkData.Local != null) Bind(PlayerNetworkData.Local);

            bool hasCharge = _ability != null && _ability.HasCharge;
            bool usable = _ability != null && _ability.CanUseNow;

            if (button != null) button.interactable = usable;
            if (iconImage != null)
                iconImage.color = hasCharge ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.6f);

            if (cooldownFillImage != null)
            {
                float active = _ability != null ? _ability.ActiveNormalized : 0f;
                cooldownFillImage.fillAmount = active;
                cooldownFillImage.enabled = active > 0f;
            }

            // Latido cuando esta listo para usarse.
            float pulse = usable ? 1f + Mathf.Sin(Time.time * 7f) * 0.06f : 1f;
            transform.localScale = _baseScale * pulse;

            // Atajos de teclado para probar en el editor: E/Espacio = DRS, Q = Pinchazo.
            if (_ability != null && usable)
            {
                if (type == PowerUpType.Drs && CarInput.DrsKeyPressed) _ability.TryActivate();
                if (type == PowerUpType.Pinchazo && CarInput.PinchazoKeyPressed) _ability.TryActivate();
            }
        }
    }
}
