using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Helpers;
using System.Text.Json;

namespace FlowerWms.Tsd.Services;

public class SyncService
{
    private readonly OfflineService _offlineService;
    private readonly ApiService _apiService;
    private bool _isSyncing;

    // ✅ Используем FlowerWms.Tsd.Models.SyncStatus
    public event EventHandler<FlowerWms.Tsd.Models.SyncStatus>? StatusChanged;
    public event EventHandler<int>? PendingCountChanged;

    public SyncService()
    {
        _offlineService = new OfflineService();
        _apiService = new ApiService();
    }

    public async Task Init()
    {   
        await CheckConnectivity();

        var pendingCount = await _offlineService.GetPendingCount();
        PendingCountChanged?.Invoke(this, pendingCount);
    }

    public async Task CheckConnectivity()
    {
        var hasInternet = await CheckInternetManual();
        StatusChanged?.Invoke(this, hasInternet ? FlowerWms.Tsd.Models.SyncStatus.Online : FlowerWms.Tsd.Models.SyncStatus.Offline);
    }

    public async Task<bool> CheckInternetManual()
    {
        try
        {
            var token = await new SecureStorageService().GetToken();
            if (string.IsNullOrEmpty(token))
                return false;

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            client.Timeout = TimeSpan.FromSeconds(3);
            
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.Ping}");
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ CheckInternetManual: {ex.Message}");
            return false;
        }
    }

    public async Task SyncAll()
    {
        if (_isSyncing)
        {
            return;
        }

        var hasInternet = await CheckInternetManual();
        if (!hasInternet)
        {
            StatusChanged?.Invoke(this, FlowerWms.Tsd.Models.SyncStatus.Offline);
            return;
        }

        var pendingCount = await _offlineService.GetPendingCount();
        if (pendingCount == 0)
        {
            return;
        }

        _isSyncing = true;
        StatusChanged?.Invoke(this, FlowerWms.Tsd.Models.SyncStatus.Syncing);

        try
        {
            var transactions = await _offlineService.GetUnsyncedTransactions();
            int successCount = 0;
            int errorCount = 0;

            foreach (var tx in transactions)
            {
                try
                {
                    var transactionId = tx.transaction_id;
                    var operationType = tx.operation_type;
                    var barcode = tx.barcode;
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(tx.payload) 
                                  ?? new Dictionary<string, object>();

                    var boxesJson = payload.ContainsKey("boxes") 
                        ? JsonSerializer.Serialize(payload["boxes"]) 
                        : "[]";
                    var boxes = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(boxesJson) 
                                ?? new List<Dictionary<string, object>>();

                    if (boxes.Count == 0)
                    {
                        await _offlineService.MarkAsError(transactionId, "Нет коробок для синхронизации");
                        errorCount++;
                        continue;
                    }

                    bool allSuccess = true;
                    foreach (var boxData in boxes)
                    {
                        var box = Box.FromJson(boxData);

                        var locationCode = payload.ContainsKey("locationCode") 
                            ? payload["locationCode"]?.ToString() ?? "UNKNOWN" 
                            : "UNKNOWN";

                        var result = await _apiService.SyncOfflineTransaction(
                            transactionId: transactionId,
                            operationType: operationType,
                            barcode: box.Barcode,
                            payload: new Dictionary<string, object>
                            {
                                ["boxId"] = box.Id,
                                ["boxNumber"] = box.BoxNumber,
                                ["productName"] = box.ProductName,
                                ["productEan13"] = box.ProductEan13,
                                ["quantity"] = box.Quantity,
                                ["locationCode"] = locationCode,
                                ["grade"] = box.Grade,
                                ["operationType"] = operationType
                            }
                        );
                        if (!(bool)result["success"])
                        {
                            allSuccess = false;
                            break;
                        }
                    }

                    if (allSuccess)
                    {
                        await _offlineService.MarkAsSynced(transactionId);
                        successCount++;
                    }
                    else
                    {
                        await _offlineService.MarkAsError(transactionId, "Ошибка синхронизации коробок");
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    await _offlineService.MarkAsError(tx.transaction_id, ex.Message);
                    errorCount++;
                }

                var remaining = await _offlineService.GetPendingCount();
                PendingCountChanged?.Invoke(this, remaining);
            }
            await _offlineService.CleanOldSynced();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ SyncAll error: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
            StatusChanged?.Invoke(this, FlowerWms.Tsd.Models.SyncStatus.Online);

            var remaining = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, remaining);
        }
    }

    public async Task SyncManual()
    {
        await SyncAll();
    }

    public async Task<int> GetPendingCount()
    {
        return await _offlineService.GetPendingCount();
    }

    public async Task<bool> HasPending()
    {
        var count = await GetPendingCount();
        return count > 0;
    }

    public bool IsSyncing => _isSyncing;

    public async Task<bool> HasPendingTransactions()
    {
        var count = await GetPendingCount();
        return count > 0;
    }
}