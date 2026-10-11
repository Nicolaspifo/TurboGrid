using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TurboGrid
{
    /// <summary>
    /// Sistema de audio del juego. Se crea SOLO al arrancar (no hay que ponerlo en ninguna escena).
    ///
    ///  - Musica: menu en las pantallas de menu, carrera en los circuitos, jingle de victoria
    ///    al terminar. Cambia con fundido cruzado y la musica de fondo es un loop perfecto.
    ///  - Efectos: gasolina, power-ups, pinchazo, choque, cuenta regresiva, botones, sala...
    ///  - Los archivos estan en Assets/Resources/Audio/SFX y /Musica (generados con
    ///    Tools/generar_audio.py). Para cambiar un sonido basta con reemplazar el archivo
    ///    manteniendo el nombre (sirve .wav, .ogg o .mp3).
    ///  - El motor y el derrape de cada auto son procedurales (CarAudio).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public enum SoundMode { Todo = 0, SoloEfectos = 1, Silencio = 2 }

        private const string KEY_MODE = "tg_sound_mode";
        private const int SFX_VOICES = 10;
        private const float MUSIC_VOLUME = 0.45f;

        private static AudioManager _instance;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly List<AudioSource> _sfx = new List<AudioSource>();
        private AudioSource _musicA, _musicB;
        private AudioSource _currentMusic;
        private string _currentMusicName;
        private Coroutine _musicRoutine;

        // Estado para detectar eventos del juego
        private RaceManager.RaceState _lastRaceState = RaceManager.RaceState.WaitingForPlayers;
        private RaceManager _trackedRace;
        private int _lastCountdownNumber = -1;
        private int _lastPlayerCount;
        private int _lastReadyCount;

        public static System.Action OnModeChanged;

        // ---------------------------------------------------------------------
        // Arranque automatico
        // ---------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureExists();
        }

        private static AudioManager EnsureExists()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("AudioManager");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AudioManager>();
            return _instance;
        }

        public static SoundMode Mode
        {
            get => (SoundMode)Mathf.Clamp(PlayerPrefs.GetInt(KEY_MODE, 0), 0, 2);
            set
            {
                PlayerPrefs.SetInt(KEY_MODE, (int)value);
                PlayerPrefs.Save();
                if (_instance != null) _instance.ApplyMode();
                OnModeChanged?.Invoke();
            }
        }

        public static string ModeDisplayName(SoundMode mode)
        {
            switch (mode)
            {
                case SoundMode.SoloEfectos: return "Solo efectos";
                case SoundMode.Silencio: return "Silencio";
                default: return "Musica y efectos";
            }
        }

        public static bool SfxEnabled => Mode != SoundMode.Silencio;
        public static bool MusicEnabled => Mode == SoundMode.Todo;

        private void Awake()
        {
            _musicA = CreateSource("MusicaA");
            _musicB = CreateSource("MusicaB");
            for (int i = 0; i < SFX_VOICES; i++) _sfx.Add(CreateSource("Efecto" + i));

            SceneManager.sceneLoaded += HandleSceneLoaded;
            PlayerNetworkData.OnPowerUpFeed += HandlePowerUpFeed;
            PlayerNetworkData.OnPinchazoFeed += HandlePinchazoFeed;
            SessionState.OnPlayersChanged += HandleSessionChanged;

            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            PlayerNetworkData.OnPowerUpFeed -= HandlePowerUpFeed;
            PlayerNetworkData.OnPinchazoFeed -= HandlePinchazoFeed;
            SessionState.OnPlayersChanged -= HandleSessionChanged;
            if (_instance == this) _instance = null;
        }

        private AudioSource CreateSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f; // juego 2D: el paneo se calcula a mano
            return src;
        }

        // ---------------------------------------------------------------------
        // API publica
        // ---------------------------------------------------------------------

        /// <summary>Reproduce un efecto (sin posicion).</summary>
        public static void Play(string sfxName, float volume = 1f, float pitchJitter = 0.04f)
        {
            if (!SfxEnabled) return;
            EnsureExists().PlayInternal(sfxName, volume, pitchJitter, 0f);
        }

        /// <summary>
        /// Reproduce un efecto en un punto del mundo: se escucha mas fuerte cerca de la
        /// camara y se panea a izquierda/derecha.
        /// </summary>
        public static void PlayAt(string sfxName, Vector3 worldPosition, float volume = 1f, float maxDistance = 650f)
        {
            if (!SfxEnabled) return;
            var cam = Camera.main;
            float pan = 0f;
            if (cam != null)
            {
                Vector2 delta = worldPosition - cam.transform.position;
                float dist = delta.magnitude;
                if (dist > maxDistance) return;
                volume *= 1f - Mathf.Clamp01(dist / maxDistance) * 0.85f;
                pan = Mathf.Clamp(delta.x / (cam.orthographicSize * cam.aspect), -1f, 1f) * 0.7f;
            }
            EnsureExists().PlayInternal(sfxName, volume, 0.05f, pan);
        }

        /// <summary>Cambia la musica con fundido cruzado.</summary>
        public static void PlayMusic(string musicName, bool loop = true, float fade = 1f, string next = null)
        {
            EnsureExists().PlayMusicInternal(musicName, loop, fade, next);
        }

        /// <summary>Detiene la musica con un fundido.</summary>
        public static void StopMusic(float fade = 0.5f)
        {
            var m = EnsureExists();
            if (m._musicRoutine != null) m.StopCoroutine(m._musicRoutine);
            m._musicRoutine = m.StartCoroutine(m.FadeOutMusic(fade));
        }

        private IEnumerator FadeOutMusic(float fade)
        {
            var src = _currentMusic;
            _currentMusicName = null;
            if (src == null) yield break;
            float start = src.volume;
            for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
            {
                src.volume = Mathf.Lerp(start, 0f, t / fade);
                yield return null;
            }
            src.Stop();
        }

        /// <summary>Nombres de todos los efectos y musicas del juego (para el panel de prueba).</summary>
        public static readonly string[] AllSfx =
        {
            "gasolina", "recoger_drs", "activar_drs", "recoger_pinchazo", "lanzar_pinchazo", "pinchado",
            "choque", "frenazo", "cuenta_beep", "cuenta_go", "boton", "jugador_listo", "jugador_entra", "aplausos"
        };

        public static readonly string[] AllMusic = { "musica_menu", "musica_carrera", "musica_victoria" };

        public static AudioClip GetClip(string clipName)
        {
            return EnsureExists().Load(clipName);
        }

        // ---------------------------------------------------------------------

        private AudioClip Load(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            if (_clips.TryGetValue(clipName, out var cached)) return cached;
            var clip = Resources.Load<AudioClip>("Audio/SFX/" + clipName);
            if (clip == null) clip = Resources.Load<AudioClip>("Audio/Musica/" + clipName);
            if (clip == null) Debug.LogWarning($"[AudioManager] No se encontro el sonido '{clipName}' en Resources/Audio.");
            _clips[clipName] = clip;
            return clip;
        }

        private void PlayInternal(string sfxName, float volume, float pitchJitter, float pan)
        {
            var clip = Load(sfxName);
            if (clip == null || volume <= 0.01f) return;

            // Buscar una voz libre; si no hay, reemplazar la que lleva mas tiempo sonando.
            AudioSource voice = null;
            float oldest = -1f;
            foreach (var s in _sfx)
            {
                if (!s.isPlaying) { voice = s; break; }
                if (s.time > oldest) { oldest = s.time; voice = s; }
            }
            voice.clip = clip;
            voice.volume = Mathf.Clamp01(volume);
            voice.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            voice.panStereo = pan;
            voice.loop = false;
            voice.Play();
        }

        private void PlayMusicInternal(string musicName, bool loop, float fade, string next)
        {
            if (_currentMusicName == musicName && _currentMusic != null && _currentMusic.isPlaying && next == null) return;
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossFade(musicName, loop, fade, next));
        }

        private IEnumerator CrossFade(string musicName, bool loop, float fade, string next)
        {
            var clip = Load(musicName);
            var from = _currentMusic;
            var to = from == _musicA ? _musicB : _musicA;
            _currentMusic = to;
            _currentMusicName = musicName;

            if (clip != null)
            {
                to.clip = clip;
                to.loop = loop;
                to.volume = 0f;
                to.Play();
            }

            float target = MusicEnabled ? MUSIC_VOLUME : 0f;
            float fromStart = from != null ? from.volume : 0f;
            for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
            {
                float k = t / fade;
                if (from != null) from.volume = Mathf.Lerp(fromStart, 0f, k);
                if (clip != null) to.volume = Mathf.Lerp(0f, target, k);
                yield return null;
            }
            if (from != null) { from.volume = 0f; from.Stop(); }
            if (clip != null) to.volume = target;

            // Jingle sin loop seguido de otra musica (ej. victoria -> menu).
            if (!loop && next != null && clip != null)
            {
                while (to.isPlaying) yield return null;
                _musicRoutine = StartCoroutine(CrossFade(next, true, 1.5f, null));
            }
        }

        private void ApplyMode()
        {
            if (_currentMusic != null) _currentMusic.volume = MusicEnabled ? MUSIC_VOLUME : 0f;
            if (!SfxEnabled)
                foreach (var s in _sfx) s.Stop();
        }

        // ---------------------------------------------------------------------
        // Escenas: musica + sonido de botones
        // ---------------------------------------------------------------------

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;

            bool race = MapCatalog.IsRaceScene(scene.name);
            PlayMusicInternal(race ? "musica_carrera" : "musica_menu", true, 1.2f, null);

            _trackedRace = null;
            _lastRaceState = RaceManager.RaceState.WaitingForPlayers;
            _lastCountdownNumber = -1;
            _lastPlayerCount = 0;
            _lastReadyCount = 0;

            // Todos los botones de la escena hacen "clic" (tambien los que se activan despues).
            foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (button.GetComponent<ButtonSound>() == null)
                    button.gameObject.AddComponent<ButtonSound>();
            }
        }

        // ---------------------------------------------------------------------
        // Eventos de carrera (cuenta regresiva, largada, fin)
        // ---------------------------------------------------------------------

        private void Update()
        {
            var race = RaceManager.Instance;
            if (race == null || !race.IsSpawned) return;

            if (race != _trackedRace)
            {
                _trackedRace = race;
                _lastRaceState = race.CurrentState.Value;
            }

            var state = race.CurrentState.Value;

            if (state == RaceManager.RaceState.Countdown)
            {
                int number = Mathf.CeilToInt(race.CountdownRemaining);
                if (number != _lastCountdownNumber && number > 0 && number <= 3)
                    Play("cuenta_beep", 0.8f, 0f);
                _lastCountdownNumber = number;
            }

            if (state != _lastRaceState)
            {
                if (state == RaceManager.RaceState.Racing && _lastRaceState == RaceManager.RaceState.Countdown)
                {
                    Play("cuenta_go", 0.9f, 0f);
                }
                else if (state == RaceManager.RaceState.Countdown && _lastRaceState == RaceManager.RaceState.Finished)
                {
                    // Revancha
                    PlayMusicInternal("musica_carrera", true, 1f, null);
                }
                else if (state == RaceManager.RaceState.Finished)
                {
                    PlayMusicInternal("musica_victoria", false, 0.4f, "musica_menu");
                    Play("aplausos", 0.7f, 0f);
                }
                _lastRaceState = state;
                _lastCountdownNumber = -1;
            }
        }

        // ---------------------------------------------------------------------
        // Power-ups y pinchazos (llegan por red a todos)
        // ---------------------------------------------------------------------

        private static bool IsLocal(ulong clientId) =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId;

        private void HandlePowerUpFeed(PlayerNetworkData.PowerUpFeed feed, ulong clientId, string playerName)
        {
            bool me = IsLocal(clientId);
            switch (feed)
            {
                case PlayerNetworkData.PowerUpFeed.RecogioDrs: Play("recoger_drs", me ? 0.9f : 0.35f); break;
                case PlayerNetworkData.PowerUpFeed.RecogioPinchazo: Play("recoger_pinchazo", me ? 0.9f : 0.45f); break;
                case PlayerNetworkData.PowerUpFeed.ActivoDrs: Play("activar_drs", me ? 1f : 0.45f); break;
            }
        }

        private void HandlePinchazoFeed(string attacker, string victim)
        {
            var local = PlayerNetworkData.Local;
            if (local != null && local.DisplayName == victim)
                Play("pinchado", 1f);
            else
                Play("lanzar_pinchazo", local != null && local.DisplayName == attacker ? 0.9f : 0.5f);
        }

        // ---------------------------------------------------------------------
        // Sala: alguien entra / alguien marca LISTO
        // ---------------------------------------------------------------------

        private void HandleSessionChanged()
        {
            var session = SessionState.Instance;
            if (session == null || session.Players == null) return;
            if (MapCatalog.IsRaceScene(SceneManager.GetActiveScene().name)) return;

            int count = 0, ready = 0;
            foreach (var p in session.Players)
            {
                if (p.IsConnecting) continue;
                count++;
                if (p.IsReady) ready++;
            }

            if (count > _lastPlayerCount && _lastPlayerCount > 0) Play("jugador_entra", 0.8f, 0f);
            if (ready > _lastReadyCount) Play("jugador_listo", 0.8f, 0f);

            _lastPlayerCount = count;
            _lastReadyCount = ready;
        }
    }
}
