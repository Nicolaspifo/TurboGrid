using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TurboGrid.EditorTools
{
    /// <summary>
    /// Mejoras v2 (lista del equipo). A diferencia de "Configurar escenas y prefabs",
    /// NO reconstruye HUD ni controles: solo ajusta lo puntual para no pisar el trabajo
    /// que el equipo hizo a mano en las escenas.
    ///  - Camara mas cerca en los circuitos.
    ///  - Botones de poder sin texto debajo.
    ///  - Limites (muros) afuera, en el borde del fondo; el pasto frena (TrackSurface).
    ///  - Pantalla de inicio: botones en las esquinas inferiores con franja oscura.
    ///  - Circuito nuevo "Gran Premio" con la pista de Kevin (mas grande y con curvas).
    ///  - Catalogo de mapas y Build Settings.
    /// Se puede ejecutar varias veces (idempotente).
    /// Batchmode: Unity -batchmode -quit -projectPath ... -executeMethod TurboGrid.EditorTools.TurboGridSetup.AplicarMejorasV2
    /// </summary>
    public static partial class TurboGridSetup
    {
        private const string GRAN_PREMIO = SCENES + "GranPremio.unity";
        private const string PISTA_VIEJA = UI + "PANTALLA JUGABILIDAD/Pista.png";
        private const string PISTA_KEVIN = "Assets/Recursos/Pista-TurboGrid.png";
        private const string FONDO_KEVIN = "Assets/Recursos/Fondo-TruboGrid.png";

        // Pista de Kevin: 1 px = 1.67 unidades de mundo (escala 167 con 100 px/unidad).
        private const float GP_SCALE = 167f;
        private const float GP_K = GP_SCALE / 100f;
        private static readonly Vector2 GpTrackCenterPx = new Vector2(765.5f, 358f); // centro del sub-sprite (px, y hacia abajo)

        // Puntos sobre el asfalto (px de la imagen completa, y hacia abajo) para gasolina y poderes.
        private static readonly int[] GpWaypointsPx =
        {
            251,309, 1301,550, 841,148, 661,648, 1284,106, 311,653, 989,648, 521,139, 1082,334, 434,446,
            1307,327, 307,98, 677,273, 1080,128, 231,490, 486,616, 825,597, 1160,646, 947,260, 1193,228,
            381,327, 230,190, 1263,437, 1176,58, 564,247, 422,79, 1306,215, 326,438, 777,234, 228,593,
            1074,596, 1178,323, 580,598, 392,605, 931,169, 1212,571, 915,596, 751,648, 1029,270, 1119,198,
            302,172, 439,372, 313,344, 1264,271, 1268,613, 1149,122, 299,503, 370,124, 871,648, 711,214,
            509,206, 453,140, 628,224, 271,244, 1257,168, 1216,113, 706,598, 294,590, 779,168, 1307,485,
            1307,391, 541,648, 383,410, 378,472, 435,648, 1126,294, 565,188, 878,194, 1015,596, 1048,648,
            257,543, 1132,596, 1114,77, 1216,636, 259,135, 1258,513, 368,656, 1104,645, 636,598, 605,648,
        };

        // Parrilla: recta de abajo, mirando a la izquierda.
        private static readonly Vector2[] GpGridPx =
        {
            new Vector2(770, 600), new Vector2(830, 646), new Vector2(890, 600), new Vector2(950, 646),
        };

        private static Vector3 GpWorld(float px, float py) =>
            new Vector3((px - GpTrackCenterPx.x) * GP_K, (GpTrackCenterPx.y - py) * GP_K, 0f);

        [MenuItem("TurboGrid/Aplicar mejoras (v2)", priority = 1)]
        public static void AplicarMejorasV2()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[TurboGrid v2] Sal del modo Play antes de aplicar.");
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string previousScene = SceneManager.GetActiveScene().path;
            Log.Clear();
            try
            {
                _font = LoadFont();
                MakeReadable(PISTA_VIEJA);
                MakeReadable(PISTA_KEVIN);
                WriteSpriteIfMissing(GENERATED + "Degradado.png", DrawVerticalGradient(8, 128));

                foreach (var path in new[] { GAME, CIRCUITO2, CIRCUITO3 })
                {
                    if (!File.Exists(path)) continue;
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    TuneRaceScene();
                    MoveLimitsOut(FindInScene<SpriteRenderer>(PREFIX + "FondoHD"));
                    AddTrackSurface(FindInScene<SpriteRenderer>(PREFIX + "PistaHD"));
                    SaveScene(scene);
                    Log.Add($"{Path.GetFileNameWithoutExtension(path)}: camara, botones, limites y pasto.");
                }

                BuildGranPremio();
                LayoutStartScreen();
                if (File.Exists(INSTRUCCIONES))
                {
                    var instr = EditorSceneManager.OpenScene(INSTRUCCIONES, OpenSceneMode.Single);
                    BuildInstructions();
                    SaveScene(instr);
                    Log.Add("Instrucciones: reglas actualizadas (10 pts, 1 s, lider, 3 poderes nuevos, pasto y derrape).");
                }
                UpdateBuildSettingsV2();
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Log.Add("ERROR: " + e.Message);
            }

            if (!Application.isBatchMode && !string.IsNullOrEmpty(previousScene) && File.Exists(previousScene))
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);

            string summary = string.Join("\n", Log);
            Debug.Log("[TurboGrid v2] Resultado:\n" + summary);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("TurboGrid - Mejoras v2", summary, "OK");
        }

        // ---------------------------------------------------------------------
        // Ajustes comunes de los circuitos
        // ---------------------------------------------------------------------

        private static void TuneRaceScene()
        {
            // Camara mas cerca (los autos ahora son mas pequeños).
            var follow = Object.FindFirstObjectByType<CameraFollow>(FindObjectsInactive.Include);
            if (follow != null)
            {
                SetFloat(follow, "baseOrthoSize", 150f);
                SetFloat(follow, "maxExtraZoom", 35f);
                SetFloat(follow, "celebrationOrthoSize", 110f);
            }

            // Botones de poder: sin texto debajo del icono.
            foreach (var buttonName in new[] { "PowerUpDRS", "PowerUpPINCHAZO" })
            {
                var button = FindInScene<Transform>(buttonName);
                var label = button != null ? button.Find("Etiqueta") : null;
                if (label != null) label.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Quita los muros pegados a la pista (invisibles) y pone 4 muros en el borde del
        /// fondo: ahora se puede salir al pasto (que frena) pero no al vacio.
        /// </summary>
        private static void MoveLimitsOut(SpriteRenderer fondo)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var col in root.GetComponentsInChildren<Collider2D>(true))
                {
                    var go = col.gameObject;
                    bool oldTrack = go.name == "Pista";
                    var sr = go.GetComponent<SpriteRenderer>();
                    bool invisibleWall = go.name.StartsWith("Square") && (sr == null || !sr.enabled);
                    if (oldTrack || invisibleWall) col.enabled = false;
                }
            }

            var old = FindInScene<Transform>(PREFIX + "Limites");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            if (fondo == null)
            {
                Log.Add($"AVISO: {SceneManager.GetActiveScene().name} sin fondo: no se crearon limites externos.");
                return;
            }

            Bounds b = fondo.bounds;
            const float thickness = 200f;
            const float inset = 60f; // un poco adentro del borde para que nunca se vea el vacio
            var rootGo = new GameObject(PREFIX + "Limites");
            float w = b.size.x, h = b.size.y;
            float left = b.min.x + inset, right = b.max.x - inset, bottom = b.min.y + inset, top = b.max.y - inset;
            MakeWall(rootGo.transform, "Arriba", new Vector2(b.center.x, top + thickness / 2f), new Vector2(w + 2 * thickness, thickness));
            MakeWall(rootGo.transform, "Abajo", new Vector2(b.center.x, bottom - thickness / 2f), new Vector2(w + 2 * thickness, thickness));
            MakeWall(rootGo.transform, "Izquierda", new Vector2(left - thickness / 2f, b.center.y), new Vector2(thickness, h + 2 * thickness));
            MakeWall(rootGo.transform, "Derecha", new Vector2(right + thickness / 2f, b.center.y), new Vector2(thickness, h + 2 * thickness));
        }

        private static void MakeWall(Transform parent, string wallName, Vector2 center, Vector2 size)
        {
            var go = new GameObject(wallName);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
        }

        private static void AddTrackSurface(SpriteRenderer track)
        {
            if (track == null) return;
            if (track.GetComponent<TrackSurface>() == null) track.gameObject.AddComponent<TrackSurface>();
        }

        private static void MakeReadable(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.isReadable) return;
            importer.isReadable = true;
            importer.SaveAndReimport();
            Log.Add($"{Path.GetFileName(path)}: Read/Write activado (deteccion de pasto).");
        }

        // ---------------------------------------------------------------------
        // Circuito "Gran Premio" (pista de Kevin)
        // ---------------------------------------------------------------------

        private static void BuildGranPremio()
        {
            Sprite pista = LoadSprite(PISTA_KEVIN, "Pista-TurboGrid_0");
            Sprite fondo = LoadSprite(FONDO_KEVIN, "Fondo-TruboGrid_0");
            if (pista == null || fondo == null)
            {
                Log.Add("AVISO: faltan Pista-TurboGrid / Fondo-TruboGrid: no se creo el Gran Premio.");
                return;
            }

            if (!File.Exists(GRAN_PREMIO))
            {
                if (!AssetDatabase.CopyAsset(GAME, GRAN_PREMIO))
                {
                    Log.Add("ERROR: no se pudo copiar Game.unity a GranPremio.unity.");
                    return;
                }
                AssetDatabase.Refresh();
            }
            var scene = EditorSceneManager.OpenScene(GRAN_PREMIO, OpenSceneMode.Single);

            // Lo del ovalo no aplica aqui: se apaga (no se borra).
            var reference = FindInScene<SpriteRenderer>(PREFIX + "PistaHD");
            int layer = reference != null ? reference.sortingLayerID : SortingLayer.NameToID("Track");
            int order = reference != null ? reference.sortingOrder : 0;
            foreach (var n in new[] { PREFIX + "FondoHD", PREFIX + "PistaHD", PREFIX + "Limites", "Pista", "Ruedas", "Conos", "Barreras", "Arboles" })
            {
                var t = FindInScene<Transform>(n);
                if (t != null) t.gameObject.SetActive(false);
            }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Square")) t.gameObject.SetActive(false);

            foreach (var n in new[] { PREFIX + "GP_Fondo", PREFIX + "GP_Pista" })
            {
                var t = FindInScene<Transform>(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            var fondoGo = new GameObject(PREFIX + "GP_Fondo");
            fondoGo.transform.position = new Vector3(0f, 8f * GP_K, 0.1f); // el centro de la pista esta 8 px bajo el centro del fondo
            fondoGo.transform.localScale = Vector3.one * GP_SCALE;
            var fondoSr = fondoGo.AddComponent<SpriteRenderer>();
            fondoSr.sprite = fondo;
            fondoSr.sortingLayerID = layer;
            fondoSr.sortingOrder = order - 1;

            var pistaGo = new GameObject(PREFIX + "GP_Pista");
            pistaGo.transform.position = new Vector3(0f, 0f, 0.05f);
            pistaGo.transform.localScale = Vector3.one * GP_SCALE;
            var pistaSr = pistaGo.AddComponent<SpriteRenderer>();
            pistaSr.sprite = pista;
            pistaSr.sortingLayerID = layer;
            pistaSr.sortingOrder = order;
            pistaGo.AddComponent<TrackSurface>();

            // Puntos de gasolina / poderes sobre el asfalto nuevo.
            var waypoints = FindInScene<Transform>("TrackWaypoints");
            if (waypoints == null) waypoints = new GameObject("TrackWaypoints").transform;
            waypoints.position = Vector3.zero;
            for (int i = waypoints.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(waypoints.GetChild(i).gameObject);
            for (int i = 0; i < GpWaypointsPx.Length / 2; i++)
            {
                var p = new GameObject("Punto" + (i + 1)).transform;
                p.SetParent(waypoints, false);
                p.position = GpWorld(GpWaypointsPx[2 * i], GpWaypointsPx[2 * i + 1]);
            }
            var fuel = Object.FindFirstObjectByType<FuelSpawner>(FindObjectsInactive.Include);
            if (fuel != null) SetRef(fuel, "waypointsRoot", waypoints);

            // Parrilla.
            var grid = FindInScene<Transform>("StartGrid");
            if (grid == null) grid = new GameObject("StartGrid").transform;
            grid.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var slots = new Transform[GpGridPx.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = grid.Find("Posicion" + (i + 1));
                if (slot == null)
                {
                    slot = new GameObject("Posicion" + (i + 1)).transform;
                    slot.SetParent(grid, false);
                }
                slot.SetPositionAndRotation(GpWorld(GpGridPx[i].x, GpGridPx[i].y), Quaternion.Euler(0f, 0f, 90f));
                slots[i] = slot;
            }
            var sgm = Object.FindFirstObjectByType<SpawnGridManager>(FindObjectsInactive.Include);
            if (sgm != null) SetArray(sgm, "startGridSlots", slots);

            // Mini-mapa: que lo detecte solo (el fondo nuevo es el sprite mas grande).
            var minimap = Object.FindFirstObjectByType<MinimapUI>(FindObjectsInactive.Include);
            if (minimap != null)
            {
                var so = new SerializedObject(minimap);
                var c = so.FindProperty("mapCenter");
                var s = so.FindProperty("mapSize");
                if (c != null) c.vector2Value = Vector2.zero;
                if (s != null) s.vector2Value = Vector2.zero;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            TuneRaceScene();
            MoveLimitsOut(fondoSr);
            SaveScene(scene);
            Log.Add("GranPremio: pista de Kevin, 80 puntos, parrilla, limites y pasto.");
        }

        // ---------------------------------------------------------------------
        // Pantalla de inicio
        // ---------------------------------------------------------------------

        private static void LayoutStartScreen()
        {
            if (!File.Exists(INICIO)) return;
            var scene = EditorSceneManager.OpenScene(INICIO, OpenSceneMode.Single);

            var jugar = FindInScene<RectTransform>("Boton Jugar");
            var salir = FindInScene<RectTransform>("Boton Salir");
            if (jugar == null || salir == null)
            {
                Log.Add("AVISO: Pantalla Inicio sin 'Boton Jugar' / 'Boton Salir'.");
                return;
            }

            // Franja oscura abajo para que los botones resalten sobre el fondo.
            var canvas = jugar.GetComponentInParent<Canvas>().rootCanvas.transform;
            var oldStrip = canvas.Find(PREFIX + "FranjaInferior");
            if (oldStrip != null) Object.DestroyImmediate(oldStrip.gameObject);
            var strip = MakeImage(canvas, PREFIX + "FranjaInferior", LoadSprite(GENERATED + "Degradado.png"), new Color(0.02f, 0.05f, 0.14f, 0.85f));
            strip.raycastTarget = false;
            SetRect(strip.rectTransform, BottomLeft, BottomRight, Bottom, Vector2.zero, new Vector2(0f, 150f));
            var fondo = canvas.Find("Fondo");
            strip.transform.SetSiblingIndex(fondo != null ? fondo.GetSiblingIndex() + 1 : 0);

            // Jugar abajo a la izquierda y Salir abajo a la derecha (debajo de los autos).
            PlaceCornerButton(jugar, BottomLeft, new Vector2(190f, 64f));
            PlaceCornerButton(salir, BottomRight, new Vector2(-190f, 64f));

            SaveScene(scene);
            Log.Add("Pantalla Inicio: botones en las esquinas con franja oscura.");
        }

        private static void PlaceCornerButton(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            const float width = 250f;
            float height = 72f;
            var img = rt.GetComponent<Image>();
            if (img != null && img.sprite != null && img.sprite.rect.width > 0f)
                height = Mathf.Clamp(width * img.sprite.rect.height / img.sprite.rect.width, 48f, 90f);
            SetRect(rt, anchor, anchor, Center, pos, new Vector2(width, height));
        }

        private static Texture2D DrawVerticalGradient(int w, int h)
        {
            var tex = NewTexture(w, h);
            for (int y = 0; y < h; y++)
            {
                float a = Mathf.SmoothStep(0f, 1f, 1f - y / (float)(h - 1)); // opaco abajo, transparente arriba
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }

        // ---------------------------------------------------------------------
        // Build Settings
        // ---------------------------------------------------------------------

        private static void UpdateBuildSettingsV2()
        {
            var wanted = new[] { INICIO, INSTRUCCIONES, MAIN_MENU, GRAN_PREMIO, GAME, CIRCUITO3 }.Where(File.Exists).ToList();
            var scenes = wanted.Select(p => new EditorBuildSettingsScene(p, true)).ToList();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (wanted.Contains(s.path)) continue;
                // Circuito2 era una copia del ovalo: ya no se ofrece (el Gran Premio la reemplaza).
                scenes.Add(new EditorBuildSettingsScene(s.path, s.enabled && s.path != CIRCUITO2));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            Log.Add("Build Settings: Inicio, Instrucciones, MainMenu, GranPremio, Game (Ovalo), Circuito3.");
        }
    }
}
