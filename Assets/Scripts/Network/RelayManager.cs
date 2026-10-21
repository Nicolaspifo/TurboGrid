using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>
    /// Gestion de la sesion de juego en la red local (LAN, 100% offline).
    ///
    /// El movil que crea la partida se convierte en Host (servidor + cliente
    /// local) y genera un codigo de 6 letras; el resto se unen con ese codigo.
    /// La IP del host se resuelve automaticamente dentro de la misma red con
    /// LanDiscovery (UDP broadcast), sin internet y sin servicios en la nube.
    ///
    /// (El nombre de la clase se mantiene por compatibilidad con la escena
    /// MainMenu, que lo referencia como componente.)
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        public static RelayManager Instance { get; private set; }

        [Tooltip("Puerto UDP en el que el host escucha las conexiones de la partida.")]
        [SerializeField] private int gamePort = 7777;

        [Tooltip("Segundos que el cliente busca al host en la red antes de rendirse.")]
        [SerializeField] private float joinTimeoutSeconds = 8f;

        public string CurrentJoinCode { get; private set; }
        public string LastError { get; private set; }

        [Tooltip("Segundos sin noticias del otro lado para dar la conexion por perdida (antes 30 s).")]
        [SerializeField] private int disconnectTimeoutMs = 6000;

        /// <summary>IP local del host (para mostrarla en la Sala).</summary>
        public string HostAddress { get; private set; }

        /// <summary>
        /// Que el celular no apague la pantalla mientras se juega: si el anfitrion se
        /// bloquea, la partida se cae para todos.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void KeepScreenOn()
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

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
            LanDiscovery.StopHostResponder();
        }

        /// <summary>
        /// El movil que llama esto se convierte en HOST. Devuelve el join code o null si fallo
        /// (el motivo queda en LastError).
        /// </summary>
        public Task<string> StartHostAsync()
        {
            LastError = null;
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    LastError = "Falta el NetworkManager en la escena.";
                    return Task.FromResult<string>(null);
                }

                var transport = nm.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    LastError = "Falta el transporte de red en la escena.";
                    return Task.FromResult<string>(null);
                }

                // Escucha en TODAS las interfaces (0.0.0.0) para que otros
                // dispositivos de la red local puedan alcanzar el host.
                transport.SetConnectionData("127.0.0.1", (ushort)gamePort, "0.0.0.0");
                ConfigureTimeouts(transport);

                CurrentJoinCode = GenerateJoinCode();
                LanDiscovery.StartHostResponder(CurrentJoinCode, gamePort);

                GameNetworkManager.Instance?.PrepareLocalConnection();

                if (!nm.StartHost())
                {
                    LanDiscovery.StopHostResponder();
                    CurrentJoinCode = null;
                    LastError = "No se pudo iniciar el host.";
                    return Task.FromResult<string>(null);
                }

                Debug.Log($"[RelayManager] Host iniciado en la red local (puerto {gamePort}). Codigo: {CurrentJoinCode}");
                return Task.FromResult(CurrentJoinCode);
            }
            catch (Exception e)
            {
                LanDiscovery.StopHostResponder();
                CurrentJoinCode = null;
                LastError = "No se pudo crear la partida en la red local.";
                Debug.LogError($"[RelayManager] Error creando la partida: {e}");
                return Task.FromResult<string>(null);
            }
        }

        /// <summary>
        /// Los demas moviles llaman esto con el codigo que les paso el host.
        /// Busca al host en la red local y se conecta directamente a el.
        /// </summary>
        public async Task<bool> JoinAsync(string joinCode)
        {
            LastError = null;
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null)
                {
                    LastError = "Falta el NetworkManager en la escena.";
                    return false;
                }

                var transport = nm.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    LastError = "Falta el transporte de red en la escena.";
                    return false;
                }

                LanHostInfo? host = await LanDiscovery.FindHostAsync(joinCode, joinTimeoutSeconds);
                if (this == null) return false; // la escena cambio mientras buscamos

                if (host == null)
                {
                    LastError = "No se encontro la partida. ¿Estan en la misma Wi-Fi? En iPhone acepta el permiso de \"Red local\" (Ajustes > Turbo Grid).";
                    return false;
                }

                transport.SetConnectionData(host.Value.Address, (ushort)host.Value.Port);
                ConfigureTimeouts(transport);

                GameNetworkManager.Instance?.PrepareLocalConnection();

                if (!nm.StartClient())
                {
                    LastError = "No se pudo iniciar el cliente.";
                    return false;
                }

                CurrentJoinCode = joinCode != null ? joinCode.Trim().ToUpperInvariant() : null;
                Debug.Log($"[RelayManager] Conectando al host {host.Value.Address}:{host.Value.Port}...");
                return true;
            }
            catch (Exception e)
            {
                LastError = "No se pudo conectar con el host en la red local.";
                Debug.LogError($"[RelayManager] Error al unirse: {e}");
                return false;
            }
        }

        public void Shutdown()
        {
            LanDiscovery.StopHostResponder();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
            CurrentJoinCode = null;
        }

        /// <summary>
        /// Si el anfitrion se cae (bateria, pantalla bloqueada, salio de la app), los
        /// demas se enteran en ~6 s en vez de quedarse congelados 30 s.
        /// </summary>
        private void ConfigureTimeouts(UnityTransport transport)
        {
            transport.HeartbeatTimeoutMS = 500;
            transport.DisconnectTimeoutMS = Mathf.Max(2000, disconnectTimeoutMs);
        }

        /// <summary>
        /// Codigo de 6 caracteres (sin O/0 ni I/1) que lleva escondidos los dos ultimos
        /// numeros de la IP del host: asi el cliente le pregunta directo, sin broadcast.
        /// </summary>
        private string GenerateJoinCode()
        {
            byte[] bytes = new byte[2];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            var ip = LanDiscovery.GetLocalIPv4();
            HostAddress = ip != null ? ip.ToString() : null;
            return LanDiscovery.EncodeHostHint(ip, (bytes[0] << 8) | bytes[1]);
        }
    }
}
