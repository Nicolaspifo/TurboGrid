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
    ///   Cliente -> "TGQ|CODIGO"   (a la red local)
    ///   Host    -> "TGA|CODIGO|PUERTO"  (unicast al que pregunto)
    ///
    /// iPhone: desde iOS 14 el broadcast necesita un permiso especial de Apple
    /// (multicast entitlement). Para no depender de el, el cliente pregunta
    /// tambien por UNICAST: primero a la IP que viene escondida en el codigo
    /// (ver <see cref="EncodeHostHint"/>) y luego a cada IP de su subred.
    /// Unicast solo necesita el permiso normal de "Red local".
    /// </summary>
    public static class LanDiscovery
    {
        public const int DiscoveryPort = 47777;

        private const string QueryPrefix = "TGQ|";
        private const string AnswerPrefix = "TGA|";
        private const float DefaultTimeoutSeconds = 8f;
        private const float ResendIntervalSeconds = 0.5f;
        private const float ScanIntervalSeconds = 1.5f;
        private const int MaxScanHosts = 1024; // subredes hasta /22

        // Alfabeto del codigo (sin O/0 ni I/1): 32 simbolos = 5 bits por caracter.
        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        // =====================================================================
        // Codigo de 6 caracteres con pista de la IP del host
        // =====================================================================

        /// <summary>
        /// Arma un codigo de 6 caracteres (30 bits): los 16 bits bajos son los dos
        /// ultimos numeros de la IP del host (ej. 1.37 de 192.168.1.37) y el resto
        /// es aleatorio. El cliente completa la IP con los dos primeros numeros de
        /// SU propia IP (estan en la misma red) y le pregunta directo al host.
        /// </summary>
        public static string EncodeHostHint(IPAddress hostIp, int randomBits)
        {
            int low16 = 0;
            if (hostIp != null)
            {
                byte[] b = hostIp.GetAddressBytes();
                if (b.Length == 4) low16 = (b[2] << 8) | b[3];
            }
            int value = ((randomBits & 0x3FFF) << 16) | low16; // 14 + 16 = 30 bits
            char[] chars = new char[6];
            for (int i = 5; i >= 0; i--)
            {
                chars[i] = CodeAlphabet[value & 31];
                value >>= 5;
            }
            return new string(chars);
        }

        /// <summary>Los 16 bits de IP escondidos en el codigo, o -1 si el codigo no es valido.</summary>
        public static int DecodeHostHint(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 6) return -1;
            int value = 0;
            foreach (char c in code.ToUpperInvariant())
            {
                int idx = CodeAlphabet.IndexOf(c);
                if (idx < 0) return -1;
                value = (value << 5) | idx;
            }
            return value & 0xFFFF;
        }

        /// <summary>IPv4 local principal (Wi-Fi), o null si no hay red.</summary>
        public static IPAddress GetLocalIPv4()
        {
            IPAddress fallback = null;
            foreach (var (address, _) in LocalIPv4WithMask())
            {
                byte[] b = address.GetAddressBytes();
                bool privateNet = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                if (privateNet) return address;
                if (fallback == null) fallback = address;
            }
            if (fallback != null) return fallback;

            // Respaldo: el sistema elige la interfaz de salida (no envia nada).
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("10.255.255.255", 1);
                    return (s.LocalEndPoint as IPEndPoint)?.Address;
                }
            }
            catch { return null; }
        }

        /// <summary>IPv4 de las interfaces activas (sin loopback) con su mascara (/24 si no se conoce).</summary>
        private static List<(IPAddress address, IPAddress mask)> LocalIPv4WithMask()
        {
            var list = new List<(IPAddress, IPAddress)>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up && nic.OperationalStatus != OperationalStatus.Unknown)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (UnicastIPAddressInformation uni in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] b = uni.Address.GetAddressBytes();
                        if (b[0] == 127 || (b[0] == 169 && b[1] == 254)) continue; // loopback / sin DHCP
                        IPAddress mask = null;
                        try { mask = uni.IPv4Mask; } catch { /* algunas plataformas no lo soportan */ }
                        if (mask == null || mask.Equals(IPAddress.Any)) mask = new IPAddress(new byte[] { 255, 255, 255, 0 });
                        list.Add((uni.Address, mask));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LanDiscovery] No se pudieron listar las interfaces de red: {e.Message}");
            }
            return list;
        }

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
            AndroidMulticast.Acquire();
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
            AndroidMulticast.Release();
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
        public static async Task<LanHostInfo?> FindHostAsync(
            string joinCode,
            float timeoutSeconds = DefaultTimeoutSeconds,
            Action<float> onProgress = null)
        {
            // El await vuelve al hilo principal de Unity, donde se puede llamar a Java.
            AndroidMulticast.Acquire();
            try { return await Task.Run(() => FindHostBlocking(joinCode, timeoutSeconds, onProgress)); }
            finally { AndroidMulticast.Release(); }
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
            List<IPEndPoint> hinted = BuildHintTargets(code);
            List<IPEndPoint> scan = BuildScanTargets();
            DateTime nextScan = DateTime.MinValue;

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
                        // 1) Directo a la IP escondida en el codigo, 2) broadcast (Android/PC).
                        SendAll(client, query, hinted);
                        SendAll(client, query, targets);
                        // 3) Barrido unicast de la subred (iPhone sin permiso de broadcast).
                        if (DateTime.UtcNow >= nextScan)
                        {
                            SendAll(client, query, scan);
                            nextScan = DateTime.UtcNow.AddSeconds(ScanIntervalSeconds);
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

        private static void SendAll(UdpClient client, byte[] data, List<IPEndPoint> targets)
        {
            foreach (IPEndPoint target in targets)
            {
                try { client.Send(data, data.Length, target); }
                catch (SocketException) { /* destino no alcanzable, seguimos */ }
                catch (Exception) { /* iOS sin permiso de red local: seguimos con el resto */ }
            }
        }

        /// <summary>IP del host a partir del codigo + los dos primeros numeros de mi IP.</summary>
        private static List<IPEndPoint> BuildHintTargets(string code)
        {
            var list = new List<IPEndPoint>();
            int hint = DecodeHostHint(code);
            if (hint < 0) return list;
            foreach (var (address, _) in LocalIPv4WithMask())
            {
                byte[] b = address.GetAddressBytes();
                var ip = new IPAddress(new[] { b[0], b[1], (byte)(hint >> 8), (byte)(hint & 0xFF) });
                if (!list.Exists(t => t.Address.Equals(ip))) list.Add(new IPEndPoint(ip, DiscoveryPort));
            }
            return list;
        }

        /// <summary>Todas las IP de mis subredes (hasta /22), sin la mia.</summary>
        private static List<IPEndPoint> BuildScanTargets()
        {
            var list = new List<IPEndPoint>();
            foreach (var (address, mask) in LocalIPv4WithMask())
            {
                uint ip = ToUInt(address), m = ToUInt(mask);
                uint hostCount = ~m;
                if (hostCount < 2 || hostCount > MaxScanHosts)
                {
                    m = 0xFFFFFF00; // red muy grande: solo el /24 propio
                    hostCount = 0xFF;
                }
                uint network = ip & m;
                for (uint h = 1; h < hostCount && list.Count < 4 * MaxScanHosts; h++)
                {
                    uint target = network | h;
                    if (target == ip) continue;
                    list.Add(new IPEndPoint(FromUInt(target), DiscoveryPort));
                }
            }
            return list;
        }

        private static uint ToUInt(IPAddress a)
        {
            byte[] b = a.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static IPAddress FromUInt(uint v) =>
            new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

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

    /// <summary>
    /// Android descarta los paquetes broadcast con el Wi-Fi en ahorro de energia;
    /// un MulticastLock los deja pasar mientras se busca o se anuncia la partida.
    /// </summary>
    internal static class AndroidMulticast
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _lock;
        private static int _refs;
#endif

        public static void Acquire()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _refs++;
                if (_lock != null) return;
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    _lock = wifi.Call<AndroidJavaObject>("createMulticastLock", "TurboGrid");
                    _lock.Call("setReferenceCounted", false);
                    _lock.Call("acquire");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LanDiscovery] Sin MulticastLock (se sigue por unicast): {e.Message}");
                _lock = null;
            }
#endif
        }

        public static void Release()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _refs = Math.Max(0, _refs - 1);
            if (_refs > 0 || _lock == null) return;
            try { _lock.Call("release"); } catch { }
            _lock.Dispose();
            _lock = null;
#endif
        }
    }
}
