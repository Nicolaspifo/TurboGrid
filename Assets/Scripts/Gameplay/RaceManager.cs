using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboGrid
{
    /// <summary>Fila de la tabla de resultados final.</summary>
    public struct RaceResult : INetworkSerializable
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public int Points;
        public int Fuel;
        public int SlotIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Points);
            serializer.SerializeValue(ref Fuel);
            serializer.SerializeValue(ref SlotIndex);
        }
    }

    /// <summary>
    /// Autoridad central de la carrera (vive en la escena de cada circuito con NetworkObject).
    /// Solo el SERVIDOR escribe el estado; los clientes leen NetworkVariables.
    ///
    /// Flujo: Esperando (todos cargan la escena) -> spawn de autos en la parrilla ->
    /// Cuenta regresiva (3s) -> Carrera (3 min) -> Fin (alguien llega a 1000 pts
    /// o se acaba el tiempo) -> Resultados -> Revancha (reinicio en el mismo mapa).
    /// </summary>
    public class RaceManager : NetworkBehaviour
    {
        public static RaceManager Instance { get; private set; }

        public enum RaceState : byte
        {
            WaitingForPlayers = 0,
            Countdown = 1,
            Racing = 2,
            Finished = 3
        }

        public NetworkVariable<RaceState> CurrentState = new NetworkVariable<RaceState>(
            RaceState.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Tiempo de servidor en que termina la fase actual (cuenta regresiva o carrera).</summary>
        public NetworkVariable<double> PhaseEndTime = new NetworkVariable<double>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        [Tooltip("Opcional. Si esta vacio se usan los prefabs de NetworkManager que tengan CarController (Vehiculo_1, 2, 3...).")]
        [SerializeField] private GameObject[] carPrefabs;

        [Tooltip("Segundos maximos esperando que todos carguen la escena antes de arrancar igual.")]
        [SerializeField] private float loadTimeout = 10f;

        /// <summary>Evento local en cada cliente al terminar la carrera (lista ordenada por puntos).</summary>
        public event Action<List<RaceResult>, bool> OnRaceFinishedClientSide; // (resultados, alguien llego a 1000)

        /// <summary>Evento local cuando empieza una revancha.</summary>
        public event Action OnRaceRestartedClientSide;

        public List<RaceResult> LastResults { get; private set; }
        public bool LastWinByScore { get; private set; }

        public bool IsRacing => IsSpawned && CurrentState.Value == RaceState.Racing;
        public bool IsFinished => IsSpawned && CurrentState.Value == RaceState.Finished;

        private bool _carsSpawned;
        private float _spawnTime;
        private List<GameObject> _resolvedCarPrefabs;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            CurrentState.OnValueChanged += HandleStateChanged;

            if (IsServer)
            {
                _spawnTime = Time.time;
                CurrentState.Value = RaceState.WaitingForPlayers;
                NetworkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnectedServer;
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentState.OnValueChanged -= HandleStateChanged;
            if (IsServer && NetworkManager != null)
            {
                if (NetworkManager.SceneManager != null)
                    NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnectedServer;
            }
        }

        private void HandleStateChanged(RaceState previous, RaceState current)
        {
            if (current == RaceState.Countdown && previous == RaceState.Finished)
            {
                LastResults = null;
                OnRaceRestartedClientSide?.Invoke();
            }
        }

        // ---------------- Tiempo (todos los clientes) ----------------

        private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        /// <summary>Segundos que quedan de carrera (3:00 antes de empezar).</summary>
        public float TimeRemaining
        {
            get
            {
                if (!IsSpawned) return GameConstants.RACE_DURATION_SECONDS;
                switch (CurrentState.Value)
                {
                    case RaceState.Racing: return Mathf.Max(0f, (float)(PhaseEndTime.Value - Now));
                    case RaceState.Finished: return Mathf.Max(0f, (float)(PhaseEndTime.Value - Now));
                    default: return GameConstants.RACE_DURATION_SECONDS;
                }
            }
        }

        public float CountdownRemaining =>
            IsSpawned && CurrentState.Value == RaceState.Countdown ? Mathf.Max(0f, (float)(PhaseEndTime.Value - Now)) : 0f;

        // ---------------- Servidor ----------------

        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
        {
            if (sceneName != gameObject.scene.name) return;
            SetupRaceServer();
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            switch (CurrentState.Value)
            {
                case RaceState.WaitingForPlayers:
                    // Respaldo por si el evento de carga no llega (p. ej. escena cargada a mano en el editor).
                    if (!_carsSpawned && Time.time - _spawnTime > loadTimeout)
                        SetupRaceServer();
                    break;

                case RaceState.Countdown:
                    if (Now >= PhaseEndTime.Value)
                    {
                        PhaseEndTime.Value = Now + GameConstants.RACE_DURATION_SECONDS;
                        CurrentState.Value = RaceState.Racing;
                    }
                    break;

                case RaceState.Racing:
                    if (Now >= PhaseEndTime.Value)
                        EndRaceServer(false);
                    break;
            }
        }

        private void SetupRaceServer()
        {
            if (_carsSpawned) return;
            _carsSpawned = true;

            SpawnCarsServer();
            if (PowerUpSpawner.Instance != null)
                PowerUpSpawner.Instance.SpawnInitialServer();

            BeginCountdownServer();
        }

        private void BeginCountdownServer()
        {
            PhaseEndTime.Value = Now + GameConstants.COUNTDOWN_SECONDS;
            CurrentState.Value = RaceState.Countdown;
        }

        private void SpawnCarsServer()
        {
            var grid = SpawnGridManager.Instance;
            if (grid != null) grid.ShuffleSlots();

            var prefabs = GetCarPrefabs();
            if (prefabs.Count == 0)
            {
                Debug.LogError("[RaceManager] No hay prefabs de auto con CarController registrados en el NetworkManager.");
                return;
            }

            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                if (NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId) != null) continue;

                var session = SessionState.Instance;
                int slot = session != null ? session.GetSlotIndex(clientId) : (int)(clientId % GameConstants.MAX_PLAYERS);
                string playerName = session != null ? session.GetName(clientId) : $"Piloto {clientId + 1}";

                Vector3 pos = Vector3.zero;
                Quaternion rot = Quaternion.identity;
                if (grid != null) grid.TryClaimSlot(out pos, out rot);

                GameObject prefab = prefabs[slot % prefabs.Count];
                NetworkObject netObj = NetworkManager.SpawnManager.InstantiateAndSpawn(
                    prefab.GetComponent<NetworkObject>(), clientId,
                    destroyWithScene: true, isPlayerObject: true, position: pos, rotation: rot);

                var data = netObj.GetComponent<PlayerNetworkData>();
                if (data != null) data.InitializeServer(playerName, slot);
            }
        }

        private List<GameObject> GetCarPrefabs()
        {
            if (_resolvedCarPrefabs != null) return _resolvedCarPrefabs;

            _resolvedCarPrefabs = new List<GameObject>();
            if (carPrefabs != null)
            {
                foreach (var p in carPrefabs)
                    if (p != null && p.GetComponent<CarController>() != null && p.GetComponent<NetworkObject>() != null)
                        _resolvedCarPrefabs.Add(p);
            }

            if (_resolvedCarPrefabs.Count == 0)
            {
                foreach (var entry in NetworkManager.NetworkConfig.Prefabs.Prefabs)
                {
                    var go = entry.Prefab;
                    if (go != null && go.GetComponent<CarController>() != null && !_resolvedCarPrefabs.Contains(go))
                        _resolvedCarPrefabs.Add(go);
                }
                _resolvedCarPrefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            }
            return _resolvedCarPrefabs;
        }

        /// <summary>Lo llama PlayerNetworkData al sumar puntos.</summary>
        public void NotifyScoreChangedServer(PlayerNetworkData player)
        {
            if (!IsServer || CurrentState.Value != RaceState.Racing) return;
            if (player.Points.Value >= GameConstants.WIN_SCORE)
                EndRaceServer(true);
        }

        private void HandleClientDisconnectedServer(ulong clientId)
        {
            if (!IsServer || CurrentState.Value == RaceState.Finished) return;

            // Si queda un solo piloto la carrera no tiene sentido: termina y gana el que queda.
            int remaining = NetworkManager.ConnectedClientsIds.Count(id => id != clientId);
            if (_carsSpawned && remaining < GameConstants.MIN_PLAYERS)
                EndRaceServer(false);
        }

        private void EndRaceServer(bool winByScore)
        {
            if (CurrentState.Value == RaceState.Finished) return;
            CurrentState.Value = RaceState.Finished;

            foreach (var player in PlayerNetworkData.All)
            {
                if (player == null || !player.IsSpawned) continue;
                player.SetStunnedServer(true, float.MaxValue); // congela a todos hasta la revancha
            }

            var results = PlayerNetworkData.All
                .Where(p => p != null && p.IsSpawned)
                .Select(p => new RaceResult
                {
                    ClientId = p.OwnerClientId,
                    Name = p.PlayerName.Value,
                    Points = p.Points.Value,
                    Fuel = p.FuelCount.Value,
                    SlotIndex = p.SlotIndex.Value
                })
                .OrderByDescending(r => r.Points)
                .ThenByDescending(r => r.Fuel)
                .ToArray();

            ShowResultsRpc(results, winByScore);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShowResultsRpc(RaceResult[] results, bool winByScore)
        {
            LastResults = new List<RaceResult>(results);
            LastWinByScore = winByScore;
            OnRaceFinishedClientSide?.Invoke(LastResults, winByScore);
        }

        /// <summary>
        /// Revancha en el mismo circuito, sin recargar la escena. Solo el host.
        /// </summary>
        public void RequestRematch()
        {
            if (!IsServer || CurrentState.Value != RaceState.Finished) return;
            if (NetworkManager.ConnectedClientsIds.Count < GameConstants.MIN_PLAYERS) return;

            LastResults = null;

            var grid = SpawnGridManager.Instance;
            if (grid != null) grid.ShuffleSlots();

            foreach (var player in PlayerNetworkData.All)
            {
                if (player == null || !player.IsSpawned) continue;
                player.ResetForRaceServer();

                Vector3 pos = player.transform.position;
                Quaternion rot = player.transform.rotation;
                if (grid != null) grid.TryClaimSlot(out pos, out rot);
                player.TeleportOwnerRpc(pos, rot);
            }

            FuelSpawner.Instance?.ResetAllServer();
            PowerUpSpawner.Instance?.ResetAllServer();

            BeginCountdownServer();
        }
    }
}
