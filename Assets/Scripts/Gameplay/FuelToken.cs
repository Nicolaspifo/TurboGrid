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
            if (previous && !current) AudioManager.PlayAt("gasolina", transform.position, 0.85f);
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

            float sqrRadius = pickupRadius * pickupRadius;
            Vector2 myPos = transform.position;
            foreach (var player in PlayerNetworkData.All)
            {
                if (player == null || !player.IsSpawned) continue;
                if (((Vector2)player.transform.position - myPos).sqrMagnitude > sqrRadius) continue;

                IsAvailable.Value = false;
                _respawnAt = NetworkManager.ServerTime.Time + GameConstants.FUEL_RESPAWN_SECONDS;
                player.AddFuelServer();
                break;
            }
        }

        /// <summary>Vuelve a mostrar el bidon (revancha).</summary>
        public void ResetServer()
        {
            if (!IsServer) return;
            IsAvailable.Value = true;
        }
    }
}
