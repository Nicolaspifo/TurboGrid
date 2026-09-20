using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Instancia las 50 fichas de combustible distribuidas por el circuito.
    /// Solo corre en el servidor/host. Asigna cada ficha a uno de los puntos
    /// definidos manualmente en la escena (hijos de "TrackWaypoints"), para
    /// asegurar que caigan dentro del trazado del ovalo y no fuera de pista.
    ///
    /// Setup en el editor:
    ///  1. Crea un GameObject vacio "TrackWaypoints" en la escena de carrera.
    ///  2. Cuelga de el tantos hijos vacios como puntos posibles de spawn
    ///     quieras a lo largo del ovalo (recomendado: 60-80 puntos para
    ///     tener variedad, se eligen 50 al azar en cada partida).
    ///  3. Arrastra ese GameObject y el prefab FuelToken en el inspector.
    /// </summary>
    public class FuelSpawner : NetworkBehaviour
    {
        [SerializeField] private Transform waypointsRoot;
        [SerializeField] private GameObject fuelTokenPrefab;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            SpawnTokens();
        }

        private void SpawnTokens()
        {
            var points = new List<Transform>();
            foreach (Transform child in waypointsRoot)
                points.Add(child);

            Shuffle(points);

            int count = Mathf.Min(GameConstants.TOTAL_FUEL_TOKENS, points.Count);
            for (int i = 0; i < count; i++)
            {
                GameObject instance = Instantiate(fuelTokenPrefab, points[i].position, Quaternion.identity);
                instance.GetComponent<NetworkObject>().Spawn(true);
            }

            Debug.Log($"[FuelSpawner] {count} fichas de combustible generadas.");
        }

        private static void Shuffle(IList<Transform> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
