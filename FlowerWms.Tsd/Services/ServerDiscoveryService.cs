using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace FlowerWms.Tsd.Services;

public class ServerDiscoveryService
{
    private readonly SecureStorageService _secureStorage;
    private const string STORAGE_KEY = "server_address";
    private const int DEFAULT_PORT = 5152;
    private const int TIMEOUT_MS = 2000; // 2 секунды на адрес

    public ServerDiscoveryService()
    {
        _secureStorage = new SecureStorageService();
    }

    /// <summary>
    /// Получить сохраненный адрес сервера
    /// </summary>
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
        catch (Exception ex){}
        return null;
    }

    /// <summary>
    /// Сохранить адрес сервера
    /// </summary>
    public async Task SaveServerAddress(string address)
    {
        try
        {
            await _secureStorage.SaveAsync(STORAGE_KEY, address);
        }
        catch (Exception ex)
        { }
    }

    /// <summary>
    /// Проверить доступность сервера по адресу с детальным логированием
    /// </summary>
    public async Task<bool> PingServer(string address)
    {
        try
        {
            
            // Пробуем оба варианта URL
            var urls = new[]
            {
                $"{address}/api/ping",
                $"{address}/ping"
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
                        var content = await response.Content.ReadAsStringAsync();
                        return true;
                    }
                }
                catch (TaskCanceledException)
                {                }
                catch (Exception ex)
                {                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    /// <summary>
    /// Получить локальный IP-адрес устройства
    /// </summary>
    public string? GetLocalIpAddress()
    {
        try
        {            
            // Способ 1: через сокет
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            if (endPoint != null)
            {
                var ip = endPoint.Address.ToString();
                return ip;
            }
        }
        catch (Exception ex)
        {        }

        // Способ 2: через Dns
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ipStr = ip.ToString();
                    return ipStr;
                }
            }
        }
        catch (Exception ex)
        {        }

        return null;
    }

    /// <summary>
    /// Поиск сервера в подсети с детальным логированием
    /// </summary>
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

        // 3. Формируем список кандидатов
        var candidates = new List<string>();
        var standardAddresses = new[] { "192.168.0.252", "192.168.0.131", "192.168.1.252", "192.168.0.141", "192.168.0.1", "192.168.0.100", "192.168.1.1" };
        
        foreach (var ip in standardAddresses)
        {
            candidates.Add($"http://{ip}:{DEFAULT_PORT}");
        }

        for (int i = 1; i <= 254; i++)
        {
            var ip = $"{baseIp}{i}";
            if (ip != localIp)
            {
                var address = $"http://{ip}:{DEFAULT_PORT}";
                if (!candidates.Contains(address)) candidates.Add(address);
            }
        }

        // 4. Потокобезопасный параллельный поиск через Parallel.ForEachAsync
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string? foundAddress = null;
        int checkedCount = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 15, // Оптимально для сетевых запросов
            CancellationToken = cts.Token
        };

        try
        {
            await Parallel.ForEachAsync(candidates, parallelOptions, async (address, token) =>
            {
                // Если кто-то уже нашел сервер — выходим
                if (Volatile.Read(ref foundAddress) != null) return;

                try
                {
                    var currentCount = Interlocked.Increment(ref checkedCount);
                    if (await PingServer(address))
                    {
                        // Потокобезопасно записываем адрес, если он еще не был найден
                        if (Interlocked.CompareExchange(ref foundAddress, address, null) == null)
                        {
                            cts.Cancel(); // Отменяем остальные запросы
                        }
                    }
                }
                catch (Exception ex)
                {                }
            });
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Нормальное поведение при отмене через cts.Cancel()
        }

        // 5. Обработка результата
        if (!string.IsNullOrEmpty(foundAddress))
        {
            await SaveServerAddress(foundAddress);
            return foundAddress;
        }
        return null;
    }


    /// <summary>
    /// Полный цикл поиска сервера
    /// </summary>
    public async Task<string?> GetServerAddress()
    {        
        // Сначала проверяем сохраненный
        var saved = await GetSavedServerAddress();
        if (!string.IsNullOrEmpty(saved))
        {
            if (await PingServer(saved))
            {
                return saved;
            }
        }

        // Если сохраненный не работает - ищем
        return await DiscoverServer();
    }
}