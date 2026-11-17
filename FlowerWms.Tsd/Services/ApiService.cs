using System.Text;
using System.Text.Json;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;
using Location = FlowerWms.Tsd.Models.Location;

namespace FlowerWms.Tsd.Services;

// HTTP-запросы к бэкенду
public class ApiService
{
    private HttpClient _httpClient;
    private readonly SecureStorageService _secureStorage;
    private readonly OfflineService _offlineService;
    private readonly DatabaseHelper _dbHelper;

    // ✅ ИСПРАВЛЕНО: полный DI
    public ApiService(
        DatabaseHelper dbHelper,
        SecureStorageService secureStorage,
        OfflineService offlineService)
    {
        _httpClient = new HttpClient();
        _dbHelper = dbHelper;
        _secureStorage = secureStorage;
        _offlineService = offlineService;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        Logger.Info("ApiService инициализирован");
    }

    // Обновляет базовый адрес API
    public void UpdateBaseUrl(string newBaseUrl)
    {
        Logger.Info($"📡 UpdateBaseUrl: {Constants.ApiBaseUrl} -> {newBaseUrl}");
        Constants.ApiBaseUrl = newBaseUrl;
    }

    // ✅ ДОБАВЛЕНО: логин через общий HttpClient, БЕЗ заголовка Authorization

// ✅ Логин через общий HttpClient, БЕЗ заголовка Authorization
    public async Task<LoginResponse?> LoginAsync(string username, string password)
    {
        Logger.Info($"📤 LoginAsync: username={username}");
        try
        {
            // НЕ используем GetHttpClient(), чтобы не тащить старый токен
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.Login}");

            // ✅ Помечаем клиента как ТСД: сервер разрешит повторный вход,
            // выбив предыдущую сессию этой же учётки (вариант D).
            request.Headers.Add("X-Client-Type", "tsd");

            var json = JsonSerializer.Serialize(new { username, password });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 LoginAsync status: {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"⚠️ LoginAsync ошибка: {content}");
                return null;
            }

