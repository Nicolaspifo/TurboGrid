using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TurboGrid
{
    /// <summary>
    /// Anima la pantalla de instrucciones:
    ///  - Las banderas caen y ondean, el titulo aparece con rebote.
    ///  - Las tarjetas entran una por una desde abajo.
    ///  - Los iconos flotan suavemente y la flecha "Siguiente" late para invitar a tocarla.
    ///  - Al tocar una tarjeta, rebota.
    /// </summary>
    public class InstructionsAnimator : MonoBehaviour
    {
        [SerializeField] private RectTransform flags;
        [SerializeField] private RectTransform title;
        [SerializeField] private RectTransform[] cards;
        [SerializeField] private RectTransform[] icons;
        [SerializeField] private RectTransform nextButton;
        [SerializeField] private RectTransform backButton;

        private Vector2[] _iconBase;
        private Vector3 _nextBaseScale;
        private Vector3 _backBaseScale;
        private bool _introDone;

        public void Configure(RectTransform flagsRect, RectTransform titleRect, RectTransform[] cardRects,
            RectTransform[] iconRects, RectTransform next, RectTransform back)
        {
            flags = flagsRect;
            title = titleRect;
            cards = cardRects;
            icons = iconRects;
            nextButton = next;
            backButton = back;
        }

        private void Start()
        {
            if (icons != null)
            {
                _iconBase = new Vector2[icons.Length];
                for (int i = 0; i < icons.Length; i++)
                    if (icons[i] != null) _iconBase[i] = icons[i].anchoredPosition;
            }
            if (nextButton != null) _nextBaseScale = nextButton.localScale;
            if (backButton != null) _backBaseScale = backButton.localScale;

            // Cada tarjeta rebota al tocarla.
            if (cards != null)
                foreach (var card in cards)
                    if (card != null && card.GetComponent<CardTap>() == null) card.gameObject.AddComponent<CardTap>();

            StartCoroutine(Intro());
        }

        private IEnumerator Intro()
        {
            if (flags != null) StartCoroutine(UIAnim.DropIn(flags, 400f, 0.9f));
            if (title != null) StartCoroutine(UIAnim.PopIn(title, 0.5f, 0.35f));

            float lastDelay = 0.5f;
            if (cards != null)
            {
                for (int i = 0; i < cards.Length; i++)
                {
                    if (cards[i] == null) continue;
                    float delay = 0.55f + i * 0.13f;
                    lastDelay = delay;
                    StartCoroutine(UIAnim.SlideIn(cards[i], UIAnim.EnsureGroup(cards[i].gameObject), new Vector2(0f, -140f), 0.55f, delay));
                }
            }

            if (nextButton != null) StartCoroutine(UIAnim.PopIn(nextButton, 0.4f, lastDelay + 0.3f));
            if (backButton != null) StartCoroutine(UIAnim.PopIn(backButton, 0.4f, lastDelay + 0.35f));

            yield return new WaitForSecondsRealtime(lastDelay + 0.8f);
            _introDone = true;
        }

        private void Update()
        {
            float t = Time.unscaledTime;

            // Banderas ondeando.
            if (flags != null) flags.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.8f) * 3f);

            // Iconos flotando, cada uno con su propio ritmo.
            if (icons != null && _iconBase != null)
            {
                for (int i = 0; i < icons.Length; i++)
                {
                    if (icons[i] == null) continue;
                    float bob = Mathf.Sin(t * 2.4f + i * 0.9f) * 6f;
                    icons[i].anchoredPosition = _iconBase[i] + new Vector2(0f, bob);
                    icons[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.7f + i) * 5f);
                }
            }

            if (!_introDone) return;

            // La flecha de "Siguiente" late para indicar que hay que tocarla.
            if (nextButton != null)
                nextButton.localScale = _nextBaseScale * (1f + Mathf.Max(0f, Mathf.Sin(t * 4f)) * 0.12f);
        }
    }
}
