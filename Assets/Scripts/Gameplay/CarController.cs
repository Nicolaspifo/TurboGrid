using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Movimiento del auto en 2D top-down. Solo el DUEÑO (owner) del NetworkObject
    /// procesa input y mueve el Rigidbody2D; el NetworkTransform se encarga de
    /// replicar la posicion/rotacion a los demas clientes automaticamente
    /// (dejalo en modo "Owner Authoritative" en el prefab).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public class CarController : NetworkBehaviour
    {
        [SerializeField] private float baseSpeed = GameConstants.CAR_BASE_SPEED;
        [SerializeField] private float rotationSpeed = GameConstants.CAR_ROTATION_SPEED;

        private Rigidbody2D _rb;
        private Vector2 _inputDirection;
        private bool _inputLocked;
        private float _speedMultiplier = 1f;

        public bool IsInputLocked => _inputLocked;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        public override void OnNetworkSpawn()
        {
            // Deshabilita el script de input en las copias remotas: solo el dueño mueve su auto.
            enabled = IsOwner;

            if (IsServer && SpawnGridManager.Instance != null)
            {
                transform.position = SpawnGridManager.Instance.ClaimStartPosition();
            }
        }

        /// <summary>
        /// Llamado por VirtualJoystick (solo existe en el cliente dueño del auto).
        /// </summary>
        public void SetMoveInput(Vector2 direction)
        {
            _inputDirection = direction;
        }

        public void SetInputLocked(bool locked)
        {
            _inputLocked = locked;
            if (locked) _inputDirection = Vector2.zero;
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            _speedMultiplier = multiplier;
        }

        private void FixedUpdate()
        {
            if (!IsOwner) return;
            if (_inputLocked)
            {
                _rb.linearVelocity = Vector2.zero;
                return;
            }

            Vector2 moveDir = _inputDirection;
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

            // Movimiento tipo "twin-ish": avanza en la direccion del joystick,
            // y rota el sprite del auto hacia esa direccion para dar sensacion de carrera.
            _rb.linearVelocity = moveDir * (baseSpeed * _speedMultiplier);

            if (moveDir.sqrMagnitude > 0.01f)
            {
                float targetAngle = Mathf.Atan2(moveDir.y, moveDir.x) * Mathf.Rad2Deg - 90f;
                float newAngle = Mathf.MoveTowardsAngle(_rb.rotation, targetAngle, rotationSpeed * Time.fixedDeltaTime);
                _rb.MoveRotation(newAngle);
            }
        }
    }
}