            var loginResponse = JsonSerializer.Deserialize<LoginResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return loginResponse;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ LoginAsync ошибка: {ex.Message}");
            return null;
        }
    }

    // Возвращает HttpClient с токеном авторизации
    private async Task<HttpClient> GetHttpClient()
    {
        var token = await _secureStorage.GetToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            Logger.Info("🔑 Токен авторизации установлен");
        }
        else
        {
            // ✅ ИСПРАВЛЕНО: сбрасываем старый токен
            _httpClient.DefaultRequestHeaders.Authorization = null;
            Logger.Warning("⚠️ Токен авторизации отсутствует");
        }
        return _httpClient;
    }

    // Проверяет доступность сервера
    public async Task<bool> PingServer()
    {
        Logger.Info($"🏓 PingServer: {Constants.ApiBaseUrl}{Constants.ApiEndpoints.Ping}");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.Ping}");
            var result = response.IsSuccessStatusCode;
            Logger.Info($"🏓 Результат PingServer: {result}, StatusCode: {(int)response.StatusCode}");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ PingServer ошибка: {ex.Message}");
            return false;
        }
    }

    // Выполняет запрос с поддержкой офлайн-режима
    private async Task<T?> RequestWithOfflineSupport<T>(
        HttpMethod method,
        string path,
        object? data = null,
        string? operationType = null,
        string? barcode = null)
    {
        Logger.Info($"📤 RequestWithOfflineSupport: method={method}, path={path}, opType={operationType}, barcode={barcode}");

        try
        {
            var client = await GetHttpClient();
            var request = new HttpRequestMessage(method, $"{Constants.ApiBaseUrl}{path}");

            if (data != null)
            {
                var json = JsonSerializer.Serialize(data);
                Logger.Info($"📦 Request body: {json}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");

            if (response.IsSuccessStatusCode)
            {
                Logger.Info($"✅ Request успешен");
                return JsonSerializer.Deserialize<T>(content);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Logger.Warning("⚠️ 401 Unauthorized - очистка токена");
                await _secureStorage.Clear();
            }

            throw new Exception($"HTTP {(int)response.StatusCode}: {content}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ RequestWithOfflineSupport ошибка: {ex.Message}");

            if (!string.IsNullOrEmpty(operationType) &&
                !string.IsNullOrEmpty(barcode))
            {
                Logger.Info($"💾 Сохранение транзакции в офлайн: type={operationType}, barcode={barcode}");
                await _offlineService.SaveTransaction(
                    operationType: operationType,
                    barcode: barcode,
                    payload: data ?? new object(),
                    deviceId: Constants.DeviceId
                );

                if (typeof(T) == typeof(Dictionary<string, object>))
                {
                    var result = new Dictionary<string, object>
                    {
                        ["success"] = false,
                        ["offline"] = true,
                        ["message"] = "Операция сохранена для синхронизации"
                    };
                    Logger.Info($"✅ Возврат офлайн-результата");
                    return (T)(object)result;
                }
            }

            throw;
        }
    }

    // Выполняет POST-запрос с обработкой ответа
    private async Task<Dictionary<string, object>> ExecutePostRequest(string url, object data)
    {
        Logger.Info($"📤 ExecutePostRequest: {url}");
        try
        {
            var client = await GetHttpClient();
            var json = JsonSerializer.Serialize(data);
            Logger.Info($"📦 Request body: {json}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {responseContent?.Substring(0, Math.Min(500, responseContent?.Length ?? 0))}...");

            if (response.IsSuccessStatusCode)
            {
                var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                Logger.Info($"✅ POST успешен");
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = responseData ?? new Dictionary<string, object>()
                };
            }

            Logger.Warning($"⚠️ POST вернул ошибку: {(int)response.StatusCode}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ExecutePostRequest ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    // Выполняет PUT-запрос с обработкой ответа
    private async Task<Dictionary<string, object>> ExecutePutRequest(string url, object data)
    {
        Logger.Info($"📤 ExecutePutRequest: {url}");
        try
        {
            var client = await GetHttpClient();
            var json = JsonSerializer.Serialize(data);
            Logger.Info($"📦 Request body: {json}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PutAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {responseContent?.Substring(0, Math.Min(500, responseContent?.Length ?? 0))}...");

            if (response.IsSuccessStatusCode)
            {
                var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                Logger.Info($"✅ PUT успешен");
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = responseData ?? new Dictionary<string, object>()
                };
            }

            Logger.Warning($"⚠️ PUT вернул ошибку: {(int)response.StatusCode}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ExecutePutRequest ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    // Выполняет GET-запрос и возвращает список объектов
    private async Task<List<T>> ExecuteGetListRequest<T>(string url, Func<Dictionary<string, object>, T> fromJson)
    {
        Logger.Info($"📤 ExecuteGetListRequest: {url}");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(url);
            Logger.Info($"📥 Response status: {(int)response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Logger.Info($"📥 Response body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");
                var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content) 
                           ?? new List<Dictionary<string, object>>();
                Logger.Info($"✅ GET список успешен, элементов: {data.Count}");
                return data.Select(fromJson).ToList();
            }

            Logger.Warning($"⚠️ GET список вернул ошибку: {(int)response.StatusCode}");
            return new List<T>();
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ExecuteGetListRequest ошибка: {ex.Message}");
            return new List<T>();
        }
    }

    // Выполняет GET-запрос и возвращает один объект
    private async Task<T?> ExecuteGetSingleRequest<T>(string url, Func<Dictionary<string, object>, T> fromJson)
    {
        Logger.Info($"📤 ExecuteGetSingleRequest: {url}");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(url);
            Logger.Info($"📥 Response status: {(int)response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Logger.Info($"📥 Response body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                if (data != null)
                {
                    Logger.Info($"✅ GET одиночный успешен");
                    return fromJson(data);
                }
                Logger.Warning($"⚠️ GET одиночный: данные пустые");
            }
            else
            {
                Logger.Warning($"⚠️ GET одиночный вернул ошибку: {(int)response.StatusCode}");
            }

            return default;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ExecuteGetSingleRequest ошибка: {ex.Message}");
            return default;
        }
    }

    // ============================================================
    // ОПЕРАЦИИ ТСД
    // ============================================================

    public async Task<Dictionary<string, object>> StartOperation(string operationType, string deviceId, string? context = null)
    {
        Logger.Info($"📤 StartOperation: operationType={operationType}, deviceId={deviceId}");
        var result = await RequestWithOfflineSupport<Dictionary<string, object>>(
            HttpMethod.Post,
            Constants.ApiEndpoints.StartOperation,
            new { operationType, context },
            "start_operation",
            operationType
        );

        return result ?? new Dictionary<string, object>();
    }

    public async Task<Dictionary<string, object>> ScanBarcode(string barcode, string deviceId, int? quantity = null, string? comment = null)
    {
        Logger.Info($"📤 ScanBarcode: barcode={barcode}, deviceId={deviceId}, quantity={quantity}");
        var result = await RequestWithOfflineSupport<Dictionary<string, object>>(
            HttpMethod.Post,
            Constants.ApiEndpoints.Scan,
            new { barcode, deviceId, quantity, comment },
            "scan",
            barcode
        );

        return result ?? new Dictionary<string, object>();
    }

    public async Task<Dictionary<string, object>> ConfirmOperation(string deviceId, string? comment = null)
    {
        Logger.Info($"📤 ConfirmOperation: deviceId={deviceId}");
        var result = await RequestWithOfflineSupport<Dictionary<string, object>>(
            HttpMethod.Post,
            Constants.ApiEndpoints.ConfirmOperation,
            new { comment },
            "confirm_operation",
            "confirm"
        );

        return result ?? new Dictionary<string, object>();
    }

    public async Task<Dictionary<string, object>> EndOperation(string deviceId)
    {
        Logger.Info($"📤 EndOperation: deviceId={deviceId}");
        var result = await RequestWithOfflineSupport<Dictionary<string, object>>(
            HttpMethod.Post,
            Constants.ApiEndpoints.EndOperation,
            new { },
            "end_operation",
            "end"
        );

        return result ?? new Dictionary<string, object>();
    }

    // ============================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ БЕЗОПАСНОГО ИЗВЛЕЧЕНИЯ
    // ============================================================

    private string? ExtractStringValue(object? value)
    {
        if (value == null)
            return null;
        
        if (value is string str)
            return str;
        
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
                return jsonElement.GetString();
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.GetInt32().ToString();
            return jsonElement.ToString();
        }
        
        return value.ToString();
    }

    private int ExtractIntValue(object? value, int defaultValue = 0)
    {
        if (value == null)
            return defaultValue;
        
        if (value is int i)
            return i;
        
        if (value is long l)
            return (int)l;
        
        if (value is double d)
            return (int)d;
        
        if (value is string str && int.TryParse(str, out var parsed))
            return parsed;
        
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return jsonElement.GetInt32();
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var strVal = jsonElement.GetString();
                if (int.TryParse(strVal, out var parsed2))
                    return parsed2;
            }
        }
        
        return defaultValue;
    }
    // ============================================================
    // МЕТОДЫ ДЛЯ ИНВЕНТАРИЗАЦИИ И УПРАВЛЕНИЯ КОРОБКАМИ
    // ============================================================

    public async Task<Dictionary<string, object>> UpdateBoxQuantityOnly(string boxId, int newQuantity)
    {
        Logger.Info($"📤 UpdateBoxQuantityOnly: boxId={boxId}, newQuantity={newQuantity}");
        return await ExecutePutRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/quantity",
            new { quantity = newQuantity }
        );
    }

    public async Task<Dictionary<string, object>> MoveBox(string boxId, string targetLocation)
    {
        Logger.Info($"📤 MoveBox: boxId={boxId}, targetLocation={targetLocation}");
        return await ExecutePutRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/move",
            new { locationCode = targetLocation }
        );
    }

    public async Task<List<Box>> GetBoxesByLocation(string locationCode)
    {
        Logger.Info($"📤 GetBoxesByLocation: locationCode={locationCode}");
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/location/{locationCode}",
            Box.FromJson
        );
    }

    public async Task<Box?> FindBoxByBarcode(string barcode)
    {
        Logger.Info($"📤 FindBoxByBarcode: barcode={barcode}");
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/barcode/{barcode}",
            Box.FromJson
        );
    }

    public async Task<Dictionary<string, object>?> GetLocationInfo(string locationCode)
    {
        Logger.Info($"📤 GetLocationInfo: locationCode={locationCode}");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/locations/{locationCode}");
            Logger.Info($"📥 Response status: {(int)response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Logger.Info($"📥 Response body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");
                return JsonSerializer.Deserialize<Dictionary<string, object>>(content);
            }

            Logger.Warning($"⚠️ GetLocationInfo вернул ошибку: {(int)response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ GetLocationInfo ошибка: {ex.Message}");
            return null;
        }
    }

    public async Task<Dictionary<string, object>> GetServerStatus()
    {
        Logger.Info($"📤 GetServerStatus");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.Ping}");
            Logger.Info($"📥 Response status: {(int)response.StatusCode}");
            
            return new Dictionary<string, object>
            {
                ["online"] = response.IsSuccessStatusCode,
                ["statusCode"] = (int)response.StatusCode,
                ["serverUrl"] = Constants.ApiBaseUrl
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ GetServerStatus ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["online"] = false,
                ["error"] = ex.Message,
                ["serverUrl"] = Constants.ApiBaseUrl
            };
        }
    }

    public async Task<bool> SendHeartbeat()
    {
        Logger.Info($"📤 SendHeartbeat");
        try
        {
            var client = await GetHttpClient();
            var response = await client.PostAsync($"{Constants.ApiBaseUrl}/api/heartbeat", null);
            var result = response.IsSuccessStatusCode;
            Logger.Info($"📥 SendHeartbeat результат: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ SendHeartbeat ошибка: {ex.Message}");
            return false;
        }
    }

    public async Task<List<Product>> GetAllProducts()
    {
        Logger.Info($"📤 GetAllProducts");
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/products",
            Product.FromJson
        );
    }

    public async Task<List<Location>> GetAllLocations()
    {
        Logger.Info($"📤 GetAllLocations");
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/locations",
            Location.FromJson
        );
    }

    // ============================================================
    // ✅ ИСПРАВЛЕНО: синхронизация продуктов/локаций через _dbHelper
    // ============================================================

    public async Task<bool> SyncProducts()
    {
        Logger.Info($"📤 SyncProducts: НАЧАЛО");
        try
        {
            var products = await GetAllProducts();
            Logger.Info($"📋 Получено {products.Count} продуктов");
            if (products.Count > 0)
            {
                var productCache = products.Select(p => new ProductCache
                {
                    product_id = p.Id,
                    ean13 = p.Ean13,
                    name = p.Name,
                    short_name = p.ShortName,
                    onec_guid = p.OneCGuid,
                    barcode = p.Barcode,
                    created_at = p.CreatedAt,
                    updated_at = p.UpdatedAt
                }).ToList();

                await _dbHelper.SyncProducts(productCache); // ✅ через DI
                Logger.Info($"✅ Синхронизировано {productCache.Count} продуктов");
                return true;
            }
            Logger.Warning($"⚠️ Продукты не получены");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ SyncProducts ошибка: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SyncLocations()
    {
        Logger.Info($"📤 SyncLocations: НАЧАЛО");
        try
        {
            var locations = await GetAllLocations();
            Logger.Info($"📋 Получено {locations.Count} локаций");
            if (locations.Count > 0)
            {
                var locationCache = locations.Select(l => new LocationCache
                {
                    location_id = l.Id,
                    code = l.Code,
                    name = l.Name,
                    barcode = l.Barcode,
                    is_active = l.IsActive ? 1 : 0,
                    created_at = l.CreatedAt
                }).ToList();

                await _dbHelper.SyncLocations(locationCache); // ✅ через DI
                Logger.Info($"✅ Синхронизировано {locationCache.Count} локаций");
                return true;
            }
            Logger.Warning($"⚠️ Локации не получены");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ SyncLocations ошибка: {ex.Message}");
            return false;
        }
    }

    public async Task<Dictionary<string, object>> CreateBox(
        string ean13, 
        int quantity, 
        string grade,
        int boxNumber,
        string locationCode = "UNKNOWN")
    {
        Logger.Info($"📤 CreateBox: ean13={ean13}, quantity={quantity}, grade={grade}, boxNumber={boxNumber}, location={locationCode}");
        return await ExecutePostRequest(
            $"{Constants.ApiBaseUrl}/api/barcodes/create-box",
            new
            {
                ean13 = ean13,
                quantity = quantity,
                grade = grade,
                boxNumber = boxNumber,
                locationCode = locationCode
            }
        );
    }

    public async Task<Box?> GetBoxByBarcode(string barcode)
    {
        Logger.Info($"📤 GetBoxByBarcode: barcode={barcode}");
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/by-barcode/{barcode}",
            Box.FromJson
        );
    }

    public async Task<Box?> GetBoxById(string boxId)
    {
        Logger.Info($"📤 GetBoxById: boxId={boxId}");
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/{boxId}",
            Box.FromJson
        );
    }

    public async Task<List<Box>> GetAllBoxes()
    {
        Logger.Info($"📤 GetAllBoxes");
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/boxes",
            Box.FromJson
        );
    }

    public async Task<Dictionary<string, object>> ReserveBox(string boxId)
    {
        Logger.Info($"📤 ReserveBox: boxId={boxId}");
        return await ExecutePostRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/reserve",
            new { boxId = boxId }
        );
    }

    // ============================================================
    // ОТГРУЗКА С ПОДДЕРЖКОЙ ОФЛАЙН (исправленная версия)
    // ============================================================

    //ShipBox — ОСНОВНОЙ (для UI, создает транзакцию)
    public async Task<Dictionary<string, object>> ShipBox(
        string boxId, 
        string? comment = null,
        bool createOfflineTransaction = true)
    {
        Logger.Info($"📤 ShipBox: boxId={boxId}, comment={comment}, createOfflineTransaction={createOfflineTransaction}");
        try
        {
            var client = await GetHttpClient();
            
            // ✅ Сервер ожидает { "boxId": "guid" }
            var request = new { boxId = boxId };
            var json = JsonSerializer.Serialize(request);
            Logger.Info($"📦 Request body: {json}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(
                $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/ship",
                content
            );
            
            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {responseContent?.Substring(0, Math.Min(500, responseContent?.Length ?? 0))}...");
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                Logger.Info($"✅ ShipBox успешен");
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = data ?? new Dictionary<string, object>()
                };
            }
            
            // Если ошибка и нужно сохранить офлайн
            if (createOfflineTransaction)
            {
                Logger.Info($"💾 Сохранение ShipBox в офлайн: boxId={boxId}");
                await _offlineService.SaveTransaction(
                    operationType: "Shipping",
                    barcode: boxId,
                    payload: new { boxId = boxId, comment = comment },
                    deviceId: Constants.DeviceId
                );
            }
            
            Logger.Warning($"⚠️ ShipBox вернул ошибку: {(int)response.StatusCode}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ShipBox ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    //ConsumeBox — ОСНОВНОЙ (для UI, создает транзакцию)
    public async Task<Dictionary<string, object>> ConsumeBox(
        string boxId, 
        int quantity, 
        string? comment = null,
        bool createOfflineTransaction = true)
    {
        Logger.Info($"📤 ConsumeBox: boxId={boxId}, quantity={quantity}, comment={comment}, createOfflineTransaction={createOfflineTransaction}");
        try
        {
            var request = new Dictionary<string, object>
            {
                ["boxId"] = boxId,
                ["quantity"] = quantity
            };
            
            if (!string.IsNullOrEmpty(comment))
            {
                request["comment"] = comment;
            }
            
            var result = await RequestWithOfflineSupport<Dictionary<string, object>>(
                HttpMethod.Post,
                "/api/boxes/consume",
                request,
                createOfflineTransaction ? "Shipping" : null,
                createOfflineTransaction ? boxId : null
            );
            
            return result ?? new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = "Не удалось выполнить списание"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ConsumeBox ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    //ShipBoxInternal — ВНУТРЕННИЙ (БЕЗ создания транзакции)
    public async Task<Dictionary<string, object>> ShipBoxInternal(string boxId, string? comment = null)
    {
        Logger.Info($"📤 ShipBoxInternal: boxId={boxId}, comment={comment}");
        try
        {
            var client = await GetHttpClient();
            
            // ✅ Сервер ожидает { "boxId": "guid" }
            //var request = new { boxId = boxId };
            //var request = comment ?? "";
            var json = JsonSerializer.Serialize(comment ?? "");
            Logger.Info($"📦 Request body: {json}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(
                $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/ship",
                content
            );
            
            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {responseContent?.Substring(0, Math.Min(500, responseContent?.Length ?? 0))}...");
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                Logger.Info($"✅ ShipBoxInternal успешен");
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = data ?? new Dictionary<string, object>()
                };
            }
            
            Logger.Warning($"⚠️ ShipBoxInternal вернул ошибку: {(int)response.StatusCode}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ShipBoxInternal ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    //ConsumeBoxInternal — ВНУТРЕННИЙ (БЕЗ создания транзакции)
    public async Task<Dictionary<string, object>> ConsumeBoxInternal(
        string boxId, 
        int quantity, 
        string? comment = null)
    {
        Logger.Info($"📤 ConsumeBoxInternal: boxId={boxId}, quantity={quantity}, comment={comment}");
        try
        {
            var client = await GetHttpClient();
            var request = new Dictionary<string, object>
            {
                ["boxId"] = boxId,
                ["quantity"] = quantity
            };
            
            if (!string.IsNullOrEmpty(comment))
            {
                request["comment"] = comment;
            }
            
            var json = JsonSerializer.Serialize(request);
            Logger.Info($"📦 Request body: {json}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(
                $"{Constants.ApiBaseUrl}/api/boxes/consume",
                content
            );
            
            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {responseContent?.Substring(0, Math.Min(500, responseContent?.Length ?? 0))}...");
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                Logger.Info($"✅ ConsumeBoxInternal успешен");
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = data ?? new Dictionary<string, object>()
                };
            }
            
            Logger.Warning($"⚠️ ConsumeBoxInternal вернул ошибку: {(int)response.StatusCode}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ ConsumeBoxInternal ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    //  НОВЫЙ МЕТОД: обновление количества коробки
    public async Task<Dictionary<string, object>> UpdateBoxQuantity(string boxId, int quantity, string? comment = null)
    {
        Logger.Info($"📤 UpdateBoxQuantity: boxId={boxId}, quantity={quantity}, comment={comment}");
        try
        {
            var request = new Dictionary<string, object>
            {
                ["quantity"] = quantity
            };
            
            if (!string.IsNullOrEmpty(comment))
            {
                request["comment"] = comment;
            }
            
            return await ExecutePutRequest(
                $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/quantity",
                request
            );
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ UpdateBoxQuantity ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    public async Task<Box?> GetBoxByNumber(int boxNumber)
    {
        Logger.Info($"📤 GetBoxByNumber: boxNumber={boxNumber}");
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/by-number/{boxNumber}",
            Box.FromJson
        );
    }

    public async Task<Dictionary<string, object>> CheckBoxNumber(int boxNumber)
    {
        Logger.Info($"📤 CheckBoxNumber: boxNumber={boxNumber}");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(
                $"{Constants.ApiBaseUrl}/api/barcodes/check-box-number/{boxNumber}"
            );
            
            var content = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                Logger.Info($"✅ CheckBoxNumber успешен");
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = data ?? new Dictionary<string, object>()
                };
            }
            
            Logger.Warning($"⚠️ CheckBoxNumber вернул ошибку: {(int)response.StatusCode}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {content}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ CheckBoxNumber ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    public async Task<Dictionary<string, object>> GetNextFreeBoxNumber()
    {
        Logger.Info($"📤 GetNextFreeBoxNumber");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(
                $"{Constants.ApiBaseUrl}/api/barcodes/next-free-box-number"
            );
            
            var content = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                Logger.Info($"✅ GetNextFreeBoxNumber успешен");
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = data ?? new Dictionary<string, object>()
                };
            }
            
            Logger.Warning($"⚠️ GetNextFreeBoxNumber вернул ошибку: {(int)response.StatusCode}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {content}"
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ GetNextFreeBoxNumber ошибка: {ex.Message}");
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    // ============================================================
    // МЕТОДЫ ДЛЯ СИНХРОНИЗАЦИИ
    // ============================================================

    // 1.2. Получение timestamp последнего изменения
    public async Task<DateTime> GetServerLastChanged()
    {
        Logger.Info($"📤 GetServerLastChanged");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/tsd/last-changed");
            Logger.Info($"📥 Response status: {(int)response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Logger.Info($"📥 Response body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                if (data != null && data.TryGetValue("lastChanged", out var value))
                {
                    if (value is long longValue)
                    {
                        var result = DateTimeOffset.FromUnixTimeMilliseconds(longValue).UtcDateTime;
                        Logger.Info($"✅ GetServerLastChanged: {result}");
                        return result;
                    }
                    if (value is string strValue)
                    {
                        if (DateTime.TryParse(strValue, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                        {
                            Logger.Info($"✅ GetServerLastChanged (ISO): {dt.ToUniversalTime()}");
                            return dt.ToUniversalTime();
                        }
                        if (long.TryParse(strValue, out var unixTime))
                        {
                            var result = DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime;
                            Logger.Info($"✅ GetServerLastChanged (Unix): {result}");
                            return result;
                        }
                    }
                }
                Logger.Warning($"⚠️ GetServerLastChanged: не удалось распарсить ответ");
            }
            else
            {
                Logger.Warning($"⚠️ GetServerLastChanged вернул ошибку: {(int)response.StatusCode}");
            }

            // ✅ БАГ #18: единый fallback
            return DateTime.MinValue;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ GetServerLastChanged ошибка: {ex.Message}");
            return DateTime.MinValue;
        }
    }

    // 1.1. Получение всех коробок для синхронизации (уже есть, но проверим)
    public async Task<List<Box>> GetAllBoxesForSync()
    {
        Logger.Info($"📤 GetAllBoxesForSync");
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/tsd/boxes/all");
            Logger.Info($"📥 Response status: {(int)response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Logger.Info($"📥 Response body: {content?.Substring(0, Math.Min(500, content?.Length ?? 0))}...");
                var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);
                
                if (data != null)
                {
                    var result = data.Select(Box.FromJson).Where(b => b != null).Cast<Box>().ToList();
                    Logger.Info($"✅ GetAllBoxesForSync: получено {result.Count} коробок");
                    return result;
                }
                Logger.Warning($"⚠️ GetAllBoxesForSync: данные пустые");
            }
            else
            {
                Logger.Warning($"⚠️ GetAllBoxesForSync вернул ошибку: {(int)response.StatusCode}");
            }
            return new List<Box>();
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ GetAllBoxesForSync ошибка: {ex.Message}");
            return new List<Box>();
        }
    }

    public async Task<SyncBoxesResponse?> SyncBoxes(SyncBoxesRequest request)
    {
        Logger.Info($"📤 SyncBoxes: {request.Transactions.Count} транзакций");
        try
        {
            var client = await GetHttpClient();
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            Logger.Info($"📦 Request body: {json?.Substring(0, Math.Min(500, json?.Length ?? 0))}...");
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(
                $"{Constants.ApiBaseUrl}/api/tsd/sync/boxes",
                content);

            var responseContent = await response.Content.ReadAsStringAsync();
            Logger.Info($"📥 Response status: {(int)response.StatusCode}, body: {responseContent?.Substring(0, Math.Min(800, responseContent?.Length ?? 0))}...");

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<SyncBoxesResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                Logger.Info($"✅ SyncBoxes успешен: success={result?.SuccessCount}, fail={result?.FailCount}");
                return result;
            }

            Logger.Warning($"⚠️ SyncBoxes вернул ошибку: {(int)response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ SyncBoxes ошибка: {ex.Message}");
            return null;
        }
    }

    // Best effort: сообщаем серверу, что клиент выходит (если сети нет — молча игнорируем)
    public async Task Logout()
    {
        try
        {
            var client = await GetHttpClient();
            await client.PostAsync($"{Constants.ApiBaseUrl}/api/auth/logout", null);
            Logger.Info("👋 Logout на сервере выполнен");
        }
        catch (Exception ex)
        {
            Logger.Warning($"⚠️ Logout на сервере не удался: {ex.Message}");
        }
    }
}