using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace FlowerWms.Tsd.Services;

public class ServerDiscoveryService
{
    private readonly SecureStorageService _secureStorage;
    private const string STORAGE_KEY = "server_address";
    private const int DEFAULT_PORT = 5152;
    private const int TIMEOUT_MS = 1200;        // 1200 мс на один запрос
    private const int MAX_PARALLEL = 20;        // 30 параллельных запросов

    // Событие для уведомления о прогрессе поиска
    public event EventHandler<string>? ScanProgressChanged;

    public ServerDiscoveryService()
    {
        _secureStorage = new SecureStorageService();
    }

    public async Task<string?> GetSavedServerAddress()
    {
        try
        {
            var address = await _secureStorage.GetAsync(STORAGE_KEY);
            if (!string.IsNullOrEmpty(address))
            {
                return address;
            }
        }
        catch (Exception ex) { }
        return null;
    }

    public async Task SaveServerAddress(string address)
    {
        try
        {
            await _secureStorage.SaveAsync(STORAGE_KEY, address);
        }
        catch (Exception ex) { }
    }

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
        catch (Exception ex) { }

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
        catch (Exception ex) { }

        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            if (endPoint != null)
            {
                var ip = endPoint.Address.ToString();
                return ip;
            }
        }
        catch (Exception ex) { }

        return null;
    }

    public async Task<string?> DiscoverServer()
    {
        // 1. Проверяем сохраненный адрес
        var savedAddress = await GetSavedServerAddress();
        if (!string.IsNullOrEmpty(savedAddress))
        {
            if (await PingServer(savedAddress))
            {
                return savedAddress;
            }
        }

        // 2. Получаем локальный IP
        var localIp = GetLocalIpAddress();
        if (string.IsNullOrEmpty(localIp) || !localIp.Contains('.'))
        {
            return null;
        }

        var lastDotIndex = localIp.LastIndexOf('.');
        var baseIp = localIp.Substring(0, lastDotIndex + 1);

        var scanRange = $"🔍 Сканирование: {baseIp}1-254";
        ScanProgressChanged?.Invoke(this, scanRange);

        // 3. Формируем список адресов
        var candidates = new List<string>();
        for (int i = 1; i <= 254; i++)
        {
            var ip = $"{baseIp}{i}";
            if (ip != localIp)
            {
                candidates.Add($"http://{ip}:{DEFAULT_PORT}");
            }
        }

        // 4. Параллельный поиск — БЕЗ ОБЩЕГО ТАЙМАУТА!
        string? foundAddress = null;
        int checkedCount = 0;
        int totalCount = candidates.Count;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MAX_PARALLEL
            // ❌ Убираем CancellationTokenSource с таймаутом
        };

        try
        {
            await Parallel.ForEachAsync(candidates, parallelOptions, async (address, token) =>
            {
                if (foundAddress != null) return;

                var current = Interlocked.Increment(ref checkedCount);
                if (current % 10 == 0 || current == totalCount)
                {
                    var progress = $"🔍 Сканирование: {current}/{totalCount}";
                    ScanProgressChanged?.Invoke(this, progress);
                }

                if (await PingServer(address))
                {
                    foundAddress = address;
                }
            });
        }
        catch (Exception ex)
        {
            // Игнорируем ошибки
        }

        // 5. Обработка результата
        if (!string.IsNullOrEmpty(foundAddress))
        {
            ScanProgressChanged?.Invoke(this, $"✅ Сервер найден: {foundAddress}");
            await SaveServerAddress(foundAddress);
            return foundAddress;
        }

        ScanProgressChanged?.Invoke(this, "❌ Сервер не найден");
        return null;
    }

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