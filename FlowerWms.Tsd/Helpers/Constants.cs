using System.Text.Json;

namespace FlowerWms.Tsd.Helpers;

public static class Constants
{
    private static string? _apiBaseUrl;
    private static string? _deviceId;
    private static readonly object _lock = new object();

    public static string ApiBaseUrl
    {
        get
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_apiBaseUrl))
                    return _apiBaseUrl;

                // Пытаемся загрузить из настроек
                _apiBaseUrl = LoadConfig("apiBaseUrl", null);
                
                if (string.IsNullOrEmpty(_apiBaseUrl))
                {
                    // Если нет в настройках - используем значение по умолчанию
                    _apiBaseUrl = "http://192.168.0.252:5152";
                }
                
                return _apiBaseUrl;
            }
        }
        set
        {
            lock (_lock)
            {
                _apiBaseUrl = value;
                // Сохраняем в настройки
                SaveConfig("apiBaseUrl", value);
            }
        }
    }

    public static string DeviceId
    {
        get
        {
            if (!string.IsNullOrEmpty(_deviceId))
                return _deviceId;

            _deviceId = LoadConfig("deviceId", "RT40_001");
            return _deviceId;
        }
    }

    private static string LoadConfig(string key, string? defaultValue)
    {
        try
        {
            var configPath = Path.Combine(FileSystem.AppDataDirectory, "config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return config?.GetValueOrDefault(key) ?? defaultValue ?? string.Empty;
            }
            return defaultValue ?? string.Empty;
        }
        catch
        {
            return defaultValue ?? string.Empty;
        }
    }

    private static void SaveConfig(string key, string value)
    {
        try
        {
            var configPath = Path.Combine(FileSystem.AppDataDirectory, "config.json");
            Dictionary<string, string> config;
            
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                config = JsonSerializer.Deserialize<Dictionary<string, string>>(json) 
                         ?? new Dictionary<string, string>();
            }
            else
            {
                config = new Dictionary<string, string>();
            }
            
            config[key] = value;
            
            var newJson = JsonSerializer.Serialize(config);
            File.WriteAllText(configPath, newJson);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения конфига: {ex.Message}");
        }
    }

    public static class ApiEndpoints
    {
        public const string Ping = "/api/ping";
        public const string Login = "/api/auth/login";
        public const string StartOperation = "/api/tsd/operation/start";
        public const string Scan = "/api/tsd/scan";
        public const string ConfirmOperation = "/api/tsd/operation/confirm";
        public const string EndOperation = "/api/tsd/operation/end";
        public const string SyncBox = "/api/barcodes/sync-box";
    }
}