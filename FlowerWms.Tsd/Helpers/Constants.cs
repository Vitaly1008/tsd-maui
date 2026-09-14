using System.Text.Json;
using Microsoft.Maui.Storage;

namespace FlowerWms.Tsd.Helpers;

// Хранение глобальных констант и настроек приложения
public static class Constants
{
    private static string? _apiBaseUrl;
    private static string? _deviceId;
    private static readonly object _lock = new object();
    private static bool _initialized;
    private const string CONFIG_KEY_API = "apiBaseUrl";
    private const string CONFIG_KEY_DEVICE = "deviceId";

    // ✅ Асинхронная инициализация: вызывается ОДИН РАЗ при старте приложения
    public static async Task InitAsync()
    {
        if (_initialized)
            return;

        try
        {
            var apiUrl = await SecureStorage.GetAsync(CONFIG_KEY_API);
            var deviceId = await SecureStorage.GetAsync(CONFIG_KEY_DEVICE);

            lock (_lock)
            {
                _apiBaseUrl = string.IsNullOrEmpty(apiUrl)
                    ? "http://192.168.0.252:5152"
                    : apiUrl;

                _deviceId = string.IsNullOrEmpty(deviceId)
                    ? "RT40_001"
                    : deviceId;

                _initialized = true;
            }
        }
        catch
        {
            lock (_lock)
            {
                _apiBaseUrl ??= "http://192.168.0.252:5152";
                _deviceId ??= "RT40_001";
                _initialized = true;
            }
        }
    }

    // Базовый URL API с сохранением в SecureStorage
    public static string ApiBaseUrl
    {
        get
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_apiBaseUrl))
                    return _apiBaseUrl;

                // Fallback, если InitAsync ещё не был вызван
                _apiBaseUrl = "http://192.168.0.252:5152";
                return _apiBaseUrl;
            }
        }
        set
        {
            lock (_lock)
            {
                _apiBaseUrl = value;
            }
            SaveConfig(CONFIG_KEY_API, value);
        }
    }

    // Идентификатор устройства
    public static string DeviceId
    {
        get
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_deviceId))
                    return _deviceId;

                _deviceId = "RT40_001";
                return _deviceId;
            }
        }
    }

    // Сохранение конфигурации в SecureStorage (fire-and-forget, не блокирует UI)
    private static void SaveConfig(string key, string value)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await SecureStorage.SetAsync(key, value);
            }
            catch
            {
                // Игнорируем ошибки записи
            }
        });
    }

    // API endpoints
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
        public const string BoxesByLocation = "/api/boxes/location";
        public const string BoxByBarcode = "/api/boxes/by-barcode/{barcode}";
        public const string MoveBox = "/api/boxes/move";
        public const string UpdateBoxQuantity = "/api/boxes/quantity";
        public const string CreateDraftBox = "/api/barcodes/create-draft-box";
        public const string ActivateBox = "/api/barcodes/activate-box";
        public const string DraftBoxByBarcode = "/api/barcodes/draft-box/barcode";
        public const string DeleteDraftBox = "/api/barcodes/draft-box";
        public const string CheckBoxNumber = "/api/barcodes/check-box-number";
        public const string DraftBoxes = "/api/barcodes/draft-boxes";
    }
}