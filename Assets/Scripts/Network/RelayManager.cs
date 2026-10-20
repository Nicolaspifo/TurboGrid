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

        // Alfabeto del codigo sin caracteres confusos (sin O/0 ni I/1).
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 6;

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
                    LastError = "No se encontro ninguna partida con ese codigo en esta red.";
                    return false;
                }

                transport.SetConnectionData(host.Value.Address, (ushort)host.Value.Port);

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
        /// Genera un codigo de 6 letras/numeros aleatorios (sin caracteres
        /// confusos) usando el RNG criptografico del sistema.
        /// </summary>
        private static string GenerateJoinCode()
        {
            char[] chars = new char[CodeLength];
            byte[] bytes = new byte[CodeLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            for (int i = 0; i < CodeLength; i++)
                chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
            return new string(chars);
        }
    }
}
