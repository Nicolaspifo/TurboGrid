using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TurboGrid
{
    /// <summary>Hace rebotar una tarjeta de UI al tocarla (pantalla de instrucciones).</summary>
    public class CardTap : MonoBehaviour, IPointerClickHandler
    {
        private Coroutine _running;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Bounce());
        }

        private IEnumerator Bounce()
        {
            const float duration = 0.35f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Sin(t / duration * Mathf.PI);
                transform.localScale = Vector3.one * (1f + k * 0.08f);
                transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 40f) * 3f * (1f - t / duration));
                yield return null;
            }
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;
            _running = null;
        }
    }
}
