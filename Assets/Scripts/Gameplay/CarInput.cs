using UnityEngine;
using UnityEngine.InputSystem;

namespace TurboGrid
{
    /// <summary>
    /// Punto unico donde se junta el input del jugador local, venga de los
    /// controles tactiles (flechas, timon, joystick) o del teclado (para probar
    /// en el editor / Multiplayer Play Mode sin celular).
    ///
    /// Los componentes de UI escriben aqui; CarController solo lee.
    /// </summary>
    public static class CarInput
    {
        // Escritos por la UI tactil
        public static bool LeftHeld;
        public static bool RightHeld;
        public static bool AccelHeld;
        public static bool BrakeHeld;
        public static float WheelSteer;          // -1 (izq) .. 1 (der)
        public static Vector2 JoystickDirection; // magnitud 0..1

        /// <summary>Acelerador: -1 (reversa/freno) .. 1.</summary>
        public static float Throttle
        {
            get
            {
                float t = 0f;
                if (AccelHeld || KeyHeld(Key.UpArrow) || KeyHeld(Key.W)) t += 1f;
                if (BrakeHeld || KeyHeld(Key.DownArrow) || KeyHeld(Key.S)) t -= 1f;
                return t;
            }
        }

        /// <summary>Direccion: -1 (izquierda) .. 1 (derecha).</summary>
        public static float Steer
        {
            get
            {
                float s = 0f;
                if (LeftHeld || KeyHeld(Key.LeftArrow) || KeyHeld(Key.A)) s -= 1f;
                if (RightHeld || KeyHeld(Key.RightArrow) || KeyHeld(Key.D)) s += 1f;
                if (Mathf.Abs(WheelSteer) > Mathf.Abs(s)) s = WheelSteer;
                return Mathf.Clamp(s, -1f, 1f);
            }
        }

        /// <summary>True si el teclado se esta usando (tiene prioridad sobre el joystick).</summary>
        public static bool KeyboardActive =>
            KeyHeld(Key.UpArrow) || KeyHeld(Key.DownArrow) || KeyHeld(Key.LeftArrow) || KeyHeld(Key.RightArrow) ||
            KeyHeld(Key.W) || KeyHeld(Key.A) || KeyHeld(Key.S) || KeyHeld(Key.D);

        public static bool DrsKeyPressed => KeyPressed(Key.E) || KeyPressed(Key.Space);
        public static bool PinchazoKeyPressed => KeyPressed(Key.Q);

        public static void ResetAll()
        {
            LeftHeld = RightHeld = AccelHeld = BrakeHeld = false;
            WheelSteer = 0f;
            JoystickDirection = Vector2.zero;
        }

        private static bool KeyHeld(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        private static bool KeyPressed(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }
    }
}
