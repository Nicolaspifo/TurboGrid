using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Muestra solo los controles tactiles del esquema que eligio ESTE jugador
    /// en el lobby (flechas, timon o joystick). Cada celular decide el suyo.
    /// </summary>
    public class ControlsUI : MonoBehaviour
    {
        [SerializeField] private GameObject arrowsRoot;    // flechas izq/der
        [SerializeField] private GameObject wheelRoot;     // timon
        [SerializeField] private GameObject joystickRoot;  // joystick
        [SerializeField] private GameObject pedalsRoot;    // acelerar/frenar (flechas y timon)

        private void OnEnable()
        {
            LocalPlayerSettings.OnChanged += Apply;
            CarInput.ResetAll();
            Apply();
        }

        private void OnDisable()
        {
            LocalPlayerSettings.OnChanged -= Apply;
            CarInput.ResetAll();
        }

        public void Apply()
        {
            var scheme = LocalPlayerSettings.Scheme;
            if (arrowsRoot != null) arrowsRoot.SetActive(scheme == ControlScheme.Flechas);
            if (wheelRoot != null) wheelRoot.SetActive(scheme == ControlScheme.Timon);
            if (joystickRoot != null) joystickRoot.SetActive(scheme == ControlScheme.Joystick);
            if (pedalsRoot != null) pedalsRoot.SetActive(scheme != ControlScheme.Joystick);
        }
    }
}
