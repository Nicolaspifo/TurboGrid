using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TurboGrid
{
    /// <summary>Resultado de una busqueda de host en la red local.</summary>
    public struct LanHostInfo
    {
        public readonly string Address;
        public readonly int Port;

        public LanHostInfo(string address, int port)
        {
            Address = address;
            Port = port;
        }
    }

    /// <summary>
    /// Descubrimiento de partidas en la red local por UDP. No usa internet ni
    /// servicios en la nube: el host responde en el puerto de descubrimiento
    /// solo cuando le preguntan por SU codigo, y el cliente encuentra la IP del
    /// host a partir de la respuesta (broadcast + loopback).
    ///
    /// Protocolo (texto ASCII):
    ///   Cliente -> "TGQ|CODIGO"   (broadcast a la red local)
    ///   Host    -> "TGA|CODIGO|PUERTO"  (unicast al que pregunto)
    /// </summary>
    public static class LanDiscovery
    {
        public const int DiscoveryPort = 47777;

        private const string QueryPrefix = "TGQ|";
        private const string AnswerPrefix = "TGA|";
        private const float DefaultTimeoutSeconds = 8f;
        private const float ResendIntervalSeconds = 0.5f;

        // =====================================================================
        // Lado host: respondedor de preguntas
        // =====================================================================

        private static Thread _serverThread;
        private static volatile bool _serverRunning;
        private static UdpClient _serverSocket;

        /// <summary>
        /// Empieza a responder preguntas de busqueda mientras el codigo coincida
        /// con <paramref name="joinCode"/>. Se puede llamar varias veces: antes
        /// se detiene la instancia anterior.
        /// </summary>
        public static void StartHostResponder(string joinCode, int gamePort)
        {
            StopHostResponder();

            string expectedCode = joinCode != null ? joinCode.Trim().ToUpperInvariant() : null;
            if (string.IsNullOrEmpty(expectedCode))
            {
                Debug.LogError("[LanDiscovery] No se puede iniciar el respondedor sin codigo.");
                return;
            }

            _serverRunning = true;
            _serverThread = new Thread(() => ServerLoop(expectedCode, gamePort))
            {
                IsBackground = true,
                Name = "TurboGrid-LanDiscovery-Host"
            };
            _serverThread.Start();
        }

        public static void StopHostResponder()
        {
            _serverRunning = false;
            try { _serverSocket?.Close(); }
            catch { /* el hilo lo limpia solo */ }
            _serverSocket = null;
            _serverThread = null;
        }

        private static void ServerLoop(string expectedCode, int gamePort)
        {
            UdpClient socket = null;
            try
            {
                socket = new UdpClient();
                socket.ExclusiveAddressUse = false;
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.EnableBroadcast = true;
                socket.Client.ReceiveTimeout = 500;
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _serverSocket = socket;

                byte[] answer = Encoding.ASCII.GetBytes(AnswerPrefix + expectedCode + "|" + gamePort);

                while (_serverRunning)
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data;
                    try
                    {
                        data = socket.Receive(ref sender);
                    }
                    catch (SocketException)
                    {
                        continue; // timeout del Receive: solo marcamos que seguimos vivos
                    }
                    catch (ObjectDisposedException)
                    {
                        break; // nos cerraron desde StopHostResponder
                    }

                    string message = Encoding.ASCII.GetString(data);
                    if (!message.StartsWith(QueryPrefix, StringComparison.Ordinal))
                        continue;

                    string askedCode = message.Substring(QueryPrefix.Length).Trim().ToUpperInvariant();
                    if (askedCode != expectedCode)
                        continue; // es la partida de otro host

                    try
                    {
                        socket.Send(answer, answer.Length, sender);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[LanDiscovery] No se pudo responder a {sender}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LanDiscovery] El respondedor de la red local fallo: {e.Message}");
            }
            finally
            {
                try { socket?.Close(); }
                catch { }
                if (_serverSocket == socket) _serverSocket = null;
            }
        }

        // =====================================================================
        // Lado cliente: buscar el host de un codigo
        // =====================================================================

        /// <summary>
        /// Busca en la red local al host cuyo codigo es <paramref name="joinCode"/>.
        /// Devuelve la IP y el puerto del juego, o null si nadie respondio a tiempo.
        /// Corre en un hilo de fondo; es seguro llamarlo desde el hilo principal.
        /// </summary>
        public static Task<LanHostInfo?> FindHostAsync(
            string joinCode,
            float timeoutSeconds = DefaultTimeoutSeconds,
            Action<float> onProgress = null)
        {
            return Task.Run(() => FindHostBlocking(joinCode, timeoutSeconds, onProgress));
        }

        private static LanHostInfo? FindHostBlocking(
            string joinCode,
            float timeoutSeconds,
            Action<float> onProgress)
        {
            string code = joinCode != null ? joinCode.Trim().ToUpperInvariant() : null;
            if (string.IsNullOrEmpty(code))
                return null;

            byte[] query = Encoding.ASCII.GetBytes(QueryPrefix + code);
            List<IPEndPoint> targets = BuildBroadcastTargets();

            DateTime start = DateTime.UtcNow;
            DateTime deadline = start.AddSeconds(Mathf.Max(2f, timeoutSeconds));
            DateTime nextSend = DateTime.MinValue;

            using (UdpClient client = new UdpClient())
            {
                client.EnableBroadcast = true;
                client.Client.ReceiveTimeout = 400;

                while (DateTime.UtcNow < deadline)
                {
                    if (DateTime.UtcNow >= nextSend)
                    {
                        foreach (IPEndPoint target in targets)
                        {
                            try { client.Send(query, query.Length, target); }
                            catch (SocketException) { /* interfaz no disponible, seguimos */ }
                        }
                        nextSend = DateTime.UtcNow.AddSeconds(ResendIntervalSeconds);

                        float elapsed = (float)(DateTime.UtcNow - start).TotalSeconds;
                        onProgress?.Invoke(elapsed);
                    }

                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data;
                    try
                    {
                        data = client.Receive(ref sender);
                    }
                    catch (SocketException)
                    {
                        continue; // timeout del Receive, seguimos esperando
                    }

                    string message = Encoding.ASCII.GetString(data);
                    if (!message.StartsWith(AnswerPrefix, StringComparison.Ordinal))
                        continue;

                    // Formato: TGA|CODIGO|PUERTO
                    string[] parts = message.Split('|');
                    if (parts.Length < 3)
                        continue;
                    if (!string.Equals(parts[1], code, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!int.TryParse(parts[2], out int port) || port <= 0 || port > 65535)
                        continue;

                    Debug.Log($"[LanDiscovery] Host encontrado para el codigo {code}: {sender.Address}:{port}");
                    return new LanHostInfo(sender.Address.ToString(), port);
                }
            }

            Debug.LogWarning($"[LanDiscovery] Nadie respondio al codigo {code} en {timeoutSeconds:0.#} s.");
            return null;
        }

        /// <summary>
        /// Destinos a los que se manda la pregunta: el broadcast general de la
        /// red (255.255.255.255), el broadcast calculado de cada interfaz local
        /// (ej. 192.168.1.255) y el loopback (para host y cliente en el mismo
        /// equipo, por ejemplo el editor).
        /// </summary>
        private static List<IPEndPoint> BuildBroadcastTargets()
        {
            var targets = new List<IPEndPoint>
            {
                new IPEndPoint(IPAddress.Broadcast, DiscoveryPort),
                new IPEndPoint(IPAddress.Loopback, DiscoveryPort)
            };

            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (UnicastIPAddressInformation uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;

                        IPAddress mask = uni.IPv4Mask;
                        if (mask == null)
                            continue;

                        byte[] ipBytes = uni.Address.GetAddressBytes();
                        byte[] maskBytes = mask.GetAddressBytes();
                        if (ipBytes.Length != 4 || maskBytes.Length != 4)
                            continue;

                        byte[] broadcastBytes = new byte[4];
                        for (int i = 0; i < 4; i++)
                            broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);

                        var broadcast = new IPAddress(broadcastBytes);
                        bool already = false;
                        foreach (IPEndPoint t in targets)
                            if (t.Address.Equals(broadcast)) { already = true; break; }
                        if (!already)
                            targets.Add(new IPEndPoint(broadcast, DiscoveryPort));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LanDiscovery] No se pudieron listar las interfaces de red: {e.Message}");
            }

            return targets;
        }
    }
}
