using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TurboGrid.EditorTools
{
    /// <summary>
    /// Monta automaticamente todo lo que el codigo necesita en escenas y prefabs:
    ///  - Prefabs de red nuevos (SessionState, PowerUp) y su registro en DefaultNetworkPrefabs.
    ///  - Lobby (MainMenu): panel de Opciones (nombre, controles, circuito) y lista de pilotos.
    ///  - Circuito (Game): parrilla de salida, camara que sigue, HUD completo, controles
    ///    tactiles (flechas / timon / joystick), mini-mapa, pantalla de resultados.
    ///  - Circuitos 2 y 3 (copias provisionales del 1 con otra ambientacion).
    ///  - Build Settings y capa "Minimapa".
    ///
    /// Es seguro ejecutarlo varias veces: los objetos generados llevan el prefijo "TG_"
    /// y se regeneran. Los objetos originales del equipo solo se desactivan, no se borran.
    /// </summary>
    public static class TurboGridSetup
    {
        private const string SCENES = "Assets/Scenes/";
        private const string MAIN_MENU = SCENES + "MainMenu.unity";
        private const string GAME = SCENES + "Game.unity";
        private const string CIRCUITO2 = SCENES + "Circuito2.unity";
        private const string CIRCUITO3 = SCENES + "Circuito3.unity";
        private const string INICIO = SCENES + "Pantalla Inicio.unity";
        private const string INSTRUCCIONES = SCENES + "Pantalla Intrucciones.unity";

        private const string PREFABS = "Assets/Prefab/";
        private const string GENERATED = "Assets/Recursos/Generados/";
        private const string UI = "Assets/Recursos/Pantallas/";

        private const string PREFIX = "TG_";

        private static TMP_FontAsset _font;
        private static readonly List<string> Log = new List<string>();

        // =====================================================================
        // Menu
        // =====================================================================

        [MenuItem("TurboGrid/Configurar escenas y prefabs", priority = 0)]
        public static void RunAll()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("TurboGrid", "Sal del modo Play antes de configurar.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string previousScene = SceneManager.GetActiveScene().path;
            Log.Clear();

            try
            {
                EditorUtility.DisplayProgressBar("TurboGrid", "Preparando assets...", 0.1f);
                EnsureLayer(CarVisuals.MINIMAP_LAYER);
                _font = LoadFont();
                GenerateSprites();

                var sessionPrefab = CreateSessionStatePrefab();
                var powerUpPrefab = CreatePowerUpPrefab();
                SetupCarAndFuelPrefabs();
                RegisterNetworkPrefabs(sessionPrefab, powerUpPrefab);

                EditorUtility.DisplayProgressBar("TurboGrid", "Lobby (MainMenu)...", 0.3f);
                SetupMainMenu(sessionPrefab);

                EditorUtility.DisplayProgressBar("TurboGrid", "Circuito 1 (Game)...", 0.5f);
                SetupRaceScene(GAME, powerUpPrefab, null);

                EditorUtility.DisplayProgressBar("TurboGrid", "Circuitos 2 y 3...", 0.75f);
                CreateExtraCircuit(CIRCUITO2, powerUpPrefab, "Desierto", new Color(1f, 0.86f, 0.62f), new Color(0.93f, 0.78f, 0.5f));
                CreateExtraCircuit(CIRCUITO3, powerUpPrefab, "Noche", new Color(0.55f, 0.62f, 0.9f), new Color(0.05f, 0.07f, 0.16f));

                EditorUtility.DisplayProgressBar("TurboGrid", "Pantallas de inicio...", 0.9f);
                SetupMenuScreens();

                UpdateBuildSettings();
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Log.Add("ERROR: " + e.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!string.IsNullOrEmpty(previousScene) && File.Exists(previousScene))
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);

            string summary = string.Join("\n", Log);
            Debug.Log("[TurboGridSetup] Resultado:\n" + summary);
            EditorUtility.DisplayDialog("TurboGrid - Configuracion", summary, "OK");
        }

        // =====================================================================
        // Assets
        // =====================================================================

        private static TMP_FontAsset LoadFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Recursos/Fonts/knewave SDF.asset");
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset");
            if (font != null && fallback != null)
            {
                if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!font.fallbackFontAssetTable.Contains(fallback))
                {
                    // Para que se vean tildes y signos (¡ ° ñ) que Knewave no trae.
                    font.fallbackFontAssetTable.Add(fallback);
                    EditorUtility.SetDirty(font);
                    Log.Add("Fuente Knewave: agregado respaldo para tildes y signos.");
                }
            }
            if (font == null) font = TMP_Settings.defaultFontAsset;
            return font;
        }

        private static void EnsureLayer(string layerName)
        {
            if (LayerMask.NameToLayer(layerName) >= 0) return;
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var prop = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(prop.stringValue))
                {
                    prop.stringValue = layerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    Log.Add($"Capa '{layerName}' creada (User Layer {i}).");
                    return;
                }
            }
            Log.Add($"AVISO: no hay capas libres para '{layerName}'.");
        }

        private static Sprite LoadSprite(string path, string subName = null)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
            if (sprites.Count == 0) return null;
            if (subName != null)
            {
                var named = sprites.FirstOrDefault(s => s.name == subName);
                if (named != null) return named;
            }
            return sprites[0];
        }

        private static Sprite S(string relativePath) => LoadSprite(UI + relativePath);

        /// <summary>Genera los sprites que no existian (timon, pedal, circulo).</summary>
        private static void GenerateSprites()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Recursos/Generados"))
                AssetDatabase.CreateFolder("Assets/Recursos", "Generados");

            WriteSpriteIfMissing(GENERATED + "Timon.png", DrawWheel(512));
            WriteSpriteIfMissing(GENERATED + "Pedal.png", DrawRoundedRect(256, 256, 48, 10));
            WriteSpriteIfMissing(GENERATED + "Circulo.png", DrawCircle(256, 14));
            WriteSpriteIfMissing(GENERATED + "Panel.png", DrawRoundedRect(128, 128, 28, 0), new Vector4(40, 40, 40, 40));
        }

        private static void WriteSpriteIfMissing(string path, Texture2D tex, Vector4 border = default)
        {
            if (File.Exists(path)) { Object.DestroyImmediate(tex); return; }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
            Log.Add($"Sprite generado: {path}");
        }

        private static Texture2D NewTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var clear = new Color[w * h];
            tex.SetPixels(clear);
            return tex;
        }

        private static Texture2D DrawCircle(int size, float border)
        {
            var tex = NewTexture(size, size);
            float r = size / 2f - 2f;
            Vector2 c = new Vector2(size / 2f, size / 2f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a = Mathf.Clamp01(r - d);
                    Color col = d > r - border ? new Color(0.06f, 0.08f, 0.15f, a) : new Color(1f, 1f, 1f, a);
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            return tex;
        }

        private static Texture2D DrawRoundedRect(int w, int h, float radius, float border)
        {
            var tex = NewTexture(w, h);
            Vector2 half = new Vector2(w / 2f, h / 2f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // Distancia con signo a un rectangulo redondeado (negativa = adentro).
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
                    Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (half - new Vector2(radius + 1f, radius + 1f));
                    float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                    float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                    float dist = outside + inside - radius;

                    float a = Mathf.Clamp01(0.5f - dist);
                    Color col = border > 0f && -dist < border
                        ? new Color(0.06f, 0.08f, 0.15f, a)
                        : new Color(1f, 1f, 1f, a);
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            return tex;
        }

        private static Texture2D DrawWheel(int size)
        {
            var tex = NewTexture(size, size);
            Vector2 c = new Vector2(size / 2f, size / 2f);
            float outer = size / 2f - 4f;
            float ringInner = outer - size * 0.11f;
            float hub = size * 0.11f;
            Color dark = new Color(0.1f, 0.12f, 0.2f, 1f);
            Color ring = new Color(0.16f, 0.36f, 0.72f, 1f);
            Color grip = new Color(0.98f, 0.55f, 0.15f, 1f);

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - c;
                    float d = p.magnitude;
                    float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                    Color col = Color.clear;

                    if (d <= outer && d >= ringInner)
                    {
                        // Aro con borde oscuro y marca naranja arriba (para ver cuanto se giro).
                        bool edge = d > outer - 6f || d < ringInner + 6f;
                        bool topMark = Mathf.Abs(Mathf.DeltaAngle(angle, 90f)) < 10f;
                        col = edge ? dark : (topMark ? grip : ring);
                        col.a = Mathf.Clamp01(outer - d + 1f) * Mathf.Clamp01(d - ringInner + 1f);
                    }
                    else if (d < ringInner)
                    {
                        // Tres rayos (izquierda, derecha, abajo) + centro.
                        float spokeHalf = size * 0.045f;
                        bool horizontal = Mathf.Abs(p.y) < spokeHalf;
                        bool down = p.y < 0f && Mathf.Abs(p.x) < spokeHalf;
                        if (d < hub) col = dark;
                        else if (horizontal || down) col = ring;
                        if (col.a > 0f && (d < hub + 4f && d > hub - 4f)) col = dark;
                    }
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            return tex;
        }

        // =====================================================================
        // Prefabs
        // =====================================================================

        private static GameObject CreateSessionStatePrefab()
        {
            string path = PREFABS + "SessionState.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject("SessionState");
            go.AddComponent<NetworkObject>();
            go.AddComponent<SessionState>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Log.Add("Prefab creado: SessionState.");
            return prefab;
        }

        private static GameObject CreatePowerUpPrefab()
        {
            string path = PREFABS + "PowerUp.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Sprite drs = LoadSprite("Assets/Recursos/Prototipo/Iconos.png", "Iconos_2");
            Sprite pinchazo = LoadSprite("Assets/Recursos/Prototipo/Iconos.png", "Iconos_4");

            GameObject root = existing != null ? PrefabUtility.LoadPrefabContents(path) : new GameObject("PowerUp");
            var sr = root.GetComponent<SpriteRenderer>();
            if (sr == null) sr = root.AddComponent<SpriteRenderer>();
            sr.sprite = drs;
            sr.sortingOrder = 6;
            if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
            var pickup = root.GetComponent<PowerUpPickup>();
            if (pickup == null) pickup = root.AddComponent<PowerUpPickup>();
            // Los iconos miden ~2 unidades: escala 24 = ~50 unidades de mundo (el auto mide ~56).
            root.transform.localScale = Vector3.one * 24f;
            SetRef(pickup, "spriteRenderer", sr);
            SetRef(pickup, "drsSprite", drs);
            SetRef(pickup, "pinchazoSprite", pinchazo);

            GameObject prefab;
            if (existing != null)
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            else
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                Log.Add("Prefab creado: PowerUp (DRS / Pinchazo).");
            }
            return prefab;
        }

        private static void SetupCarAndFuelPrefabs()
        {
            // Autos por encima de la pista; bidones y power-ups entre ambos.
            foreach (var name in new[] { "Vehiculo_1", "Vehiculo_2", "Vehiculo_3" })
            {
                string path = PREFABS + name + ".prefab";
                if (!File.Exists(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                var sr = root.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sortingOrder = 10;
                var rb = root.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.gravityScale = 0f;
                    rb.interpolation = RigidbodyInterpolation2D.Interpolate;
                    rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                    rb.angularDamping = 5f;
                }
                var binder = root.GetComponent<PlayerLocalUIBinder>();
                if (binder != null) Object.DestroyImmediate(binder, true);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            string fuelPath = PREFABS + "FuelToken.prefab";
            if (File.Exists(fuelPath))
            {
                var root = PrefabUtility.LoadPrefabContents(fuelPath);
                var sr = root.GetComponent<SpriteRenderer>();
                if (sr != null) sr.sortingOrder = 5;
                PrefabUtility.SaveAsPrefabAsset(root, fuelPath);
                PrefabUtility.UnloadPrefabContents(root);
            }
            Log.Add("Prefabs de autos y gasolina: orden de dibujo y fisica ajustados.");
        }

        private static void RegisterNetworkPrefabs(params GameObject[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (list == null)
            {
                Log.Add("AVISO: no se encontro Assets/DefaultNetworkPrefabs.asset.");
                return;
            }

            // Quitar entradas rotas (prefabs borrados).
            foreach (var broken in list.PrefabList.Where(p => p == null || p.Prefab == null).ToList())
                list.Remove(broken);

            foreach (var prefab in prefabs)
            {
                if (prefab != null && !list.Contains(prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
            Log.Add("DefaultNetworkPrefabs: SessionState y PowerUp registrados.");
        }

        // =====================================================================
        // MainMenu (lobby)
        // =====================================================================

        private static void SetupMainMenu(GameObject sessionPrefab)
        {
            var scene = EditorSceneManager.OpenScene(MAIN_MENU, OpenSceneMode.Single);

            // Red
            var gnm = Object.FindFirstObjectByType<GameNetworkManager>(FindObjectsInactive.Include);
            if (gnm != null) SetRef(gnm, "sessionStatePrefab", sessionPrefab.GetComponent<NetworkObject>());
            var nm = Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (nm != null)
            {
                nm.NetworkConfig.ConnectionApproval = true;
                EditorUtility.SetDirty(nm);
            }

            EnsureEventSystem();

            var lobby = Object.FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
            var canvas = lobby != null ? lobby.GetComponentInParent<Canvas>() : Object.FindFirstObjectByType<Canvas>();
            if (lobby == null || canvas == null)
            {
                Log.Add("AVISO: MainMenu no tiene LobbyUI/Canvas; se omite el lobby.");
                SaveScene(scene);
                return;
            }

            // Referencias existentes que podian estar vacias
            AssignIfEmpty(lobby, "hostButton", FindComp<Button>("BotonCrearPartida"));
            AssignIfEmpty(lobby, "joinButton", FindComp<Button>("BotonUnirse"));
            AssignIfEmpty(lobby, "startRaceButton", FindComp<Button>("BotonJugar"));
            AssignIfEmpty(lobby, "joinCodeInputField", FindComp<TMP_InputField>("InputFieldConexion"));
            AssignIfEmpty(lobby, "joinCodeDisplayText", FindComp<TMP_Text>("TextoCodigoCreacion"));
            AssignIfEmpty(lobby, "statusText", FindComp<TMP_Text>("TextoEstado"));

            RemoveGenerated(canvas.transform);

            // El canvas del lobby usa resolucion de referencia 800x600 (ancho fijo 800).
            Transform root = canvas.transform;
            Sprite box = S("PANTALLA4/Elemento_Espacio.png");
            Sprite arrow = S("PANTALLA2/Boton_Siguiente.png");

            // Lista de pilotos (debajo del estado)
            var players = MakeText(root, PREFIX + "TextoJugadores", "", 15, TextAlignmentOptions.Center, Color.white);
            SetRect(players.rectTransform, Center, Center, Center, new Vector2(12f, -104f), new Vector2(330f, 40f));
            players.enableAutoSizing = true;
            players.fontSizeMin = 9;
            players.fontSizeMax = 15;

            // Boton "Opciones" (arriba a la izquierda)
            var optionsBtn = MakeButton(root, PREFIX + "BotonOpciones", box, "Opciones", 15);
            SetRect((RectTransform)optionsBtn.transform, TopLeft, TopLeft, Center, new Vector2(70f, -26f), new Vector2(118f, 34f));

            // Panel de opciones (overlay)
            var overlay = MakeImage(root, PREFIX + "PanelOpciones", null, new Color(0f, 0f, 0f, 0.7f));
            Stretch(overlay.rectTransform);
            var panel = MakeImage(overlay.transform, "Caja", LoadSprite(GENERATED + "Panel.png"), new Color(0.07f, 0.2f, 0.45f, 0.97f));
            panel.type = Image.Type.Sliced;
            SetRect(panel.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(520f, 300f));

            var title = MakeText(panel.transform, "Titulo", "OPCIONES", 28, TextAlignmentOptions.Center, Color.white);
            SetRect(title.rectTransform, Center, Center, Center, new Vector2(0f, 118f), new Vector2(400f, 40f));

            // Fila: nombre
            var nameLabel = MakeText(panel.transform, "EtiquetaNombre", "Piloto", 18, TextAlignmentOptions.Right, new Color(1f, 0.8f, 0.3f));
            SetRect(nameLabel.rectTransform, Center, Center, Center, new Vector2(-150f, 62f), new Vector2(150f, 34f));
            var nameInput = MakeInputField(panel.transform, "InputNombre", box, "Tu nombre", 17);
            SetRect((RectTransform)nameInput.transform, Center, Center, Center, new Vector2(70f, 62f), new Vector2(240f, 40f));

            // Fila: controles
            var ctrlLabel = MakeText(panel.transform, "EtiquetaControles", "Controles", 18, TextAlignmentOptions.Right, new Color(1f, 0.8f, 0.3f));
            SetRect(ctrlLabel.rectTransform, Center, Center, Center, new Vector2(-150f, 10f), new Vector2(150f, 34f));
            MakeSelector(panel.transform, "SelectorControles", arrow, box, new Vector2(70f, 10f), out var ctrlText, out var ctrlPrev, out var ctrlNext);

            // Fila: circuito (solo host)
            var mapRow = MakeUI(panel.transform, "FilaCircuito");
            SetRect(mapRow, Center, Center, Center, new Vector2(0f, -42f), new Vector2(500f, 44f));
            var mapLabel = MakeText(mapRow, "EtiquetaCircuito", "Circuito", 18, TextAlignmentOptions.Right, new Color(1f, 0.8f, 0.3f));
            SetRect(mapLabel.rectTransform, Center, Center, Center, new Vector2(-150f, 0f), new Vector2(150f, 34f));
            MakeSelector(mapRow, "SelectorCircuito", arrow, box, new Vector2(70f, 0f), out var mapText, out var mapPrev, out var mapNext);

            var hint = MakeText(panel.transform, "Ayuda", "El circuito lo elige quien crea la partida", 12, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.7f));
            SetRect(hint.rectTransform, Center, Center, Center, new Vector2(0f, -78f), new Vector2(460f, 22f));

            var closeBtn = MakeButton(panel.transform, "BotonListo", LoadSprite(UI + "PANTALLA2/Boton_Listo.png"), "", 16);
            SetRect((RectTransform)closeBtn.transform, Center, Center, Center, new Vector2(0f, -115f), new Vector2(170f, 49f));

            // Conectar al LobbyUI
            SetRef(lobby, "playersText", players);
            SetRef(lobby, "optionsButton", optionsBtn);
            SetRef(lobby, "optionsPanel", overlay.gameObject);
            SetRef(lobby, "closeOptionsButton", closeBtn);
            SetRef(lobby, "playerNameInput", nameInput);
            SetRef(lobby, "controlSchemeText", ctrlText);
            SetRef(lobby, "controlPrevButton", ctrlPrev);
            SetRef(lobby, "controlNextButton", ctrlNext);
            SetRef(lobby, "mapRow", mapRow.gameObject);
            SetRef(lobby, "mapText", mapText);
            SetRef(lobby, "mapPrevButton", mapPrev);
            SetRef(lobby, "mapNextButton", mapNext);

            overlay.gameObject.SetActive(false);
            SaveScene(scene);
            Log.Add("MainMenu: panel de Opciones (nombre, controles, circuito), lista de pilotos, aprobacion de conexiones.");
        }

        private static void MakeSelector(Transform parent, string name, Sprite arrow, Sprite box, Vector2 pos,
            out TMP_Text label, out Button prev, out Button next)
        {
            var row = MakeUI(parent, name);
            SetRect(row, Center, Center, Center, pos, new Vector2(260f, 40f));

            var bg = MakeImage(row, "Fondo", box, Color.white);
            SetRect(bg.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(170f, 40f));
            label = MakeText(row, "Valor", "-", 16, TextAlignmentOptions.Center, Color.white);
            SetRect(label.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(160f, 36f));
            label.enableAutoSizing = true;
            label.fontSizeMin = 9;
            label.fontSizeMax = 16;

            prev = MakeButton(row, "Anterior", arrow, "", 12);
            SetRect((RectTransform)prev.transform, Center, Center, Center, new Vector2(-110f, 0f), new Vector2(38f, 36f));
            prev.transform.localScale = new Vector3(-1f, 1f, 1f);
            next = MakeButton(row, "Siguiente", arrow, "", 12);
            SetRect((RectTransform)next.transform, Center, Center, Center, new Vector2(110f, 0f), new Vector2(38f, 36f));
        }

        // =====================================================================
        // Escena de carrera
        // =====================================================================

        private static void SetupRaceScene(string path, GameObject powerUpPrefab, System.Action<Scene> extra)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            EnsureEventSystem();
            SetupStartGrid();
            SetupManagers(powerUpPrefab);
            SetupCamera();

            var canvas = FindInScene<Canvas>("Canvas");
            if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas != null)
            {
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;
                }

                DisableMockups(canvas.transform);
                RemoveGenerated(canvas.transform);
                BuildHud(canvas.transform);
                BuildControls(canvas.transform);
                BuildResults(canvas.transform);
            }
            else
            {
                Log.Add($"AVISO: {Path.GetFileName(path)} no tiene Canvas.");
            }

            extra?.Invoke(scene);
            SaveScene(scene);
            Log.Add($"{Path.GetFileNameWithoutExtension(path)}: parrilla, camara, HUD, controles, mini-mapa y resultados listos.");
        }

        private static void SetupStartGrid()
        {
            var grid = FindInScene<Transform>("StartGrid");
            if (grid == null)
            {
                grid = new GameObject("StartGrid").transform;
                Log.Add("AVISO: no habia StartGrid; se creo uno nuevo.");
            }
            grid.position = Vector3.zero;
            grid.rotation = Quaternion.identity;

            // Recta principal (abajo), detras de la linea de meta (x ~ -12), mirando a la derecha (+X).
            // El sprite del auto mira hacia arriba, por eso rotacion Z = -90.
            Vector3[] positions =
            {
                new Vector3(-75f, -205f, 0f),
                new Vector3(-125f, -255f, 0f),
                new Vector3(-175f, -205f, 0f),
                new Vector3(-225f, -255f, 0f),
            };

            var slots = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var slot = grid.Find("Posicion" + (i + 1));
                if (slot == null)
                {
                    slot = new GameObject("Posicion" + (i + 1)).transform;
                    slot.SetParent(grid, false);
                }
                slot.position = positions[i];
                slot.rotation = Quaternion.Euler(0f, 0f, -90f);
                slots[i] = slot;
            }

            var sgm = Object.FindFirstObjectByType<SpawnGridManager>(FindObjectsInactive.Include);
            if (sgm == null)
            {
                var host = FindInScene<Transform>("RaceManager");
                sgm = (host != null ? host.gameObject : grid.gameObject).AddComponent<SpawnGridManager>();
            }
            SetArray(sgm, "startGridSlots", slots);
        }

        private static void SetupManagers(GameObject powerUpPrefab)
        {
            var race = Object.FindFirstObjectByType<RaceManager>(FindObjectsInactive.Include);
            if (race == null)
            {
                Log.Add("AVISO: la escena no tiene RaceManager.");
                return;
            }

            var spawner = race.GetComponent<PowerUpSpawner>();
            if (spawner == null) spawner = race.gameObject.AddComponent<PowerUpSpawner>();
            SetRef(spawner, "powerUpPrefab", powerUpPrefab);

            var cars = new[] { "Vehiculo_1", "Vehiculo_2", "Vehiculo_3" }
                .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + n + ".prefab"))
                .Where(p => p != null)
                .Cast<Object>()
                .ToArray();
            SetArray(race, "carPrefabs", cars);

            var fuel = Object.FindFirstObjectByType<FuelSpawner>(FindObjectsInactive.Include);
            if (fuel != null)
            {
                AssignIfEmpty(fuel, "waypointsRoot", FindInScene<Transform>("TrackWaypoints"));
                AssignIfEmpty(fuel, "fuelTokenPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PREFABS + "FuelToken.prefab"));
            }
        }

        private static void SetupCamera()
        {
            var cam = FindInScene<Camera>("Main Camera");
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null) return;
            cam.orthographic = true;
            cam.backgroundColor = new Color(cam.backgroundColor.r, cam.backgroundColor.g, cam.backgroundColor.b, 1f);
            if (cam.GetComponent<CameraFollow>() == null) cam.gameObject.AddComponent<CameraFollow>();
        }

        private static void DisableMockups(Transform canvas)
        {
            // Imagenes guia del diseñador y HUD anterior: se desactivan (no se borran).
            string[] names = { "Player1", "Player2", "w", "Elemento_4", "Elemento_2", "HUD", "EndRacePanel" };
            foreach (Transform child in canvas)
            {
                if (names.Contains(child.name) && child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }

            // El EndRaceUI viejo estaba en el mismo panel que ocultaba (nunca se mostraba).
            foreach (var old in canvas.GetComponentsInChildren<EndRaceUI>(true))
            {
                if (!old.name.StartsWith(PREFIX)) Object.DestroyImmediate(old);
            }
            foreach (var old in canvas.GetComponentsInChildren<HUD_RaceUI>(true))
            {
                if (!old.name.StartsWith(PREFIX)) Object.DestroyImmediate(old);
            }
        }

        private static void BuildHud(Transform canvas)
        {
            var hudRoot = MakeUI(canvas, PREFIX + "HUD");
            Stretch(hudRoot);
            var hud = hudRoot.gameObject.AddComponent<HUD_RaceUI>();

            // --- Puntaje (arriba izquierda) con el arte del acumulador ---
            var scoreBg = MakeImage(hudRoot, "Puntaje", S("PANTALLA JUGABILIDAD/Player1_Acumulador.png"), Color.white);
            SetRect(scoreBg.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(30f, -24f), new Vector2(420f, 120f));
            scoreBg.preserveAspect = true;
            var position = MakeText(scoreBg.transform, "Posicion", "-", 54, TextAlignmentOptions.Center, Color.white);
            SetRect(position.rectTransform, Center, Center, Center, new Vector2(-173f, 2f), new Vector2(70f, 90f));
            var points = MakeText(scoreBg.transform, "Puntos", "0 pts", 48, TextAlignmentOptions.Center, Color.white);
            SetRect(points.rectTransform, Center, Center, Center, new Vector2(70f, 2f), new Vector2(240f, 80f));
            points.enableAutoSizing = true;
            points.fontSizeMin = 20;
            points.fontSizeMax = 48;

            // Barra de progreso hacia 1000
            var barBg = MakeImage(hudRoot, "BarraFondo", LoadSprite(GENERATED + "Panel.png"), new Color(0.05f, 0.08f, 0.2f, 0.85f));
            barBg.type = Image.Type.Sliced;
            SetRect(barBg.rectTransform, TopLeft, TopLeft, TopLeft, new Vector2(40f, -150f), new Vector2(400f, 26f));
            var barFill = MakeImage(barBg.transform, "Relleno", LoadSprite(GENERATED + "Panel.png"), new Color(1f, 0.74f, 0.1f, 1f));
            barFill.type = Image.Type.Filled;
            barFill.fillMethod = Image.FillMethod.Horizontal;
            barFill.fillAmount = 0f;
            SetRect(barFill.rectTransform, Vector2.zero, Vector2.one, Center, Vector2.zero, new Vector2(-8f, -8f));
            var barText = MakeText(barBg.transform, "Meta", $"Meta: {GameConstants.WIN_SCORE}", 18, TextAlignmentOptions.Center, Color.white);
            Stretch(barText.rectTransform);

            // --- Tiempo (arriba derecha) ---
            var timeBg = MakeImage(hudRoot, "Tiempo", S("PANTALLA JUGABILIDAD/Item_Tiempo.png"), Color.white);
            SetRect(timeBg.rectTransform, TopRight, TopRight, TopRight, new Vector2(-30f, -24f), new Vector2(420f, 120f));
            timeBg.preserveAspect = true;
            var timer = MakeText(timeBg.transform, "TextoTiempo", "03:00", 52, TextAlignmentOptions.Center, Color.white);
            SetRect(timer.rectTransform, Center, Center, Center, new Vector2(62f, 2f), new Vector2(250f, 80f));

            // --- Tabla de posiciones (debajo del tiempo) ---
            var boardBg = MakeImage(hudRoot, "TablaPosiciones", LoadSprite(GENERATED + "Panel.png"), new Color(0.05f, 0.1f, 0.3f, 0.75f));
            boardBg.type = Image.Type.Sliced;
            SetRect(boardBg.rectTransform, TopRight, TopRight, TopRight, new Vector2(-40f, -150f), new Vector2(380f, 190f));
            var boardTitle = MakeText(boardBg.transform, "Titulo", "POSICIONES", 24, TextAlignmentOptions.Center, new Color(1f, 0.8f, 0.3f));
            SetRect(boardTitle.rectTransform, Top, Top, Top, new Vector2(0f, -6f), new Vector2(360f, 34f));
            var board = MakeText(boardBg.transform, "Lista", "", 26, TextAlignmentOptions.TopLeft, Color.white);
            SetRect(board.rectTransform, Vector2.zero, Vector2.one, Center, new Vector2(0f, -18f), new Vector2(-36f, -52f));
            board.lineSpacing = 4f;

            // --- Mini-mapa (arriba al centro) ---
            var mapFrame = MakeImage(hudRoot, "MiniMapaMarco", LoadSprite(GENERATED + "Panel.png"), new Color(0.03f, 0.05f, 0.12f, 0.9f));
            mapFrame.type = Image.Type.Sliced;
            SetRect(mapFrame.rectTransform, Top, Top, Top, new Vector2(0f, -16f), new Vector2(392f, 232f));
            var mapGo = MakeUI(mapFrame.transform, "MiniMapa");
            SetRect(mapGo, Center, Center, Center, Vector2.zero, new Vector2(376f, 216f));
            var raw = mapGo.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            mapGo.gameObject.AddComponent<MinimapUI>();

            // --- Avisos (debajo del mini-mapa) ---
            var feed = MakeText(hudRoot, "Avisos", "", 40, TextAlignmentOptions.Center, Color.white);
            SetRect(feed.rectTransform, Top, Top, Top, new Vector2(0f, -262f), new Vector2(1000f, 60f));

            // --- Cuenta regresiva (centro) ---
            var countdownRoot = MakeUI(hudRoot, "CuentaRegresiva");
            Stretch(countdownRoot);
            var countdown = MakeText(countdownRoot, "Numero", "3", 200, TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.2f));
            SetRect(countdown.rectTransform, Center, Center, Center, new Vector2(0f, 40f), new Vector2(1400f, 320f));
            countdown.enableAutoSizing = true;
            countdown.fontSizeMin = 60;
            countdown.fontSizeMax = 200;
            countdown.outlineWidth = 0.2f;
            countdown.outlineColor = new Color32(10, 20, 50, 255);

            hud.Configure(points, barFill, position, timer, countdown, countdownRoot.gameObject, board, feed);
        }

        private static void BuildControls(Transform canvas)
        {
            var root = MakeUI(canvas, PREFIX + "Controles");
            Stretch(root);
            var controls = root.gameObject.AddComponent<ControlsUI>();

            Sprite arrow = S("PANTALLA2/Boton_Siguiente.png");
            Sprite pedal = LoadSprite(GENERATED + "Pedal.png");
            Sprite wheelSprite = LoadSprite(GENERATED + "Timon.png");

            // --- Flechas (abajo izquierda) ---
            var arrows = MakeUI(root, "Flechas");
            Stretch(arrows);
            var left = MakeImage(arrows, "Izquierda", arrow, Color.white);
            SetRect(left.rectTransform, BottomLeft, BottomLeft, Center, new Vector2(170f, 170f), new Vector2(220f, 208f));
            left.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            left.gameObject.AddComponent<HoldButton>().Configure(HoldButton.Action.Izquierda, left);
            var right = MakeImage(arrows, "Derecha", arrow, Color.white);
            SetRect(right.rectTransform, BottomLeft, BottomLeft, Center, new Vector2(420f, 170f), new Vector2(220f, 208f));
            right.gameObject.AddComponent<HoldButton>().Configure(HoldButton.Action.Derecha, right);

            // --- Timon (abajo izquierda) ---
            var wheelRoot = MakeUI(root, "Timon");
            Stretch(wheelRoot);
            var wheelArea = MakeImage(wheelRoot, "AreaTimon", null, new Color(1f, 1f, 1f, 0.001f)); // zona tactil
            SetRect(wheelArea.rectTransform, BottomLeft, BottomLeft, Center, new Vector2(270f, 250f), new Vector2(420f, 420f));
            var wheelImage = MakeImage(wheelArea.transform, "Volante", wheelSprite, Color.white);
            wheelImage.raycastTarget = false;
            SetRect(wheelImage.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(360f, 360f));
            wheelArea.gameObject.AddComponent<SteeringWheel>().Configure(wheelImage.rectTransform);

            // --- Joystick (reutiliza el del diseñador si existe) ---
            var joyRoot = MakeUI(root, "Joystick");
            Stretch(joyRoot);
            var oldJoystick = canvas.GetComponentsInChildren<VirtualJoystick>(true).FirstOrDefault(j => !j.transform.IsChildOf(root));
            VirtualJoystick joystick;
            if (oldJoystick != null)
            {
                joystick = Object.Instantiate(oldJoystick.gameObject, joyRoot).GetComponent<VirtualJoystick>();
                joystick.name = "JoystickFondo";
                joystick.gameObject.SetActive(true);
                oldJoystick.gameObject.SetActive(false);
            }
            else
            {
                var bg = MakeImage(joyRoot, "JoystickFondo", S("PANTALLA JUGABILIDAD/Joystick_Parte1.png"), Color.white);
                var handle = MakeImage(bg.transform, "JoystickHandle", S("PANTALLA JUGABILIDAD/Joystick_Parte2.png"), Color.white);
                SetRect(handle.rectTransform, Center, Center, Center, Vector2.zero, new Vector2(170f, 170f));
                joystick = bg.gameObject.AddComponent<VirtualJoystick>();
            }
            var joyRect = (RectTransform)joystick.transform;
            SetRect(joyRect, BottomLeft, BottomLeft, Center, new Vector2(260f, 250f), new Vector2(340f, 340f));
            joyRect.localScale = Vector3.one;
            var joyImage = joystick.GetComponent<Image>();
            if (joyImage != null) joyImage.raycastTarget = true;
            Transform handleT = joystick.transform.childCount > 0 ? joystick.transform.GetChild(0) : null;
            if (handleT != null)
            {
                var hr = (RectTransform)handleT;
                SetRect(hr, Center, Center, Center, Vector2.zero, new Vector2(170f, 170f));
                hr.localScale = Vector3.one;
                var hImg = hr.GetComponent<Image>();
                if (hImg != null) hImg.raycastTarget = false;
            }
            SetRef(joystick, "background", joyRect);
            SetRef(joystick, "handle", handleT);
            SetFloat(joystick, "handleRange", 110f);

            // --- Pedales (abajo derecha) ---
            var pedals = MakeUI(root, "Pedales");
            Stretch(pedals);
            var gas = MakeImage(pedals, "Acelerar", pedal, new Color(0.35f, 0.78f, 0.25f));
            SetRect(gas.rectTransform, BottomRight, BottomRight, Center, new Vector2(-150f, 170f), new Vector2(210f, 250f));
            gas.gameObject.AddComponent<HoldButton>().Configure(HoldButton.Action.Acelerar, gas);
            var gasText = MakeText(gas.transform, "Texto", "GAS", 44, TextAlignmentOptions.Center, Color.white);
            Stretch(gasText.rectTransform);
            gasText.raycastTarget = false;

            var brake = MakeImage(pedals, "Frenar", pedal, new Color(0.9f, 0.27f, 0.22f));
            SetRect(brake.rectTransform, BottomRight, BottomRight, Center, new Vector2(-385f, 130f), new Vector2(180f, 170f));
            brake.gameObject.AddComponent<HoldButton>().Configure(HoldButton.Action.Frenar, brake);
            var brakeText = MakeText(brake.transform, "Texto", "FRENO", 36, TextAlignmentOptions.Center, Color.white);
            Stretch(brakeText.rectTransform);
            brakeText.raycastTarget = false;

            SetRef(controls, "arrowsRoot", arrows.gameObject);
            SetRef(controls, "wheelRoot", wheelRoot.gameObject);
            SetRef(controls, "joystickRoot", joyRoot.gameObject);
            SetRef(controls, "pedalsRoot", pedals.gameObject);

            // --- Botones de power-up (sobre los pedales) ---
            BuildAbilityButton(canvas, root, PowerUpType.Drs, "DRS", "AbilityDRS", "PANTALLA JUGABILIDAD/Boton_velocidad.png", new Vector2(-150f, 430f));
            BuildAbilityButton(canvas, root, PowerUpType.Pinchazo, "PINCHAZO", "AbilityPinchazo", "PANTALLA JUGABILIDAD/Boton_poder.png", new Vector2(-340f, 360f));
        }

        private static void BuildAbilityButton(Transform canvas, Transform parent, PowerUpType type, string label,
            string oldName, string spritePath, Vector2 pos)
        {
            // Desactivar el boton viejo (tenia el relleno de cooldown sobre la misma imagen).
            var old = canvas.Find(oldName);
            if (old != null) old.gameObject.SetActive(false);

            var img = MakeImage(parent, "PowerUp" + label, S(spritePath), Color.white);
            SetRect(img.rectTransform, BottomRight, BottomRight, Center, pos, new Vector2(160f, 160f));
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.disabledColor = Color.white; // el gris lo maneja AbilityButtonUI
            button.colors = colors;

            var fill = MakeImage(img.transform, "DuracionActiva", LoadSprite(GENERATED + "Circulo.png"), new Color(0.4f, 0.8f, 1f, 0.45f));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;
            Stretch(fill.rectTransform);

            var text = MakeText(img.transform, "Etiqueta", label, 24, TextAlignmentOptions.Center, Color.white);
            SetRect(text.rectTransform, Bottom, Bottom, Top, new Vector2(0f, -2f), new Vector2(220f, 34f));
            text.raycastTarget = false;
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(10, 20, 50, 255);

            var ui = img.gameObject.AddComponent<AbilityButtonUI>();
            ui.Configure(type, button, img, fill);
        }

        private static void BuildResults(Transform canvas)
        {
            var holder = MakeUI(canvas, PREFIX + "Resultados");
            Stretch(holder);
            var endUi = holder.gameObject.AddComponent<EndRaceUI>();

            var panel = MakeImage(holder, "Panel", S("PANTALLA4/Fondo_pantallas - copia.png"), Color.white);
            Stretch(panel.rectTransform);
            if (panel.sprite == null) panel.color = new Color(0.05f, 0.1f, 0.3f, 0.95f);

            Sprite box = S("PANTALLA4/Elemento_Espacio.png");

            var titleBg = MakeImage(panel.transform, "CajaTitulo", box, Color.white);
            SetRect(titleBg.rectTransform, Top, Top, Top, new Vector2(-250f, -70f), new Vector2(760f, 150f));
            var title = MakeText(titleBg.transform, "Titulo", "RESULTADOS", 72, TextAlignmentOptions.Center, Color.white);
            Stretch(title.rectTransform);
            title.enableAutoSizing = true;
            title.fontSizeMin = 30;
            title.fontSizeMax = 72;

            var subtitle = MakeText(panel.transform, "Subtitulo", "", 36, TextAlignmentOptions.Center, new Color(1f, 0.8f, 0.3f));
            SetRect(subtitle.rectTransform, Top, Top, Top, new Vector2(-250f, -235f), new Vector2(760f, 50f));

            var trophy = MakeImage(panel.transform, "Trofeo", S("PANTALLA6/Elemento_1.png"), Color.white);
            trophy.preserveAspect = true;
            SetRect(trophy.rectTransform, Right, Right, Center, new Vector2(-260f, 60f), new Vector2(460f, 440f));

            var list = MakeUI(panel.transform, "Lista");
            SetRect(list, Center, Center, Center, new Vector2(-250f, -60f), new Vector2(900f, 440f));
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var rowPrefab = CreateResultRowPrefab(box);

            var rematch = MakeButton(panel.transform, "BotonRevancha", box, "REVANCHA", 40);
            SetRect((RectTransform)rematch.transform, Bottom, Bottom, Center, new Vector2(-560f, 110f), new Vector2(420f, 120f));
            ((Image)rematch.targetGraphic).color = new Color(0.45f, 0.85f, 0.35f);
            var exit = MakeButton(panel.transform, "BotonSalir", box, "SALIR", 40);
            SetRect((RectTransform)exit.transform, Bottom, Bottom, Center, new Vector2(-80f, 110f), new Vector2(420f, 120f));

            var waiting = MakeText(panel.transform, "Espera", "", 28, TextAlignmentOptions.Center, Color.white);
            SetRect(waiting.rectTransform, Bottom, Bottom, Center, new Vector2(-320f, 200f), new Vector2(1000f, 40f));

            endUi.Configure(panel.gameObject, list, rowPrefab, title, subtitle, rematch, exit, waiting);
            panel.gameObject.SetActive(false);
        }

        private static GameObject CreateResultRowPrefab(Sprite box)
        {
            string path = PREFABS + "TG_FilaResultado.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var row = new GameObject("TG_FilaResultado", typeof(RectTransform));
            var rt = (RectTransform)row.transform;
            rt.sizeDelta = new Vector2(900f, 96f);
            var bg = row.AddComponent<Image>();
            bg.sprite = box;
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 96f;

            var nameText = MakeText(row.transform, "Nombre", "1. Piloto", 44, TextAlignmentOptions.Left, Color.white);
            SetRect(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(0.62f, 1f), Center, new Vector2(10f, 0f), new Vector2(-40f, -10f));
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 20;
            nameText.fontSizeMax = 44;
            var pointsText = MakeText(row.transform, "Puntos", "0 pts", 34, TextAlignmentOptions.Right, new Color(1f, 0.85f, 0.3f));
            SetRect(pointsText.rectTransform, new Vector2(0.55f, 0f), new Vector2(1f, 1f), Center, new Vector2(-20f, 0f), new Vector2(-20f, -10f));
            pointsText.enableAutoSizing = true;
            pointsText.fontSizeMin = 16;
            pointsText.fontSizeMax = 34;

            var prefab = PrefabUtility.SaveAsPrefabAsset(row, path);
            Object.DestroyImmediate(row);
            return prefab;
        }

        // =====================================================================
        // Circuitos 2 y 3 (provisionales)
        // =====================================================================

        private static void CreateExtraCircuit(string path, GameObject powerUpPrefab, string theme, Color trackTint, Color background)
        {
            bool isNew = !File.Exists(path);
            if (isNew)
            {
                if (!AssetDatabase.CopyAsset(GAME, path))
                {
                    Log.Add($"AVISO: no se pudo copiar Game.unity a {path}.");
                    return;
                }
                Log.Add($"{Path.GetFileNameWithoutExtension(path)} creado a partir del Circuito 1 (ambientacion: {theme}).");
            }
            else
            {
                // Ya existia: solo se actualiza la configuracion, no se toca el arte del equipo.
                SetupRaceScene(path, powerUpPrefab, null);
                return;
            }

            SetupRaceScene(path, powerUpPrefab, scene =>
            {
                // Ambientacion provisional para que se distingan hasta que llegue el arte final.
                var track = FindInScene<SpriteRenderer>("Pista");
                if (track != null) track.color = trackTint;
                var cam = FindInScene<Camera>("Main Camera");
                if (cam != null) cam.backgroundColor = background;

                // Aviso visible en el editor para el equipo de mapas.
                var note = new GameObject("LEEME_Mapas");
                note.transform.position = new Vector3(0f, 0f, 0f);
                note.AddComponent<TurboGridNote>().note =
                    "Circuito provisional: copia del Circuito 1 con otro color. Reemplazar el sprite de 'Pista', " +
                    "los muros ('Square'), los puntos de 'TrackWaypoints' y la parrilla 'StartGrid'.";
            });
        }

        // =====================================================================
        // Pantallas sin red: Inicio -> Instrucciones -> Sala (MainMenu)
        // =====================================================================

        private static void SetupMenuScreens()
        {
            SetupMenuScreen(INICIO, new (string, string, bool)[]
            {
                ("Boton Jugar", "Pantalla Intrucciones", false),
                ("Boton Salir", null, true),
            });
            SetupMenuScreen(INSTRUCCIONES, new (string, string, bool)[]
            {
                ("Boton Siguiente", "MainMenu", false),
                ("Boton Atras", "Pantalla Inicio", false),
            });
        }

        private static void SetupMenuScreen(string path, (string button, string scene, bool quit)[] links)
        {
            if (!File.Exists(path)) return;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            EnsureEventSystem();

            // Igual que en el lobby: el canvas debe escalar con la pantalla del celular.
            // Match = 1 (alto) porque los botones estan cerca de los bordes laterales.
            foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(800f, 600f);
                    scaler.matchWidthOrHeight = 1f;
                }
            }

            // La imagen guia del diseñador se veia en pantallas anchas: se desactiva.
            var guide = FindInScene<Transform>("Imagen Guia");
            if (guide != null) guide.gameObject.SetActive(false);

            foreach (var link in links)
            {
                var button = FindInScene<Button>(link.button);
                if (button == null) continue;
                var nav = button.GetComponent<SceneButton>();
                if (nav == null) nav = button.gameObject.AddComponent<SceneButton>();
                nav.Configure(link.scene, link.quit);

                // "Atras" usa la misma flecha que "Siguiente": se voltea.
                if (link.button == "Boton Atras")
                {
                    var s = button.transform.localScale;
                    button.transform.localScale = new Vector3(-Mathf.Abs(s.x), s.y, s.z);
                }
            }

            SaveScene(scene);
            Log.Add($"{Path.GetFileNameWithoutExtension(path)}: botones conectados, escala para celular.");
        }

        private static void UpdateBuildSettings()
        {
            var wanted = new[] { INICIO, INSTRUCCIONES, MAIN_MENU, GAME, CIRCUITO2, CIRCUITO3 }.Where(File.Exists).ToList();
            var scenes = wanted.Select(p => new EditorBuildSettingsScene(p, true)).ToList();
            // Conservar escenas adicionales que el equipo haya agregado (desactivadas no se tocan).
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (!wanted.Contains(s.path)) scenes.Add(s);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            Log.Add("Build Settings: Inicio, Instrucciones, MainMenu, Game, Circuito2, Circuito3.");
        }

        // =====================================================================
        // Helpers de escena
        // =====================================================================

        private static void SaveScene(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureEventSystem()
        {
            var es = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (es == null)
            {
                es = new GameObject("EventSystem").AddComponent<EventSystem>();
                Log.Add($"{SceneManager.GetActiveScene().name}: faltaba EventSystem (sin el no hay clics). Agregado.");
            }
            if (es.GetComponent<InputSystemUIInputModule>() == null)
            {
                var legacy = es.GetComponent<StandaloneInputModule>();
                if (legacy != null) Object.DestroyImmediate(legacy);
                es.gameObject.AddComponent<InputSystemUIInputModule>();
            }
            es.gameObject.SetActive(true);
        }

        private static T FindInScene<T>(string name) where T : Component
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name)
                    {
                        var c = t.GetComponent<T>();
                        if (c != null) return c;
                    }
                }
            }
            return null;
        }

        private static T FindComp<T>(string name) where T : Component => FindInScene<T>(name);

        private static void RemoveGenerated(Transform parent)
        {
            var toDelete = new List<GameObject>();
            foreach (Transform child in parent)
                if (child.name.StartsWith(PREFIX)) toDelete.Add(child.gameObject);
            foreach (var go in toDelete) Object.DestroyImmediate(go);
        }

        // ---- Referencias serializadas ----

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[TurboGridSetup] {target.GetType().Name} no tiene el campo '{field}'.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignIfEmpty(Object target, string field, Object value)
        {
            if (value == null) return;
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null || prop.objectReferenceValue != null) return;
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, IList<Object> values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null || !prop.isArray) return;
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, Transform[] values) =>
            SetArray(target, field, values.Cast<Object>().ToList());

        // ---- Construccion de UI ----

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 Top = new Vector2(0.5f, 1f);
        private static readonly Vector2 Bottom = new Vector2(0.5f, 0f);
        private static readonly Vector2 Right = new Vector2(1f, 0.5f);
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 TopRight = new Vector2(1f, 1f);
        private static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

        private static RectTransform MakeUI(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Center;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static Image MakeImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var rt = MakeUI(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, TextAlignmentOptions align, Color color)
        {
            var rt = MakeUI(parent, name);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        private static Button MakeButton(Transform parent, string name, Sprite sprite, string label, float fontSize)
        {
            var img = MakeImage(parent, name, sprite, Color.white);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            if (!string.IsNullOrEmpty(label))
            {
                var text = MakeText(img.transform, "Texto", label, fontSize, TextAlignmentOptions.Center, Color.white);
                Stretch(text.rectTransform);
                text.enableAutoSizing = true;
                text.fontSizeMin = Mathf.Max(8f, fontSize * 0.5f);
                text.fontSizeMax = fontSize;
                text.margin = new Vector4(8f, 4f, 8f, 4f);
            }
            return button;
        }

        private static TMP_InputField MakeInputField(Transform parent, string name, Sprite sprite, string placeholder, float fontSize)
        {
            var bg = MakeImage(parent, name, sprite, Color.white);
            var input = bg.gameObject.AddComponent<TMP_InputField>();

            var area = MakeUI(bg.transform, "Area");
            Stretch(area);
            area.offsetMin = new Vector2(12f, 4f);
            area.offsetMax = new Vector2(-12f, -4f);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = MakeText(area, "Placeholder", placeholder, fontSize, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.45f));
            Stretch(ph.rectTransform);
            var text = MakeText(area, "Texto", "", fontSize, TextAlignmentOptions.Center, Color.white);
            Stretch(text.rectTransform);

            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = bg;
            input.fontAsset = _font;
            input.pointSize = fontSize;
            return input;
        }
    }
}
