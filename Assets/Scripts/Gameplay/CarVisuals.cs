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
        public const string CARS_SORTING_LAYER = "Cars";

        private int _sortingLayerId;

        private PlayerNetworkData _data;
        private SpriteRenderer _body;
        private Color _bodyBaseColor = Color.white;
        private TrailRenderer _trail;
        private TrailRenderer _skidLeft, _skidRight, _dust;
        private Vector3 _lastPos;
        private Vector2 _velocity;
        private TextMeshPro _label;
        private Transform _labelRoot;
        private SpriteRenderer _minimapMarker;
        private float _labelOffset = 55f;

        private SpriteRenderer _shieldAura, _magnetAura, _doubleAura;

        private static Sprite _circleSprite;
        private static Sprite _ringSprite;
        private static Material _trailMaterial;

        private void Awake()
        {
            _data = GetComponent<PlayerNetworkData>();
            _body = GetComponent<SpriteRenderer>();
            if (_body == null) _body = GetComponentInChildren<SpriteRenderer>();
            if (_body != null) _bodyBaseColor = _body.color;

            // Los autos deben dibujarse en la capa "Cars" (encima de "Track"). Si un prefab
            // quedo en "Default" se dibuja DEBAJO de la pista y el auto se vuelve invisible.
            int carsLayer = SortingLayer.NameToID(CARS_SORTING_LAYER);
            if (_body != null && SortingLayer.IsValid(carsLayer)) _body.sortingLayerID = carsLayer;
            _sortingLayerId = _body != null ? _body.sortingLayerID : 0;

            // Tamaño aproximado del auto en unidades de mundo para ubicar la etiqueta.
            if (_body != null) _labelOffset = Mathf.Max(30f, _body.bounds.extents.magnitude + 5f);

            CreateTrail();
            CreateGroundMarks();
            CreateLabel();
            CreateMinimapMarker();
            CreateAuras();

            // Motor y derrape procedurales.
            if (GetComponent<CarAudio>() == null) gameObject.AddComponent<CarAudio>();
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
            _trail.sortingLayerID = _sortingLayerId;
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
            _label.alignment = TextAlignmentOptions.Bottom; // las etiquetas extra crecen hacia arriba
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.rectTransform.sizeDelta = new Vector2(900f, 200f);
            _label.rectTransform.pivot = new Vector2(0.5f, 0f);
            _label.fontStyle = FontStyles.Bold;
            _label.outlineWidth = 0.25f;
            _label.outlineColor = new Color32(0, 0, 0, 200);
            _label.sortingLayerID = _sortingLayerId;
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
            _minimapMarker.sortingLayerID = _sortingLayerId;
            _minimapMarker.sortingOrder = 100;
        }

        /// <summary>Burbujas de color alrededor del auto para Escudo, Iman y Puntos x2.</summary>
        private void CreateAuras()
        {
            _shieldAura = MakeAura("AuraEscudo", 2);
            _magnetAura = MakeAura("AuraIman", -2);
            _doubleAura = MakeAura("AuraDoble", -3);
        }

        private SpriteRenderer MakeAura(string auraName, int orderOffset)
        {
            var go = new GameObject(auraName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetRingSprite();
            sr.sortingLayerID = _sortingLayerId;
            sr.sortingOrder = (_body != null ? _body.sortingOrder : 0) + orderOffset;
            sr.enabled = false;
            return sr;
        }

        /// <summary>Escala local para que el aura mida <paramref name="worldSize"/> en el mundo.</summary>
        private void SetAuraSize(SpriteRenderer aura, float worldSize)
        {
            float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            aura.transform.localScale = Vector3.one * (worldSize / parentScale);
        }

        private void UpdateAuras()
        {
            if (_shieldAura == null) return;
            bool finished = RaceManager.Instance != null && RaceManager.Instance.IsFinished;
            float t = Time.time;
            float carSize = _labelOffset * 2f;

            bool shield = !finished && _data.HasShield.Value;
            _shieldAura.enabled = shield;
            if (shield)
            {
                SetAuraSize(_shieldAura, carSize * (1.05f + Mathf.Sin(t * 5f) * 0.05f));
                _shieldAura.color = new Color(0.3f, 1f, 0.8f, 0.75f + Mathf.Sin(t * 5f) * 0.2f);
            }

            bool magnet = !finished && _data.IsMagnetActive;
            _magnetAura.enabled = magnet;
            if (magnet)
            {
                // Ondas que se cierran hacia el auto: muestran el radio de atraccion.
                float k = 1f - Mathf.Repeat(t * 1.2f, 1f);
                SetAuraSize(_magnetAura, Mathf.Lerp(carSize, GameConstants.IMAN_RADIUS * 2f, k));
                _magnetAura.color = new Color(1f, 0.35f, 0.35f, 0.15f + 0.55f * (1f - k));
                _magnetAura.transform.rotation = Quaternion.identity;
            }

            bool dbl = !finished && _data.IsDoublePointsActive;
            _doubleAura.enabled = dbl;
            if (dbl)
            {
                SetAuraSize(_doubleAura, carSize * (1.25f + Mathf.Sin(t * 8f) * 0.08f));
                _doubleAura.color = new Color(1f, 0.82f, 0.25f, 0.6f);
            }
        }

        /// <summary>Anillo suave (borde brillante, centro transparente) generado por codigo.</summary>
        public static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var center = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
            float r = size / 2f - 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center) / r; // 0 centro, 1 borde
                    float edge = Mathf.Clamp01((1f - d) * r);                  // antialias exterior
                    float a = Mathf.Pow(Mathf.Clamp01(d), 3f) * 0.85f + (d > 0.9f ? 0.15f : 0f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * edge));
                }
            }
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _ringSprite;
        }

        /// <summary>Marcas de llanta (derrape) en las ruedas traseras y polvo (pasto).</summary>
        private void CreateGroundMarks()
        {
            Vector3 ext = _body != null && _body.sprite != null ? _body.sprite.bounds.extents : new Vector3(2.5f, 4f, 0f);
            _skidLeft = MakeMark("MarcaIzq", new Vector3(-ext.x * 0.55f, -ext.y * 0.6f, 0f), 6f, 2.2f,
                new Color(0.08f, 0.08f, 0.09f, 0.55f), new Color(0.08f, 0.08f, 0.09f, 0f));
            _skidRight = MakeMark("MarcaDer", new Vector3(ext.x * 0.55f, -ext.y * 0.6f, 0f), 6f, 2.2f,
                new Color(0.08f, 0.08f, 0.09f, 0.55f), new Color(0.08f, 0.08f, 0.09f, 0f));
            _dust = MakeMark("Polvo", new Vector3(0f, -ext.y * 0.8f, 0f), 22f, 0.55f,
                new Color(0.55f, 0.45f, 0.28f, 0.45f), new Color(0.55f, 0.45f, 0.28f, 0f));
            _lastPos = transform.position;
        }

        private TrailRenderer MakeMark(string markName, Vector3 localPos, float width, float time, Color start, Color end)
        {
            var go = new GameObject(markName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = time;
            tr.startWidth = width;
            tr.endWidth = width * 0.9f;
            tr.minVertexDistance = 3f;
            tr.emitting = false;
            if (_trailMaterial != null) tr.sharedMaterial = _trailMaterial;
            // Sobre el asfalto pero debajo de gasolina, power-ups y autos.
            int trackLayer = SortingLayer.NameToID("Track");
            if (SortingLayer.IsValid(trackLayer)) tr.sortingLayerID = trackLayer;
            tr.sortingOrder = 50;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
                      new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
            tr.colorGradient = g;
            return tr;
        }

        private void UpdateGroundMarks()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 pos = transform.position;
            _velocity = Vector2.Lerp(_velocity, (pos - _lastPos) / dt, 1f - Mathf.Exp(-dt * 12f));
            _lastPos = pos;

            Vector2 forward = transform.up;
            Vector2 right = new Vector2(forward.y, -forward.x);
            float speed = _velocity.magnitude;
            float lateral = Mathf.Abs(Vector2.Dot(_velocity, right));

            bool offRoad = TrackSurface.Instance != null && !TrackSurface.Instance.IsOnRoad(pos);
            var car = _data != null ? _data.Car : null;
            bool ownerDrift = _data != null && _data.IsOwner && car != null && car.DriftAmount > 0.35f;
            // Derrape visible: el auto se mueve "de lado" (sirve tambien para autos remotos).
            bool sliding = !offRoad && (ownerDrift || (speed > 70f && lateral > 40f));

            if (_skidLeft != null) _skidLeft.emitting = sliding;
            if (_skidRight != null) _skidRight.emitting = sliding;
            if (_dust != null) _dust.emitting = offRoad && speed > 35f;
        }

        private void LateUpdate()
        {
            UpdateGroundMarks();

            if (_data == null || !_data.IsSpawned) return;

            Color playerColor = _data.PlayerColor;

            // Etiqueta con nombre
            if (_labelRoot != null)
            {
                _labelRoot.position = transform.position + Vector3.up * _labelOffset;
                _labelRoot.rotation = Quaternion.identity;
                string text = BuildLabel();
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
            UpdateAuras();

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

        /// <summary>
        /// Nombre del piloto + estado de sus power-ups, para que TODOS vean quien
        /// tiene DRS o Pinchazo guardado y quien esta usando el DRS.
        /// </summary>
        private string BuildLabel()
        {
            string name = _data.IsOwner ? $"{_data.DisplayName} (tu)" : _data.DisplayName;
            string tags = string.Empty;

            // Fin de carrera: corona al ganador.
            var race = RaceManager.Instance;
            if (race != null && race.IsFinished && race.LastResults != null && race.LastResults.Count > 0)
            {
                var results = race.LastResults;
                bool tie = results.Count > 1 && results[0].Points == results[1].Points;
                if (!tie && results[0].ClientId == _data.OwnerClientId)
                {
                    bool on = Mathf.Repeat(Time.time * 3f, 1f) > 0.3f;
                    return $"<size=120%><color=#{(on ? "FFD54F" : "FFFFFF")}>¡GANADOR!</color></size>\n{name}";
                }
                return name;
            }

            if (_data.IsDrsActive.Value)
            {
                // Parpadea mientras dura el DRS.
                bool on = Mathf.Repeat(Time.time * 6f, 1f) > 0.35f;
                tags += on ? "<color=#4FC3F7>¡DRS ACTIVO!</color>" : "<color=#FFFFFF>¡DRS ACTIVO!</color>";
            }
            else if (_data.HasDrs.Value)
            {
                tags += "<color=#4FC3F7>[DRS]</color>";
            }

            if (_data.HasPinchazo.Value)
            {
                if (tags.Length > 0) tags += " ";
                tags += "<color=#C77DFF>[PINCHAZO]</color>";
            }

            if (_data.HasShield.Value)
            {
                if (tags.Length > 0) tags += " ";
                tags += "<color=#4DD9AC>[ESCUDO]</color>";
            }
            if (_data.IsMagnetActive)
            {
                if (tags.Length > 0) tags += " ";
                tags += "<color=#FF6B6B>IMAN</color>";
            }
            if (_data.IsDoublePointsActive)
            {
                if (tags.Length > 0) tags += " ";
                tags += "<color=#FFD54F>x2</color>";
            }

            if (_data.IsStunned.Value && (RaceManager.Instance == null || !RaceManager.Instance.IsFinished))
            {
                if (tags.Length > 0) tags += " ";
                tags += "<color=#FF6B6B>¡PINCHADO!</color>";
            }

            return tags.Length > 0 ? $"<size=75%>{tags}</size>\n{name}" : name;
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
