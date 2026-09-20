using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboGrid
{
    /// <summary>
    /// Orquesta el ciclo de vida de la conexion: escucha altas/bajas de jugadores,
    /// valida el limite de 4 jugadores y dispara el cambio a la escena de carrera
    /// cuando el host decide arrancar. Vive en la escena de Lobby (DontDestroyOnLoad).
    /// </summary>
    public class GameNetworkManager : MonoBehaviour
    {
        public static GameNetworkManager Instance { get; private set; }

        public IReadOnlyList<ulong> ConnectedClientIds => _connectedClientIds;
        private readonly List<ulong> _connectedClientIds = new List<ulong>();

        public System.Action<ulong> OnPlayerJoined;
        public System.Action<ulong> OnPlayerLeft;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            NetworkManager.Singleton.ConnectionApprovalCallback += HandleConnectionApproval;
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton == null) return;
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            NetworkManager.Singleton.ConnectionApprovalCallback -= HandleConnectionApproval;
        }

        /// <summary>
        /// Corre solo en el servidor/host. Rechaza conexiones si ya hay 4 jugadores
        /// o si la partida ya empezo.
        /// </summary>
        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            bool raceInProgress = RaceManager.Instance != null &&
                                   RaceManager.Instance.CurrentState.Value != RaceManager.RaceState.WaitingForPlayers;

            bool full = _connectedClientIds.Count >= GameConstants.MAX_PLAYERS;

            if (full || raceInProgress)
            {
                response.Approved = false;
                response.Reason = full ? "Partida llena" : "La carrera ya comenzo";
                response.CreatePlayerObject = false;
            }
            else
            {
                response.Approved = true;
                response.CreatePlayerObject = true;
            }

            response.Pending = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!_connectedClientIds.Contains(clientId))
                _connectedClientIds.Add(clientId);

            Debug.Log($"[GameNetworkManager] Cliente conectado: {clientId}. Total: {_connectedClientIds.Count}");
            OnPlayerJoined?.Invoke(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            _connectedClientIds.Remove(clientId);
            Debug.Log($"[GameNetworkManager] Cliente desconectado: {clientId}. Total: {_connectedClientIds.Count}");
            OnPlayerLeft?.Invoke(clientId);
        }

        /// <summary>
        /// Llamado por el host desde LobbyUI cuando hay 2-4 jugadores y presiona "Empezar".
        /// </summary>
        public void HostStartRace(string raceSceneName)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            if (_connectedClientIds.Count < GameConstants.MIN_PLAYERS)
            {
                Debug.LogWarning("[GameNetworkManager] Faltan jugadores para arrancar.");
                return;
            }

            NetworkManager.Singleton.SceneManager.LoadScene(raceSceneName, LoadSceneMode.Single);
        }
    }
}
