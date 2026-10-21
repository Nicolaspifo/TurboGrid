using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Pantalla de carga: oscurece todo, muestra una rueda girando y un mensaje
    /// ("Buscando la partida...", "Conectando con la sala..."). Bloquea los toques
    /// mientras esta visible. Se construye sola por codigo sobre el canvas indicado.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        private TMP_Text _message;
        private RectTransform _spinner;
        private CanvasGroup _group;
        private float _targetAlpha;

        private static Sprite _ringSprite;

        public bool IsVisible => _targetAlpha > 0f;

        public static LoadingOverlay Create(Canvas canvas, TMP_FontAsset font)
        {
            var go = new GameObject("TG_Cargando", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            var overlay = go.AddComponent<LoadingOverlay>();
            overlay.Build(font);
            return overlay;
        }

        private void Build(TMP_FontAsset font)
        {
            var dim = gameObject.AddComponent<Image>();
            dim.color = new Color(0.02f, 0.05f, 0.14f, 0.82f); // bloquea los toques al fondo
            _group = gameObject.AddComponent<CanvasGroup>();

            var spinGo = new GameObject("Rueda", typeof(RectTransform));
            _spinner = (RectTransform)spinGo.transform;
            _spinner.SetParent(transform, false);
            _spinner.sizeDelta = new Vector2(72f, 72f);
            _spinner.anchoredPosition = new Vector2(0f, 26f);
            var ring = spinGo.AddComponent<Image>();
            ring.sprite = RingSprite();
            ring.color = new Color(1f, 0.55f, 0.15f);
            ring.raycastTarget = false;

            var textGo = new GameObject("Mensaje", typeof(RectTransform));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(transform, false);
            trt.sizeDelta = new Vector2(620f, 60f);
            trt.anchoredPosition = new Vector2(0f, -44f);
            _message = textGo.AddComponent<TextMeshProUGUI>();
            if (font != null) _message.font = font;
            _message.fontSize = 22;
            _message.alignment = TextAlignmentOptions.Center;
            _message.color = Color.white;
            _message.raycastTarget = false;

            _group.alpha = 0f;
            gameObject.SetActive(false);
        }

        public void Show(string message)
        {
            if (_message != null) _message.text = message;
            _targetAlpha = 1f;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
            }
        }

        public void Hide()
        {
            _targetAlpha = 0f;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _spinner.Rotate(0f, 0f, -300f * dt);
            // Pulso suave del texto para que se note que "esta trabajando".
            if (_message != null)
            {
                var c = _message.color;
                c.a = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 4f);
                _message.color = c;
            }

            _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, dt * 5f);
            _group.blocksRaycasts = _targetAlpha > 0f;
            if (_targetAlpha <= 0f && _group.alpha <= 0f) gameObject.SetActive(false);
        }

        /// <summary>Anillo con un hueco (estilo "cargando") generado por codigo.</summary>
        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var c = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
            float outer = size / 2f - 2f, inner = outer - 14f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x, y) - c;
                    float d = p.magnitude;
                    float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                    // Hueco de ~90 grados y desvanecido en la "cola" del anillo
                    float ang = (Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg + 360f) % 360f;
                    a *= ang < 90f ? 0f : Mathf.Clamp01((ang - 90f) / 270f) * 0.85f + 0.15f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _ringSprite;
        }
    }
}
