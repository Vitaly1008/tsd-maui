using System.Text.Json;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Services;

// Очередь синхронизации
public class SyncQueueService : IDisposable
{
    private readonly DatabaseHelper _dbHelper;
    private readonly ApiService _apiService;
    private readonly OfflineService _offlineService;
    private readonly System.Timers.Timer? _autoSyncTimer;
    private bool _disposed;
    private int _isProcessingFlag;   // ✅ БАГ #6: Interlocked вместо bool

    public event EventHandler<int>? PendingCountChanged;
    public event EventHandler<bool>? SyncStatusChanged;

    public SyncQueueService(
        DatabaseHelper dbHelper,
        ApiService apiService,
        OfflineService offlineService)
    {
        _dbHelper = dbHelper;
        _apiService = apiService;
        _offlineService = offlineService;

        Logger.Info("SyncQueueService инициализирован");
    }

    public async Task EnqueueAsync(string operationType, string barcode, object payload, string deviceId, long? createdAt = null)
    {
        Logger.Info($"📥 EnqueueAsync: operationType={operationType}, barcode={barcode}, deviceId={deviceId}");
        Logger.Info($"📦 Payload: {JsonSerializer.Serialize(payload)}");

        try
        {
            var transactionId = await _offlineService.SaveTransaction(
                operationType,
                barcode,
                payload,
                deviceId,
                createdAt
            );

            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);

            Logger.Info($"✅ Транзакция добавлена в очередь: {transactionId}, тип: {operationType}, pending={pendingCount}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка добавления в очередь: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    public async Task<int> GetPendingCount()
    {
        Logger.Info($"🔍 GetPendingCount: вызов");
        var count = await _offlineService.GetPendingCount();
        Logger.Info($"📊 GetPendingCount: {count}");
        return count;
    }

    // ============================================================
    // ✅ УПРОЩЁННАЯ ЛОГИКА (ручная синхронизация)
    //
    // Сценарий 1 (нет связи)     → response == null → исключение,
    //                              транзакции НЕ трогаем
    // Сценарий 2 (сервер лежит)  → response == null → исключение,
    //                              транзакции НЕ трогаем
    // Сценарий 3 (бизнес-ошибка) → сервер ответил:
    //                              Success         → удаляем
    //                              ProblemBoxCreated → удаляем
    //                              иначе           → НЕ трогаем
    // ============================================================
    public async Task ProcessQueueAsync()
    {
        Logger.Info($"🔄 НАЧАЛО ProcessQueueAsync");

        if (Interlocked.CompareExchange(ref _isProcessingFlag, 1, 0) != 0)
        {
            Logger.Info("⚠️ Обработка очереди уже выполняется");
            return;
        }

        try
        {
            SyncStatusChanged?.Invoke(this, true);

            var operations = await _offlineService.GetUnsyncedTransactions();
            Logger.Info($"📋 Получено {operations.Count} транзакций");

            if (!operations.Any())
            {
                Logger.Info("✅ Нет операций для синхронизации");
                return;
            }

            // ✅ TC-021: сначала ВСЕ приёмки, потом ВСЕ отгрузки.
            // Внутри каждой группы — по created_at ASC.
            var receiving = operations
                .Where(o => o.operation_type == "Receiving")
                .OrderBy(o => o.created_at)
                .ToList();

            var shipping = operations
                .Where(o => o.operation_type == "Shipping")
                .OrderBy(o => o.created_at)
                .ToList();

            var ordered = receiving.Concat(shipping).ToList();

            Logger.Info($"📋 Порядок отправки: Receiving={receiving.Count}, Shipping={shipping.Count}");
            foreach (var o in ordered)
                Logger.Info($"   → {o.transaction_id}: {o.operation_type}, created_at={o.created_at}");

            // ✅ 1. Собираем ВСЕ транзакции в один запрос
            var request = new SyncBoxesRequest();
            foreach (var op in ordered)
            {
                Dictionary<string, object> payload;
                try
                {
                    payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload)
                            ?? new Dictionary<string, object>();
                }
                catch (Exception ex)
                {
                    Logger.Error($"❌ Не удалось распарсить payload {op.transaction_id}: {ex.Message}");
                    // ✅ Невалидный payload — оставляем транзакцию,
                    //    оператор разберётся вручную (кнопка ✕ в UI)
                    continue;
                }

                // ✅ Локация берётся из boxes_cache — единственного источника правды.
                //    Это гарантирует, что смена локации в середине сессии
                //    (когда ProcessLocationScan обновил boxes_cache) уйдёт на сервер.
                //    Если коробки нет в кэше — fallback на payload, потом UNKNOWN.
                var boxCache = await _dbHelper.GetBoxByBarcode(op.barcode);
                var locationCode = boxCache?.location_code
                                ?? ExtractString(payload, "locationCode")
                                ?? ExtractString(payload, "LocationCode")
                                ?? "UNKNOWN";

                request.Transactions.Add(new TransactionSyncDto
                {
                    TransactionId = op.transaction_id,
                    OperationType = op.operation_type,
                    Barcode = op.barcode,
                    LocationCode = locationCode,
                    Payload = payload,
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(op.created_at).UtcDateTime
                });
            }

            if (!request.Transactions.Any())
            {
                Logger.Warning("⚠️ Все транзакции отброшены из-за невалидного payload");
                return;
            }

            // ✅ 2. Один запрос на сервер
            var response = await _apiService.SyncBoxes(request);

            if (response == null)
            {
                // ✅ Сценарий 1 и 2: связь упала / сервер лежит.
                //    Транзакции НЕ трогаем. Бросаем исключение,
                //    чтобы UI показал оператору понятное сообщение.
                Logger.Error("❌ SyncBoxes вернул null — транзакции остаются в очереди");
                throw new InvalidOperationException(
                    "Сервер недоступен. Транзакции остались в очереди.\nПовторите синхронизацию позже."
                );
            }

            Logger.Info($"📊 Ответ сервера: total={response.TotalProcessed}, success={response.SuccessCount}, fail={response.FailCount}, problemBoxes={response.ProblemBoxesCreated.Count}");

            // ✅ 3. Обрабатываем ответ построчно
            int deleted = 0, skipped = 0;

            foreach (var txResult in response.ProcessedTransactions)
            {
                if (txResult.Success)
                {
                    await _offlineService.DeleteTransaction(txResult.TransactionId);
                    deleted++;
                    continue;
                }

                if (txResult.ProblemBoxCreated)
                {
                    // ✅ Сценарий 3 (terminal): сервер сам создал ProblemBox.
                    //    Локальную транзакцию удаляем — она не станет валидной от повторов.
                    Logger.Warning($"⚠️ ProblemBox создан сервером для {txResult.TransactionId}: {txResult.Error}");
                    await _offlineService.DeleteTransaction(txResult.TransactionId);
                    deleted++;
                    continue;
                }

                // ✅ Сценарий 3 (transient, но сервер жив):
                //    сервер ответил, но не создал ProblemBox.
                //    Транзакцию НЕ трогаем — оператор повторит синхронизацию вручную.
                Logger.Warning($"⚠️ Транзакция {txResult.TransactionId} не обработана: {txResult.Error}");
                skipped++;
            }

            Logger.Info($"📊 Итог синхронизации: удалено={deleted}, пропущено={skipped}");

            var pending = await _offlineService.GetPendingCount();
            Logger.Info($"📊 Осталось в очереди: {pending}");
            PendingCountChanged?.Invoke(this, pending);
        }
        finally
        {
            Interlocked.Exchange(ref _isProcessingFlag, 0);
            SyncStatusChanged?.Invoke(this, false);
            Logger.Info($"🏁 КОНЕЦ ProcessQueueAsync");
        }
    }

    // ✅ Хелпер для безопасного извлечения строк
    private static string? ExtractString(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is string s) return s;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.String)
            return je.GetString();
        return value.ToString();
    }

    public async Task<int> ClearSyncTable()
    {
        Logger.Info($"🗑️ ClearSyncTable: вызов");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();

            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
            Logger.Info($"📊 Найдено несинхронизированных транзакций: {count}");

            if (count == 0)
            {
                Logger.Info("✅ Нет несинхронизированных транзакций для очистки");
                return 0;
            }

            var deleted = await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE is_synced = 0"
            );

            Logger.Info($"✅ Очищено {deleted} несинхронизированных транзакций");

            var pendingCount = await _offlineService.GetPendingCount();
            Logger.Info($"📊 Осталось в очереди: {pendingCount}");
            PendingCountChanged?.Invoke(this, pendingCount);

            return deleted;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка очистки таблицы синхронизации: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    public async Task DeleteTransaction(string transactionId)
    {
        Logger.Info($"🗑️ DeleteTransaction: {transactionId}");
        try
        {
            await _offlineService.DeleteTransaction(transactionId);
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            Logger.Info($"✅ Транзакция удалена: {transactionId}, осталось: {pendingCount}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка удаления транзакции: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    public async Task<List<OfflineTransaction>> GetAllPendingTransactions()
    {
        Logger.Info($"🔍 GetAllPendingTransactions: вызов");
        var result = await _offlineService.GetAllUnsyncedTransactions();
        Logger.Info($"📋 Получено {result.Count} транзакций");
        foreach (var tx in result)
        {
            Logger.Info($"   - {tx.transaction_id}: {tx.operation_type}, {tx.barcode}");
        }
        return result;
    }

    public async Task<bool> DeletePendingTransaction(string transactionId)
    {
        Logger.Info($"🗑️ DeletePendingTransaction: {transactionId}");
        try
        {
            var transaction = await _offlineService.GetTransactionById(transactionId);
            if (transaction == null)
            {
                Logger.Warning($"⚠️ Транзакция {transactionId} не найдена");
                return false;
            }

            if (transaction.is_synced == 1)
            {
                Logger.Warning($"⚠️ Транзакция {transactionId} уже синхронизирована");
                return false;
            }

            Logger.Info($"🔄 Откат транзакции: {transactionId}");
            await _offlineService.RevertTransaction(transactionId);

            Logger.Info($"🗑️ Удаление транзакции: {transactionId}");
            await _offlineService.DeleteTransaction(transactionId);

            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);

            Logger.Info($"✅ Транзакция удалена и откачена: {transactionId}, осталось: {pendingCount}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка удаления транзакции: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    public async Task<bool> SyncSingleTransaction(string transactionId)
    {
        Logger.Info($"🔄 SyncSingleTransaction: {transactionId}");

        var transaction = await _offlineService.GetTransactionById(transactionId);
        if (transaction == null || transaction.is_synced == 1)
            return false;

        Dictionary<string, object> payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, object>>(transaction.payload)
                    ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Не удалось распарсить payload {transactionId}: {ex.Message}");
            return false;
        }

        // ✅ Локация берётся из boxes_cache (см. комментарий в ProcessQueueAsync)
        var boxCache = await _dbHelper.GetBoxByBarcode(transaction.barcode);
        var locationCode = boxCache?.location_code
                        ?? ExtractString(payload, "locationCode")
                        ?? ExtractString(payload, "LocationCode")
                        ?? "UNKNOWN";

        var request = new SyncBoxesRequest
        {
            Transactions = new List<TransactionSyncDto>
            {
                new TransactionSyncDto
                {
                    TransactionId = transaction.transaction_id,
                    OperationType = transaction.operation_type,
                    Barcode = transaction.barcode,
                    LocationCode = locationCode,
                    Payload = payload,
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(transaction.created_at).UtcDateTime
                }
            }
        };

        var response = await _apiService.SyncBoxes(request);
        if (response == null)
        {
            // Связь упала — не трогаем транзакцию
            Logger.Error($"❌ SyncSingleTransaction: SyncBoxes вернул null");
            return false;
        }

        var txResult = response.ProcessedTransactions.FirstOrDefault();
        if (txResult == null)
        {
            // Сервер не вернул результат — не трогаем
            Logger.Warning($"⚠️ SyncSingleTransaction: сервер не вернул результат");
            return false;
        }

        if (txResult.Success || txResult.ProblemBoxCreated)
        {
            await _offlineService.DeleteTransaction(transactionId);
            var pending = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pending);
            Logger.Info($"✅ SyncSingleTransaction: {transactionId} удалена (Success={txResult.Success}, ProblemBox={txResult.ProblemBoxCreated})");
            return txResult.Success;
        }

        // Транзиентная ошибка сервера — не трогаем
        Logger.Warning($"⚠️ SyncSingleTransaction: транзакция {transactionId} не обработана: {txResult.Error}");
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;

        Logger.Info($"🗑️ Dispose SyncQueueService");
        _autoSyncTimer?.Stop();
        _autoSyncTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}