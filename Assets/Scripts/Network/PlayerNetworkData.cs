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

        /// <summary>Solo en clientes: aviso para mostrar "¡Pinchazo a X!" en el HUD.</summary>
        public static event Action<string, string> OnPinchazoFeed; // (atacante, victima)

        /// <summary>Tipos de aviso de power-up que ven todos los jugadores.</summary>
        public enum PowerUpFeed : byte
        {
            RecogioDrs = 0,
            RecogioPinchazo = 1,
            ActivoDrs = 2
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
        }

        public void AddFuelServer()
        {
            if (!IsServer) return;
            FuelCount.Value += 1;
            Points.Value += GameConstants.POINTS_PER_FUEL;
            RaceManager.Instance?.NotifyScoreChangedServer(this);
        }

        /// <summary>Guarda un power-up recogido. Devuelve false si ya tenia uno de ese tipo.</summary>
        public bool GivePowerUpServer(PowerUpType type)
        {
            if (!IsServer) return false;
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
                target.SetStunnedServer(true, GameConstants.PINCHAZO_STUN_DURATION);
                AnnouncePinchazoRpc(DisplayName, target.DisplayName);
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
