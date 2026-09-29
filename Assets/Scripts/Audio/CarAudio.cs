using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Audio PROCEDURAL de cada auto (se sintetiza en tiempo real, sin archivos):
    ///  - Motor: dos dientes de sierra levemente desafinados + sub-grave + "retumbe" de
    ///    cilindros. El tono sube PAREJO con la velocidad (solo una ondulacion leve al
    ///    cambiar de marcha) y suena mas fuerte al acelerar.
    ///  - DRS: el motor se abre (mas brillante y un poco mas agudo) y se suma el soplido
    ///    de aire del turbo. Sin pitidos fijos.
    ///  - Derrape: chillido de llanta con armonicos que tiembla y cambia de tono
    ///    (efecto "stick-slip") + roce de asfalto.
    ///  - Frenazo: efecto corto al frenar fuerte (solo el auto propio).
    /// Los autos remotos suenan mas bajo y segun su distancia a la camara.
    /// Lo agrega CarVisuals automaticamente.
    /// </summary>
    public class CarAudio : MonoBehaviour
    {
        private const int GEARS = 4;

        private PlayerNetworkData _data;
        private CarController _car;
        private AudioSource _engineSource;
        private AudioSource _skidSource;
        private int _sampleRate;

        // Afinacion del motor (Hz): ralenti -> velocidad maxima.
        private const float IDLE_FREQ = 78f;
        private const float TOP_FREQ = 200f;

        // Parametros que lee el hilo de audio (se escriben desde Update).
        private volatile float _engineFreq = IDLE_FREQ;
        private volatile float _engineGain;
        private volatile float _drsAmount;   // 0..1
        private volatile float _skidGain;
        private volatile float _skidPitch = 1150f;

        // Estado del hilo de audio
        private double _phase, _phase2, _subPhase, _squealPhase;
        private float _curFreq = IDLE_FREQ, _curGain, _curDrs, _curSkid, _curSkidPitch = 1150f;
        private float _lp1, _lp2, _nlp, _airA, _airB;
        private float _snA, _snB, _wander, _flutter;
        private uint _rand = 0x9E3779B9u;

        // Estado del hilo principal
        private Vector3 _lastPos;
        private float _lastAngle;
        private float _speedEstimate;
        private float _turnRate;
        private float _lastBrakeSound = -10f;
        private bool _wasBraking;

        // Modo prueba (panel "Probar sonidos"): el motor suena sin auto.
        private bool _preview;
        private float _previewSpeed01, _previewThrottle, _previewSkid;
        private bool _previewDrs;

        /// <summary>Crea un motor de prueba que no depende de un auto.</summary>
        public static CarAudio CreatePreview()
        {
            var go = new GameObject("PruebaMotor");
            var audio = go.AddComponent<CarAudio>();
            audio._preview = true;
            return audio;
        }

        /// <summary>Controla el motor de prueba (velocidad 0..1, acelerador 0..1, derrape 0..1).</summary>
        public void SetPreview(float speed01, float throttle, float skid, bool drs)
        {
            _previewSpeed01 = speed01;
            _previewThrottle = throttle;
            _previewSkid = skid;
            _previewDrs = drs;
        }

        private void Awake()
        {
            _data = GetComponent<PlayerNetworkData>();
            _car = GetComponent<CarController>();
            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;

            _engineSource = CreateStreamSource("Motor", OnEngineRead);
            _skidSource = CreateStreamSource("Derrape", OnSkidRead);

            _lastPos = transform.position;
            _lastAngle = transform.eulerAngles.z;
        }

        private AudioSource CreateStreamSource(string sourceName, AudioClip.PCMReaderCallback reader)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = true;
            src.volume = 1f;
            // Clip "infinito": Unity pide muestras al callback a medida que las necesita.
            src.clip = AudioClip.Create(sourceName, _sampleRate, 1, _sampleRate, true, reader);
            src.Play();
            return src;
        }

        private void OnDestroy()
        {
            if (_engineSource != null) _engineSource.Stop();
            if (_skidSource != null) _skidSource.Stop();
        }

        // ---------------------------------------------------------------------
        // Hilo principal: calcula que debe sonar
        // ---------------------------------------------------------------------

        private void Update()
        {
            if (_preview)
            {
                UpdatePreview();
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            // Velocidad y giro (sirve igual para autos propios y remotos).
            Vector3 pos = transform.position;
            float angle = transform.eulerAngles.z;
            float measured = (pos - _lastPos).magnitude / dt;
            _speedEstimate = Mathf.Lerp(_speedEstimate, measured, 1f - Mathf.Exp(-dt * 8f));
            float turn = Mathf.Abs(Mathf.DeltaAngle(_lastAngle, angle)) / dt;
            _turnRate = Mathf.Lerp(_turnRate, turn, 1f - Mathf.Exp(-dt * 10f));
            _lastPos = pos;
            _lastAngle = angle;

            bool isLocal = _data != null && _data.IsSpawned && _data.IsOwner;
            float maxSpeed = _car != null ? Mathf.Max(1f, _car.MaxSpeed) : 170f;
            float speed = isLocal && _car != null ? Mathf.Abs(_car.CurrentSpeed) : _speedEstimate;
            float speed01 = Mathf.Clamp01(speed / maxSpeed);
            bool drs = _data != null && _data.IsSpawned && _data.IsDrsActive.Value;
            if (drs) speed01 = Mathf.Clamp01(speed / (maxSpeed * GameConstants.DRS_SPEED_MULTIPLIER)) * 1.15f;

            bool racingOrCountdown = RaceManager.Instance != null && RaceManager.Instance.IsSpawned &&
                                     RaceManager.Instance.CurrentState.Value != RaceManager.RaceState.WaitingForPlayers;
            bool finished = RaceManager.Instance != null && RaceManager.Instance.IsFinished;

            float throttle = isLocal ? Mathf.Max(0f, CarInput.Throttle) : speed01;
            if (isLocal && LocalPlayerSettings.Scheme == ControlScheme.Joystick && !CarInput.KeyboardActive)
                throttle = Mathf.Clamp01(CarInput.JoystickDirection.magnitude);

            _engineFreq = EngineFrequency(speed01, drs);

            float baseGain = isLocal ? 0.32f : 0.2f;
            float load = 0.55f + 0.45f * Mathf.Max(throttle, speed01 * 0.6f);
            float gain = baseGain * load;
            if (!racingOrCountdown) gain *= 0.5f;  // ralenti antes de la largada
            if (finished) gain *= 0.35f;
            if (!AudioManager.SfxEnabled) gain = 0f;

            // Atenuacion por distancia para autos remotos.
            float distFactor = isLocal ? 1f : DistanceFactor(pos, 600f);
            _engineGain = gain * distFactor;
            _drsAmount = drs && AudioManager.SfxEnabled ? 1f : 0f;

            // --- Derrape: giro fuerte a alta velocidad, o freno fuerte ---
            float turnNorm = Mathf.Clamp01(_turnRate / GameConstants.CAR_TURN_SPEED);
            float skid = Mathf.Clamp01((turnNorm * speed01 - 0.42f) * 3.2f);
            bool braking = isLocal && CarInput.Throttle < -0.1f && _car != null && _car.CurrentSpeed > maxSpeed * 0.45f;
            if (braking) skid = Mathf.Max(skid, 0.8f * speed01);
            if (!AudioManager.SfxEnabled || finished) skid = 0f;
            _skidGain = skid * (isLocal ? 0.26f : 0.16f) * distFactor;
            _skidPitch = 1050f + 320f * speed01;

            // Frenazo (efecto corto) al empezar a frenar fuerte
            if (braking && !_wasBraking && Time.time - _lastBrakeSound > 1.2f)
            {
                AudioManager.Play("frenazo", 0.45f);
                _lastBrakeSound = Time.time;
            }
            _wasBraking = braking;

            // Paneo segun la posicion en pantalla
            var cam = Camera.main;
            if (cam != null && !isLocal)
            {
                float pan = Mathf.Clamp((pos.x - cam.transform.position.x) / (cam.orthographicSize * cam.aspect), -1f, 1f) * 0.7f;
                _engineSource.panStereo = pan;
                _skidSource.panStereo = pan;
            }
        }

        private void UpdatePreview()
        {
            float speed01 = _previewSpeed01;
            _engineFreq = EngineFrequency(speed01, _previewDrs);

            bool on = AudioManager.SfxEnabled;
            _engineGain = on ? 0.32f * (0.55f + 0.45f * Mathf.Max(_previewThrottle, speed01 * 0.6f)) : 0f;
            _drsAmount = on && _previewDrs ? 1f : 0f;
            _skidGain = on ? _previewSkid * 0.26f : 0f;
            _skidPitch = 1050f + 320f * Mathf.Clamp01(speed01);
        }

        /// <summary>
        /// Tono del motor segun la velocidad: sube parejo (curva suave) con una
        /// ondulacion muy leve por marcha para que no suene plano.
        /// </summary>
        private static float EngineFrequency(float speed01, bool drs)
        {
            float s = Mathf.Clamp(speed01, 0f, 1.2f);
            float gearPos = s * GEARS;
            float inGear = gearPos - Mathf.Floor(gearPos);
            float freq = IDLE_FREQ + (TOP_FREQ - IDLE_FREQ) * Mathf.Pow(s, 0.85f) + (inGear - 0.5f) * 8f;
            if (drs) freq *= 1.06f;
            return freq;
        }

        private static float DistanceFactor(Vector3 pos, float maxDistance)
        {
            var cam = Camera.main;
            if (cam == null) return 0.5f;
            float d = Vector2.Distance(pos, cam.transform.position);
            return Mathf.Clamp01(1f - d / maxDistance);
        }

        // ---------------------------------------------------------------------
        // Hilo de audio: sintesis
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
            float targetFreq = _engineFreq;
            float targetGain = _engineGain;
            float targetDrs = _drsAmount;
            float sr = _sampleRate;
            float pitchSmooth = 1f - Mathf.Exp(-1f / (sr * 0.12f));   // subida suave del tono
            float gainSmooth = 1f - Mathf.Exp(-1f / (sr * 0.08f));
            float drsSmooth = 1f - Mathf.Exp(-1f / (sr * 0.25f));
            float airHp = 1f - Mathf.Exp(-2f * Mathf.PI * 2200f / sr);
            float airLp = 1f - Mathf.Exp(-2f * Mathf.PI * 6500f / sr);

            for (int i = 0; i < data.Length; i++)
            {
                _curFreq += (targetFreq - _curFreq) * pitchSmooth;
                _curGain += (targetGain - _curGain) * gainSmooth;
                _curDrs += (targetDrs - _curDrs) * drsSmooth;

                _phase += _curFreq / sr;
                if (_phase >= 1.0) _phase -= 1.0;
                _phase2 += _curFreq * 1.007 / sr;            // desafinado leve = motor "vivo"
                if (_phase2 >= 1.0) _phase2 -= 1.0;
                _subPhase += _curFreq * 0.5 / sr;
                if (_subPhase >= 1.0) _subPhase -= 1.0;

                float saw = (float)(_phase * 2.0 - 1.0) * 0.5f + (float)(_phase2 * 2.0 - 1.0) * 0.4f;
                float sub = _subPhase < 0.5 ? 0.35f : -0.35f;
                float rumble = 0.8f + 0.2f * Mathf.Sin((float)(_subPhase * 2.0 * Mathf.PI));
                _nlp += (NextNoise() - _nlp) * 0.12f;
                float raw = (saw + sub) * rumble + _nlp * 0.25f;

                // Filtro: mas abierto (brillante) con DRS
                float cutoff = 2600f + 1400f * _curDrs;
                float lpCoef = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sr);
                _lp1 += (raw - _lp1) * lpCoef;
                _lp2 += (_lp1 - _lp2) * lpCoef;
                float engine = Mathf.Clamp(_lp2 * 1.5f * (1f + 0.25f * _curDrs), -1f, 1f);

                // DRS: soplido de aire del turbo (ruido pasa-banda), sin tono fijo.
                float n = NextNoise();
                _airA += (n - _airA) * airHp;
                float airHigh = n - _airA;
                _airB += (airHigh - _airB) * airLp;
                float air = _airB * 0.55f * _curDrs;

                data[i] = (engine + air) * _curGain;
            }
        }

        private void OnSkidRead(float[] data)
        {
            float target = _skidGain;
            float targetPitch = _skidPitch;
            float sr = _sampleRate;
            float gainSmooth = 1f - Mathf.Exp(-1f / (sr * 0.05f));
            float pitchSmooth = 1f - Mathf.Exp(-1f / (sr * 0.2f));
            float wanderCoef = 1f - Mathf.Exp(-2f * Mathf.PI * 4f / sr);    // deriva lenta del tono
            float flutterCoef = 1f - Mathf.Exp(-2f * Mathf.PI * 45f / sr);  // temblor de la goma
            float hpCoef = 1f - Mathf.Exp(-2f * Mathf.PI * 1400f / sr);
            float lpCoef = 1f - Mathf.Exp(-2f * Mathf.PI * 4200f / sr);

            for (int i = 0; i < data.Length; i++)
            {
                _curSkid += (target - _curSkid) * gainSmooth;
                _curSkidPitch += (targetPitch - _curSkidPitch) * pitchSmooth;
                if (_curSkid < 0.0005f && target < 0.0005f)
                {
                    data[i] = 0f;
                    continue;
                }

                // El tono del chillido deriva y tiembla (la llanta "agarra y suelta").
                _wander += (NextNoise() - _wander) * wanderCoef;
                _flutter += (NextNoise() - _flutter) * flutterCoef;
                // (el ruido filtrado a 4 Hz tiene desviacion ~0.01: x12 = ~12% de variacion de tono)
                float freq = _curSkidPitch * (1f + Mathf.Clamp(_wander * 12f, -0.3f, 0.3f));

                _squealPhase += freq / sr;
                if (_squealPhase >= 1.0) _squealPhase -= 1.0;
                float ph = (float)(_squealPhase * 2.0 * Mathf.PI);
                float squeal = Mathf.Sin(ph) + 0.55f * Mathf.Sin(2f * ph) + 0.3f * Mathf.Sin(3f * ph);
                squeal = squeal / (1f + Mathf.Abs(squeal) * 0.6f);        // saturacion suave
                float am = Mathf.Clamp01(0.65f + _flutter * 6f);

                // Roce del asfalto: ruido pasa-banda
                float n = NextNoise();
                _snA += (n - _snA) * hpCoef;
                float high = n - _snA;
                _snB += (high - _snB) * lpCoef;

                data[i] = (squeal * 0.55f * am + _snB * 0.5f) * _curSkid;
            }
        }
    }
}
