using System.Text.Json;
using Microsoft.Maui.Storage;

namespace FlowerWms.Tsd.Helpers;

public static class Constants
{
    private static string? _apiBaseUrl;
    private static string? _deviceId;
    private static readonly object _lock = new object();
    private const string CONFIG_KEY_API = "apiBaseUrl";
    private const string CONFIG_KEY_DEVICE = "deviceId";

    public static string ApiBaseUrl
    {
        get
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_apiBaseUrl))
                    return _apiBaseUrl;

                // Загружаем из SecureStorage
                _apiBaseUrl = LoadConfig(CONFIG_KEY_API, null);
                
                if (string.IsNullOrEmpty(_apiBaseUrl))
                {
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
                SaveConfig(CONFIG_KEY_API, value);
            }
        }
    }

    public static string DeviceId
    {
        get
        {
            if (!string.IsNullOrEmpty(_deviceId))
                return _deviceId;

            _deviceId = LoadConfig(CONFIG_KEY_DEVICE, "RT40_001");
            return _deviceId;
        }
    }

    // ✅ Используем SecureStorage для безопасного хранения
    private static string LoadConfig(string key, string? defaultValue)
    {
        try
        {
            var task = Task.Run(async () => await SecureStorage.GetAsync(key));
            var value = task.Result;
            
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
            return defaultValue ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка загрузки конфига {key}: {ex.Message}");
            return defaultValue ?? string.Empty;
        }
    }

    private static void SaveConfig(string key, string value)
    {
        try
        {
            var task = Task.Run(async () => await SecureStorage.SetAsync(key, value));
            task.Wait();
            System.Diagnostics.Debug.WriteLine($"✅ Конфиг сохранен: {key}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения конфига {key}: {ex.Message}");
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
        public const string Products = "/api/products";
        public const string BoxesByLocation = "/api/boxes/location";  // НОВЫЙ
        public const string BoxByBarcode = "/api/boxes/by-barcode/{barcode}";
        public const string MoveBox = "/api/boxes/move";              // НОВЫЙ
        public const string UpdateBoxQuantity = "/api/boxes/quantity"; // НОВЫЙ
        public const string CreateDraftBox = "/api/barcodes/create-draft-box";
        public const string ActivateBox = "/api/barcodes/activate-box";
        public const string DraftBoxByBarcode = "/api/barcodes/draft-box/barcode";
        public const string DeleteDraftBox = "/api/barcodes/draft-box";
        public const string CheckBoxNumber = "/api/barcodes/check-box-number";
        public const string DraftBoxes = "/api/barcodes/draft-boxes";
    }
}