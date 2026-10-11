using System.Collections.Generic;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Lista de circuitos jugables. Cada circuito es una escena que debe estar
    /// en File > Build Profiles > Scene List. Si una escena no esta en el build,
    /// el lobby no la ofrece.
    /// </summary>
    public static class MapCatalog
    {
        public struct MapInfo
        {
            public string DisplayName;
            public string SceneName;
        }

        private static readonly MapInfo[] AllMaps =
        {
            new MapInfo { DisplayName = "Circuito 1: Ovalo", SceneName = "Game" },
            new MapInfo { DisplayName = "Circuito 2", SceneName = "Circuito2" },
            new MapInfo { DisplayName = "Circuito 3", SceneName = "Circuito3" },
        };

        private static List<MapInfo> _available;

        /// <summary>
        /// Circuitos cuya escena esta incluida en el build.
        /// </summary>
        public static IReadOnlyList<MapInfo> Available
        {
            get
            {
                if (_available == null)
                {
                    _available = new List<MapInfo>();
                    foreach (var map in AllMaps)
                    {
                        if (Application.CanStreamedLevelBeLoaded(map.SceneName))
                            _available.Add(map);
                    }
                }
                return _available;
            }
        }

        /// <summary>
        /// Devuelve el nombre de escena para un indice; -1 (o invalido) = aleatorio.
        /// </summary>
        public static string ResolveScene(int index)
        {
            var maps = Available;
            if (maps.Count == 0) return AllMaps[0].SceneName;
            if (index < 0 || index >= maps.Count)
                index = Random.Range(0, maps.Count);
            return maps[index].SceneName;
        }

        public static string DisplayNameFor(int index)
        {
            var maps = Available;
            if (index < 0 || index >= maps.Count) return "Aleatorio";
            return maps[index].DisplayName;
        }

        public static bool IsRaceScene(string sceneName)
        {
            foreach (var map in AllMaps)
                if (map.SceneName == sceneName) return true;
            return false;
        }
    }

    /// <summary>
    /// Colores de identificacion de cada jugador (HUD, mini-mapa, resultados).
    /// </summary>
    public static class PlayerColors
    {
        private static readonly Color[] Palette =
        {
            new Color(0.93f, 0.23f, 0.23f), // rojo
            new Color(0.20f, 0.55f, 0.95f), // azul
            new Color(0.98f, 0.78f, 0.15f), // amarillo
            new Color(0.25f, 0.80f, 0.35f), // verde
        };

        public static Color Get(int slotIndex)
        {
            if (slotIndex < 0) return Color.white;
            return Palette[slotIndex % Palette.Length];
        }

        public static string Hex(int slotIndex) => ColorUtility.ToHtmlStringRGB(Get(slotIndex));
    }
}
