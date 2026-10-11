using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Acceso comodo a un power-up del auto (DRS o Pinchazo) para la UI.
    /// El estado real (si lo tiene guardado, si esta activo) vive en PlayerNetworkData
    /// y lo decide el servidor: aqui solo se lee y se pide activarlo.
    ///
    /// Flujo: el auto recoge el power-up en pista -> queda "cargado" -> el jugador
    /// toca el boton -> se activa (DRS: +velocidad 3s / Pinchazo: bloquea a un rival 2s).
    /// </summary>
    [RequireComponent(typeof(PlayerNetworkData))]
    public abstract class AbilityBase : MonoBehaviour
    {
        protected PlayerNetworkData Data;

        public abstract PowerUpType Type { get; }

        protected virtual void Awake()
        {
            Data = GetComponent<PlayerNetworkData>();
        }

        /// <summary>El jugador tiene este power-up guardado y puede usarlo.</summary>
        public bool HasCharge =>
            Data != null && Data.IsSpawned && (Type == PowerUpType.Drs ? Data.HasDrs.Value : Data.HasPinchazo.Value);

        /// <summary>0..1 del efecto en curso (solo DRS tiene duracion propia).</summary>
        public float ActiveNormalized
        {
            get
            {
                if (Data == null || !Data.IsSpawned || Type != PowerUpType.Drs || !Data.IsDrsActive.Value) return 0f;
                double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0;
                return Mathf.Clamp01((float)((Data.DrsEndTime.Value - now) / GameConstants.DRS_DURATION));
            }
        }

        public bool CanUseNow =>
            HasCharge && RaceManager.Instance != null && RaceManager.Instance.IsRacing && !Data.IsStunned.Value;

        public void TryActivate()
        {
            if (!CanUseNow) return;
            Data.RequestUsePowerUp(Type);
        }
    }
}
