using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Estado de red de cada jugador: fichas recolectadas, nombre, si esta
    /// bloqueado por un Pinchazo. Va en el prefab Player_Car junto al CarController.
    /// Solo el servidor escribe estos valores.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class PlayerNetworkData : NetworkBehaviour
    {
        public NetworkVariable<int> FuelCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<bool> IsStunned = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CarController _carController;
        private float _stunEndTime;

        private void Awake()
        {
            _carController = GetComponent<CarController>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                string localName = "Piloto " + (OwnerClientId + 1);
                SubmitNameServerRpc(localName);
            }

            IsStunned.OnValueChanged += HandleStunChanged;
        }

        public override void OnNetworkDespawn()
        {
            IsStunned.OnValueChanged -= HandleStunChanged;
        }

        private void HandleStunChanged(bool previous, bool current)
        {
            if (_carController != null)
                _carController.SetInputLocked(current);
        }

        [ServerRpc]
        private void SubmitNameServerRpc(string name)
        {
            PlayerName.Value = name;
        }

        /// <summary>
        /// Solo debe llamarse desde codigo que corre en el servidor
        /// (FuelToken, RaceManager, abilities, etc).
        /// </summary>
        public void AddFuelServer(int amount)
        {
            if (!IsServer) return;
            FuelCount.Value += amount;
        }

        public void SetStunnedServer(bool stunned, float duration)
        {
            if (!IsServer) return;
            IsStunned.Value = stunned;

            if (stunned)
            {
                _stunEndTime = Time.time + duration;
                if (duration < float.MaxValue)
                    Invoke(nameof(ClearStunIfExpired), duration);
            }
        }

        private void ClearStunIfExpired()
        {
            if (!IsServer) return;
            if (Time.time >= _stunEndTime)
                IsStunned.Value = false;
        }
    }
}
