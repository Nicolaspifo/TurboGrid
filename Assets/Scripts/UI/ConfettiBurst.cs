using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Lluvia de confeti hecha con imagenes de UI (funciona en cualquier canvas, sin
    /// sistema de particulas). Llamar ConfettiBurst.Play(contenedor, cantidad).
    /// El contenedor debe ser un RectTransform que ocupe toda la pantalla.
    /// </summary>
    public class ConfettiBurst : MonoBehaviour
    {
        private struct Piece
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Spin;
            public float Wobble;
            public float Life;
        }

        private static readonly Color[] Colors =
        {
            new Color(1f, 0.84f, 0.2f),   // dorado
            new Color(0.93f, 0.23f, 0.23f),
            new Color(0.2f, 0.55f, 0.95f),
            new Color(0.25f, 0.8f, 0.35f),
            new Color(0.78f, 0.49f, 1f),
            Color.white,
        };

        private readonly List<Piece> _pieces = new List<Piece>();
        private float _gravity = -900f;
        private float _lifetime = 4f;

        public static ConfettiBurst Play(RectTransform container, int amount, bool fromTop)
        {
            if (container == null) return null;
            var go = new GameObject("Confeti", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(container, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            var burst = go.AddComponent<ConfettiBurst>();
            burst.Spawn(amount, fromTop);
            return burst;
        }

        private void Spawn(int amount, bool fromTop)
        {
            var area = ((RectTransform)transform).rect;
            for (int i = 0; i < amount; i++)
            {
                var go = new GameObject("Pieza", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.sizeDelta = new Vector2(Random.Range(12f, 26f), Random.Range(7f, 14f));

                var img = go.AddComponent<Image>();
                img.color = Colors[Random.Range(0, Colors.Length)];
                img.raycastTarget = false;

                Vector2 start;
                Vector2 velocity;
                if (fromTop)
                {
                    start = new Vector2(Random.Range(area.xMin, area.xMax), area.yMax + Random.Range(10f, 300f));
                    velocity = new Vector2(Random.Range(-80f, 80f), Random.Range(-250f, -60f));
                }
                else
                {
                    // Explosion desde el centro hacia arriba.
                    start = new Vector2(Random.Range(-60f, 60f), Random.Range(-40f, 40f));
                    float angle = Random.Range(35f, 145f) * Mathf.Deg2Rad;
                    float speed = Random.Range(600f, 1300f);
                    velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                }
                rt.anchoredPosition = start;
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                _pieces.Add(new Piece
                {
                    Rect = rt,
                    Image = img,
                    Velocity = velocity,
                    Spin = Random.Range(-540f, 540f),
                    Wobble = Random.Range(0f, 10f),
                    Life = _lifetime + Random.Range(-0.8f, 0.8f),
                });
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _pieces.Count - 1; i >= 0; i--)
            {
                var p = _pieces[i];
                p.Life -= dt;
                if (p.Life <= 0f || p.Rect == null)
                {
                    if (p.Rect != null) Destroy(p.Rect.gameObject);
                    _pieces.RemoveAt(i);
                    continue;
                }

                // Gravedad + resistencia del aire: el confeti cae lento y oscilando.
                p.Velocity.y = Mathf.Max(p.Velocity.y + _gravity * dt, -260f);
                p.Velocity.x *= 1f - 1.5f * dt;
                float sway = Mathf.Sin(Time.unscaledTime * 4f + p.Wobble) * 60f;
                p.Rect.anchoredPosition += (p.Velocity + new Vector2(sway, 0f)) * dt;
                p.Rect.localRotation *= Quaternion.Euler(0f, 0f, p.Spin * dt);
                // "Voltea" la pieza para que parezca papel.
                float flip = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f + p.Wobble));
                p.Rect.localScale = new Vector3(1f, 0.3f + 0.7f * flip, 1f);
                if (p.Life < 0.6f)
                {
                    var c = p.Image.color;
                    c.a = p.Life / 0.6f;
                    p.Image.color = c;
                }
                _pieces[i] = p;
            }

            if (_pieces.Count == 0) Destroy(gameObject);
        }
    }
}
