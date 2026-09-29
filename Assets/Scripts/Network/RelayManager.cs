using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Encapsula todo lo relacionado con Unity Relay: el celular que crea la partida
    /// se convierte en Host (servidor + cliente local), y el resto se conectan
    /// como clientes usando el codigo de union (join code) de 6 caracteres.
    /// Funciona igual entre iOS y Android porque va por internet.
    ///
    /// Requiere el proyecto vinculado en Unity Cloud con Relay activado.
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        public static RelayManager Instance { get; private set; }

        [SerializeField] private string connectionType = "dtls"; // "dtls" = seguro; "udp" para pruebas
        [SerializeField] private int maxConnections = GameConstants.MAX_PLAYERS - 1; // el host no cuenta

        public string CurrentJoinCode { get; private set; }
        public string LastError { get; private set; }

        private Task _signInTask;

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

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Inicializa Unity Services e inicia sesion anonima. Es seguro llamarlo varias
        /// veces: todas esperan la misma tarea.
        /// </summary>
        public Task EnsureSignedInAsync()
        {
            if (_signInTask == null || _signInTask.IsFaulted || _signInTask.IsCanceled)
                _signInTask = SignInInternalAsync();
            return _signInTask;
        }

        private async Task SignInInternalAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
#if UNITY_EDITOR
                // En el editor (y en las ventanas de Multiplayer Play Mode) cada proceso
                // necesita su propio perfil; si no, todos comparten el mismo PlayerId.
                options.SetProfile("editor" + System.Diagnostics.Process.GetCurrentProcess().Id);
#endif
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log($"[RelayManager] Sesion iniciada. PlayerId: {AuthenticationService.Instance.PlayerId}");
        }

        /// <summary>
        /// El movil que llama esto se convierte en HOST. Devuelve el join code o null si fallo
        /// (el motivo queda en LastError).
        /// </summary>
        public async Task<string> StartHostWithRelayAsync()
        {
            LastError = null;
            try
            {
                await EnsureSignedInAsync();

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                CurrentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetRelayServerData(allocation.ToRelayServerData(connectionType));

                GameNetworkManager.Instance?.PrepareLocalConnection();

                if (!NetworkManager.Singleton.StartHost())
                {
                    LastError = "No se pudo iniciar el host.";
                    return null;
                }

                Debug.Log($"[RelayManager] Host iniciado. Join code: {CurrentJoinCode}");
                return CurrentJoinCode;
            }
            catch (Exception e)
            {
                LastError = "Error de red al crear la partida. Revisa tu conexion a internet.";
                Debug.LogError($"[RelayManager] Error creando la partida: {e}");
                return null;
            }
        }

        /// <summary>
        /// Los demas moviles llaman esto con el codigo que les paso el host.
        /// </summary>
        public async Task<bool> JoinWithRelayAsync(string joinCode)
        {
            LastError = null;
            try
            {
                await EnsureSignedInAsync();

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                transport.SetRelayServerData(joinAllocation.ToRelayServerData(connectionType));

                GameNetworkManager.Instance?.PrepareLocalConnection();

                if (!NetworkManager.Singleton.StartClient())
                {
                    LastError = "No se pudo iniciar el cliente.";
                    return false;
                }

                CurrentJoinCode = joinCode;
                Debug.Log("[RelayManager] Conectando a la partida...");
                return true;
            }
            catch (RelayServiceException e)
            {
                LastError = "Codigo invalido o la sala ya no existe.";
                Debug.LogWarning($"[RelayManager] Codigo invalido o error al unirse: {e}");
                return false;
            }
            catch (Exception e)
            {
                LastError = "Error de red al unirse. Revisa tu conexion a internet.";
                Debug.LogError($"[RelayManager] Error al unirse: {e}");
                return false;
            }
        }

        public void Shutdown()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            CurrentJoinCode = null;
        }
    }
}
