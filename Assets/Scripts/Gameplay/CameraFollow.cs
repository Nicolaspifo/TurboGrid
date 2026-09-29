using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Camara que sigue al auto del jugador LOCAL. Se adelanta un poco en la direccion
    /// de movimiento y se aleja levemente con la velocidad para ver lo que viene.
    /// Va en la Main Camera de cada circuito. Antes de que spawnee el auto, muestra
    /// el circuito completo (vista de parrilla).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private float followSmoothTime = 0.15f;
        [SerializeField] private float baseOrthoSize = 230f;
        [SerializeField] private float maxExtraZoom = 50f;
        [SerializeField] private float lookAhead = 0.35f; // segundos de adelanto segun la velocidad

        // Al terminar la carrera la camara puede enfocar al ganador (todos los clientes).
        private static ulong? _focusClientId;
        [SerializeField] private float celebrationOrthoSize = 150f;

        /// <summary>Enfoca el auto de ese jugador (null = volver al auto propio).</summary>
        public static void FocusOn(ulong? clientId)
        {
            _focusClientId = clientId;
        }

        private Camera _camera;
        private Vector3 _velocity;
        private float _zoomVelocity;
        private float _overviewSize;
        private Vector3 _overviewPosition;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _focusClientId = null; // escena nueva: sin enfoque pendiente
            _overviewSize = _camera.orthographicSize;
            _overviewPosition = transform.position;

            // Los marcadores del mini-mapa no se ven en la camara principal.
            int minimapLayer = LayerMask.NameToLayer(CarVisuals.MINIMAP_LAYER);
            if (minimapLayer >= 0) _camera.cullingMask &= ~(1 << minimapLayer);
        }

        private void LateUpdate()
        {
            // Celebracion: zoom al ganador.
            if (_focusClientId.HasValue)
            {
                PlayerNetworkData winner = null;
                foreach (var p in PlayerNetworkData.All)
                    if (p != null && p.IsSpawned && p.OwnerClientId == _focusClientId.Value) { winner = p; break; }
                if (winner != null)
                {
                    Vector3 focus = winner.transform.position;
                    focus.z = transform.position.z;
                    transform.position = Vector3.SmoothDamp(transform.position, focus, ref _velocity, 0.35f);
                    _camera.orthographicSize = Mathf.SmoothDamp(_camera.orthographicSize, celebrationOrthoSize, ref _zoomVelocity, 0.45f);
                    return;
                }
            }

            var local = PlayerNetworkData.Local;
            if (local == null)
            {
                // Sin auto local: vista general del circuito.
                transform.position = Vector3.SmoothDamp(transform.position, _overviewPosition, ref _velocity, 0.4f);
                _camera.orthographicSize = Mathf.SmoothDamp(_camera.orthographicSize, _overviewSize, ref _zoomVelocity, 0.4f);
                return;
            }

            var car = local.Car;
            float speed = car != null ? Mathf.Abs(car.CurrentSpeed) : 0f;
            float speed01 = car != null && car.MaxSpeed > 0f ? Mathf.Clamp01(speed / car.MaxSpeed) : 0f;

            Vector3 target = local.transform.position + local.transform.up * (car != null ? car.CurrentSpeed * lookAhead : 0f);
            target.z = transform.position.z;

            transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, followSmoothTime);

            float targetSize = baseOrthoSize + maxExtraZoom * speed01;
            _camera.orthographicSize = Mathf.SmoothDamp(_camera.orthographicSize, targetSize, ref _zoomVelocity, 0.5f);
        }
    }
}
