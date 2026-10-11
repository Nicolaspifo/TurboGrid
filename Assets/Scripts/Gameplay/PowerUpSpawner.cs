using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Mantiene 2 power-ups en pista: 1 DRS y 1 Pinchazo. Cuando alguien recoge uno,
    /// reaparece despues de unos segundos en otro punto aleatorio del circuito.
    /// Usa los puntos de TrackWaypoints que no ocupo la gasolina.
    /// Solo corre en el servidor. Va en el mismo GameObject que RaceManager
    /// (o cualquier objeto de la escena de carrera).
    /// </summary>
    public class PowerUpSpawner : MonoBehaviour
    {
        public static PowerUpSpawner Instance { get; private set; }

        [SerializeField] private GameObject powerUpPrefab;
        [Tooltip("Distancia minima entre un power-up nuevo y el anterior (para que 'se mueva').")]
        [SerializeField] private float minDistanceFromPrevious = 250f;

        private readonly List<PowerUpPickup> _pickups = new List<PowerUpPickup>();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Lo llama RaceManager (servidor) cuando la escena esta lista.</summary>
        public void SpawnInitialServer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (_pickups.Count > 0) return;

            if (powerUpPrefab == null)
            {
                Debug.LogError("[PowerUpSpawner] Falta asignar powerUpPrefab. Ejecuta TurboGrid > Configurar escenas.");
                return;
            }

            SpawnOne(PowerUpType.Drs, Vector2.positiveInfinity);
            SpawnOne(PowerUpType.Pinchazo, _pickups.Count > 0 ? (Vector2)_pickups[0].transform.position : Vector2.positiveInfinity);
        }

        private void SpawnOne(PowerUpType type, Vector2 avoid)
        {
            Vector2 pos = PickSpawnPosition(avoid);
            GameObject instance = Instantiate(powerUpPrefab, pos, Quaternion.identity);
            var pickup = instance.GetComponent<PowerUpPickup>();
            instance.GetComponent<NetworkObject>().Spawn(true);
            pickup.InitializeServer(this, type, pos);
            _pickups.Add(pickup);
        }

        public void ResetAllServer()
        {
            foreach (var p in _pickups)
                if (p != null) p.ResetServer();
        }

        /// <summary>
        /// Elige un punto libre de la pista, lejos del punto anterior y de los otros power-ups.
        /// </summary>
        public Vector2 PickSpawnPosition(Vector2 previous)
        {
            List<Vector2> candidates = new List<Vector2>();
            var fuel = FuelSpawner.Instance;
            if (fuel != null)
            {
                candidates.AddRange(fuel.UnusedPoints);
                if (candidates.Count < 4) candidates.AddRange(fuel.AllPoints);
            }

            if (candidates.Count == 0)
                return float.IsInfinity(previous.x) ? Vector2.zero : previous;

            // Filtrar puntos muy cerca del anterior o de otro power-up.
            var good = new List<Vector2>();
            float minSqr = minDistanceFromPrevious * minDistanceFromPrevious;
            foreach (var c in candidates)
            {
                if (!float.IsInfinity(previous.x) && (c - previous).sqrMagnitude < minSqr) continue;
                bool nearOther = false;
                foreach (var p in _pickups)
                {
                    if (p == null) continue;
                    if (((Vector2)p.transform.position - c).sqrMagnitude < minSqr * 0.25f) { nearOther = true; break; }
                }
                if (!nearOther) good.Add(c);
            }

            var pool = good.Count > 0 ? good : candidates;
            return pool[Random.Range(0, pool.Count)];
        }
    }
}
