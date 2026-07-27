using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Helpers;
using System.Text.Json;

namespace FlowerWms.Tsd.Services;

public class SyncService
{
    private readonly OfflineService _offlineService;
    private readonly ApiService _apiService;
    private readonly LoggerService _logger;
    private bool _isSyncing;

    public event EventHandler<SyncStatus>? StatusChanged;
    public event EventHandler<int>? PendingCountChanged;

    public SyncService()
    {
        _offlineService = new OfflineService();
        _apiService = new ApiService();
        _logger = new LoggerService();
    }

    public async Task Init()
    {
        _logger.Info("🔄 Инициализация SyncService");
        
        await CheckConnectivity();

        var pendingCount = await _offlineService.GetPendingCount();
        PendingCountChanged?.Invoke(this, pendingCount);

        await SyncAll();
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
                return false;

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            client.Timeout = TimeSpan.FromSeconds(3);
            
            // Используем новый Ping эндпоинт
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}{Constants.ApiEndpoints.Ping}");
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Success("🌐 Интернет доступен");
                return true;
            }
            
            _logger.Warning($"🌐 Сервер ответил с кодом: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning($"🌐 Проверка интернета: недоступен ({ex.Message})");
            return false;
        }
    }

    public async Task SyncAll()
    {
        if (_isSyncing)
        {
            _logger.Warning("🔄 Синхронизация уже выполняется");
            return;
        }

        var hasInternet = await CheckInternetManual();
        if (!hasInternet)
        {
            _logger.Warning("🌐 Нет интернета, синхронизация отложена");
            return;
        }

        var pendingCount = await _offlineService.GetPendingCount();
        if (pendingCount == 0)
        {
            _logger.Info("✅ Нет транзакций для синхронизации");
            return;
        }

        _isSyncing = true;
        StatusChanged?.Invoke(this, SyncStatus.Syncing);

        _logger.Info($"🔄 Начало синхронизации ({pendingCount} транзакций)");

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

                    _logger.Info($"  📤 Синхронизация: {transactionId}");

                    var boxesJson = payload.ContainsKey("boxes") 
                        ? JsonSerializer.Serialize(payload["boxes"]) 
                        : "[]";
                    var boxes = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(boxesJson) 
                                ?? new List<Dictionary<string, object>>();

                    if (boxes.Count == 0)
                    {
                        _logger.Warning($"⚠️ Нет коробок в транзакции!");
                        await _offlineService.MarkAsError(transactionId, "Нет коробок для синхронизации");
                        errorCount++;
                        continue;
                    }

                    bool allSuccess = true;
                    foreach (var boxData in boxes)
                    {
                        var box = Box.FromJson(boxData);
                        _logger.Info($"  📤 Отправка коробки: {box.Barcode}");

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

                        _logger.Info($"  📥 Результат: {result["success"]}");

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
                        _logger.Success($"  ✅ {transactionId} синхронизирована");
                    }
                    else
                    {
                        await _offlineService.MarkAsError(transactionId, "Ошибка синхронизации коробок");
                        errorCount++;
                        _logger.Error($"  ❌ {transactionId}: ошибка синхронизации");
                    }
                }
                catch (Exception ex)
                {
                    await _offlineService.MarkAsError(tx.transaction_id, ex.Message);
                    errorCount++;
                    _logger.Error($"  ❌ {tx.transaction_id}: {ex.Message}");
                }

                var remaining = await _offlineService.GetPendingCount();
                PendingCountChanged?.Invoke(this, remaining);
            }

            _logger.Success($"✅ Синхронизация завершена: {successCount} успешно, {errorCount} ошибок");
            await _offlineService.CleanOldSynced();
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка синхронизации: {ex.Message}");
        }
        finally
        {
            _isSyncing = false;
            StatusChanged?.Invoke(this, SyncStatus.Online);

            var remaining = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, remaining);
        }
    }

    public async Task SyncManual()
    {
        _logger.Info("🔄 Ручная синхронизация");
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

    /// <summary>
    /// Проверка наличия неподтвержденных транзакций
    /// </summary>
    public async Task<bool> HasPendingTransactions()
    {
        var count = await GetPendingCount();
        return count > 0;
    }
}

public enum SyncStatus
{
    Online,
    Offline,
    Syncing
}