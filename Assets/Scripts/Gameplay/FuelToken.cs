using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Bidon de gasolina. El SERVIDOR detecta por distancia que un auto lo toco,
    /// suma 10 puntos, lo oculta y lo hace reaparecer 10 segundos despues en el
    /// mismo lugar. La deteccion por distancia (y no por triggers de fisica) es
    /// fiable aunque los autos remotos sean Kinematic en el servidor.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class FuelToken : NetworkBehaviour
    {
        public NetworkVariable<bool> IsAvailable = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [SerializeField] private float pickupRadius = GameConstants.FUEL_PICKUP_RADIUS;

        private SpriteRenderer[] _renderers;
        private Collider2D[] _colliders;
        private Vector3 _baseScale;
        private float _popTimer = 1f;
        private double _respawnAt;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _colliders = GetComponentsInChildren<Collider2D>(true);
            _baseScale = transform.localScale;

            // Las colisiones fisicas no se usan (solo decorativo); evita que los autos choquen con el bidon.
            foreach (var c in _colliders) c.isTrigger = true;
        }

        public override void OnNetworkSpawn()
        {
            IsAvailable.OnValueChanged += HandleAvailabilityChanged;
            ApplyVisibility(IsAvailable.Value, animate: false);
        }

        public override void OnNetworkDespawn()
        {
            IsAvailable.OnValueChanged -= HandleAvailabilityChanged;
        }

        private void HandleAvailabilityChanged(bool previous, bool current)
        {
            ApplyVisibility(current, animate: current);
            if (previous && !current)
            {
                AudioManager.PlayAt("gasolina", transform.position, 0.85f);
                SpawnFlyToCar();
            }
        }

        private void ApplyVisibility(bool visible, bool animate)
        {
            foreach (var r in _renderers) r.enabled = visible;
            foreach (var c in _colliders) c.enabled = visible;
            _popTimer = animate ? 0f : 1f;
            transform.localScale = animate ? Vector3.zero : _baseScale;
        }

        private void Update()
        {
            // Animacion de reaparicion (todos los clientes).
            if (_popTimer < 1f)
            {
                _popTimer += Time.deltaTime * 3f;
                float t = Mathf.Clamp01(_popTimer);
                float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.3f;
                transform.localScale = _baseScale * (t * overshoot);
            }
            else if (IsAvailable.Value)
            {
                // Pequeño "latido" para que se note en pista.
                float pulse = 1f + Mathf.Sin(Time.time * 4f + transform.position.x) * 0.06f;
                transform.localScale = _baseScale * pulse;
            }

            if (!IsServer || !IsSpawned) return;

            if (!IsAvailable.Value)
            {
                if (NetworkManager.ServerTime.Time >= _respawnAt)
                    IsAvailable.Value = true;
                return;
            }

            if (RaceManager.Instance == null || !RaceManager.Instance.IsRacing) return;

            Vector2 myPos = transform.position;
            foreach (var player in PlayerNetworkData.All)
            {
                if (player == null || !player.IsSpawned) continue;
                // Con Iman el radio de recoleccion es mucho mas grande.
                float radius = player.IsMagnetActive ? GameConstants.IMAN_RADIUS : pickupRadius;
                if (((Vector2)player.transform.position - myPos).sqrMagnitude > radius * radius) continue;

                IsAvailable.Value = false;
                _respawnAt = NetworkManager.ServerTime.Time + GameConstants.FUEL_RESPAWN_SECONDS;
                player.AddFuelServer();
                break;
            }
        }

        /// <summary>
        /// Efecto visual: una copia del bidon vuela hacia el auto mas cercano y se achica
        /// (con Iman se ve como si la gasolina fuera atraida).
        /// </summary>
        private void SpawnFlyToCar()
        {
            if (_renderers == null || _renderers.Length == 0 || _renderers[0].sprite == null) return;
            PlayerNetworkData nearest = null;
            float best = float.MaxValue;
            foreach (var p in PlayerNetworkData.All)
            {
                if (p == null || !p.IsSpawned) continue;
                float d = ((Vector2)p.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest == null) return;

            var go = new GameObject("GasolinaVolando");
            go.transform.position = transform.position;
            go.transform.localScale = _baseScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _renderers[0].sprite;
            sr.sortingLayerID = _renderers[0].sortingLayerID;
            sr.sortingOrder = _renderers[0].sortingOrder + 1;
            go.AddComponent<FlyToTarget>().Init(nearest.transform, 0.28f);
        }

        /// <summary>Vuelve a mostrar el bidon (revancha).</summary>
        public void ResetServer()
        {
            if (!IsServer) return;
            IsAvailable.Value = true;
        }
    }

    /// <summary>Mueve un objeto hacia un objetivo, lo achica y lo destruye al llegar.</summary>
    public class FlyToTarget : MonoBehaviour
    {
        private Transform _target;
        private float _duration;
        private float _t;
        private Vector3 _start;
        private Vector3 _startScale;

        public void Init(Transform target, float duration)
        {
            _target = target;
            _duration = Mathf.Max(0.05f, duration);
            _start = transform.position;
            _startScale = transform.localScale;
        }

        private void Update()
        {
            _t += Time.deltaTime / _duration;
            if (_target == null || _t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            float k = _t * _t; // acelera al acercarse
            transform.position = Vector3.Lerp(_start, _target.position, k);
            transform.localScale = _startScale * (1f - 0.8f * _t);
        }
    }
}
