using System.Text;
using System.Text.Json;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

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
                    ["data"] = JsonSerializer.Deserialize<object>(content)
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
}