using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

using Unity.Networking.Transport.Relay;

namespace TurboGrid
{
    /// <summary>
    /// Encapsula todo lo relacionado con Unity Relay: el celular que crea la partida
    /// se convierte en Host (servidor + cliente local), y el resto se conectan
    /// como clientes usando el codigo de union (join code) de 6 caracteres.
    ///
    /// Requiere los paquetes:
    ///   - com.unity.services.multiplayer  (incluye Relay + Authentication + Sessions)
    ///   - com.unity.netcode.gameobjects
    ///   - com.unity.transport
    ///
    /// Configura tu proyecto en el Unity Dashboard (Services > Linked Cloud Project)
    /// antes de probar en dispositivos reales.
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        public static RelayManager Instance { get; private set; }

        [SerializeField] private string connectionType = "dtls"; // "dtls" para conexion segura, "udp" para testing
        [SerializeField] private int maxConnections = GameConstants.MAX_PLAYERS - 1; // el host no cuenta como conexion entrante

        public string CurrentJoinCode { get; private set; }
        public bool IsInitializing { get; private set; }

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

        /// <summary>
        /// Debe llamarse una vez al arrancar la app (por ejemplo desde el MainMenu),
        /// antes de crear o unirse a una partida.
        /// </summary>
        public async Task EnsureSignedInAsync()
        {
            if (IsInitializing) return;
            IsInitializing = true;

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                Debug.Log($"[RelayManager] Sesion iniciada. PlayerId: {AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RelayManager] Error al inicializar servicios: {e}");
            }
            finally
            {
                IsInitializing = false;
            }
        }

        /// <summary>
        /// El movil que llama esto se convierte en HOST (servidor + cliente local).
        /// Devuelve el join code que los demas jugadores deben ingresar.
        /// </summary>
        public async Task<string> StartHostWithRelayAsync()
        {
            await EnsureSignedInAsync();

            try
            {
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                CurrentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                RelayServerData relayServerData = allocation.ToRelayServerData(connectionType);
                transport.SetRelayServerData(relayServerData);

                bool started = NetworkManager.Singleton.StartHost();
                if (!started)
                {
                    Debug.LogError("[RelayManager] StartHost() fallo.");
                    return null;
                }

                Debug.Log($"[RelayManager] Host iniciado. Join code: {CurrentJoinCode}");
                return CurrentJoinCode;
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"[RelayManager] Error creando allocation: {e}");
                return null;
            }
        }

        /// <summary>
        /// Los demas moviles llaman esto con el codigo que les paso el host.
        /// </summary>
        public async Task<bool> JoinWithRelayAsync(string joinCode)
        {
            await EnsureSignedInAsync();

            try
            {
                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                RelayServerData relayServerData = joinAllocation.ToRelayServerData(connectionType);
                transport.SetRelayServerData(relayServerData);

                bool started = NetworkManager.Singleton.StartClient();
                if (!started)
                {
                    Debug.LogError("[RelayManager] StartClient() fallo.");
                    return false;
                }

                Debug.Log("[RelayManager] Union a la partida exitosa.");
                return true;
            }
            catch (RelayServiceException e)
            {
                Debug.LogError($"[RelayManager] Codigo invalido o error al unirse: {e}");
                return false;
            }
        }

        public void Shutdown()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            CurrentJoinCode = null;
        }
    }
}
