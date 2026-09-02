using System.Text.Json;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Services;

// Работа с офлайн-транзакциями
public class OfflineService
{
    private readonly DatabaseHelper _dbHelper;

    public OfflineService()
    {
        _dbHelper = new DatabaseHelper();
        Logger.Info("OfflineService инициализирован");
    }

    // Сохраняет транзакцию в офлайн-хранилище
    public async Task<string> SaveTransaction(
        string operationType,
        string barcode,
        object payload,
        string deviceId)
    {
        Logger.Info($"📥 SaveTransaction: operationType={operationType}, barcode={barcode}, deviceId={deviceId}");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var transactionId = $"offline_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            Logger.Info($"📋 Сгенерирован ID: {transactionId}");

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

            await db.InsertAsync(transaction);
            Logger.Info($"✅ Транзакция сохранена: {transactionId}");
            return transactionId;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка сохранения транзакции: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    // Возвращает несинхронизированные транзакции
    public async Task<List<OfflineTransaction>> GetUnsyncedTransactions()
    {
        Logger.Info($"🔍 GetUnsyncedTransactions: вызов");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var result = await db.QueryAsync<OfflineTransaction>(
                "SELECT * FROM offline_transactions WHERE is_synced = 0 ORDER BY created_at ASC"
            );
            Logger.Info($"📋 GetUnsyncedTransactions: найдено {result.Count} транзакций");
            foreach (var tx in result)
            {
                Logger.Info($"   - {tx.transaction_id}: {tx.operation_type}, {tx.barcode}, retry={tx.retry_count}");
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка получения транзакций: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return new List<OfflineTransaction>();
        }
    }

    // Возвращает количество ожидающих синхронизации транзакций
    public async Task<int> GetPendingCount()
    {
        Logger.Info($"🔍 GetPendingCount: вызов");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
            Logger.Info($"📊 GetPendingCount: {count}");
            return count;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка подсчета: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return 0;
        }
    }

