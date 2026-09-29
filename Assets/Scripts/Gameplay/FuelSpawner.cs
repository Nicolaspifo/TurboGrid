using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Instancia las 50 fichas de combustible distribuidas por el circuito.
    /// Solo corre en el servidor/host. Cada ficha cae en uno de los puntos definidos
    /// en la escena (hijos de "TrackWaypoints"), para asegurar que queden dentro de
    /// la pista. Se eligen 50 al azar entre todos los puntos (se recomiendan 60-80).
    /// Los puntos que sobran los usa PowerUpSpawner.
    /// </summary>
    public class FuelSpawner : NetworkBehaviour
    {
        public static FuelSpawner Instance { get; private set; }

        [SerializeField] private Transform waypointsRoot;
        [SerializeField] private GameObject fuelTokenPrefab;

        private readonly List<FuelToken> _tokens = new List<FuelToken>();
        private readonly List<Vector2> _unusedPoints = new List<Vector2>();
        private readonly List<Vector2> _allPoints = new List<Vector2>();

        public IReadOnlyList<Vector2> UnusedPoints => _unusedPoints;
        public IReadOnlyList<Vector2> AllPoints => _allPoints;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            CollectPoints();
            if (!IsServer) return;
            SpawnTokens();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void CollectPoints()
        {
            _allPoints.Clear();
            if (waypointsRoot == null)
            {
                Debug.LogError("[FuelSpawner] Falta asignar waypointsRoot (TrackWaypoints).");
                return;
            }

            foreach (Transform child in waypointsRoot)
            {
                Vector2 p = child.position;
                // Ignorar puntos duplicados (hay algunos repetidos en la escena).
                bool duplicate = false;
                foreach (var existing in _allPoints)
                {
                    if ((existing - p).sqrMagnitude < 4f) { duplicate = true; break; }
                }
                if (!duplicate) _allPoints.Add(p);
            }
        }

        private void SpawnTokens()
        {
            if (fuelTokenPrefab == null)
            {
                Debug.LogError("[FuelSpawner] Falta asignar fuelTokenPrefab.");
                return;
            }

            var points = new List<Vector2>(_allPoints);
            Shuffle(points);

            int count = Mathf.Min(GameConstants.TOTAL_FUEL_TOKENS, points.Count);
            for (int i = 0; i < count; i++)
            {
                GameObject instance = Instantiate(fuelTokenPrefab, points[i], Quaternion.identity);
                var netObj = instance.GetComponent<NetworkObject>();
                netObj.Spawn(true);
                var token = instance.GetComponent<FuelToken>();
                if (token != null) _tokens.Add(token);
            }

            _unusedPoints.Clear();
            for (int i = count; i < points.Count; i++)
                _unusedPoints.Add(points[i]);

            if (count < GameConstants.TOTAL_FUEL_TOKENS)
                Debug.LogWarning($"[FuelSpawner] Solo hay {points.Count} puntos distintos: se generaron {count} de {GameConstants.TOTAL_FUEL_TOKENS} fichas. Agrega mas hijos a TrackWaypoints.");
            else
                Debug.Log($"[FuelSpawner] {count} fichas de combustible generadas.");
        }

        /// <summary>Revancha: todas las fichas vuelven a estar disponibles.</summary>
        public void ResetAllServer()
        {
            if (!IsServer) return;
            foreach (var token in _tokens)
                if (token != null) token.ResetServer();
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
