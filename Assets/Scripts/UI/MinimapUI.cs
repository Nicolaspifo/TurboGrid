using UnityEngine;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Mini-mapa del circuito: una segunda camara ortografica ve el circuito completo
    /// y lo dibuja en una RenderTexture que se muestra en este RawImage del HUD.
    /// Los autos se ven con un circulo de su color (CarVisuals, capa "Minimapa").
    ///
    /// Si no se asignan limites, se calculan con el SpriteRenderer mas grande de la
    /// escena (normalmente la pista).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class MinimapUI : MonoBehaviour
    {
        [Tooltip("Centro y tamaño del area a mostrar. Si size = 0 se calcula automaticamente.")]
        [SerializeField] private Vector2 mapCenter;
        [SerializeField] private Vector2 mapSize;
        [SerializeField] private float padding = 20f;
        [SerializeField] private int textureHeight = 256;
        [SerializeField] private Color backgroundColor = new Color(0.1f, 0.12f, 0.2f, 1f);

        private RawImage _image;
        private Camera _minimapCamera;
        private RenderTexture _texture;

        private void Awake()
        {
            _image = GetComponent<RawImage>();
        }

        private void Start()
        {
            if (mapSize.sqrMagnitude < 1f) AutoDetectBounds();
            CreateCamera();
        }

        private void OnDestroy()
        {
            if (_minimapCamera != null) Destroy(_minimapCamera.gameObject);
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
        }

        private void AutoDetectBounds()
        {
            SpriteRenderer biggest = null;
            float biggestArea = 0f;
            foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            {
                if (sr.gameObject.scene != gameObject.scene) continue;
                Vector3 size = sr.bounds.size;
                float area = size.x * size.y;
                if (area > biggestArea)
                {
                    biggestArea = area;
                    biggest = sr;
                }
            }

            if (biggest != null)
            {
                mapCenter = biggest.bounds.center;
                mapSize = biggest.bounds.size;
            }
            else
            {
                mapCenter = Vector2.zero;
                mapSize = new Vector2(1400f, 800f);
            }
        }

        private void CreateCamera()
        {
            Rect rect = ((RectTransform)transform).rect;
            float uiAspect = rect.height > 1f ? rect.width / rect.height : mapSize.x / Mathf.Max(1f, mapSize.y);

            int height = Mathf.Max(64, textureHeight);
            int width = Mathf.Max(64, Mathf.RoundToInt(height * uiAspect));
            _texture = new RenderTexture(width, height, 16) { name = "MinimapaRT" };

            var go = new GameObject("CamaraMinimapa");
            go.transform.position = new Vector3(mapCenter.x, mapCenter.y, -500f);
            _minimapCamera = go.AddComponent<Camera>();
            _minimapCamera.orthographic = true;
            float sizeByHeight = mapSize.y / 2f + padding;
            float sizeByWidth = (mapSize.x / 2f + padding) / uiAspect;
            _minimapCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth);
            _minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            _minimapCamera.backgroundColor = backgroundColor;
            _minimapCamera.nearClipPlane = 0.1f;
            _minimapCamera.farClipPlane = 2000f;
            _minimapCamera.targetTexture = _texture;
            _minimapCamera.depth = -10f;

            // No dibujar la UI ni las etiquetas de nombre.
            int mask = ~0;
            mask &= ~(1 << 5);                     // UI
            mask &= ~(1 << CarVisuals.LABEL_LAYER); // etiquetas
            _minimapCamera.cullingMask = mask;

            _image.texture = _texture;
            _image.color = Color.white;
        }
    }
}
