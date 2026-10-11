using UnityEngine;
using UnityEngine.EventSystems;

namespace TurboGrid
{
    /// <summary>
    /// Timon virtual: se arrastra girando alrededor de su centro. El angulo girado
    /// (hasta maxAngle) se convierte en direccion -1..1. Al soltar vuelve al centro.
    /// Se usa junto a los botones de acelerar/frenar (HoldButton).
    /// </summary>
    public class SteeringWheel : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform wheel;       // imagen que rota
        [SerializeField] private float maxAngle = 120f;     // grados para girar "a fondo"
        [SerializeField] private float returnSpeed = 540f;  // grados/seg al soltar

        private bool _dragging;
        private int _pointerId;
        private float _angle;          // + = girado a la izquierda (antihorario)
        private float _lastPointerAngle;

        public float Value => Mathf.Clamp(-_angle / maxAngle, -1f, 1f);

        public void Configure(RectTransform wheelImage)
        {
            wheel = wheelImage;
        }

        private void Awake()
        {
            if (wheel == null) wheel = transform as RectTransform;
        }

        private void OnDisable()
        {
            _dragging = false;
            _angle = 0f;
            ApplyRotation();
            CarInput.WheelSteer = 0f;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _dragging = true;
            _pointerId = eventData.pointerId;
            _lastPointerAngle = PointerAngle(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || eventData.pointerId != _pointerId) return;
            float current = PointerAngle(eventData);
            float delta = Mathf.DeltaAngle(_lastPointerAngle, current);
            _lastPointerAngle = current;
            _angle = Mathf.Clamp(_angle + delta, -maxAngle, maxAngle);
            ApplyRotation();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _dragging = false;
        }

        private void Update()
        {
            if (!_dragging && Mathf.Abs(_angle) > 0.01f)
            {
                _angle = Mathf.MoveTowards(_angle, 0f, returnSpeed * Time.deltaTime);
                ApplyRotation();
            }
            CarInput.WheelSteer = Value;
        }

        private float PointerAngle(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 local);
            return Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        }

        private void ApplyRotation()
        {
            if (wheel != null) wheel.localRotation = Quaternion.Euler(0f, 0f, _angle);
        }
    }
}
