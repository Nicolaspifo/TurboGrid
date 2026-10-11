using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Datos de un jugador dentro de la sala (se sincronizan a todos).
    /// SlotIndex (0-3) define su color y la variante de auto.
    /// </summary>
    public struct PlayerSlot : INetworkSerializable, IEquatable<PlayerSlot>
    {
        // Estados dentro de la sala
        public const byte CONECTANDO = 0; // aprobado, sincronizando con el host
        public const byte CONECTADO = 1;  // ya esta en la sala
        public const byte LISTO = 2;      // toco "LISTO"

        public ulong ClientId;
        public FixedString32Bytes Name;
        public int SlotIndex;
        public byte State;

        public bool IsHost => ClientId == NetworkManager.ServerClientId;
        public bool IsReady => State == LISTO;
        public bool IsConnecting => State == CONECTANDO;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref SlotIndex);
            serializer.SerializeValue(ref State);
        }

        public bool Equals(PlayerSlot other) =>
            ClientId == other.ClientId && Name.Equals(other.Name) && SlotIndex == other.SlotIndex && State == other.State;

        public override bool Equals(object obj) => obj is PlayerSlot other && Equals(other);

        public override int GetHashCode() => ClientId.GetHashCode();
    }

    /// <summary>
    /// Estado de la sala compartido por todos: lista de jugadores (nombre + color)
    /// y el mapa elegido por el host. Lo crea el host al iniciar la partida y
    /// sobrevive a los cambios de escena (lobby -> carrera -> revancha).
    ///
    /// Prefab: "SessionState" (lo crea TurboGrid > Configurar escenas) registrado en
    /// DefaultNetworkPrefabs y asignado en GameNetworkManager.
    /// </summary>
    public class SessionState : NetworkBehaviour
    {
        public static SessionState Instance { get; private set; }

        /// <summary>Se dispara en todos los clientes cuando cambia la lista de jugadores.</summary>
        public static event Action OnPlayersChanged;

        public NetworkList<PlayerSlot> Players;

        public NetworkVariable<int> MapIndex = new NetworkVariable<int>(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private void Awake()
        {
            Players = new NetworkList<PlayerSlot>();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Players.OnListChanged += HandleListChanged;
            OnPlayersChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            Players.OnListChanged -= HandleListChanged;
            if (Instance == this) Instance = null;
            OnPlayersChanged?.Invoke();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        private void HandleListChanged(NetworkListEvent<PlayerSlot> change)
        {
            OnPlayersChanged?.Invoke();
        }

        public int PlayerCount => Players != null ? Players.Count : 0;

        public bool TryGetPlayer(ulong clientId, out PlayerSlot slot)
        {
            if (Players != null)
            {
                foreach (var p in Players)
                {
                    if (p.ClientId == clientId)
                    {
                        slot = p;
                        return true;
                    }
                }
            }
            slot = default;
            return false;
        }

        public string GetName(ulong clientId)
        {
            return TryGetPlayer(clientId, out var slot) ? slot.Name.ToString() : $"Piloto {clientId + 1}";
        }

        public int GetSlotIndex(ulong clientId)
        {
            return TryGetPlayer(clientId, out var slot) ? slot.SlotIndex : (int)(clientId % GameConstants.MAX_PLAYERS);
        }

        // ---------------- Solo servidor ----------------

        /// <summary>
        /// La sala se puede arrancar si hay al menos 2 pilotos, nadie se esta conectando
        /// y todos los invitados tocaron LISTO (el host no necesita marcar listo).
        /// </summary>
        public bool CanStart(out string reason)
        {
            int count = PlayerCount;
            if (count < GameConstants.MIN_PLAYERS)
            {
                reason = $"Esperando pilotos ({count}/{GameConstants.MAX_PLAYERS})... comparte el codigo";
                return false;
            }
            int notReady = 0;
            foreach (var p in Players)
            {
                if (p.IsConnecting)
                {
                    reason = $"{p.Name} se esta conectando...";
                    return false;
                }
                if (!p.IsHost && !p.IsReady) notReady++;
            }
            if (notReady > 0)
            {
                reason = notReady == 1 ? "Falta 1 piloto por marcar LISTO" : $"Faltan {notReady} pilotos por marcar LISTO";
                return false;
            }
            reason = "¡Todos listos! Toca COMENZAR";
            return true;
        }

        /// <summary>El invitado local marca o desmarca "LISTO".</summary>
        public void RequestSetReady(bool ready)
        {
            SetReadyRpc(ready);
        }

        [Rpc(SendTo.Server)]
        private void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!TryGetPlayer(sender, out var slot) || slot.IsConnecting) return;
            SetStateServer(sender, ready ? PlayerSlot.LISTO : PlayerSlot.CONECTADO);
        }

        public void SetStateServer(ulong clientId, byte state)
        {
            if (!IsServer) return;
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].ClientId != clientId) continue;
                var slot = Players[i];
                if (slot.State == state) return;
                slot.State = state;
                Players[i] = slot;
                return;
            }
        }

        public void AddPlayerServer(ulong clientId, string requestedName, byte state = PlayerSlot.CONECTADO)
        {
            if (!IsServer) return;
            if (TryGetPlayer(clientId, out _))
            {
                // Ya estaba (p. ej. registrado al aprobar la conexion): solo actualizar el estado.
                if (state != PlayerSlot.CONECTANDO) SetStateServer(clientId, state);
                return;
            }

            int slot = FindFreeSlot();
            string name = LocalPlayerSettings.SanitizeName(requestedName);
            if (string.IsNullOrEmpty(name)) name = $"Piloto {slot + 1}";
            name = MakeUnique(name);

            Players.Add(new PlayerSlot
            {
                ClientId = clientId,
                Name = new FixedString32Bytes(name),
                SlotIndex = slot,
                State = state
            });
        }

        public void RemovePlayerServer(ulong clientId)
        {
            if (!IsServer) return;
            for (int i = Players.Count - 1; i >= 0; i--)
            {
                if (Players[i].ClientId == clientId)
                    Players.RemoveAt(i);
            }
        }

        public void SetMapServer(int mapIndex)
        {
            if (!IsServer) return;
            MapIndex.Value = mapIndex;
        }

        private int FindFreeSlot()
        {
            for (int s = 0; s < GameConstants.MAX_PLAYERS; s++)
            {
                bool used = false;
                foreach (var p in Players)
                    if (p.SlotIndex == s) { used = true; break; }
                if (!used) return s;
            }
            return Players.Count % GameConstants.MAX_PLAYERS;
        }

        private string MakeUnique(string name)
        {
            string candidate = name;
            int n = 2;
            while (NameInUse(candidate))
            {
                string suffix = " " + n++;
                string baseName = name.Length + suffix.Length > LocalPlayerSettings.MAX_NAME_LENGTH
                    ? name.Substring(0, Mathf.Max(1, LocalPlayerSettings.MAX_NAME_LENGTH - suffix.Length))
                    : name;
                candidate = baseName + suffix;
            }
            return candidate;
        }

        private bool NameInUse(string name)
        {
            foreach (var p in Players)
                if (p.Name.ToString() == name) return true;
            return false;
        }
    }
}
