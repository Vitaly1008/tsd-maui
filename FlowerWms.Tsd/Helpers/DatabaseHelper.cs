using SQLite;

namespace FlowerWms.Tsd.Helpers;

public class DatabaseHelper
{
    private SQLiteAsyncConnection? _database;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private bool _isInitialized;

    public DatabaseHelper()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "alpha_wms_offline.db");
    }

    public async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (_database != null && _isInitialized)
            return _database;

        await _semaphore.WaitAsync();
        try
        {
            if (_database != null && _isInitialized)
                return _database;

            _database = new SQLiteAsyncConnection(_dbPath);
            await InitializeDatabaseAsync();
            _isInitialized = true;
            
            return _database;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task InitializeDatabaseAsync()
    {
        if (_database == null) return;

        try
        {
            // ✅ Создаем таблицы
            await _database.CreateTableAsync<OfflineTransaction>();
            await _database.CreateTableAsync<BoxCache>();
            await _database.CreateTableAsync<LocationCache>();

            // ✅ Создаем индексы
            await _database.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_transactions_synced ON offline_transactions(is_synced, created_at)"
            );
            await _database.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_transactions_device ON offline_transactions(device_id, is_synced)"
            );
            await _database.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_boxes_barcode ON boxes_cache(barcode)"
            );
            await _database.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_transactions_created ON offline_transactions(created_at)"
            );
            
            System.Diagnostics.Debug.WriteLine("✅ База данных инициализирована");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка инициализации БД: {ex.Message}");
            throw;
        }
    }

    // ✅ Добавляем метод для проверки существования таблицы
    public async Task<bool> TableExists(string tableName)
    {
        var db = await GetDatabaseAsync();
        try
        {
            var count = await db.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'"
            );
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    // ✅ Добавляем метод для очистки старых данных
    public async Task CleanOldData(int daysToKeep = 30)
    {
        var db = await GetDatabaseAsync();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysToKeep).ToUnixTimeMilliseconds();
        
        try
        {
            await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE is_synced = 1 AND synced_at < ?",
                cutoff
            );
            
            System.Diagnostics.Debug.WriteLine($"✅ Очищены старые данные (старше {daysToKeep} дней)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка очистки данных: {ex.Message}");
        }
    }

    // ✅ Добавляем метод для компактизации БД
    public async Task VacuumAsync()
    {
        var db = await GetDatabaseAsync();
        try
        {
            await db.ExecuteAsync("VACUUM");
            System.Diagnostics.Debug.WriteLine("✅ База данных скомпактизирована");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка компактизации: {ex.Message}");
        }
    }
}