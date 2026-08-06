using System.Text.Json;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Services;

public class SyncQueueService
{
    private readonly DatabaseHelper _dbHelper;
    private readonly ApiService _apiService;
    private readonly SyncService _syncService;
    private Timer? _autoSyncTimer;
    private bool _isSyncing;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private const int AUTO_SYNC_INTERVAL_MS = 30000; // 30 секунд

    public event EventHandler<int>? PendingCountChanged;
    public event EventHandler<bool>? SyncStatusChanged;

    public SyncQueueService()
    {
        _dbHelper = new DatabaseHelper();
        _apiService = new ApiService();
        _syncService = new SyncService();
        
        StartAutoSync();
    }

    /// <summary>
    /// Добавить транзакцию в очередь синхронизации
    /// </summary>
    public async Task<string> EnqueueAsync(
        string operationType,
        string barcode,
        object payload,
        string deviceId)
    {
        try
        {
            var transactionId = $"txn_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}";
            
            var transaction = new OfflineTransaction
            {
                transaction_id = transactionId,
                operation_type = operationType,
                barcode = barcode,
                payload = JsonSerializer.Serialize(payload),
                device_id = deviceId,
                created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                is_synced = 0,
                retry_count = 0
            };

            var db = await _dbHelper.GetDatabaseAsync();
            await db.InsertAsync(transaction);

            await NotifyPendingCount();

            // Если есть сеть - сразу синхронизируем
            if (await _syncService.CheckInternetManual())
            {
                _ = Task.Run(() => ProcessQueueAsync());
            }

            return transactionId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка добавления в очередь: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Обработка очереди синхронизации
    /// </summary>
    public async Task ProcessQueueAsync()
    {
        if (_isSyncing) return;
        if (!await _syncLock.WaitAsync(0)) return;

        try
        {
            _isSyncing = true;
            SyncStatusChanged?.Invoke(this, true);

            var hasInternet = await _syncService.CheckInternetManual();
            if (!hasInternet)
            {
                System.Diagnostics.Debug.WriteLine("📴 Нет интернета, синхронизация отложена");
                return;
            }

            var db = await _dbHelper.GetDatabaseAsync();
            var transactions = await db.Table<OfflineTransaction>()
                .Where(t => t.is_synced == 0 && t.retry_count < 5)
                .OrderBy(t => t.created_at)
                .Take(50)
                .ToListAsync();

            if (transactions.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("✅ Нет транзакций для синхронизации");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"🔄 Начинаем синхронизацию: {transactions.Count} транзакций");

            int successCount = 0;
            int errorCount = 0;

            foreach (var tx in transactions)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(tx.payload) 
                                  ?? new Dictionary<string, object>();

                    // Определяем тип операции
                    var success = tx.operation_type switch
                    {
                        "Receiving" => await SyncReceivingAsync(tx, payload),
                        "Shipping" => await SyncShippingAsync(tx, payload),
                        "Move" => await SyncMoveAsync(tx, payload),
                        "Inventory" => await SyncInventoryAsync(tx, payload),
                        _ => false
                    };

                    if (success)
                    {
                        await db.ExecuteAsync(
                            "DELETE FROM offline_transactions WHERE transaction_id = ?",
                            tx.transaction_id
                        );
                        successCount++;
                        System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано: {tx.transaction_id}");
                    }
                    else
                    {
                        await db.ExecuteAsync(
                            "UPDATE offline_transactions SET retry_count = retry_count + 1, error_message = ? WHERE transaction_id = ?",
                            $"Ошибка синхронизации",
                            tx.transaction_id
                        );
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    await db.ExecuteAsync(
                        "UPDATE offline_transactions SET retry_count = retry_count + 1, error_message = ? WHERE transaction_id = ?",
                        ex.Message,
                        tx.transaction_id
                    );
                    errorCount++;
                    System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"📊 Синхронизация завершена: успешно {successCount}, ошибок {errorCount}");

            await NotifyPendingCount();
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
            SyncStatusChanged?.Invoke(this, false);
        }
    }

    // --- Синхронизация разных типов операций ---

    private async Task<bool> SyncReceivingAsync(OfflineTransaction tx, Dictionary<string, object> payload)
    {
        if (payload.TryGetValue("boxes", out var boxesObj))
        {
            var boxesJson = JsonSerializer.Serialize(boxesObj);
            var boxes = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(boxesJson) 
                        ?? new List<Dictionary<string, object>>();

            var locationCode = payload.GetValueOrDefault("locationCode", "UNKNOWN")?.ToString() ?? "UNKNOWN";

            foreach (var boxData in boxes)
            {
                var barcode = boxData.GetValueOrDefault("barcode", "")?.ToString() ?? "";
                if (string.IsNullOrEmpty(barcode)) continue;

                var syncPayload = new Dictionary<string, object>
                {
                    ["boxId"] = boxData.GetValueOrDefault("id", Guid.NewGuid().ToString())?.ToString() ?? "",
                    ["boxNumber"] = boxData.GetValueOrDefault("boxNumber", 0),
                    ["productName"] = boxData.GetValueOrDefault("productName", "")?.ToString() ?? "",
                    ["productEan13"] = boxData.GetValueOrDefault("productEan13", "")?.ToString() ?? "",
                    ["quantity"] = boxData.GetValueOrDefault("quantity", 100),
                    ["locationCode"] = locationCode,
                    ["grade"] = boxData.GetValueOrDefault("grade", "Premium")?.ToString() ?? "Premium",
                    ["operationType"] = "Receiving",
                    ["status"] = boxData.GetValueOrDefault("status", 1)
                };

                var result = await _apiService.SyncOfflineTransaction(
                    transactionId: tx.transaction_id,
                    operationType: "Receiving",
                    barcode: barcode,
                    payload: syncPayload
                );

                if (!(bool)(result.GetValueOrDefault("success", false)))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private async Task<bool> SyncShippingAsync(OfflineTransaction tx, Dictionary<string, object> payload)
    {
        // Аналогично Receiving, но с операцией Shipping
        if (payload.TryGetValue("boxes", out var boxesObj))
        {
            var boxesJson = JsonSerializer.Serialize(boxesObj);
            var boxes = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(boxesJson) 
                        ?? new List<Dictionary<string, object>>();

            foreach (var boxData in boxes)
            {
                var barcode = boxData.GetValueOrDefault("barcode", "")?.ToString() ?? "";
                if (string.IsNullOrEmpty(barcode)) continue;

                var syncPayload = new Dictionary<string, object>
                {
                    ["boxId"] = boxData.GetValueOrDefault("id", Guid.NewGuid().ToString())?.ToString() ?? "",
                    ["boxNumber"] = boxData.GetValueOrDefault("boxNumber", 0),
                    ["productName"] = boxData.GetValueOrDefault("productName", "")?.ToString() ?? "",
                    ["productEan13"] = boxData.GetValueOrDefault("productEan13", "")?.ToString() ?? "",
                    ["quantity"] = boxData.GetValueOrDefault("quantity", 100),
                    ["operationType"] = "Shipping"
                };

                var result = await _apiService.SyncOfflineTransaction(
                    transactionId: tx.transaction_id,
                    operationType: "Shipping",
                    barcode: barcode,
                    payload: syncPayload
                );

                if (!(bool)(result.GetValueOrDefault("success", false)))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private async Task<bool> SyncMoveAsync(OfflineTransaction tx, Dictionary<string, object> payload)
    {
        var boxId = payload.GetValueOrDefault("boxId", "")?.ToString() ?? "";
        var targetLocation = payload.GetValueOrDefault("targetLocation", "")?.ToString() ?? "";
        
        if (string.IsNullOrEmpty(boxId) || string.IsNullOrEmpty(targetLocation))
            return false;

        var result = await _apiService.MoveBox(boxId, targetLocation);
        return (bool)(result.GetValueOrDefault("success", false));
    }

    private async Task<bool> SyncInventoryAsync(OfflineTransaction tx, Dictionary<string, object> payload)
    {
        var boxId = payload.GetValueOrDefault("boxId", "")?.ToString() ?? "";
        var newQuantity = payload.GetValueOrDefault("newQuantity", 0) is int q ? q : 0;
        
        if (string.IsNullOrEmpty(boxId))
            return false;

        var result = await _apiService.UpdateBoxQuantity(boxId, newQuantity);
        return (bool)(result.GetValueOrDefault("success", false));
    }

    /// <summary>
    /// Запуск автоматической синхронизации
    /// </summary>
    private void StartAutoSync()
    {
        _autoSyncTimer = new Timer(async _ =>
        {
            try
            {
                await ProcessQueueAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка авто-синхронизации: {ex.Message}");
            }
        }, null, AUTO_SYNC_INTERVAL_MS, AUTO_SYNC_INTERVAL_MS);
    }

    public async Task<int> GetPendingCount()
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            return await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
        }
        catch
        {
            return 0;
        }
    }

    private async Task NotifyPendingCount()
    {
        var count = await GetPendingCount();
        PendingCountChanged?.Invoke(this, count);
    }

    public void Dispose()
    {
        _autoSyncTimer?.Dispose();
        _syncLock?.Dispose();
    }
}