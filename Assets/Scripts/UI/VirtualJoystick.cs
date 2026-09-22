using UnityEngine;
using UnityEngine.EventSystems;

namespace TurboGrid
{
    /// <summary>
    /// Joystick tactil clasico: un "fondo" fijo y un "stick" que se arrastra
    /// dentro de un radio maximo. Es puramente input local, no tiene nada de
    /// red -- solo entrega un Vector2 normalizado al CarController del jugador
    /// local via localCarController.SetMoveInput().
    ///
    /// Setup UI: Canvas (Screen Space - Overlay) > panel "JoystickBackground"
    /// (Image, ancla abajo-izquierda) > hijo "JoystickHandle" (Image).
    /// Agrega este script al JoystickBackground con Image.raycastTarget = true.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform background;
        [SerializeField] private RectTransform handle;
        [SerializeField] private float handleRange = 100f; // radio maximo en pixeles

        public Vector2 Direction { get; private set; }


        public static VirtualJoystick Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }
        // Se asigna en runtime cuando el auto local del jugador spawnea.
        private CarController _localCarController;

        public void BindLocalCar(CarController car)
        {
            _localCarController = car;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

            Vector2 clamped = Vector2.ClampMagnitude(localPoint, handleRange);
            handle.anchoredPosition = clamped;

            Direction = clamped / handleRange; // normalizado entre -1 y 1
            _localCarController?.SetMoveInput(Direction);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            handle.anchoredPosition = Vector2.zero;
            Direction = Vector2.zero;
            _localCarController?.SetMoveInput(Vector2.zero);
        }
    }
}
