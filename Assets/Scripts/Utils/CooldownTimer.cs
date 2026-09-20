using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Utilidad simple para manejar cooldowns de habilidades.
    /// Se usa tanto en servidor (para validar) como en cliente (para mostrar UI).
    /// </summary>
    public class CooldownTimer
    {
        public float Duration { get; private set; }
        public float ReadyAtTime { get; private set; }

        public CooldownTimer(float duration)
        {
            Duration = duration;
            ReadyAtTime = 0f;
        }

        public bool IsReady => Time.time >= ReadyAtTime;

        public float RemainingTime => Mathf.Max(0f, ReadyAtTime - Time.time);

        public float NormalizedRemaining => Duration <= 0f ? 0f : Mathf.Clamp01(RemainingTime / Duration);

        public void Trigger()
        {
            ReadyAtTime = Time.time + Duration;
        }

        public void ForceReady()
        {
            ReadyAtTime = 0f;
        }
    }
}
