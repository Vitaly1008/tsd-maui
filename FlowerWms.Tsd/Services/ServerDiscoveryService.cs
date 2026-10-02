using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace FlowerWms.Tsd.Services;

// Поиск сервера в локальной сети
public class ServerDiscoveryService
{
    private readonly SecureStorageService _secureStorage;
    private const string STORAGE_KEY = "server_address";
    private const int DEFAULT_PORT = 5152;
    private const int TIMEOUT_MS = 5000;   // ✅ было 1200
    private const int MAX_PARALLEL = 40;   // ✅ было 20

    public event EventHandler<string>? ScanProgressChanged;

    public ServerDiscoveryService(SecureStorageService secureStorage)
    {
        _secureStorage = secureStorage;
    }

    // Возвращает сохраненный адрес сервера
    public async Task<string?> GetSavedServerAddress()
    {
        try
        {
            var address = await _secureStorage.GetServerAddress(STORAGE_KEY); // ✅ переименовано
            if (!string.IsNullOrEmpty(address))
            {
                return address;
            }
        }
        catch
        {
            // Игнорируем ошибки
        }
        return null;
    }

    // Сохраняет адрес сервера
    public async Task SaveServerAddress(string address)
    {
        try
        {
            await _secureStorage.SaveServerAddress(STORAGE_KEY, address); // ✅ переименовано
        }
        catch
        {
            // Игнорируем ошибки
        }
    }

    // Проверяет доступность сервера по адресу
    public async Task<bool> PingServer(string address)
    {
        try
        {
            if (string.IsNullOrEmpty(address))
                return false;

            var cleanAddress = address.TrimEnd('/');

            var urls = new[]
            {
                $"{cleanAddress}/api/ping",
                $"{cleanAddress}/ping"
            };

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(TIMEOUT_MS);

            foreach (var url in urls)
            {
                try
                {
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Игнорируем
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // Возвращает локальный IP-адрес устройства
    public string? GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ipStr = ip.ToString();
                    if (!ipStr.StartsWith("127."))
                    {
                        return ipStr;
                    }
                }
            }
        }
        catch { }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ipStr = ip.Address.ToString();
                            if (!ipStr.StartsWith("127."))
                            {
                                return ipStr;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            if (endPoint != null)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        return null;
    }

    // Выполняет поиск сервера в локальной сети
    public async Task<string?> DiscoverServer()
    {
        var savedAddress = await GetSavedServerAddress();
        if (!string.IsNullOrEmpty(savedAddress))
        {
            if (await PingServer(savedAddress))
            {
                return savedAddress;
            }
        }

        var localIp = GetLocalIpAddress();
        if (string.IsNullOrEmpty(localIp) || !localIp.Contains('.'))
        {
            return null;
        }

        var lastDotIndex = localIp.LastIndexOf('.');
        var baseIp = localIp.Substring(0, lastDotIndex + 1);

        var scanRange = $"Сканирование: {baseIp}1-254";
        ScanProgressChanged?.Invoke(this, scanRange);

        var candidates = new List<string>();
        for (int i = 1; i <= 254; i++)
        {
            var ip = $"{baseIp}{i}";
            if (ip != localIp)
            {
                candidates.Add($"http://{ip}:{DEFAULT_PORT}");
            }
        }

        // ✅ ИСПРАВЛЕНО: потокобезопасная переменная
        string? foundAddress = null;
        int checkedCount = 0;
        int totalCount = candidates.Count;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MAX_PARALLEL
        };

        try
        {
            await Parallel.ForEachAsync(candidates, parallelOptions, async (address, token) =>
            {
                // ✅ ИСПРАВЛЕНО: волатильное чтение
                if (Volatile.Read(ref foundAddress) != null) return;

                var current = Interlocked.Increment(ref checkedCount);
                if (current % 10 == 0 || current == totalCount)
                {
                    var progress = $"Сканирование: {current}/{totalCount}";
                    ScanProgressChanged?.Invoke(this, progress);
                }

                if (await PingServer(address))
                {
                    // ✅ ИСПРАВЛЕНО: атомарная запись, только первый победит
                    Interlocked.CompareExchange(ref foundAddress, address, null);
                }
            });
        }
        catch
        {
            // Игнорируем ошибки
        }

        if (!string.IsNullOrEmpty(foundAddress))
        {
            ScanProgressChanged?.Invoke(this, $"Сервер найден: {foundAddress}");
            await SaveServerAddress(foundAddress);
            return foundAddress;
        }

        ScanProgressChanged?.Invoke(this, "Сервер не найден");
        return null;
    }

    // Возвращает адрес сервера (с проверкой сохраненного или поиском)
    public async Task<string?> GetServerAddress()
    {
        var saved = await GetSavedServerAddress();
        if (!string.IsNullOrEmpty(saved))
        {
            if (await PingServer(saved))
            {
                return saved;
            }
        }

        return await DiscoverServer();
    }
}