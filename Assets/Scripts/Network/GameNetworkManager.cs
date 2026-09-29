using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TurboGrid
{
    /// <summary>
    /// Orquesta el ciclo de vida de la conexion:
    ///  - Aprobacion de conexiones (maximo 4, no se puede entrar con la carrera empezada).
    ///  - Registro de jugadores en SessionState (nombre enviado en el payload de conexion).
    ///  - Arranque de la carrera en el mapa elegido.
    ///  - Volver al menu cuando se cae la conexion o el jugador sale.
    /// Vive en la escena MainMenu (DontDestroyOnLoad).
    /// </summary>
    public class GameNetworkManager : MonoBehaviour
    {
        public static GameNetworkManager Instance { get; private set; }

        /// <summary>Mensaje para mostrar en el menu despues de una desconexion.</summary>
        public static string LastDisconnectReason { get; set; }

        [Tooltip("Prefab con NetworkObject + SessionState. Lo asigna TurboGrid > Configurar escenas.")]
        [SerializeField] private NetworkObject sessionStatePrefab;

        public IReadOnlyList<ulong> ConnectedClientIds => _connectedClientIds;
        private readonly List<ulong> _connectedClientIds = new List<ulong>();

        // Nombres recibidos en la aprobacion, pendientes de registrar en SessionState.
        private readonly Dictionary<ulong, string> _pendingNames = new Dictionary<ulong, string>();

        public System.Action<ulong> OnPlayerJoined;
        public System.Action<ulong> OnPlayerLeft;

        private NetworkManager _subscribedManager;
        private bool _returningToMenu;

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
            Subscribe();
        }

        private void Subscribe()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm == _subscribedManager) return;

            nm.OnClientConnectedCallback += HandleClientConnected;
            nm.OnClientDisconnectCallback += HandleClientDisconnected;
            nm.OnServerStarted += HandleServerStarted;
            nm.OnClientStopped += HandleClientStopped;
            nm.ConnectionApprovalCallback = HandleConnectionApproval;
            _subscribedManager = nm;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_subscribedManager == null) return;
            _subscribedManager.OnClientConnectedCallback -= HandleClientConnected;
            _subscribedManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _subscribedManager.OnServerStarted -= HandleServerStarted;
            _subscribedManager.OnClientStopped -= HandleClientStopped;
            if (_subscribedManager.ConnectionApprovalCallback == (System.Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse>)HandleConnectionApproval)
                _subscribedManager.ConnectionApprovalCallback = null;
        }

        /// <summary>
        /// Prepara la configuracion antes de StartHost/StartClient: activa la aprobacion
        /// de conexiones y adjunta el nombre del jugador. Lo llama RelayManager.
        /// </summary>
        public void PrepareLocalConnection()
        {
            Subscribe();
            var nm = NetworkManager.Singleton;
            nm.NetworkConfig.ConnectionApproval = true;
            string name = LocalPlayerSettings.PlayerName;
            nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(name ?? string.Empty);
            _connectedClientIds.Clear();
            _pendingNames.Clear();
            _returningToMenu = false;
        }

        /// <summary>
        /// Corre solo en el servidor/host (tambien para el propio host).
        /// </summary>
        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            bool isHostItself = request.ClientNetworkId == NetworkManager.ServerClientId;
            bool raceInProgress = MapCatalog.IsRaceScene(SceneManager.GetActiveScene().name);
            bool full = _connectedClientIds.Count >= GameConstants.MAX_PLAYERS;

            // Los autos los crea RaceManager al cargar el circuito, no al conectarse.
            response.CreatePlayerObject = false;
            response.Pending = false;

            if (!isHostItself && (full || raceInProgress))
            {
                response.Approved = false;
                response.Reason = full ? "La sala esta llena (maximo 4)." : "La carrera ya comenzo.";
                return;
            }

            response.Approved = true;
            string requestedName = request.Payload != null && request.Payload.Length > 0
                ? Encoding.UTF8.GetString(request.Payload)
                : string.Empty;
            _pendingNames[request.ClientNetworkId] = requestedName;
        }

        private void HandleServerStarted()
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsServer) return;

            if (sessionStatePrefab == null)
            {
                Debug.LogError("[GameNetworkManager] Falta asignar sessionStatePrefab. Ejecuta TurboGrid > Configurar escenas.");
                return;
            }

            if (SessionState.Instance == null)
            {
                // destroyWithScene = false: sobrevive al cambio lobby -> carrera.
                nm.SpawnManager.InstantiateAndSpawn(sessionStatePrefab, NetworkManager.ServerClientId, destroyWithScene: false);
            }

            // El host ya esta conectado cuando llega OnServerStarted en algunas versiones.
            foreach (ulong id in nm.ConnectedClientsIds)
                RegisterPlayer(id);
        }

        private void HandleClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (!_connectedClientIds.Contains(clientId))
                _connectedClientIds.Add(clientId);

            if (nm.IsServer)
                RegisterPlayer(clientId);

            Debug.Log($"[GameNetworkManager] Cliente conectado: {clientId}. Total: {_connectedClientIds.Count}");
            OnPlayerJoined?.Invoke(clientId);
        }

        private void RegisterPlayer(ulong clientId)
        {
            if (!_connectedClientIds.Contains(clientId))
                _connectedClientIds.Add(clientId);

            if (SessionState.Instance == null) return;
            _pendingNames.TryGetValue(clientId, out string name);
            SessionState.Instance.AddPlayerServer(clientId, name);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            _connectedClientIds.Remove(clientId);
            _pendingNames.Remove(clientId);

            if (nm != null && nm.IsServer && SessionState.Instance != null)
                SessionState.Instance.RemovePlayerServer(clientId);

            Debug.Log($"[GameNetworkManager] Cliente desconectado: {clientId}. Total: {_connectedClientIds.Count}");
            OnPlayerLeft?.Invoke(clientId);

            // Si soy cliente y me desconectaron (host se fue, rechazo, error de red), volver al menu.
            if (nm != null && !nm.IsServer && clientId == nm.LocalClientId)
            {
                string reason = nm.DisconnectReason;
                ReturnToMainMenu(string.IsNullOrEmpty(reason) ? "El host cerro la partida o se perdio la conexion." : reason);
            }
        }

        private void HandleClientStopped(bool wasHost)
        {
            // Cubre el caso en que la conexion se cae sin OnClientDisconnectCallback local.
            if (!wasHost && !_returningToMenu && SceneManager.GetActiveScene().name != GameConstants.MAIN_MENU_SCENE)
            {
                ReturnToMainMenu("El host cerro la partida o se perdio la conexion.");
            }
        }

        /// <summary>
        /// Llamado por el host desde LobbyUI cuando hay 2-4 jugadores.
        /// mapIndex = -1 elige un circuito al azar.
        /// </summary>
        public bool HostStartRace(int mapIndex)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsHost) return false;
            if (_connectedClientIds.Count < GameConstants.MIN_PLAYERS)
            {
                Debug.LogWarning("[GameNetworkManager] Faltan jugadores para arrancar.");
                return false;
            }

            string sceneName = MapCatalog.ResolveScene(mapIndex);
            if (SessionState.Instance != null)
                SessionState.Instance.SetMapServer(mapIndex);

            var status = nm.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[GameNetworkManager] No se pudo cargar '{sceneName}': {status}. Revisa que este en Build Profiles.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Apaga la red y vuelve al menu. Si lo llama el host, todos los clientes
        /// son desconectados (y ellos tambien vuelven al menu).
        /// </summary>
        public void ReturnToMainMenu(string reason = null)
        {
            if (_returningToMenu) return;
            _returningToMenu = true;
            LastDisconnectReason = reason;

            if (RelayManager.Instance != null)
                RelayManager.Instance.Shutdown();
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            // La escena MainMenu trae su propio NetworkManager/Managers: destruimos los
            // persistentes para no quedar con duplicados.
            if (NetworkManager.Singleton != null)
                Destroy(NetworkManager.Singleton.gameObject);
            if (RelayManager.Instance != null && RelayManager.Instance.gameObject != gameObject)
                Destroy(RelayManager.Instance.gameObject);

            Destroy(gameObject);
            SceneManager.LoadScene(GameConstants.MAIN_MENU_SCENE);
        }
    }
}
