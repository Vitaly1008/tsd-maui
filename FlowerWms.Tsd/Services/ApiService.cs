using System.Text;
using System.Text.Json;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;
using Location = FlowerWms.Tsd.Models.Location;

namespace FlowerWms.Tsd.Services;

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

    /// <summary>
    /// Обновить базовый адрес API
    /// </summary>
    public void UpdateBaseUrl(string newBaseUrl)
    {
        Constants.ApiBaseUrl = newBaseUrl;
    }

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

    /// <summary>
    /// Проверка доступности сервера
    /// </summary>
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

            if (!string.IsNullOrEmpty(operationType) && !string.IsNullOrEmpty(barcode))
            {   
                await _offlineService.SaveTransaction(
                    operationType: operationType,
                    barcode: barcode,
                    payload: data ?? new object(),
                    deviceId: Constants.DeviceId
                );

                // Возвращаем заглушку для офлайн-режима
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
            var request = new HttpRequestMessage(HttpMethod.Post, $"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.SyncBox}");
            
            var data = new
            {
                barcode,
                locationCode = payload.ContainsKey("locationCode") ? payload["locationCode"]?.ToString() : "UNKNOWN",
                operationType
            };
            
            var json = JsonSerializer.Serialize(data);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("deviceId", Constants.DeviceId);

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = JsonSerializer.Deserialize<object>(content) ?? new object()
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

    // ============================================================
    // НОВЫЕ МЕТОДЫ ДЛЯ ИНВЕНТАРИЗАЦИИ
    // ============================================================

    /// <summary>
    /// Обновление количества коробки
    /// </summary>
    public async Task<Dictionary<string, object>> UpdateBoxQuantity(string boxId, int newQuantity)
    {
        try
        {
            var client = await GetHttpClient();
            var request = new HttpRequestMessage(HttpMethod.Put, $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/quantity");
            
            var data = new { quantity = newQuantity };
            var json = JsonSerializer.Serialize(data);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = JsonSerializer.Deserialize<object>(content) ?? new object()
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

    /// <summary>
    /// Перемещение коробки на новую локацию
    /// </summary>
    public async Task<Dictionary<string, object>> MoveBox(string boxId, string targetLocation)
    {
        try
        {
            var client = await GetHttpClient();
            var request = new HttpRequestMessage(HttpMethod.Put, $"{Constants.ApiBaseUrl}/api/boxes/{boxId}/move");
            
            var data = new { locationCode = targetLocation };
            var json = JsonSerializer.Serialize(data);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["data"] = JsonSerializer.Deserialize<object>(content) ?? new object()
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

    /// <summary>
    /// Получение списка коробок по коду локации
    /// </summary>
    public async Task<List<Box>> GetBoxesByLocation(string locationCode)
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/boxes/location/{locationCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var boxesData = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content) 
                                ?? new List<Dictionary<string, object>>();
                
                return boxesData.Select(Box.FromJson).ToList();
            }

            return new List<Box>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения коробок: {ex.Message}");
            return new List<Box>();
        }
    }

    /// <summary>
    /// Поиск коробки по штрихкоду
    /// </summary>
    public async Task<Box?> FindBoxByBarcode(string barcode)
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/boxes/barcode/{barcode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var boxData = JsonSerializer.Deserialize<Dictionary<string, object>>(content);
                if (boxData != null)
                {
                    return Box.FromJson(boxData);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка поиска коробки: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Получение информации о локации по коду
    /// </summary>
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
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения локации: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Синхронизация всех офлайн-транзакций
    /// </summary>
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

    /// <summary>
    /// Проверка статуса сервера с детальной информацией
    /// </summary>
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

    /// <summary>
    /// Отправка heartbeat для поддержания сессии
    /// </summary>
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

    /// <summary>
    /// Синхронизация справочника продуктов
    /// </summary>
    public async Task<bool> SyncProducts()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/products");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var products = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);
                
                if (products != null && products.Count > 0)
                {
                    var dbHelper = new DatabaseHelper();
                    var productCache = new List<ProductCache>();
                    
                    foreach (var p in products)
                    {
                        var id = p.GetValueOrDefault("id", "")?.ToString() ?? "";
                        var ean13 = p.GetValueOrDefault("ean13", "")?.ToString() ?? "";
                        var name = p.GetValueOrDefault("name", "")?.ToString() ?? "";
                        var shortName = p.GetValueOrDefault("shortName", "")?.ToString();
                        var onecGuid = p.GetValueOrDefault("oneCGuid", "")?.ToString();
                        var barcode = p.GetValueOrDefault("barcode", "")?.ToString();
                        var createdAt = p.GetValueOrDefault("createdAt", 0) is long ca ? ca : 0;
                        var updatedAt = p.GetValueOrDefault("updatedAt", 0) is long ua ? ua : 0;
                        
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(ean13) && !string.IsNullOrEmpty(name))
                        {
                            productCache.Add(new ProductCache
                            {
                                product_id = id,
                                ean13 = ean13,
                                name = name,
                                short_name = shortName,
                                onec_guid = onecGuid,
                                barcode = barcode,
                                created_at = createdAt,
                                updated_at = updatedAt
                            });
                        }
                    }
                    
                    await dbHelper.SyncProducts(productCache);
                    System.Diagnostics.Debug.WriteLine($"Синхронизировано {productCache.Count} продуктов");
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации продуктов: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Получить список всех локаций с сервера
    /// </summary>
    public async Task<List<Location>> GetLocations()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/locations");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var locationsData = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content) 
                                    ?? new List<Dictionary<string, object>>();
                
                return locationsData.Select(Location.FromJson).ToList();
            }

            return new List<Location>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения локаций: {ex.Message}");
            return new List<Location>();
        }
    }

    /// <summary>
    /// Синхронизация справочника локаций
    /// </summary>
    public async Task<bool> SyncLocations()
    {
        try
        {
            var client = await GetHttpClient();
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/locations");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var locations = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);
                
                if (locations != null && locations.Count > 0)
                {
                    var dbHelper = new DatabaseHelper();
                    var locationCache = new List<LocationCache>();
                    
                    foreach (var loc in locations)
                    {
                        var id = loc.GetValueOrDefault("id", "")?.ToString() ?? "";
                        var code = loc.GetValueOrDefault("code", "")?.ToString() ?? "";
                        var name = loc.GetValueOrDefault("name", "")?.ToString() ?? "";
                        var barcode = loc.GetValueOrDefault("barcode", "")?.ToString();
                        var isActive = loc.GetValueOrDefault("isActive", true) is bool ia ? ia : true;
                        var createdAt = loc.GetValueOrDefault("createdAt", 0) is long ca ? ca : 0;
                        
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(code))
                        {
                            locationCache.Add(new LocationCache
                            {
                                location_id = id,
                                code = code,
                                name = name,
                                barcode = barcode,
                                is_active = isActive ? 1 : 0,
                                created_at = createdAt
                            });
                        }
                    }
                    
                    await dbHelper.SyncLocations(locationCache);
                    System.Diagnostics.Debug.WriteLine($"Синхронизировано {locationCache.Count} локаций");
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации локаций: {ex.Message}");
            return false;
        }
    }

}