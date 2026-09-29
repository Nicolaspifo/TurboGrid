using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Boton tactil que se MANTIENE presionado (flechas, acelerador, freno).
    /// Soporta multitouch: se puede acelerar y girar a la vez con dos dedos.
    /// Da feedback visual: se achica y oscurece mientras se presiona.
    /// </summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        public enum Action
        {
            Izquierda,
            Derecha,
            Acelerar,
            Frenar
        }

        [SerializeField] private Action action;
        [SerializeField] private Graphic targetGraphic;
        [SerializeField] private Color pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);

        private int _pointerId = int.MinValue;
        private bool _held;
        private Color _normalColor = Color.white;
        private Vector3 _normalScale;

        public bool IsHeld => _held;

        public void Configure(Action newAction, Graphic graphic)
        {
            action = newAction;
            targetGraphic = graphic;
        }

        private void Awake()
        {
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
            if (targetGraphic != null) _normalColor = targetGraphic.color;
            _normalScale = transform.localScale;
        }

        private void OnDisable()
        {
            SetHeld(false);
            _pointerId = int.MinValue;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pointerId = eventData.pointerId;
            SetHeld(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointerId) return;
            _pointerId = int.MinValue;
            SetHeld(false);
        }

        // Si el dedo se desliza fuera del boton, se suelta; si vuelve a entrar presionado, se reactiva.
        public void OnPointerExit(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId) SetHeld(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId && eventData.eligibleForClick) SetHeld(true);
        }

        private void SetHeld(bool held)
        {
            _held = held;
            switch (action)
            {
                case Action.Izquierda: CarInput.LeftHeld = held; break;
                case Action.Derecha: CarInput.RightHeld = held; break;
                case Action.Acelerar: CarInput.AccelHeld = held; break;
                case Action.Frenar: CarInput.BrakeHeld = held; break;
            }

            if (targetGraphic != null) targetGraphic.color = held ? pressedColor * _normalColor : _normalColor;
            transform.localScale = held ? _normalScale * 0.92f : _normalScale;
        }
    }
}
