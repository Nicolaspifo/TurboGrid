using System.Collections.Generic;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Parrilla de salida: 4 casillas fijas (hijos asignados en el inspector).
    /// En cada carrera el servidor las baraja y le da una a cada jugador.
    /// La ROTACION de cada casilla define hacia donde mira el auto al salir
    /// (el sprite del auto mira hacia arriba = eje Y local).
    /// </summary>
    public class SpawnGridManager : MonoBehaviour
    {
        public static SpawnGridManager Instance { get; private set; }

        [SerializeField] private Transform[] startGridSlots; // las 4 casillas

        private readonly List<int> _availableSlots = new List<int>();

        public int SlotCount => startGridSlots != null ? startGridSlots.Length : 0;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Baraja las casillas. Llamar al inicio de cada carrera (solo servidor).</summary>
        public void ShuffleSlots()
        {
            _availableSlots.Clear();
            for (int i = 0; i < SlotCount; i++)
                _availableSlots.Add(i);

            for (int i = _availableSlots.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_availableSlots[i], _availableSlots[j]) = (_availableSlots[j], _availableSlots[i]);
            }
        }

        /// <summary>
        /// Devuelve una casilla aun no usada (posicion + rotacion). Solo servidor.
        /// </summary>
        public bool TryClaimSlot(out Vector3 position, out Quaternion rotation)
        {
            if (_availableSlots.Count == 0)
            {
                Debug.LogWarning("[SpawnGridManager] No quedan casillas libres.");
                position = transform.position;
                rotation = Quaternion.identity;
                return false;
            }

            int slotIndex = _availableSlots[0];
            _availableSlots.RemoveAt(0);
            Transform slot = startGridSlots[slotIndex];
            position = new Vector3(slot.position.x, slot.position.y, 0f);
            rotation = Quaternion.Euler(0f, 0f, slot.eulerAngles.z);
            return true;
        }
    }
}
