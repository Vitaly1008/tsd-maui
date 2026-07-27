using SQLite;

namespace FlowerWms.Tsd.Helpers;

public class DatabaseHelper
{
    private SQLiteAsyncConnection? _database;
    private readonly string _dbPath;

    public DatabaseHelper()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "alpha_wms_offline.db");
    }

    public SQLiteAsyncConnection Database
    {
        get
        {
            if (_database == null)
            {
                _database = new SQLiteAsyncConnection(_dbPath);
                
                var task = Task.Run(async () =>
                {
                    await _database.CreateTableAsync<OfflineTransaction>();
                    await _database.CreateTableAsync<BoxCache>();
                    await _database.CreateTableAsync<LocationCache>();

                    await _database.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS idx_transactions_synced ON offline_transactions(is_synced, created_at)"
                    );
                    await _database.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS idx_transactions_device ON offline_transactions(device_id, is_synced)"
                    );
                    await _database.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS idx_boxes_barcode ON boxes_cache(barcode)"
                    );
                });
                task.Wait();
            }
            return _database;
        }
    }

    public async Task<bool> TableExists(string tableName)
    {
        var db = Database;
        var count = await db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'"
        );
        return count > 0;
    }
}