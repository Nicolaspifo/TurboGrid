using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Asigna a cada jugador una casilla aleatoria de la parrilla de salida
    /// (4 posiciones fijas en la escena) cuando su NetworkObject spawnea.
    /// Corre solo en el servidor. Engancha esto al evento de spawn del
    /// NetworkManager (Player Prefab) o llamalo desde el prefab del auto
    /// en su propio OnNetworkSpawn si prefieres descentralizarlo.
    /// </summary>
    public class SpawnGridManager : MonoBehaviour
    {
        public static SpawnGridManager Instance { get; private set; }

        [SerializeField] private Transform[] startGridSlots; // asigna las 4 casillas en el inspector

        private readonly List<int> _availableSlots = new List<int>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (!NetworkManager.Singleton.IsServer) return;

            for (int i = 0; i < startGridSlots.Length; i++)
                _availableSlots.Add(i);

            Shuffle(_availableSlots);
        }

        /// <summary>
        /// Devuelve una posicion de salida aun no usada. Llamar SOLO desde el servidor,
        /// una vez por jugador, apenas se conecta.
        /// </summary>
        public Vector3 ClaimStartPosition()
        {
            if (_availableSlots.Count == 0)
            {
                Debug.LogWarning("[SpawnGridManager] No quedan casillas libres, usando (0,0,0).");
                return Vector3.zero;
            }

            int slotIndex = _availableSlots[0];
            _availableSlots.RemoveAt(0);
            return startGridSlots[slotIndex].position;
        }

        private static void Shuffle(IList<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
