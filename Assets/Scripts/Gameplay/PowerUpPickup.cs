using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    public enum PowerUpType : byte
    {
        Drs = 0,
        Pinchazo = 1
    }

    /// <summary>
    /// Power-up que se recoge en pista (DRS o Pinchazo). Al tocarlo, el auto lo guarda
    /// y el jugador lo activa con su boton. Despues de recogido, reaparece en otro
    /// punto aleatorio de la pista (ver PowerUpSpawner).
    ///
    /// Un solo prefab sirve para ambos tipos: el sprite cambia segun Type.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PowerUpPickup : NetworkBehaviour
    {
        public NetworkVariable<PowerUpType> Type = new NetworkVariable<PowerUpType>(
            PowerUpType.Drs, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsAvailable = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<Vector2> Position = new NetworkVariable<Vector2>(
            Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Sprite drsSprite;
        [SerializeField] private Sprite pinchazoSprite;
        [SerializeField] private float pickupRadius = GameConstants.POWERUP_PICKUP_RADIUS;

        private Vector3 _baseScale;
        private float _popTimer = 1f;
        private double _respawnAt;
        private PowerUpSpawner _spawner;

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            _baseScale = transform.localScale;
        }

        public override void OnNetworkSpawn()
        {
            Type.OnValueChanged += HandleTypeChanged;
            IsAvailable.OnValueChanged += HandleAvailabilityChanged;
            Position.OnValueChanged += HandlePositionChanged;

            ApplySprite();
            transform.position = Position.Value;
            ApplyVisibility(IsAvailable.Value, animate: false);
        }

        public override void OnNetworkDespawn()
        {
            Type.OnValueChanged -= HandleTypeChanged;
            IsAvailable.OnValueChanged -= HandleAvailabilityChanged;
            Position.OnValueChanged -= HandlePositionChanged;
        }

        public void InitializeServer(PowerUpSpawner spawner, PowerUpType type, Vector2 position)
        {
            _spawner = spawner;
            Type.Value = type;
            Position.Value = position;
            transform.position = position;
            IsAvailable.Value = true;
        }

        private void HandleTypeChanged(PowerUpType previous, PowerUpType current) => ApplySprite();

        private void HandlePositionChanged(Vector2 previous, Vector2 current) => transform.position = current;

        private void HandleAvailabilityChanged(bool previous, bool current) => ApplyVisibility(current, animate: current);

        private void ApplySprite()
        {
            if (spriteRenderer == null) return;
            var sprite = Type.Value == PowerUpType.Drs ? drsSprite : pinchazoSprite;
            if (sprite != null) spriteRenderer.sprite = sprite;
        }

        private void ApplyVisibility(bool visible, bool animate)
        {
            if (spriteRenderer != null) spriteRenderer.enabled = visible;
            _popTimer = animate ? 0f : 1f;
            transform.localScale = animate ? Vector3.zero : _baseScale;
        }

        private void Update()
        {
            // Animacion: aparece con rebote y luego flota/gira suavemente.
            if (_popTimer < 1f)
            {
                _popTimer += Time.deltaTime * 2.5f;
                float t = Mathf.Clamp01(_popTimer);
                transform.localScale = _baseScale * (t * (1f + Mathf.Sin(t * Mathf.PI) * 0.35f));
            }
            else if (IsAvailable.Value)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.08f;
                transform.localScale = _baseScale * pulse;
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2f) * 12f);
            }

            if (!IsServer || !IsSpawned) return;

            if (!IsAvailable.Value)
            {
                if (NetworkManager.ServerTime.Time >= _respawnAt)
                {
                    Vector2 next = _spawner != null ? _spawner.PickSpawnPosition(Position.Value) : Position.Value;
                    Position.Value = next;
                    transform.position = next;
                    IsAvailable.Value = true;
                }
                return;
            }

            if (RaceManager.Instance == null || !RaceManager.Instance.IsRacing) return;

            float sqrRadius = pickupRadius * pickupRadius;
            Vector2 myPos = transform.position;
            foreach (var player in PlayerNetworkData.All)
            {
                if (player == null || !player.IsSpawned) continue;
                if (((Vector2)player.transform.position - myPos).sqrMagnitude > sqrRadius) continue;
                if (!player.GivePowerUpServer(Type.Value)) continue; // ya tenia uno: lo deja para otro

                IsAvailable.Value = false;
                _respawnAt = NetworkManager.ServerTime.Time + GameConstants.POWERUP_RESPAWN_SECONDS;
                break;
            }
        }

        /// <summary>Revancha: reaparece de inmediato en un punto nuevo.</summary>
        public void ResetServer()
        {
            if (!IsServer) return;
            Vector2 next = _spawner != null ? _spawner.PickSpawnPosition(Position.Value) : Position.Value;
            Position.Value = next;
            transform.position = next;
            IsAvailable.Value = true;
        }
    }
}
