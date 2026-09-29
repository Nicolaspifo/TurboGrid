using System.Collections;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Pequeñas animaciones por codigo para la UI (sin Animator): aparecer con rebote,
    /// desvanecer, caer, etc. Usan tiempo "unscaled" para funcionar aunque el juego se pause.
    /// </summary>
    public static class UIAnim
    {
        public static float EaseOutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }

        public static float EaseOutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        public static float EaseOutBounce(float t)
        {
            t = Mathf.Clamp01(t);
            const float n = 7.5625f, d = 2.75f;
            if (t < 1f / d) return n * t * t;
            if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
            if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
            t -= 2.625f / d;
            return n * t * t + 0.984375f;
        }

        /// <summary>Escala de 0 a la escala original con rebote.</summary>
        public static IEnumerator PopIn(Transform target, float duration, float delay = 0f)
        {
            if (target == null) yield break;
            Vector3 finalScale = target.localScale;
            target.localScale = Vector3.zero;
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (target == null) yield break;
                target.localScale = finalScale * EaseOutBack(t / duration, 2.2f);
                yield return null;
            }
            if (target != null) target.localScale = finalScale;
        }

        /// <summary>Cambia la opacidad de un CanvasGroup.</summary>
        public static IEnumerator Fade(CanvasGroup group, float from, float to, float duration, float delay = 0f)
        {
            if (group == null) yield break;
            group.alpha = from;
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (group == null) yield break;
                group.alpha = Mathf.Lerp(from, to, EaseOutCubic(t / duration));
                yield return null;
            }
            if (group != null) group.alpha = to;
        }

        /// <summary>Cae desde arriba y rebota en su posicion final.</summary>
        public static IEnumerator DropIn(RectTransform target, float height, float duration, float delay = 0f)
        {
            if (target == null) yield break;
            Vector2 finalPos = target.anchoredPosition;
            target.anchoredPosition = finalPos + Vector2.up * height;
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (target == null) yield break;
                target.anchoredPosition = finalPos + Vector2.up * height * (1f - EaseOutBounce(t / duration));
                yield return null;
            }
            if (target != null) target.anchoredPosition = finalPos;
        }

        /// <summary>Entra deslizandose desde un costado y apareciendo.</summary>
        public static IEnumerator SlideIn(RectTransform target, CanvasGroup group, Vector2 offset, float duration, float delay = 0f)
        {
            if (target == null) yield break;
            Vector2 finalPos = target.anchoredPosition;
            target.anchoredPosition = finalPos + offset;
            if (group != null) group.alpha = 0f;
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (target == null) yield break;
                float k = EaseOutBack(t / duration, 1.4f);
                target.anchoredPosition = finalPos + offset * (1f - k);
                if (group != null) group.alpha = Mathf.Clamp01(t / (duration * 0.6f));
                yield return null;
            }
            if (target != null) target.anchoredPosition = finalPos;
            if (group != null) group.alpha = 1f;
        }

        public static CanvasGroup EnsureGroup(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();
            return group;
        }

        /// <summary>Busca un hijo por nombre a cualquier profundidad.</summary>
        public static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
