using TMPro;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Efectos visuales del auto, en todos los clientes:
    ///  - Nombre del piloto flotando sobre el auto (con su color).
    ///  - Estela azul mientras el DRS esta activo.
    ///  - Parpadeo + tinte morado mientras esta bloqueado por un Pinchazo.
    ///  - Marcador de color para el mini-mapa (capa "Minimapa").
    /// Se agrega solo desde PlayerNetworkData; no hace falta configurarlo en el prefab.
    /// </summary>
    public class CarVisuals : MonoBehaviour
    {
        public const string MINIMAP_LAYER = "Minimapa";
        public const int LABEL_LAYER = 2; // Ignore Raycast

        private PlayerNetworkData _data;
        private SpriteRenderer _body;
        private Color _bodyBaseColor = Color.white;
        private TrailRenderer _trail;
        private TextMeshPro _label;
        private Transform _labelRoot;
        private SpriteRenderer _minimapMarker;
        private float _labelOffset = 55f;

        private static Sprite _circleSprite;
        private static Material _trailMaterial;

        private void Awake()
        {
            _data = GetComponent<PlayerNetworkData>();
            _body = GetComponent<SpriteRenderer>();
            if (_body == null) _body = GetComponentInChildren<SpriteRenderer>();
            if (_body != null) _bodyBaseColor = _body.color;

            // Tamaño aproximado del auto en unidades de mundo para ubicar la etiqueta.
            if (_body != null) _labelOffset = Mathf.Max(40f, _body.bounds.extents.magnitude + 20f);

            CreateTrail();
            CreateLabel();
            CreateMinimapMarker();
        }

        private void OnDestroy()
        {
            if (_labelRoot != null) Destroy(_labelRoot.gameObject);
        }

        private void CreateTrail()
        {
            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.35f;
            _trail.startWidth = 26f;
            _trail.endWidth = 0f;
            _trail.minVertexDistance = 4f;
            _trail.emitting = false;
            _trail.sortingOrder = (_body != null ? _body.sortingOrder : 0) - 1;
            // Se usa el mismo material del sprite del auto (siempre incluido en el build).
            // Shader.Find("Sprites/Default") puede devolver null en celular si el shader no se incluyo.
            if (_trailMaterial == null && _body != null) _trailMaterial = _body.sharedMaterial;
            if (_trailMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null) _trailMaterial = new Material(shader);
            }
            if (_trailMaterial != null) _trail.sharedMaterial = _trailMaterial;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.3f, 0.75f, 1f), 0f), new GradientColorKey(new Color(0.1f, 0.35f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = gradient;
        }

        private void CreateLabel()
        {
            // La etiqueta NO es hija del auto para que no gire con el.
            var go = new GameObject($"{name}_Etiqueta");
            // Capa "Ignore Raycast": el mini-mapa la excluye para no llenarse de texto.
            go.layer = LABEL_LAYER;
            _labelRoot = go.transform;
            _label = go.AddComponent<TextMeshPro>();
            _label.fontSize = 240f; // en texto 3D, ~10 puntos = 1 unidad de mundo
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.rectTransform.sizeDelta = new Vector2(900f, 120f);
            _label.fontStyle = FontStyles.Bold;
            _label.outlineWidth = 0.25f;
            _label.outlineColor = new Color32(0, 0, 0, 200);
            _label.sortingOrder = 50;
            _label.text = string.Empty;
        }

        private void CreateMinimapMarker()
        {
            int layer = LayerMask.NameToLayer(MINIMAP_LAYER);
            if (layer < 0) return; // sin capa: el mini-mapa muestra los autos tal cual

            var go = new GameObject("MarcadorMinimapa");
            go.layer = layer;
            go.transform.SetParent(transform, false);
            // Compensar la escala del auto para que el marcador mida ~70 unidades de mundo.
            float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            go.transform.localScale = Vector3.one * (70f / parentScale);
            _minimapMarker = go.AddComponent<SpriteRenderer>();
            _minimapMarker.sprite = GetCircleSprite();
            _minimapMarker.sortingOrder = 100;
        }

        private void LateUpdate()
        {
            if (_data == null || !_data.IsSpawned) return;

            Color playerColor = _data.PlayerColor;

            // Etiqueta con nombre
            if (_labelRoot != null)
            {
                _labelRoot.position = transform.position + Vector3.up * _labelOffset;
                _labelRoot.rotation = Quaternion.identity;
                string text = _data.IsOwner ? $"{_data.DisplayName} (tu)" : _data.DisplayName;
                if (_label.text != text) _label.text = text;
                _label.color = playerColor;
            }

            if (_minimapMarker != null)
            {
                _minimapMarker.color = playerColor;
                // Pulso del propio jugador para encontrarse rapido en el mini-mapa.
                float s = _data.IsOwner ? 1f + Mathf.Sin(Time.time * 6f) * 0.2f : 1f;
                _minimapMarker.transform.localScale = Vector3.one * (70f / Mathf.Max(0.0001f, transform.lossyScale.x)) * s;
            }

            // DRS
            if (_trail != null) _trail.emitting = _data.IsDrsActive.Value;

            // Pinchazo: parpadeo morado
            if (_body != null)
            {
                bool frozenByEnd = RaceManager.Instance != null && RaceManager.Instance.IsFinished;
                if (_data.IsStunned.Value && !frozenByEnd)
                {
                    bool flash = Mathf.Repeat(Time.time * 10f, 1f) > 0.5f;
                    _body.color = flash ? new Color(0.75f, 0.35f, 1f, 1f) : new Color(1f, 1f, 1f, 0.45f);
                }
                else if (_data.IsDrsActive.Value)
                {
                    _body.color = Color.Lerp(_bodyBaseColor, new Color(0.6f, 0.85f, 1f), 0.5f + Mathf.Sin(Time.time * 12f) * 0.25f);
                }
                else
                {
                    _body.color = _bodyBaseColor;
                }
            }
        }

        public static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size / 2f - 1f;
            var center = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(r - d);
                    // Borde oscuro para que se distinga sobre cualquier fondo (el relleno se tiñe).
                    Color c = d > r - 7f ? new Color(0.1f, 0.1f, 0.1f, alpha) : new Color(1f, 1f, 1f, alpha);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            // pixelsPerUnit = size -> el sprite mide 1 unidad; se escala desde afuera.
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _circleSprite;
        }
    }
}
