using System.Text.Json;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Services;

// Работа с офлайн-транзакциями
public class OfflineService
{
    private readonly DatabaseHelper _dbHelper;

    public OfflineService()
    {
        _dbHelper = new DatabaseHelper();
    }

    // Сохраняет транзакцию в офлайн-хранилище
    public async Task<string> SaveTransaction(
        string operationType,
        string barcode,
        object payload,
        string deviceId)
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var transactionId = $"offline_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";

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
            return transactionId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка сохранения транзакции: {ex.Message}");
            throw;
        }
    }

    // Возвращает несинхронизированные транзакции
    public async Task<List<OfflineTransaction>> GetUnsyncedTransactions()
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            return await db.QueryAsync<OfflineTransaction>(
                "SELECT * FROM offline_transactions WHERE is_synced = 0 AND retry_count < 5 ORDER BY created_at ASC LIMIT 100"
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка получения транзакций: {ex.Message}");
            return new List<OfflineTransaction>();
        }
    }

    // Возвращает количество ожидающих синхронизации транзакций
    public async Task<int> GetPendingCount()
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            return await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка подсчета: {ex.Message}");
            return 0;
        }
    }

    // Отмечает транзакцию как синхронизированную
    public async Task MarkAsSynced(string transactionId)
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE offline_transactions SET is_synced = 1, synced_at = ? WHERE transaction_id = ?",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                transactionId
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка обновления: {ex.Message}");
        }
    }

    // Отмечает транзакцию с ошибкой и увеличивает счетчик попыток
    public async Task MarkAsError(string transactionId, string error)
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE offline_transactions SET retry_count = retry_count + 1, error_message = ? WHERE transaction_id = ?",
                error,
                transactionId
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка обновления: {ex.Message}");
        }
    }

    // Удаляет транзакцию
    public async Task DeleteTransaction(string transactionId)
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE transaction_id = ?",
                transactionId
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка удаления: {ex.Message}");
            throw;
        }
    }

    // Очищает старые синхронизированные транзакции
    public async Task CleanOldSynced(int olderThanDays = 30)
    {
        await _dbHelper.CleanOldData(olderThanDays);
    }

    // Возвращает транзакции с пагинацией
    public async Task<List<OfflineTransaction>> GetTransactions(int limit = 50, int offset = 0)
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            return await db.QueryAsync<OfflineTransaction>(
                "SELECT * FROM offline_transactions ORDER BY created_at DESC LIMIT ? OFFSET ?",
                limit, offset
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка получения транзакций: {ex.Message}");
            return new List<OfflineTransaction>();
        }
    }
}