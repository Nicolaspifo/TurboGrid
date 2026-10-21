using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Estado de red de cada auto: puntos, gasolina recogida, power-ups guardados,
    /// DRS activo y bloqueo por Pinchazo. Va en el prefab del auto junto al CarController.
    /// Solo el servidor escribe estos valores.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class PlayerNetworkData : NetworkBehaviour
    {
        /// <summary>Todos los autos spawneados (servidor y clientes).</summary>
        public static readonly List<PlayerNetworkData> All = new List<PlayerNetworkData>();

        /// <summary>El auto del jugador local (null si todavia no spawnea).</summary>
        public static PlayerNetworkData Local { get; private set; }

        /// <summary>Se dispara cuando spawnea el auto del jugador local.</summary>
        public static event Action<PlayerNetworkData> OnLocalPlayerSpawned;

        /// <summary>Se dispara cuando se agrega o quita un auto (para HUD/mini-mapa).</summary>
        public static event Action OnRosterChanged;

        public NetworkVariable<int> Points = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> FuelCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsStunned = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsDrsActive = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> HasDrs = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> HasPinchazo = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<int> SlotIndex = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Tiempo de servidor en que termina el efecto actual (para barras del HUD).</summary>
        public NetworkVariable<double> DrsEndTime = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<double> StunEndTime = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // ---- Poderes instantaneos (se activan al recogerlos) ----
        public NetworkVariable<bool> HasShield = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<double> ShieldEndTime = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<double> MagnetEndTime = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public NetworkVariable<double> DoublePointsEndTime = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public bool IsMagnetActive => IsSpawned && Now < MagnetEndTime.Value;
        public bool IsDoublePointsActive => IsSpawned && Now < DoublePointsEndTime.Value;
        public float ShieldRemaining => HasShield.Value ? Mathf.Max(0f, (float)(ShieldEndTime.Value - Now)) : 0f;
        public float MagnetRemaining => Mathf.Max(0f, (float)(MagnetEndTime.Value - Now));
        public float DoublePointsRemaining => Mathf.Max(0f, (float)(DoublePointsEndTime.Value - Now));

        /// <summary>Solo en clientes: aviso para mostrar "¡Pinchazo a X!" en el HUD.</summary>
        public static event Action<string, string> OnPinchazoFeed; // (atacante, victima)

        /// <summary>Tipos de aviso de power-up que ven todos los jugadores.</summary>
        public enum PowerUpFeed : byte
        {
            RecogioDrs = 0,
            RecogioPinchazo = 1,
            ActivoDrs = 2,
            RecogioEscudo = 3,
            RecogioIman = 4,
            RecogioDoblePuntos = 5,
            EscudoBloqueo = 6   // el escudo de este piloto bloqueo un Pinchazo
        }

        /// <summary>En todos los clientes: (tipo de aviso, clientId del piloto, nombre del piloto).</summary>
        public static event Action<PowerUpFeed, ulong, string> OnPowerUpFeed;

        public CarController Car { get; private set; }
        public Color PlayerColor => PlayerColors.Get(SlotIndex.Value);
        public string DisplayName => PlayerName.Value.IsEmpty ? $"Piloto {OwnerClientId + 1}" : PlayerName.Value.ToString();

        private void Awake()
        {
            Car = GetComponent<CarController>();
            if (GetComponent<CarVisuals>() == null)
                gameObject.AddComponent<CarVisuals>();
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) All.Add(this);

            IsStunned.OnValueChanged += HandleStunChanged;
            Car.SetInputLocked(IsStunned.Value);

            if (IsOwner)
            {
                Local = this;
                OnLocalPlayerSpawned?.Invoke(this);
            }
            OnRosterChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            IsStunned.OnValueChanged -= HandleStunChanged;
            if (Local == this) Local = null;
            OnRosterChanged?.Invoke();
        }

        public override void OnDestroy()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            base.OnDestroy();
        }

        private void HandleStunChanged(bool previous, bool current)
        {
            Car.SetInputLocked(current);
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            double now = NetworkManager.ServerTime.Time;
            if (IsDrsActive.Value && now >= DrsEndTime.Value)
                IsDrsActive.Value = false;

            if (IsStunned.Value && StunEndTime.Value > 0 && now >= StunEndTime.Value)
                IsStunned.Value = false;

            if (HasShield.Value && now >= ShieldEndTime.Value)
                HasShield.Value = false;
        }

        // ---------------- Solo servidor ----------------

        public void InitializeServer(string displayName, int slotIndex)
        {
            if (!IsServer) return;
            PlayerName.Value = new FixedString32Bytes(displayName);
            SlotIndex.Value = slotIndex;
            ResetForRaceServer();
        }

        public void ResetForRaceServer()
        {
            if (!IsServer) return;
            Points.Value = 0;
            FuelCount.Value = 0;
            HasDrs.Value = false;
            HasPinchazo.Value = false;
            IsDrsActive.Value = false;
            DrsEndTime.Value = 0;
            IsStunned.Value = false;
            StunEndTime.Value = 0;
            HasShield.Value = false;
            ShieldEndTime.Value = 0;
            MagnetEndTime.Value = 0;
            DoublePointsEndTime.Value = 0;
        }

        public void AddFuelServer()
        {
            if (!IsServer) return;
            FuelCount.Value += 1;
            Points.Value += GameConstants.POINTS_PER_FUEL * (IsDoublePointsActive ? 2 : 1);
            RaceManager.Instance?.NotifyScoreChangedServer(this);
        }

        /// <summary>
        /// Da un power-up recogido. DRS y Pinchazo se guardan (devuelve false si ya
        /// tenia uno); Escudo, Iman y Doble puntos se activan al instante (o se renuevan).
        /// </summary>
        public bool GivePowerUpServer(PowerUpType type)
        {
            if (!IsServer) return false;
            switch (type)
            {
                case PowerUpType.Escudo:
                    HasShield.Value = true;
                    ShieldEndTime.Value = Now + GameConstants.ESCUDO_DURATION;
                    AnnouncePowerUpRpc(PowerUpFeed.RecogioEscudo, DisplayName);
                    return true;
                case PowerUpType.Iman:
                    MagnetEndTime.Value = Now + GameConstants.IMAN_DURATION;
                    AnnouncePowerUpRpc(PowerUpFeed.RecogioIman, DisplayName);
                    return true;
                case PowerUpType.DoblePuntos:
                    DoublePointsEndTime.Value = Now + GameConstants.DOBLE_PUNTOS_DURATION;
                    AnnouncePowerUpRpc(PowerUpFeed.RecogioDoblePuntos, DisplayName);
                    return true;
            }

            if (type == PowerUpType.Drs)
            {
                if (HasDrs.Value) return false;
                HasDrs.Value = true;
                AnnouncePowerUpRpc(PowerUpFeed.RecogioDrs, DisplayName);
            }
            else
            {
                if (HasPinchazo.Value) return false;
                HasPinchazo.Value = true;
                AnnouncePowerUpRpc(PowerUpFeed.RecogioPinchazo, DisplayName);
            }
            return true;
        }

        /// <summary>
        /// Bloqueo indefinido (fin de carrera) o temporal (Pinchazo).
        /// </summary>
        public void SetStunnedServer(bool stunned, float duration)
        {
            if (!IsServer) return;
            IsStunned.Value = stunned;
            StunEndTime.Value = stunned && duration < float.MaxValue
                ? NetworkManager.ServerTime.Time + duration
                : 0;
        }

        /// <summary>
        /// El NetworkTransform es Owner Authoritative: para mover el auto (revancha)
        /// el servidor le pide al dueño que se teletransporte.
        /// </summary>
        [Rpc(SendTo.Owner)]
        public void TeleportOwnerRpc(Vector3 position, Quaternion rotation)
        {
            Car.Teleport(position, rotation);
        }

        // ---------------- Activacion de power-ups (desde el dueño) ----------------

        public void RequestUsePowerUp(PowerUpType type)
        {
            if (!IsOwner) return;
            UsePowerUpRpc(type);
        }

        [Rpc(SendTo.Server)]
        private void UsePowerUpRpc(PowerUpType type)
        {
            if (RaceManager.Instance == null || !RaceManager.Instance.IsRacing) return;
            if (IsStunned.Value) return;

            if (type == PowerUpType.Drs)
            {
                if (!HasDrs.Value) return;
                HasDrs.Value = false;
                IsDrsActive.Value = true;
                DrsEndTime.Value = NetworkManager.ServerTime.Time + GameConstants.DRS_DURATION;
                AnnouncePowerUpRpc(PowerUpFeed.ActivoDrs, DisplayName);
            }
            else
            {
                if (!HasPinchazo.Value) return;
                PlayerNetworkData target = PickLeaderRivalServer();
                if (target == null) return; // no hay rivales: se conserva el power-up
                HasPinchazo.Value = false;
                if (target.HasShield.Value)
                {
                    // El escudo absorbe el pinchazo y se gasta.
                    target.HasShield.Value = false;
                    target.ShieldEndTime.Value = 0;
                    target.AnnouncePowerUpRpc(PowerUpFeed.EscudoBloqueo, target.DisplayName);
                }
                else
                {
                    target.SetStunnedServer(true, GameConstants.PINCHAZO_STUN_DURATION);
                    AnnouncePinchazoRpc(DisplayName, target.DisplayName);
                }
            }
        }

        /// <summary>
        /// El rival con MAS puntos de la partida (nunca quien lo lanza).
        /// Si ya estaba pinchado, solo se refresca el bloqueo.
        /// </summary>
        private PlayerNetworkData PickLeaderRivalServer()
        {
            PlayerNetworkData leader = null;
            foreach (var p in All)
            {
                if (p == null || p == this || !p.IsSpawned) continue;
                if (p.OwnerClientId == OwnerClientId) continue;
                if (leader == null || p.Points.Value > leader.Points.Value) leader = p;
            }
            return leader;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AnnouncePowerUpRpc(PowerUpFeed feed, FixedString32Bytes playerName)
        {
            OnPowerUpFeed?.Invoke(feed, OwnerClientId, playerName.ToString());
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AnnouncePinchazoRpc(FixedString32Bytes attacker, FixedString32Bytes victim)
        {
            OnPinchazoFeed?.Invoke(attacker.ToString(), victim.ToString());
        }
    }
}
