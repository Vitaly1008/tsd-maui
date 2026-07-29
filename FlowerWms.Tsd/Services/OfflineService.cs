using System.Text.Json;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Services;

public class OfflineService
{
    private readonly DatabaseHelper _dbHelper;

    public OfflineService()
    {
        _dbHelper = new DatabaseHelper();
    }

    public async Task<string> SaveTransaction(
        string operationType,
        string barcode,
        object payload,
        string deviceId)
    {
        try
        {
            var db = _dbHelper.Database;
            var transactionId = $"offline_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

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
            throw;
        }
    }

    public async Task<List<OfflineTransaction>> GetUnsyncedTransactions()
    {
        try
        {
            var db = _dbHelper.Database;
            return await db.QueryAsync<OfflineTransaction>(
                "SELECT * FROM offline_transactions WHERE is_synced = 0 AND retry_count < 5 ORDER BY created_at ASC"
            );
        }
        catch (Exception ex)
        {
            return new List<OfflineTransaction>();
        }
    }

    public async Task<int> GetPendingCount()
    {
        try
        {
            var db = _dbHelper.Database;
            return await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
        }
        catch
        {
            return 0;
        }
    }

    public async Task MarkAsSynced(string transactionId)
    {
        try
        {
            var db = _dbHelper.Database;
            await db.ExecuteAsync(
                "UPDATE offline_transactions SET is_synced = 1, synced_at = ? WHERE transaction_id = ?",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                transactionId
            );
        }
        catch (Exception ex)
        {
            /* Do nothing*/
        }
    }

    public async Task MarkAsError(string transactionId, string error)
    {
        try
        {
            var db = _dbHelper.Database;
            await db.ExecuteAsync(
                "UPDATE offline_transactions SET retry_count = retry_count + 1, error_message = ? WHERE transaction_id = ?",
                error,
                transactionId
            );
        }
        catch (Exception ex)
        {
            /* Do nothing*/
        }
    }

    public async Task DeleteTransaction(string transactionId)
    {
        try
        {
            var db = _dbHelper.Database;
            await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE transaction_id = ?",
                transactionId
            );
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public async Task CleanOldSynced(int olderThanDays = 30)
    {
        try
        {
            var db = _dbHelper.Database;
            var cutoff = DateTimeOffset.UtcNow.AddDays(-olderThanDays).ToUnixTimeMilliseconds();
            
            await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE is_synced = 1 AND synced_at < ?",
                cutoff
            );
        }
        catch (Exception ex)
        {
            /* Do nothing*/
        }
    }
}