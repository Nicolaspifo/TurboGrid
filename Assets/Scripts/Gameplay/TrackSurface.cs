using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Sabe si un punto del mundo esta SOBRE el asfalto o fuera de la pista (pasto,
    /// la isla del medio...). Se pone en el objeto que dibuja la pista (SpriteRenderer):
    /// lo opaco de la imagen es pista y lo transparente es "fuera".
    ///
    /// Requiere que la textura de la pista tenga "Read/Write" activado (lo hace el menu
    /// TurboGrid). Si no se puede leer, todo cuenta como pista (no frena a nadie).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class TrackSurface : MonoBehaviour
    {
        public static TrackSurface Instance { get; private set; }

        [Tooltip("Opacidad minima (0-255) para considerar un pixel como pista.")]
        [SerializeField, Range(1, 254)] private int alphaThreshold = 200; // alto: la sombra semitransparente de la pista no cuenta
        [Tooltip("Reduccion de la mascara (1 = cada pixel, 4 = un cuarto). Menos memoria.")]
        [SerializeField, Range(1, 8)] private int downsample = 2;
        [Tooltip("Rellena huecos chicos de la mascara (lineas punteadas semitransparentes), en pixeles de la mascara.")]
        [SerializeField, Range(0, 8)] private int closeRadius = 3;

        private bool[] _mask;
        private int _maskW, _maskH;
        private Rect _rect;          // rect del sprite dentro de la textura (pixeles)
        private Vector2 _pivotPx;    // pivote del sprite (pixeles, relativo al rect)
        private float _ppu;
        private bool _ready;

        private void Awake()
        {
            Instance = this;
            BuildMask();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BuildMask()
        {
            var sr = GetComponent<SpriteRenderer>();
            var sprite = sr != null ? sr.sprite : null;
            if (sprite == null) return;

            var tex = sprite.texture;
            if (tex == null || !tex.isReadable)
            {
                Debug.LogWarning($"[TrackSurface] La textura '{(tex != null ? tex.name : "?")}' no tiene Read/Write: " +
                                 "no se detectara cuando un auto sale de la pista. Ejecuta TurboGrid > Aplicar mejoras.");
                return;
            }

            _rect = sprite.textureRect;
            _pivotPx = sprite.pivot;
            _ppu = sprite.pixelsPerUnit;

            Color32[] pixels;
            try { pixels = tex.GetPixels32(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TrackSurface] No se pudo leer la pista: {e.Message}");
                return;
            }

            int step = Mathf.Max(1, downsample);
            _maskW = Mathf.CeilToInt(_rect.width / step);
            _maskH = Mathf.CeilToInt(_rect.height / step);
            _mask = new bool[_maskW * _maskH];
            int texW = tex.width;
            int x0 = (int)_rect.x, y0 = (int)_rect.y;

            for (int my = 0; my < _maskH; my++)
            {
                int ty = y0 + my * step;
                for (int mx = 0; mx < _maskW; mx++)
                {
                    int tx = x0 + mx * step;
                    _mask[my * _maskW + mx] = pixels[ty * texW + tx].a >= alphaThreshold;
                }
            }
            if (closeRadius > 0)
            {
                _mask = Morph(_mask, _maskW, _maskH, closeRadius, true);  // dilatar
                _mask = Morph(_mask, _maskW, _maskH, closeRadius, false); // erosionar
            }
            _ready = true;
        }

        /// <summary>Dilatacion (o erosion) separable con un cuadrado de radio r.</summary>
        private static bool[] Morph(bool[] src, int w, int h, int r, bool dilate)
        {
            var tmp = new bool[src.Length];
            var dst = new bool[src.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool v = !dilate;
                    for (int k = -r; k <= r; k++)
                    {
                        int xx = Mathf.Clamp(x + k, 0, w - 1);
                        bool c = src[y * w + xx];
                        if (dilate ? c : !c) { v = dilate; break; }
                    }
                    tmp[y * w + x] = v;
                }
            }
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool v = !dilate;
                    for (int k = -r; k <= r; k++)
                    {
                        int yy = Mathf.Clamp(y + k, 0, h - 1);
                        bool c = tmp[yy * w + x];
                        if (dilate ? c : !c) { v = dilate; break; }
                    }
                    dst[y * w + x] = v;
                }
            }
            return dst;
        }

        /// <summary>True si el punto esta sobre el asfalto (o si no hay mascara disponible).</summary>
        public bool IsOnRoad(Vector2 worldPoint)
        {
            if (!_ready) return true;

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            float px = local.x * _ppu + _pivotPx.x;
            float py = local.y * _ppu + _pivotPx.y;
            if (px < 0f || py < 0f || px >= _rect.width || py >= _rect.height) return false;

            int step = Mathf.Max(1, downsample);
            int mx = Mathf.Clamp((int)(px / step), 0, _maskW - 1);
            int my = Mathf.Clamp((int)(py / step), 0, _maskH - 1);
            return _mask[my * _maskW + mx];
        }
    }
}