    // Отмечает транзакцию как синхронизированную
    public async Task MarkAsSynced(string transactionId)
    {
        Logger.Info($"📝 MarkAsSynced: {transactionId}");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE offline_transactions SET is_synced = 1, synced_at = ? WHERE transaction_id = ?",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                transactionId
            );
            Logger.Info($"✅ Транзакция отмечена как синхронизированная: {transactionId}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка обновления: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
        }
    }

    // Отмечает транзакцию с ошибкой и увеличивает счетчик попыток
    public async Task MarkAsError(string transactionId, string error)
    {
        Logger.Info($"⚠️ MarkAsError: {transactionId}, error={error}");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE offline_transactions SET retry_count = retry_count + 1, error_message = ? WHERE transaction_id = ?",
                error,
                transactionId
            );
            Logger.Info($"✅ Транзакция отмечена как ошибка: {transactionId}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка обновления: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
        }
    }

    // Удаляет транзакцию
    public async Task DeleteTransaction(string transactionId)
    {
        Logger.Info($"🗑️ DeleteTransaction: {transactionId}");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE transaction_id = ?",
                transactionId
            );
            Logger.Info($"✅ Транзакция удалена: {transactionId}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка удаления: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    // Очищает старые синхронизированные транзакции
    public async Task CleanOldSynced(int olderThanDays = 30)
    {
        Logger.Info($"🧹 CleanOldSynced: olderThanDays={olderThanDays}");
        await _dbHelper.CleanOldData(olderThanDays);
        Logger.Info($"✅ CleanOldSynced завершен");
    }

    // Возвращает транзакции с пагинацией
    public async Task<List<OfflineTransaction>> GetTransactions(int limit = 50, int offset = 0)
    {
        Logger.Info($"🔍 GetTransactions: limit={limit}, offset={offset}");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var result = await db.QueryAsync<OfflineTransaction>(
                "SELECT * FROM offline_transactions ORDER BY created_at DESC LIMIT ? OFFSET ?",
                limit, offset
            );
            Logger.Info($"📋 GetTransactions: найдено {result.Count} транзакций");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка получения транзакций: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return new List<OfflineTransaction>();
        }
    }

    // Возвращает все несинхронизированные транзакции (без ограничений)
    public async Task<List<OfflineTransaction>> GetAllUnsyncedTransactions()
    {
        Logger.Info($"🔍 GetAllUnsyncedTransactions: вызов");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var result = await db.QueryAsync<OfflineTransaction>(
                "SELECT * FROM offline_transactions WHERE is_synced = 0 ORDER BY created_at ASC"
            );
            Logger.Info($"📋 GetAllUnsyncedTransactions: найдено {result.Count} транзакций");
            foreach (var tx in result)
            {
                Logger.Info($"   - {tx.transaction_id}: {tx.operation_type}, {tx.barcode}, retry={tx.retry_count}");
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка получения транзакций: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return new List<OfflineTransaction>();
        }
    }

    // Возвращает транзакцию по ID
    public async Task<OfflineTransaction?> GetTransactionById(string transactionId)
    {
        Logger.Info($"🔍 GetTransactionById: {transactionId}");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var result = await db.Table<OfflineTransaction>()
                .FirstOrDefaultAsync(t => t.transaction_id == transactionId);
            if (result != null)
            {
                Logger.Info($"✅ Транзакция найдена: {transactionId}, type={result.operation_type}");
            }
            else
            {
                Logger.Warning($"⚠️ Транзакция не найдена: {transactionId}");
            }
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка получения транзакции: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return null;
        }
    }

    // Получает транзакции с фильтрацией по типу операции
    public async Task<List<OfflineTransaction>> GetUnsyncedTransactionsByType(string operationType)
    {
        Logger.Info($"🔍 GetUnsyncedTransactionsByType: operationType={operationType}");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var result = await db.QueryAsync<OfflineTransaction>(
                "SELECT * FROM offline_transactions WHERE is_synced = 0 AND operation_type = ? ORDER BY created_at ASC",
                operationType
            );
            Logger.Info($"📋 GetUnsyncedTransactionsByType: найдено {result.Count} транзакций для {operationType}");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка получения транзакций: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return new List<OfflineTransaction>();
        }
    }

    /// <summary>
    /// Откатывает изменения, связанные с транзакцией
    /// </summary>
    public async Task<bool> RevertTransaction(string transactionId)
    {
        Logger.Info($"🔄 RevertTransaction: {transactionId}");
        try
        {
            var transaction = await GetTransactionById(transactionId);
            if (transaction == null)
            {
                Logger.Warning($"⚠️ Транзакция не найдена для отката: {transactionId}");
                return false;
            }

            Logger.Info($"📋 Тип транзакции для отката: {transaction.operation_type}, barcode={transaction.barcode}");

            // Парсим payload
            var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(transaction.payload);
            var barcode = transaction.barcode;

            if (transaction.operation_type == "Receiving")
            {
                // Приемка: удаляем коробку из локальной БД
                Logger.Info($"🗑️ Откат приемки: удаление коробки {barcode}");
                await _dbHelper.DeleteBoxByBarcode(barcode);
                Logger.Info($"✅ Откат приемки завершен: удалена коробка {barcode}");
            }
            else if (transaction.operation_type == "Shipping")
            {
                // Отгрузка: восстанавливаем количество
                Logger.Info($"🔄 Откат отгрузки: восстановление количества для {barcode}");
                var box = await _dbHelper.GetBoxByBarcode(barcode);
                if (box != null)
                {
                    var shippedQuantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
                    var restoredQuantity = box.current_quantity + shippedQuantity;
                    Logger.Info($"📊 Было: {box.current_quantity}, списано: {shippedQuantity}, будет: {restoredQuantity}");
                    
                    await _dbHelper.ForceUpdateBoxStatus(
                        barcode: barcode,
                        newStatus: BoxStatus.Active,
                        newQuantity: restoredQuantity
                    );
                    Logger.Info($"✅ Откат отгрузки завершен: восстановлено {shippedQuantity} шт. для {barcode}");
                }
                else
                {
                    Logger.Warning($"⚠️ Коробка не найдена в локальной БД для отката: {barcode}");
                }
            }
            else
            {
                Logger.Warning($"⚠️ Неизвестный тип операции для отката: {transaction.operation_type}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка отката транзакции: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return false;
        }
    }
}