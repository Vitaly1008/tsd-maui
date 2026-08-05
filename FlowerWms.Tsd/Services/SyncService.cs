using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Helpers;
using System.Text.Json;

namespace FlowerWms.Tsd.Services;

public class SyncService
{
    private readonly OfflineService _offlineService;
    private readonly ApiService _apiService;
    private bool _isSyncing;
    private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);

    public event EventHandler<SyncStatus>? StatusChanged;
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
        StatusChanged?.Invoke(this, hasInternet ? SyncStatus.Online : SyncStatus.Offline);
    }

    public async Task<bool> CheckInternetManual()
    {
        try
        {
            var token = await new SecureStorageService().GetToken();
            if (string.IsNullOrEmpty(token))
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Нет токена для проверки интернета");
                return false;
            }

            // ✅ Используем ApiService для проверки, а не создаем новый HttpClient
            // Просто проверяем через ApiService
            var apiService = new ApiService();
            return await apiService.PingServer();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ CheckInternetManual: {ex.Message}");
            return false;
        }
    }

    public async Task SyncAll()
    {
        // ✅ Защита от повторного вызова
        if (_isSyncing)
        {
            System.Diagnostics.Debug.WriteLine("⚠️ Синхронизация уже выполняется");
            return;
        }

        // ✅ Используем семафор для защиты
        if (!await _syncLock.WaitAsync(TimeSpan.Zero))
        {
            System.Diagnostics.Debug.WriteLine("⚠️ Синхронизация уже выполняется (семафор)");
            return;
        }

        try
        {
            var hasInternet = await CheckInternetManual();
            if (!hasInternet)
            {
                StatusChanged?.Invoke(this, SyncStatus.Offline);
                return;
            }

            var pendingCount = await _offlineService.GetPendingCount();
            if (pendingCount == 0)
            {
                System.Diagnostics.Debug.WriteLine("✅ Нет транзакций для синхронизации");
                return;
            }

            _isSyncing = true;
            StatusChanged?.Invoke(this, SyncStatus.Syncing);

            var transactions = await _offlineService.GetUnsyncedTransactions();
            int successCount = 0;
            int errorCount = 0;

            foreach (var tx in transactions)
            {
                // ✅ Проверяем, не превышен ли лимит попыток
                if (tx.retry_count >= 5)
                {
                    errorCount++;
                    continue;
                }

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
                        
                        if (result.TryGetValue("success", out var successObj) && successObj is bool success && !success)
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

                // ✅ Обновляем счетчик после каждой транзакции
                var remaining = await _offlineService.GetPendingCount();
                PendingCountChanged?.Invoke(this, remaining);
            }
            
            // ✅ Очищаем старые синхронизированные транзакции
            await _offlineService.CleanOldSynced();
            
            System.Diagnostics.Debug.WriteLine($"✅ Синхронизация завершена: успешно {successCount}, ошибок {errorCount}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ SyncAll error: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
            StatusChanged?.Invoke(this, SyncStatus.Online);

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