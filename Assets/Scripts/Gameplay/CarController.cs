using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Movimiento arcade del auto en 2D (vista cenital). Solo el DUEÑO procesa input
    /// y mueve el Rigidbody2D; el NetworkTransform (Owner Authoritative) replica la
    /// posicion a los demas. En las copias remotas el Rigidbody queda Kinematic para
    /// que la fisica local no pelee con la posicion recibida por red.
    ///
    /// El sprite del auto mira hacia ARRIBA (transform.up = adelante).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public class CarController : NetworkBehaviour
    {
        [Header("Velocidad (unidades de mundo)")]
        [SerializeField] private float maxSpeed = GameConstants.CAR_MAX_SPEED;
        [SerializeField] private float reverseSpeed = GameConstants.CAR_MAX_SPEED * 0.4f;
        [SerializeField] private float acceleration = GameConstants.CAR_ACCELERATION;
        [SerializeField] private float brakeForce = GameConstants.CAR_BRAKE;
        [SerializeField] private float coastDeceleration = 120f;

        [Header("Giro")]
        [SerializeField] private float turnSpeed = GameConstants.CAR_TURN_SPEED;
        [Tooltip("Porcentaje de la velocidad maxima a partir del cual se gira con fuerza completa.")]
        [SerializeField] private float fullTurnSpeedRatio = 0.35f;
        [Tooltip("Que tanto se conserva la velocidad lateral (0 = sin derrape, 1 = hielo).")]
        [SerializeField, Range(0f, 1f)] private float lateralGrip = 0.15f;

        private Rigidbody2D _rb;
        private PlayerNetworkData _data;
        private bool _inputLocked;
        private float _speed; // velocidad escalar sobre el eje "adelante"

        public bool IsInputLocked => _inputLocked;
        public float CurrentSpeed => _speed;
        public float MaxSpeed => maxSpeed;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _data = GetComponent<PlayerNetworkData>();
            _rb.gravityScale = 0f;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public override void OnNetworkSpawn()
        {
            // Solo el dueño simula fisica; las copias remotas siguen al NetworkTransform.
            _rb.bodyType = IsOwner ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
            // La interpolacion de fisica solo sirve para el auto propio; en los remotos
            // pelearia con la interpolacion del NetworkTransform y causaria tirones.
            _rb.interpolation = IsOwner ? RigidbodyInterpolation2D.Interpolate : RigidbodyInterpolation2D.None;
            _rb.angularDamping = 5f;
            _speed = 0f;
        }

        /// <summary>
        /// Teletransporta el auto (solo dueño o servidor antes de ceder control).
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            _rb.position = position;
            _rb.rotation = rotation.eulerAngles.z;
            _rb.linearVelocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            _speed = 0f;
        }

        public void SetInputLocked(bool locked)
        {
            _inputLocked = locked;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsOwner) return;

            float dt = Time.fixedDeltaTime;
            Vector2 forward = transform.up;

            // Tomar la velocidad real despues de la fisica (choques contra muros/autos).
            _speed = Vector2.Dot(_rb.linearVelocity, forward);

            bool canDrive = !_inputLocked && RaceManager.Instance != null && RaceManager.Instance.IsRacing;

            float throttle = 0f;
            float steer = 0f;

            if (canDrive)
            {
                ReadInput(forward, out throttle, out steer);
            }

            float speedCap = maxSpeed * (_data != null && _data.IsDrsActive.Value ? GameConstants.DRS_SPEED_MULTIPLIER : 1f);

            // --- Aceleracion / freno ---
            if (_inputLocked)
            {
                // Pinchazo o fin de carrera: frenado fuerte.
                _speed = Mathf.MoveTowards(_speed, 0f, brakeForce * 2f * dt);
            }
            else if (throttle > 0.01f)
            {
                float accel = _speed < 0f ? brakeForce : acceleration;
                _speed = Mathf.MoveTowards(_speed, speedCap * throttle, accel * dt);
            }
            else if (throttle < -0.01f)
            {
                float accel = _speed > 0f ? brakeForce : acceleration * 0.6f;
                _speed = Mathf.MoveTowards(_speed, -reverseSpeed * -throttle, accel * dt);
            }
            else
            {
                _speed = Mathf.MoveTowards(_speed, 0f, coastDeceleration * dt);
            }

            // Si termina el DRS, bajar suavemente al limite normal.
            if (_speed > speedCap) _speed = Mathf.MoveTowards(_speed, speedCap, brakeForce * dt);

            // --- Giro (depende de la velocidad, como un auto real) ---
            if (Mathf.Abs(steer) > 0.01f)
            {
                float speedFactor = Mathf.Clamp01(Mathf.Abs(_speed) / (maxSpeed * fullTurnSpeedRatio));
                float direction = _speed >= 0f ? 1f : -1f;
                float deltaAngle = -steer * turnSpeed * speedFactor * direction * dt;
                _rb.MoveRotation(_rb.rotation + deltaAngle);
            }
            _rb.angularVelocity = 0f;

            // --- Velocidad final: adelante + un poco de derrape lateral ---
            Vector2 newForward = Quaternion.Euler(0f, 0f, _rb.rotation) * Vector2.up;
            Vector2 right = new Vector2(newForward.y, -newForward.x);
            float lateral = Vector2.Dot(_rb.linearVelocity, right) * lateralGrip;
            _rb.linearVelocity = newForward * _speed + right * lateral;
        }

        private float _lastCrashSound = -10f;

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!IsSpawned || !IsOwner) return;
            float impact = collision.relativeVelocity.magnitude;
            if (impact < 55f || Time.time - _lastCrashSound < 0.25f) return;
            _lastCrashSound = Time.time;
            AudioManager.Play("choque", Mathf.Clamp01(impact / 220f) * 0.9f + 0.1f);
        }

        private void ReadInput(Vector2 forward, out float throttle, out float steer)
        {
            var scheme = LocalPlayerSettings.Scheme;

            if (scheme == ControlScheme.Joystick && !CarInput.KeyboardActive)
            {
                // El auto gira hacia donde apunta el stick y acelera segun la inclinacion.
                // El stick viene en espacio de PANTALLA (arriba = arriba en la UI), pero el
                // angulo del auto se mide en espacio de MUNDO: hay que rotarlo segun la
                // camara (que gira con el auto) para que "arriba del stick" siempre sea
                // "adelante del auto".
                Vector2 dir = CarInput.JoystickDirection;
                dir = ScreenToWorldDirection(dir);

                float magnitude = Mathf.Clamp01(dir.magnitude);
                if (magnitude < 0.15f)
                {
                    throttle = 0f;
                    steer = 0f;
                    return;
                }

                float signedAngle = Vector2.SignedAngle(forward, dir); // + = izquierda
                steer = Mathf.Clamp(-signedAngle / 45f, -1f, 1f);
                // Si el stick apunta muy hacia atras, bajar la velocidad para girar mas cerrado.
                throttle = Mathf.Abs(signedAngle) > 100f ? magnitude * 0.5f : magnitude;
                return;
            }

            throttle = CarInput.Throttle;
            steer = CarInput.Steer;
        }

        /// <summary>Convierte una direccion de pantalla (UI) a direccion de mundo
        /// usando la rotacion Z de la camara principal.</summary>
        private static Vector2 ScreenToWorldDirection(Vector2 screenDirection)
        {
            Camera cam = Camera.main;
            if (cam == null) return screenDirection;
            return (Vector2)(Quaternion.Euler(0f, 0f, cam.transform.eulerAngles.z) * (Vector3)screenDirection);
        }
    }
}
