using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Motor V12 PROCEDURAL de cada auto (se sintetiza en tiempo real, sin archivos).
    ///
    /// Como suena un V12: 12 cilindros en 4 tiempos = 6 explosiones por vuelta del
    /// ciguenal. El tono dominante es el "orden 6" (6 x vueltas por segundo): de ~140 Hz
    /// en ralenti (1400 rpm) a ~900 Hz a fondo (9000 rpm), el "grito" agudo tipico de
    /// Ferrari/Lamborghini. Se construye con:
    ///  - Armonicos de ordenes 3, 6, 9, 12, 18 y 24 del ciguenal (el 6 manda).
    ///  - Carraspeo del escape: ruido que pulsa con cada explosion.
    ///  - Resonancia que se abre con las rpm (el motor "grita" arriba).
    ///  - Mas brillo y volumen al acelerar; las rpm suben parejo con la velocidad.
    ///  - DRS: +6% de rpm, sonido mas abierto y soplido de aire del turbo.
    /// Los autos remotos suenan mas bajo y segun su distancia a la camara.
    /// Lo agrega CarVisuals automaticamente.
    /// </summary>
    public class CarAudio : MonoBehaviour
    {
        private const float IDLE_RPM = 1400f;
        private const float MAX_RPM = 9000f;
        private const int GEARS = 6;

        private PlayerNetworkData _data;
        private CarController _car;
        private AudioSource _engineSource;
        private int _sampleRate;

        // Parametros que lee el hilo de audio (se escriben desde Update).
        private volatile float _rpm = IDLE_RPM;
        private volatile float _engineGain;
        private volatile float _throttle;
        private volatile float _drsAmount;

        // Estado del hilo de audio
        private double _crankPhase;          // 0..1 por vuelta del ciguenal
        private float _curRpm = IDLE_RPM, _curGain, _curThrottle, _curDrs;
        private float _lp1, _lp2, _exA, _airA, _airB;
        private float _bpLow, _bpBand;       // filtro resonante (state-variable)
        private uint _rand = 0x9E3779B9u;

        // Estado del hilo principal
        private Vector3 _lastPos;
        private float _speedEstimate;
        private float _lastBrakeSound = -10f;
        private bool _wasBraking;

        // Modo prueba (panel "Probar sonidos"): el motor suena sin auto.
        private bool _preview;
        private float _previewSpeed01, _previewThrottle;
        private bool _previewDrs;

        /// <summary>Crea un motor de prueba que no depende de un auto.</summary>
        public static CarAudio CreatePreview()
        {
            var go = new GameObject("PruebaMotor");
            var audio = go.AddComponent<CarAudio>();
            audio._preview = true;
            return audio;
        }

        /// <summary>Controla el motor de prueba (velocidad 0..1.15, acelerador 0..1).</summary>
        public void SetPreview(float speed01, float throttle, bool drs)
        {
            _previewSpeed01 = speed01;
            _previewThrottle = throttle;
            _previewDrs = drs;
        }

        private void Awake()
        {
            _data = GetComponent<PlayerNetworkData>();
            _car = GetComponent<CarController>();
            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

            var go = new GameObject("MotorV12");
            go.transform.SetParent(transform, false);
            _engineSource = go.AddComponent<AudioSource>();
            _engineSource.playOnAwake = false;
            _engineSource.spatialBlend = 0f;
            _engineSource.loop = true;
            // Clip "infinito": Unity pide muestras al callback a medida que las necesita.
            _engineSource.clip = AudioClip.Create("MotorV12", _sampleRate, 1, _sampleRate, true, OnEngineRead);
            _engineSource.Play();

            _lastPos = transform.position;
        }

        private void OnDestroy()
        {
            if (_engineSource != null) _engineSource.Stop();
        }

        // ---------------------------------------------------------------------
        // Hilo principal: calcula que debe sonar
        // ---------------------------------------------------------------------

        /// <summary>RPM segun la velocidad: suben parejo con una ondulacion leve por marcha.</summary>
        private static float RpmFor(float speed01, bool drs)
        {
            float s = Mathf.Clamp(speed01, 0f, 1.2f);
            float gearPos = Mathf.Min(s, 1f) * GEARS;
            float inGear = gearPos - Mathf.Floor(gearPos);
            float rpm = IDLE_RPM + (MAX_RPM - IDLE_RPM) * Mathf.Pow(Mathf.Min(s, 1f), 0.8f) + (inGear - 0.5f) * 180f;
            if (s > 1f) rpm += (s - 1f) * 2500f; // DRS por encima del tope normal
            if (drs) rpm *= 1.06f;
            return Mathf.Clamp(rpm, IDLE_RPM * 0.9f, MAX_RPM * 1.12f);
        }

        private void Update()
        {
            if (_preview)
            {
                bool on = AudioManager.SfxEnabled;
                _rpm = RpmFor(_previewSpeed01, _previewDrs);
                _throttle = _previewThrottle;
                _engineGain = on ? 0.3f * (0.6f + 0.4f * Mathf.Max(_previewThrottle, _previewSpeed01 * 0.5f)) : 0f;
                _drsAmount = on && _previewDrs ? 1f : 0f;
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            // Velocidad (sirve igual para autos propios y remotos).
            Vector3 pos = transform.position;
            float measured = (pos - _lastPos).magnitude / dt;
            _speedEstimate = Mathf.Lerp(_speedEstimate, measured, 1f - Mathf.Exp(-dt * 8f));
            _lastPos = pos;

            bool isLocal = _data != null && _data.IsSpawned && _data.IsOwner;
            float maxSpeed = _car != null ? Mathf.Max(1f, _car.MaxSpeed) : 170f;
            float speed = isLocal && _car != null ? Mathf.Abs(_car.CurrentSpeed) : _speedEstimate;
            float speed01 = speed / maxSpeed; // puede pasar de 1 con DRS
            bool drs = _data != null && _data.IsSpawned && _data.IsDrsActive.Value;

            bool racingOrCountdown = RaceManager.Instance != null && RaceManager.Instance.IsSpawned &&
                                     RaceManager.Instance.CurrentState.Value != RaceManager.RaceState.WaitingForPlayers;
            bool finished = RaceManager.Instance != null && RaceManager.Instance.IsFinished;

            float throttle = isLocal ? Mathf.Max(0f, CarInput.Throttle) : Mathf.Clamp01(speed01);
            if (isLocal && LocalPlayerSettings.Scheme == ControlScheme.Joystick && !CarInput.KeyboardActive)
                throttle = Mathf.Clamp01(CarInput.JoystickDirection.magnitude);

            _rpm = RpmFor(speed01, drs);
            _throttle = throttle;

            float baseGain = isLocal ? 0.3f : 0.18f;
            float gain = baseGain * (0.6f + 0.4f * Mathf.Max(throttle, Mathf.Clamp01(speed01) * 0.5f));
            if (!racingOrCountdown) gain *= 0.6f;  // ralenti antes de la largada
            if (finished) gain *= 0.35f;
            if (!AudioManager.SfxEnabled) gain = 0f;

            float distFactor = isLocal ? 1f : DistanceFactor(pos, 600f);
            _engineGain = gain * distFactor;
            _drsAmount = drs && AudioManager.SfxEnabled ? 1f : 0f;

            // Frenazo (efecto corto) al empezar a frenar fuerte con el auto propio
            bool braking = isLocal && CarInput.Throttle < -0.1f && _car != null && _car.CurrentSpeed > maxSpeed * 0.45f;
            if (braking && !_wasBraking && Time.time - _lastBrakeSound > 1.2f && !finished)
            {
                AudioManager.Play("frenazo", 0.4f);
                _lastBrakeSound = Time.time;
            }
            _wasBraking = braking;

            // Paneo segun la posicion en pantalla
            var cam = Camera.main;
            if (cam != null && !isLocal)
            {
                _engineSource.panStereo = Mathf.Clamp((pos.x - cam.transform.position.x) /
                                                      (cam.orthographicSize * cam.aspect), -1f, 1f) * 0.7f;
            }
        }

        private static float DistanceFactor(Vector3 pos, float maxDistance)
        {
            var cam = Camera.main;
            if (cam == null) return 0.5f;
            float d = Vector2.Distance(pos, cam.transform.position);
            return Mathf.Clamp01(1f - d / maxDistance);
        }

        // ---------------------------------------------------------------------
        // Hilo de audio: sintesis del V12
        // ---------------------------------------------------------------------

        private float NextNoise()
        {
            // xorshift: ruido rapido y sin reservar memoria (seguro en el hilo de audio)
            _rand ^= _rand << 13;
            _rand ^= _rand >> 17;
            _rand ^= _rand << 5;
            return (_rand / (float)uint.MaxValue) * 2f - 1f;
        }

        private void OnEngineRead(float[] data)
        {
            float targetRpm = _rpm;
            float targetGain = _engineGain;
            float targetThrottle = _throttle;
            float targetDrs = _drsAmount;
            float sr = _sampleRate;

            float rpmSmooth = 1f - Mathf.Exp(-1f / (sr * 0.12f));     // subida pareja de rpm
            float gainSmooth = 1f - Mathf.Exp(-1f / (sr * 0.08f));
            float thrSmooth = 1f - Mathf.Exp(-1f / (sr * 0.06f));
            float drsSmooth = 1f - Mathf.Exp(-1f / (sr * 0.25f));
            float exhaustLp = 1f - Mathf.Exp(-2f * Mathf.PI * 2800f / sr);
            float airHp = 1f - Mathf.Exp(-2f * Mathf.PI * 2200f / sr);
            float airLp = 1f - Mathf.Exp(-2f * Mathf.PI * 6500f / sr);
            const float twoPi = 2f * Mathf.PI;

            for (int i = 0; i < data.Length; i++)
            {
                _curRpm += (targetRpm - _curRpm) * rpmSmooth;
                _curGain += (targetGain - _curGain) * gainSmooth;
                _curThrottle += (targetThrottle - _curThrottle) * thrSmooth;
                _curDrs += (targetDrs - _curDrs) * drsSmooth;

                float crankHz = _curRpm / 60f;
                _crankPhase += crankHz / sr;
                if (_crankPhase >= 1.0) _crankPhase -= 1.0;
                float th = (float)(_crankPhase * twoPi);

                // Armonicos por orden del ciguenal (el 6 = explosiones de un V12)
                float bright = 0.45f + 0.55f * Mathf.Max(_curThrottle, _curDrs);
                float fire = Mathf.Sin(6f * th);
                float v12 =
                    0.14f * Mathf.Sin(3f * th) +
                    1.00f * fire +
                    0.28f * Mathf.Sin(9f * th) +
                    0.55f * bright * Mathf.Sin(12f * th) +
                    0.30f * bright * Mathf.Sin(18f * th) +
                    0.14f * bright * Mathf.Sin(24f * th);

                // Carraspeo del escape: ruido que pulsa con cada explosion
                float pulse = 0.5f + 0.5f * fire;
                _exA += (NextNoise() - _exA) * exhaustLp;
                float exhaust = _exA * pulse * pulse * (0.35f + 0.45f * _curThrottle);

                float raw = v12 * 0.55f + exhaust;

                // Resonancia que sube con las rpm (el "grito" del V12)
                float formantHz = 700f + _curRpm * 0.12f + 600f * _curDrs;
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(formantHz, sr * 0.2f) / sr);
                const float q = 0.55f;
                float high = raw - _bpLow - q * _bpBand;
                _bpBand += f * high;
                _bpLow += f * _bpBand;
                float scream = _bpBand * (0.25f + 0.35f * _curThrottle);

                // Saturacion suave + paso-bajo final (quita la aspereza digital)
                float mixed = raw + scream;
                mixed = mixed / (1f + Mathf.Abs(mixed) * 0.8f);
                float cutoff = 4200f + 2500f * _curDrs;
                float lpCoef = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sr);
                _lp1 += (mixed - _lp1) * lpCoef;
                _lp2 += (_lp1 - _lp2) * lpCoef;

                // DRS: soplido de aire del turbo (ruido pasa-banda), sin tono fijo
                float n = NextNoise();
                _airA += (n - _airA) * airHp;
                _airB += ((n - _airA) - _airB) * airLp;
                float air = _airB * 0.45f * _curDrs;

                data[i] = (_lp2 * 1.4f + air) * _curGain;
            }
        }
    }
}
