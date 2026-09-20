using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Pantalla de resultados finales. Se suscribe a RaceManager.OnRaceFinishedClientSide,
    /// que entrega la lista de jugadores ya ordenada por fichas recolectadas (de mayor a menor).
    /// </summary>
    public class EndRaceUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform resultsListContainer;
        [SerializeField] private GameObject resultRowPrefab; // fila con 2 TMP_Text: nombre y fichas

        private void OnEnable()
        {
            panelRoot.SetActive(false);
            if (RaceManager.Instance != null)
                RaceManager.Instance.OnRaceFinishedClientSide += HandleRaceFinished;
        }

        private void OnDisable()
        {
            if (RaceManager.Instance != null)
                RaceManager.Instance.OnRaceFinishedClientSide -= HandleRaceFinished;
        }

        private void HandleRaceFinished(List<(ulong clientId, FixedString32Bytes name, int fuel)> results)
        {
            panelRoot.SetActive(true);

            foreach (Transform child in resultsListContainer)
                Destroy(child.gameObject);

            for (int i = 0; i < results.Count; i++)
            {
                var row = Instantiate(resultRowPrefab, resultsListContainer);
                var texts = row.GetComponentsInChildren<TMP_Text>();
                // texts[0] = posicion + nombre, texts[1] = fichas
                string prefix = i == 0 ? "🏆 " : $"{i + 1}. ";
                texts[0].text = $"{prefix}{results[i].name}";
                texts[1].text = $"{results[i].fuel} fichas";
            }
        }
    }
}
