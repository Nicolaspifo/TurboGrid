using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Audio PROCEDURAL de cada auto (se sintetiza en tiempo real, sin archivos):
    ///  - Motor: onda diente de sierra + sub-grave + "retumbe" de cilindros. El tono sube
    ///    con la velocidad y simula cambios de marcha; al acelerar suena mas fuerte y
    ///    el DRS le agrega un silbido de turbo.
    ///  - Derrape: ruido filtrado + chillido agudo cuando el auto gira fuerte a alta
    ///    velocidad o frena en seco.
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

        // Parametros que lee el hilo de audio (se escriben desde Update).
        private volatile float _engineFreq = 45f;
        private volatile float _engineGain;
        private volatile float _turboGain;
        private volatile float _skidGain;

        // Estado del hilo de audio
        private double _phase, _subPhase, _turboPhase, _squealPhase;
        private float _curFreq = 45f, _curGain, _curTurbo, _curSkid;
        private float _lp1, _lp2, _nlp, _snA, _snB;
        private uint _rand = 0x9E3779B9u;

        // Estado del hilo principal
        private Vector3 _lastPos;
        private float _lastAngle;
        private float _speedEstimate;
        private float _turnRate;
        private float _lastBrakeSound = -10f;
        private bool _wasBraking;

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

            // --- Motor con "cambios de marcha": dentro de cada marcha el tono sube y al cambiar baja ---
            float gearPos = Mathf.Clamp(speed01, 0f, 1.2f) * GEARS;
            int gear = Mathf.Min(GEARS - 1, Mathf.FloorToInt(gearPos));
            float inGear = Mathf.Clamp01(gearPos - gear);
            float freq = 42f + gear * 14f + inGear * 58f;
            if (drs) freq *= 1.18f;
            _engineFreq = freq;

            float baseGain = isLocal ? 0.32f : 0.2f;
            float load = 0.55f + 0.45f * Mathf.Max(throttle, speed01 * 0.6f);
            float gain = baseGain * load;
            if (!racingOrCountdown) gain *= 0.5f;  // ralenti antes de la largada
            if (finished) gain *= 0.35f;
            if (!AudioManager.SfxEnabled) gain = 0f;

            // Atenuacion por distancia para autos remotos.
            float distFactor = isLocal ? 1f : DistanceFactor(pos, 600f);
            _engineGain = gain * distFactor;
            _turboGain = drs ? 0.12f * distFactor * (AudioManager.SfxEnabled ? 1f : 0f) : 0f;

            // --- Derrape: giro fuerte a alta velocidad, o freno fuerte ---
            float turnNorm = Mathf.Clamp01(_turnRate / GameConstants.CAR_TURN_SPEED);
            float skid = Mathf.Clamp01((turnNorm * speed01 - 0.42f) * 3.2f);
            bool braking = isLocal && CarInput.Throttle < -0.1f && _car != null && _car.CurrentSpeed > maxSpeed * 0.45f;
            if (braking) skid = Mathf.Max(skid, 0.8f * speed01);
            if (!AudioManager.SfxEnabled || finished) skid = 0f;
            _skidGain = skid * (isLocal ? 0.28f : 0.18f) * distFactor;

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
            float targetTurbo = _turboGain;
            float sr = _sampleRate;
            float smooth = 1f - Mathf.Exp(-1f / (sr * 0.05f));     // ~50 ms para el tono
            float gainSmooth = 1f - Mathf.Exp(-1f / (sr * 0.08f));
            float lpCoef = 1f - Mathf.Exp(-2f * Mathf.PI * 1800f / sr);

            for (int i = 0; i < data.Length; i++)
            {
                _curFreq += (targetFreq - _curFreq) * smooth;
                _curGain += (targetGain - _curGain) * gainSmooth;
                _curTurbo += (targetTurbo - _curTurbo) * gainSmooth;

                _phase += _curFreq / sr;
                if (_phase >= 1.0) _phase -= 1.0;
                _subPhase += _curFreq * 0.5 / sr;
                if (_subPhase >= 1.0) _subPhase -= 1.0;
                _turboPhase += (_curFreq * 11.0) / sr;
                if (_turboPhase >= 1.0) _turboPhase -= 1.0;

                float saw = (float)(_phase * 2.0 - 1.0);
                float sub = _subPhase < 0.5 ? 0.6f : -0.6f;
                // "retumbe" de los cilindros (modulacion de amplitud)
                float rumble = 0.7f + 0.3f * Mathf.Sin((float)(_subPhase * 2.0 * Mathf.PI));
                _nlp += (NextNoise() - _nlp) * 0.08f;

                float raw = (saw * 0.55f + sub * 0.45f) * rumble + _nlp * 0.35f;
                // Suavizar (dos filtros paso-bajo) para que no suene "chiptune"
                _lp1 += (raw - _lp1) * lpCoef;
                _lp2 += (_lp1 - _lp2) * lpCoef;
                float engine = Mathf.Clamp(_lp2 * 1.6f, -1f, 1f);

                float turbo = Mathf.Sin((float)(_turboPhase * 2.0 * Mathf.PI)) * _curTurbo;
                data[i] = engine * _curGain + turbo;
            }
        }

        private void OnSkidRead(float[] data)
        {
            float target = _skidGain;
            float sr = _sampleRate;
            float gainSmooth = 1f - Mathf.Exp(-1f / (sr * 0.06f));
            float hpCoef = 1f - Mathf.Exp(-2f * Mathf.PI * 1500f / sr);
            float lpCoef = 1f - Mathf.Exp(-2f * Mathf.PI * 5500f / sr);

            for (int i = 0; i < data.Length; i++)
            {
                _curSkid += (target - _curSkid) * gainSmooth;
                if (_curSkid < 0.0005f && target < 0.0005f)
                {
                    data[i] = 0f;
                    continue;
                }

                // Ruido pasa-banda (roce de la llanta)
                float n = NextNoise();
                _snA += (n - _snA) * hpCoef;          // paso-bajo
                float high = n - _snA;                // paso-alto
                _snB += (high - _snB) * lpCoef;       // recorta los agudos extremos

                // Chillido agudo inestable
                float jitter = 1f + 0.03f * NextNoise();
                _squealPhase += 2350.0 * jitter / sr;
                if (_squealPhase >= 1.0) _squealPhase -= 1.0;
                float squeal = Mathf.Sin((float)(_squealPhase * 2.0 * Mathf.PI));

                data[i] = (_snB * 0.9f + squeal * 0.35f) * _curSkid;
            }
        }
    }
}
