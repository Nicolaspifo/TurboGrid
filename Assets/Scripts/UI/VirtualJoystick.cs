using UnityEngine;
using UnityEngine.EventSystems;

namespace TurboGrid
{
    /// <summary>
    /// Joystick tactil clasico: un "fondo" fijo y un "stick" que se arrastra dentro
    /// de un radio maximo. Es input local (sin red): escribe la direccion en CarInput
    /// y el CarController del jugador local la lee.
    ///
    /// Setup: Canvas > "JoystickBackground" (Image, raycastTarget) > hijo "JoystickHandle".
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;
        [SerializeField] private float handleRange = 100f; // radio maximo en unidades del canvas

        public Vector2 Direction { get; private set; }

        public static VirtualJoystick Instance { get; private set; }

        private int _pointerId = int.MinValue;

        private void Awake()
        {
            Instance = this;
            if (background == null) background = transform as RectTransform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            Release();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pointerId = eventData.pointerId;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

            Vector2 clamped = Vector2.ClampMagnitude(localPoint, handleRange);
            if (handle != null) handle.anchoredPosition = clamped;

            Direction = clamped / handleRange; // magnitud 0..1
            CarInput.JoystickDirection = Direction;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            Release();
        }

        private void Release()
        {
            _pointerId = int.MinValue;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            Direction = Vector2.zero;
            CarInput.JoystickDirection = Vector2.zero;
        }
    }
}
