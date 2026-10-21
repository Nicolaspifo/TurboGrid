using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Panel "PROBAR SONIDOS": un boton por cada efecto y musica del juego, mas demos
    /// del motor y derrape procedurales. Se abre desde Opciones (boton PROBAR) y se
    /// construye solo por codigo, asi que funciona en el editor y en el celular.
    /// </summary>
    public class SoundTestPanel : MonoBehaviour
    {
        private static SoundTestPanel _open;

        private static readonly Color ColSfx = new Color(0.16f, 0.4f, 0.8f);
        private static readonly Color ColMusic = new Color(0.55f, 0.3f, 0.8f);
        private static readonly Color ColEngine = new Color(0.85f, 0.45f, 0.15f);
        private static readonly Color ColClose = new Color(0.8f, 0.2f, 0.2f);

        private TMP_FontAsset _font;
        private TMP_Text _status;
        private CarAudio _engine;
        private Coroutine _demo;

        /// <summary>Abre el panel sobre el canvas indicado (si ya esta abierto, lo trae al frente).</summary>
        public static SoundTestPanel Open(Canvas canvas, TMP_FontAsset font)
        {
            if (canvas == null) return null;
            if (_open != null)
            {
                _open.transform.SetAsLastSibling();
                return _open;
            }

            var go = new GameObject("TG_ProbarSonidos", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            _open = go.AddComponent<SoundTestPanel>();
            _open._font = font;
            _open.Build();
            return _open;
        }

        private void OnDestroy()
        {
            if (_open == this) _open = null;
            if (_engine != null) Destroy(_engine.gameObject);
        }

        // ---------------------------------------------------------------------
        // Construccion
        // ---------------------------------------------------------------------

        private void Build()
        {
            var overlay = gameObject.AddComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.8f); // bloquea los toques al fondo

            var box = NewImage(transform, "Caja", new Color(0.06f, 0.16f, 0.4f, 0.98f));
            SetRect(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(780f, 350f));
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.02f, 0.04f, 0.1f);
            outline.effectDistance = new Vector2(3f, -3f);

            var title = NewText(box.transform, "Titulo", "PROBAR SONIDOS", 24, new Color(1f, 0.82f, 0.3f));
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(500f, 34f));

            var close = NewButton(box.transform, "Cerrar", "CERRAR", ColClose, Close);
            SetRect((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-62f, -22f), new Vector2(100f, 30f));

            _status = NewText(box.transform, "Estado", "Toca un boton para escucharlo", 12, new Color(1f, 1f, 1f, 0.8f));
            SetRect(_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(740f, 20f));

            var grid = new GameObject("Botones", typeof(RectTransform));
            var gridRt = (RectTransform)grid.transform;
            gridRt.SetParent(box.transform, false);
            SetRect(gridRt, new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(748f, 256f));
            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(180f, 30f);
            layout.spacing = new Vector2(8f, 7f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.childAlignment = TextAnchor.UpperCenter;

            // Efectos
            foreach (var sfx in AudioManager.AllSfx)
            {
                string name = sfx;
                NewButton(gridRt, name, Pretty(name), ColSfx, () =>
                {
                    StopDemo();
                    AudioManager.Play(name, 1f, 0f);
                    SetStatus($"Efecto: {name}");
                });
            }

            // Musica
            foreach (var music in AudioManager.AllMusic)
            {
                string name = music;
                bool loop = name != "musica_victoria";
                NewButton(gridRt, name, Pretty(name), ColMusic, () =>
                {
                    AudioManager.PlayMusic(name, loop, 0.4f);
                    SetStatus(loop ? $"Musica: {name} (loop, se repite sin corte)" : $"Musica: {name} (una sola vez)");
                });
            }
            NewButton(gridRt, "PararMusica", "Parar musica", ColMusic, () =>
            {
                AudioManager.StopMusic();
                SetStatus("Musica detenida");
            });

            // Procedurales
            NewButton(gridRt, "Motor", "V12: acelerar", ColEngine, () => StartDemo(EngineRev(false), "Motor V12: ralenti -> 9000 rpm -> suelta el acelerador"));
            NewButton(gridRt, "MotorDrs", "V12 con DRS", ColEngine, () => StartDemo(EngineRev(true), "V12 con DRS: mas rpm, mas abierto + soplido del turbo"));
            NewButton(gridRt, "Ralenti", "V12 en ralenti", ColEngine, () => StartDemo(Idle(), "V12 en ralenti (antes de la largada)"));

            if (!AudioManager.SfxEnabled)
                SetStatus("El sonido esta en SILENCIO: cambialo en Opciones > Sonido");
        }

        private static string Pretty(string id)
        {
            switch (id)
            {
                case "gasolina": return "Gasolina";
                case "recoger_drs": return "Recoger DRS";
                case "activar_drs": return "Activar DRS";
                case "recoger_pinchazo": return "Recoger Pinchazo";
                case "lanzar_pinchazo": return "Lanzar Pinchazo";
                case "pinchado": return "Te pincharon";
                case "escudo": return "Recoger Escudo";
                case "escudo_bloqueo": return "Escudo bloquea";
                case "iman": return "Iman";
                case "doble_puntos": return "Puntos x2";
                case "choque": return "Choque";
                case "frenazo": return "Frenazo";
                case "cuenta_beep": return "Cuenta: bip";
                case "cuenta_go": return "Cuenta: ¡YA!";
                case "boton": return "Boton";
                case "jugador_listo": return "Piloto listo";
                case "jugador_entra": return "Piloto entra";
                case "aplausos": return "Aplausos";
                case "musica_menu": return "Musica menu";
                case "musica_carrera": return "Musica carrera";
                case "musica_victoria": return "Musica victoria";
                default: return id;
            }
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }

        // ---------------------------------------------------------------------
        // Demos del motor procedural
        // ---------------------------------------------------------------------

        private void StartDemo(IEnumerator routine, string description)
        {
            StopDemo();
            if (_engine == null) _engine = CarAudio.CreatePreview();
            SetStatus(description);
            _demo = StartCoroutine(routine);
        }

        private void StopDemo()
        {
            if (_demo != null) StopCoroutine(_demo);
            _demo = null;
            if (_engine != null) _engine.SetPreview(0f, 0f, false);
            if (_engine != null) { Destroy(_engine.gameObject); _engine = null; }
        }

        private IEnumerator EngineRev(bool drs)
        {
            // Ralenti -> acelera a fondo (cambia marchas) -> suelta el acelerador
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
            {
                _engine.SetPreview(0f, 0f, drs);
                yield return null;
            }
            for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime)
            {
                _engine.SetPreview(Mathf.Clamp01(t / 4f) * (drs ? 1.15f : 1f), 1f, drs);
                yield return null;
            }
            for (float t = 0f; t < 2.5f; t += Time.unscaledDeltaTime)
            {
                _engine.SetPreview(Mathf.Lerp(drs ? 1.15f : 1f, 0.1f, t / 2.5f), 0f, drs);
                yield return null;
            }
            StopDemo();
            SetStatus("Listo");
        }

        private IEnumerator Idle()
        {
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime)
            {
                _engine.SetPreview(0f, 0f, false);
                yield return null;
            }
            StopDemo();
            SetStatus("Listo");
        }

        private void Close()
        {
            StopDemo();
            // Volver a la musica normal del menu.
            AudioManager.PlayMusic("musica_menu", true, 0.8f);
            Destroy(gameObject);
        }

        // ---------------------------------------------------------------------
        // Helpers de UI
        // ---------------------------------------------------------------------

        private static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private TMP_Text NewText(Transform parent, string name, string text, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = Mathf.Max(8f, size * 0.5f);
            tmp.fontSizeMax = size;
            return tmp;
        }

        private Button NewButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var img = NewImage(parent, name, color);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);

            var text = NewText(img.transform, "Texto", label, 14, Color.white);
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 2f);
            rt.offsetMax = new Vector2(-4f, -2f);
            return button;
        }
    }
}
