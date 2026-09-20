using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Autoridad central de la carrera. Solo el SERVIDOR escribe el estado;
    /// los clientes solo leen NetworkVariables para actualizar su HUD.
    /// Vive en un GameObject con NetworkObject dentro de la escena de carrera.
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

        // --- Estado sincronizado (solo el servidor escribe) ---
        public NetworkVariable<RaceState> CurrentState = new NetworkVariable<RaceState>(
            RaceState.WaitingForPlayers, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(
            GameConstants.RACE_DURATION_SECONDS, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public NetworkVariable<float> CountdownRemaining = new NetworkVariable<float>(
            GameConstants.COUNTDOWN_SECONDS, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Evento local en clientes para reaccionar al fin de carrera (mostrar EndRaceUI, etc.)
        public System.Action<List<(ulong clientId, FixedString32Bytes name, int fuel)>> OnRaceFinishedClientSide;

        private float _countdownTimer;
        private float _raceTimer;
        private bool _hasStarted;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _raceTimer = GameConstants.RACE_DURATION_SECONDS;
                CurrentState.Value = RaceState.WaitingForPlayers;
            }

            CurrentState.OnValueChanged += HandleStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            CurrentState.OnValueChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(RaceState previous, RaceState current)
        {
            if (current == RaceState.Finished && IsClient)
            {
                BuildAndBroadcastFinalResultsLocal();
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            switch (CurrentState.Value)
            {
                case RaceState.WaitingForPlayers:
                    TryBeginCountdownIfReady();
                    break;

                case RaceState.Countdown:
                    _countdownTimer -= Time.deltaTime;
                    CountdownRemaining.Value = Mathf.Max(0f, _countdownTimer);
                    if (_countdownTimer <= 0f)
                    {
                        CurrentState.Value = RaceState.Racing;
                    }
                    break;

                case RaceState.Racing:
                    _raceTimer -= Time.deltaTime;
                    TimeRemaining.Value = Mathf.Max(0f, _raceTimer);
                    if (_raceTimer <= 0f)
                    {
                        EndRace();
                    }
                    break;

                case RaceState.Finished:
                    // no-op, esperando volver al lobby o reiniciar
                    break;
            }
        }

        /// <summary>
        /// El host puede llamar esto manualmente (ej: boton "Listos") o se puede
        /// disparar automaticamente cuando se alcanza el minimo de jugadores.
        /// Aqui lo dejamos simple: arranca cuando hay >= MIN_PLAYERS conectados
        /// y todos los NetworkObjects de jugador ya spawnearon.
        /// </summary>
        private void TryBeginCountdownIfReady()
        {
            if (_hasStarted) return;

            int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
            if (playerCount < GameConstants.MIN_PLAYERS) return;

            _hasStarted = true;
            _countdownTimer = GameConstants.COUNTDOWN_SECONDS;
            CurrentState.Value = RaceState.Countdown;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReportFuelCollectedServerRpc(ServerRpcParams rpcParams = default)
        {
            // Punto de extension: aqui podrias validar anti-cheat adicional
            // (ej: distancia entre jugador y ficha) antes de sumar puntaje.
            // La suma real de fichas vive en PlayerNetworkData (ver FuelToken.cs).
        }

        private void EndRace()
        {
            CurrentState.Value = RaceState.Finished;

            // Congela a todos los jugadores
            foreach (var player in FindObjectsOfType<PlayerNetworkData>())
            {
                player.SetStunnedServer(true, float.MaxValue); // bloqueo permanente hasta reiniciar
            }
        }

        private void BuildAndBroadcastFinalResultsLocal()
        {
            var results = FindObjectsOfType<PlayerNetworkData>()
                .Select(p => (p.OwnerClientId, p.PlayerName.Value, p.FuelCount.Value))
                .OrderByDescending(r => r.Item3)
                .ToList();

            OnRaceFinishedClientSide?.Invoke(results);
        }
    }
}
