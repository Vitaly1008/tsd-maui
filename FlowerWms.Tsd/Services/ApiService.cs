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
    private bool _isOffline;

    public ApiService()
    {
        _httpClient = new HttpClient();
        _secureStorage = new SecureStorageService();
        _offlineService = new OfflineService();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    // Обновляет базовый адрес API
    public void UpdateBaseUrl(string newBaseUrl)
    {
        Constants.ApiBaseUrl = newBaseUrl;
    }

    // Возвращает HttpClient с токеном авторизации
    private async Task<HttpClient> GetHttpClient()
    {
        var token = await _secureStorage.GetToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return _httpClient;
    }

    // Проверяет доступность сервера
    public async Task<bool> PingServer()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.Ping}");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ✅ ИСПРАВЛЕНО: убрано исключение для Shipping
    // Выполняет запрос с поддержкой офлайн-режима
    private async Task<T?> RequestWithOfflineSupport<T>(
        HttpMethod method,
        string path,
        object? data = null,
        string? operationType = null,
        string? barcode = null)
    {
        try
        {
            var client = await GetHttpClient();
            var request = new HttpRequestMessage(method, $"{Constants.ApiBaseUrl}{path}");
            
            if (data != null)
            {
                var json = JsonSerializer.Serialize(data);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _isOffline = false;
                return JsonSerializer.Deserialize<T>(content);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await _secureStorage.Clear();
            }

            throw new Exception($"HTTP {(int)response.StatusCode}: {content}");
        }
        catch (Exception ex)
        {
            _isOffline = true;

            // ✅ ИСПРАВЛЕНО: сохраняем ВСЕ операции, включая Shipping
            if (!string.IsNullOrEmpty(operationType) && 
                !string.IsNullOrEmpty(barcode))
            {   
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
                    return (T)(object)result;
                }
            }

            throw;
        }
    }

    // Выполняет POST-запрос с обработкой ответа
    private async Task<Dictionary<string, object>> ExecutePostRequest(string url, object data)
    {
        try
        {
            var client = await GetHttpClient();
            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = responseData ?? new Dictionary<string, object>()
                };
            }

            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
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
        try
        {
            var client = await GetHttpClient();
            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PutAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = responseData ?? new Dictionary<string, object>()
                };
            }

            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
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
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content) 
                           ?? new List<Dictionary<string, object>>();
                return data.Select(fromJson).ToList();
            }

            return new List<T>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка GET-запроса: {ex.Message}");
            return new List<T>();
        }
    }

    // Выполняет GET-запрос и возвращает один объект
    private async Task<T?> ExecuteGetSingleRequest<T>(string url, Func<Dictionary<string, object>, T> fromJson)
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                if (data != null)
                {
                    return fromJson(data);
                }
            }

            return default;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка GET-запроса: {ex.Message}");
            return default;
        }
    }

    // ============================================================
    // ОПЕРАЦИИ ТСД
    // ============================================================

    public async Task<Dictionary<string, object>> StartOperation(string operationType, string deviceId, string? context = null)
    {   
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
        var result = await RequestWithOfflineSupport<Dictionary<string, object>>(
            HttpMethod.Post,
            Constants.ApiEndpoints.EndOperation,
            new { },
            "end_operation",
            "end"
        );

        return result ?? new Dictionary<string, object>();
    }

    public async Task<Dictionary<string, object>> SyncOfflineTransaction(
        string transactionId,
        string operationType,
        string barcode,
        Dictionary<string, object> payload)
    {
        try
        {
            var client = await GetHttpClient();
            
            var syncRequest = new
            {
                barcode = barcode,
                locationCode = payload.ContainsKey("locationCode") 
                    ? payload["locationCode"]?.ToString() 
                    : (payload.ContainsKey("LocationCode") ? payload["LocationCode"]?.ToString() : "UNKNOWN"),
                operationType = operationType,
                status = payload.ContainsKey("status") 
                ? Convert.ToInt32(payload["status"]) 
                : 1
            };
            
            var json = JsonSerializer.Serialize(syncRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(
                $"{Constants.ApiBaseUrl}/api/barcodes/sync-box", 
                content
            );
            
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = JsonSerializer.Deserialize<object>(responseContent) ?? new object()
                };
            }

            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    // ============================================================
    // МЕТОДЫ ДЛЯ ИНВЕНТАРИЗАЦИИ И УПРАВЛЕНИЯ КОРОБКАМИ
    // ============================================================

    public async Task<Dictionary<string, object>> UpdateBoxQuantity(string boxId, int newQuantity)
    {
        return await ExecutePutRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/quantity",
            new { quantity = newQuantity }
        );
    }

    public async Task<Dictionary<string, object>> MoveBox(string boxId, string targetLocation)
    {
        return await ExecutePutRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/move",
            new { locationCode = targetLocation }
        );
    }

    public async Task<List<Box>> GetBoxesByLocation(string locationCode)
    {
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/location/{locationCode}",
            Box.FromJson
        );
    }

    public async Task<Box?> FindBoxByBarcode(string barcode)
    {
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/barcode/{barcode}",
            Box.FromJson
        );
    }

    public async Task<Dictionary<string, object>?> GetLocationInfo(string locationCode)
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/locations/{locationCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<Dictionary<string, object>>(content);
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка получения локации: {ex.Message}");
            return null;
        }
    }

    public async Task<Dictionary<string, object>> SyncAllOfflineTransactions()
    {
        try
        {
            var transactions = await _offlineService.GetUnsyncedTransactions();
            int successCount = 0;
            int errorCount = 0;
            var errors = new List<string>();

            foreach (var tx in transactions)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(tx.payload) 
                                  ?? new Dictionary<string, object>();

                    var result = await SyncOfflineTransaction(
                        transactionId: tx.transaction_id,
                        operationType: tx.operation_type,
                        barcode: tx.barcode,
                        payload: payload
                    );

                    if (result.TryGetValue("success", out var successObj) && successObj is bool success && success)
                    {
                        await _offlineService.MarkAsSynced(tx.transaction_id);
                        successCount++;
                    }
                    else
                    {
                        var errorMsg = result.ContainsKey("message") 
                            ? result["message"]?.ToString() ?? "Неизвестная ошибка"
                            : "Неизвестная ошибка";
                        await _offlineService.MarkAsError(tx.transaction_id, errorMsg);
                        errorCount++;
                        errors.Add(errorMsg);
                    }
                }
                catch (Exception ex)
                {
                    await _offlineService.MarkAsError(tx.transaction_id, ex.Message);
                    errorCount++;
                    errors.Add(ex.Message);
                }
            }

            return new Dictionary<string, object>
            {
                ["success"] = errorCount == 0,
                ["synced"] = successCount,
                ["errors"] = errorCount,
                ["errorMessages"] = errors
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    public async Task<Dictionary<string, object>> GetServerStatus()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.Ping}");
            
            return new Dictionary<string, object>
            {
                ["online"] = response.IsSuccessStatusCode,
                ["statusCode"] = (int)response.StatusCode,
                ["serverUrl"] = Constants.ApiBaseUrl
            };
        }
        catch (Exception ex)
        {
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
        try
        {
            var client = await GetHttpClient();
            var response = await client.PostAsync($"{Constants.ApiBaseUrl}/api/heartbeat", null);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<Product>> GetAllProducts()
    {
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/products",
            Product.FromJson
        );
    }

    public async Task<List<Location>> GetAllLocations()
    {
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/locations",
            Location.FromJson
        );
    }

    public async Task<bool> SyncProducts()
    {
        try
        {
            var products = await GetAllProducts();
            if (products.Count > 0)
            {
                var dbHelper = new DatabaseHelper();
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
                
                await dbHelper.SyncProducts(productCache);
                System.Diagnostics.Debug.WriteLine($"Синхронизировано {productCache.Count} продуктов");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации продуктов: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SyncLocations()
    {
        try
        {
            var locations = await GetAllLocations();
            if (locations.Count > 0)
            {
                var dbHelper = new DatabaseHelper();
                var locationCache = locations.Select(l => new LocationCache
                {
                    location_id = l.Id,
                    code = l.Code,
                    name = l.Name,
                    barcode = l.Barcode,
                    is_active = l.IsActive ? 1 : 0,
                    created_at = l.CreatedAt
                }).ToList();
                
                await dbHelper.SyncLocations(locationCache);
                System.Diagnostics.Debug.WriteLine($"Синхронизировано {locationCache.Count} локаций");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации локаций: {ex.Message}");
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
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/by-barcode/{barcode}",
            Box.FromJson
        );
    }

    public async Task<Box?> GetBoxById(string boxId)
    {
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/{boxId}",
            Box.FromJson
        );
    }

    public async Task<List<Box>> GetAllBoxes()
    {
        return await ExecuteGetListRequest(
            $"{Constants.ApiBaseUrl}/api/boxes",
            Box.FromJson
        );
    }

    public async Task<Dictionary<string, object>> ReserveBox(string boxId)
    {
        return await ExecutePostRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/reserve",
            new { boxId = boxId }
        );
    }

    // ============================================================
    // ОТГРУЗКА С ПОДДЕРЖКОЙ ОФЛАЙН (исправленная версия)
    // ============================================================

    public async Task<Dictionary<string, object>> ShipBox(string boxId, string? comment = null)
    {
        try
        {
            // ✅ ИСПРАВЛЕНО: отправляем только comment как строку
            var result = await RequestWithOfflineSupport<Dictionary<string, object>>(
                HttpMethod.Post,
                $"/api/boxes/{boxId}/ship",
                new { comment = comment ?? "Отгрузка через ТСД" },
                "Shipping",
                boxId
            );
            
            if (result != null && result.TryGetValue("success", out var success) && success is bool s && s)
            {
                var updatedBox = await GetBoxById(boxId);
                if (updatedBox != null)
                {
                    result["data"] = updatedBox.ToDictionary();
                }
            }
            
            return result ?? new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = "Не удалось выполнить отгрузку"
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    public async Task<Dictionary<string, object>> ConsumeBox(string boxId, int quantity, string? comment = null)
    {
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
                "Shipping",
                boxId
            );
            
            return result ?? new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = "Не удалось выполнить списание"
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    // ✅ НОВЫЙ МЕТОД: обновление количества коробки
    public async Task<Dictionary<string, object>> UpdateBoxQuantity(string boxId, int quantity, string? comment = null)
    {
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
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    public async Task<Box?> GetBoxByNumber(int boxNumber)
    {
        return await ExecuteGetSingleRequest(
            $"{Constants.ApiBaseUrl}/api/boxes/by-number/{boxNumber}",
            Box.FromJson
        );
    }

    public async Task<Dictionary<string, object>> CheckBoxNumber(int boxNumber)
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(
                $"{Constants.ApiBaseUrl}/api/barcodes/check-box-number/{boxNumber}"
            );
            
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = data ?? new Dictionary<string, object>()
                };
            }
            
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {content}"
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    public async Task<Dictionary<string, object>> GetNextFreeBoxNumber()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync(
                $"{Constants.ApiBaseUrl}/api/barcodes/next-free-box-number"
            );
            
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = data ?? new Dictionary<string, object>()
                };
            }
            
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {content}"
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }

    public async Task<Dictionary<string, object>> ActivateBox(
        string boxId, 
        string locationCode = "UNKNOWN",
        string? comment = null)
    {
        return await ExecutePostRequest(
            $"{Constants.ApiBaseUrl}/api/barcodes/activate-box/{boxId}",
            new 
            { 
                comment = comment ?? $"Активация через ТСД, локация: {locationCode}",
                locationCode = locationCode
            }
        );
    }

    //получение коробок isPartial
    public async Task<List<Box>> GetPartialBoxes()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/stock/partial-boxes");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);
                
                if (data != null)
                {
                    var boxes = new List<Box>();
                    foreach (var item in data)
                    {
                        var box = Box.FromJson(item);
                        if (box != null)
                        {
                            box.IsPartial = true;
                            boxes.Add(box);
                        }
                    }
                    return boxes;
                }
            }
            
            return new List<Box>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения частичных коробок: {ex.Message}");
            return new List<Box>();
        }
    }

    // ============================================================
    // МЕТОДЫ ДЛЯ СИНХРОНИЗАЦИИ
    // ============================================================

    public async Task<DateTime> GetServerLastChanged()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/tsd/last-changed");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                if (data != null && data.TryGetValue("lastChanged", out var value))
                {
                    return DateTime.Parse(value?.ToString() ?? DateTime.UtcNow.ToString("O"));
                }
            }
            return DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    public async Task<List<Box>> GetAllBoxesForSync()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/tsd/boxes/all");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);
                
                if (data != null)
                {
                    return data.Select(Box.FromJson).Where(b => b != null).Cast<Box>().ToList();
                }
            }
            return new List<Box>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка получения коробок: {ex.Message}");
            return new List<Box>();
        }
    }

    public async Task<Dictionary<string, object>> SyncBoxes(List<object> transactions)
    {
        try
        {
            var client = await GetHttpClient();
            var request = new { transactions = transactions };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await client.PostAsync(
                $"{Constants.ApiBaseUrl}/api/tsd/sync/boxes",
                content
            );
            
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent) 
                    ?? new Dictionary<string, object>();
            }
            
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = $"HTTP {(int)response.StatusCode}: {responseContent}"
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["message"] = ex.Message
            };
        }
    }
}