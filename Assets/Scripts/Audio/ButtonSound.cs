using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>Clic al tocar cualquier boton (lo agrega AudioManager automaticamente).</summary>
    public class ButtonSound : MonoBehaviour
    {
        private void Awake()
        {
            var button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(() => AudioManager.Play("boton", 0.6f, 0.02f));
        }
    }
}
