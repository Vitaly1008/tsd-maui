using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace FlowerWms.Tsd.Services;

public class ServerDiscoveryService
{
    private readonly LoggerService _logger;
    private readonly SecureStorageService _secureStorage;
    private const string STORAGE_KEY = "server_address";
    private const int DEFAULT_PORT = 5152;
    private const int TIMEOUT_MS = 1000; // 1 секунда на адрес

    public ServerDiscoveryService()
    {
        _logger = new LoggerService();
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
                _logger.Info($"📂 Найден сохраненный адрес: {address}");
                return address;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка чтения сохраненного адреса: {ex.Message}");
        }
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
            _logger.Success($"✅ Адрес сервера сохранен: {address}");
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка сохранения адреса: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверить доступность сервера по адресу
    /// </summary>
    public async Task<bool> PingServer(string address)
    {
        try
        {
            var url = $"{address}/api/ping";
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(TIMEOUT_MS);
            
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Проверить доступность сервера и получить информацию
    /// </summary>
    public async Task<(bool IsAvailable, string? ServerUrl, string? PrimaryIp)> PingServerWithInfo(string address)
    {
        try
        {
            var url = $"{address}/api/ping";
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMilliseconds(TIMEOUT_MS);
            
            var response = await client.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                try
                {
                    var json = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                    if (json != null)
                    {
                        var serverUrl = json.GetValueOrDefault("serverUrl")?.ToString();
                        var primaryIp = json.GetValueOrDefault("primaryIp")?.ToString();
                        _logger.Success($"✅ Сервер доступен: {address}");
                        return (true, serverUrl ?? address, primaryIp);
                    }
                }
                catch { }
                return (true, address, null);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"⏳ Сервер {address} не отвечает: {ex.Message}");
        }
        return (false, null, null);
    }

    /// <summary>
    /// Получить локальный IP-адрес устройства
    /// </summary>
    private string? GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString();
        }
        catch
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Полный поиск сервера в подсети 255.255.255.0
    /// </summary>
    public async Task<string?> DiscoverServer()
    {
        _logger.Info("🔍 ===== НАЧАЛО ПОИСКА СЕРВЕРА =====");

        // 1. Проверяем сохраненный адрес
        var savedAddress = await GetSavedServerAddress();
        if (!string.IsNullOrEmpty(savedAddress))
        {
            _logger.Info($"📂 Проверка сохраненного адреса: {savedAddress}");
            if (await PingServer(savedAddress))
            {
                _logger.Success($"✅ Сервер найден по сохраненному адресу: {savedAddress}");
                return savedAddress;
            }
            _logger.Warning($"⚠️ Сохраненный адрес {savedAddress} не доступен");
        }

        // 2. Получаем локальный IP
        var localIp = GetLocalIpAddress();
        if (localIp == null)
        {
            _logger.Error("❌ Не удалось определить IP-адрес устройства");
            return null;
        }

        _logger.Info($"📡 Локальный IP: {localIp}");
        
        var lastDotIndex = localIp.LastIndexOf('.');
        if (lastDotIndex == -1)
        {
            _logger.Error($"❌ Некорректный IP-адрес: {localIp}");
            return null;
        }
        
        var baseIp = localIp.Substring(0, lastDotIndex + 1);
        _logger.Info($"🌐 Подсеть: {baseIp}xxx (маска 255.255.255.0)");
        
        // 3. Создаем список всех адресов в подсети (1-254)
        var candidates = new List<string>();
        for (int i = 1; i <= 254; i++)
        {
            var ip = $"{baseIp}{i}";
            if (ip != localIp) // исключаем себя
            {
                candidates.Add($"http://{ip}:{DEFAULT_PORT}");
            }
        }

        _logger.Info($"🔍 Поиск среди {candidates.Count} адресов...");
        _logger.Info($"⏳ Это может занять до 30 секунд...");

        // 4. Параллельный поиск с ограничением
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // Максимум 30 секунд
        
        var semaphore = new SemaphoreSlim(20); // 20 параллельных запросов
        var foundAddress = string.Empty;
        var found = false;
        
        var tasks = candidates.Select(async address =>
        {
            if (found) return;
            
            await semaphore.WaitAsync(cts.Token);
            try
            {
                if (cts.Token.IsCancellationRequested || found)
                    return;
                
                if (await PingServer(address))
                {
                    found = true;
                    foundAddress = address;
                    cts.Cancel();
                    _logger.Success($"✅ Сервер найден: {address}");
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Ожидаемо при отмене
        }

        if (!string.IsNullOrEmpty(foundAddress))
        {
            await SaveServerAddress(foundAddress);
            _logger.Success($"✅ Адрес сервера сохранен: {foundAddress}");
            return foundAddress;
        }

        _logger.Error("❌ Сервер не найден в подсети");
        return null;
    }

    /// <summary>
    /// Полный цикл поиска и проверки сервера
    /// </summary>
    public async Task<string?> GetServerAddress()
    {
        // Сначала проверяем сохраненный
        var saved = await GetSavedServerAddress();
        if (!string.IsNullOrEmpty(saved) && await PingServer(saved))
        {
            _logger.Success($"✅ Используем сохраненный адрес: {saved}");
            return saved;
        }

        // Если сохраненный не работает - ищем
        _logger.Info("🔍 Сохраненный адрес не работает, начинаем поиск...");
        return await DiscoverServer();
    }
}