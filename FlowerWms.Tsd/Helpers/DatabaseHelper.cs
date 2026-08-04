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
            await _database.CreateTableAsync<OfflineTransaction>();
            await _database.CreateTableAsync<BoxCache>();
            await _database.CreateTableAsync<LocationCache>();
            await _database.CreateTableAsync<ProductCache>();

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
                "CREATE INDEX IF NOT EXISTS idx_products_ean13 ON products_cache(ean13)"
            );
            await _database.ExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_products_id ON products_cache(product_id)"
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

    // ============================================================
    // МЕТОДЫ ДЛЯ РАБОТЫ С ПРОДУКТАМИ
    // ============================================================

    public async Task<ProductCache?> GetProductByEan13(string ean13)
    {
        try
        {
            var db = await GetDatabaseAsync();
            return await db.Table<ProductCache>().FirstOrDefaultAsync(p => p.ean13 == ean13);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения продукта: {ex.Message}");
            return null;
        }
    }

    public async Task<ProductCache?> GetProductById(string productId)
    {
        try
        {
            var db = await GetDatabaseAsync();
            return await db.Table<ProductCache>().FirstOrDefaultAsync(p => p.product_id == productId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения продукта по ID: {ex.Message}");
            return null;
        }
    }

    public async Task SaveProduct(ProductCache product)
    {
        try
        {
            var db = await GetDatabaseAsync();
            product.updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.InsertOrReplaceAsync(product);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения продукта: {ex.Message}");
        }
    }

    public async Task SyncProducts(List<ProductCache> products)
    {
        try
        {
            var db = await GetDatabaseAsync();
            foreach (var product in products)
            {
                product.updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await db.InsertOrReplaceAsync(product);
            }
            System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано {products.Count} продуктов");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации продуктов: {ex.Message}");
        }
    }

    // ============================================================
    // ✅ МЕТОДЫ ДЛЯ РАБОТЫ С КОРОБКАМИ
    // ============================================================

    public async Task SaveBox(BoxCache box)
    {
        try
        {
            var db = await GetDatabaseAsync();
            
            // ✅ Принудительно проверяем статус
            if (box.status == 0)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Статус 0, меняем на 1 для коробки {box.barcode}");
                box.status = 1;
            }
            
            box.updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // ✅ Используем InsertOrReplace с проверкой
            try
            {
                // Пытаемся обновить существующую запись
                var existing = await db.Table<BoxCache>()
                    .FirstOrDefaultAsync(b => b.barcode == box.barcode);
                
                if (existing != null)
                {
                    // ✅ Обновляем существующую запись
                    await db.UpdateAsync(box);
                    System.Diagnostics.Debug.WriteLine($"✅ Коробка обновлена: {box.barcode}, статус={box.status}");
                }
                else
                {
                    // ✅ Вставляем новую запись
                    await db.InsertAsync(box);
                    System.Diagnostics.Debug.WriteLine($"✅ Коробка создана: {box.barcode}, статус={box.status}");
                }
            }
            catch (SQLiteException ex) when (ex.Message.Contains("PRIMARY KEY"))
            {
                // Если все равно ошибка PRIMARY KEY - вставляем принудительно
                System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка PRIMARY KEY, вставляем принудительно: {ex.Message}");
                
                // Удаляем существующую запись
                await db.ExecuteAsync("DELETE FROM boxes_cache WHERE box_id = ?", box.box_id);
                await db.InsertAsync(box);
                
                System.Diagnostics.Debug.WriteLine($"✅ Коробка вставлена принудительно: {box.barcode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения коробки: {ex.Message}");
                throw;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения коробки: {ex.Message}");
            throw;
        }
    }

    public async Task<BoxCache?> GetBoxByBarcode(string barcode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var box = await db.Table<BoxCache>().FirstOrDefaultAsync(b => b.barcode == barcode);
            
            if (box != null)
            {
                System.Diagnostics.Debug.WriteLine($"📦 Найдена коробка: {barcode}, статус={box.status}");
            }
            
            return box;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения коробки: {ex.Message}");
            return null;
        }
    }

    public async Task<List<BoxCache>> GetBoxesByLocation(string locationCode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var boxes = await db.Table<BoxCache>()
                .Where(b => b.location_code == locationCode && b.status == 1)
                .ToListAsync();
            
            System.Diagnostics.Debug.WriteLine($"📦 Найдено {boxes.Count} активных коробок в локации {locationCode}");
            return boxes;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения коробок по локации: {ex.Message}");
            return new List<BoxCache>();
        }
    }

    public async Task<List<BoxCache>> GetAllActiveBoxes()
    {
        try
        {
            var db = await GetDatabaseAsync();
            return await db.Table<BoxCache>()
                .Where(b => b.status == 1)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения активных коробок: {ex.Message}");
            return new List<BoxCache>();
        }
    }

    public async Task UpdateBoxStatus(string boxId, int newStatus)
    {
        try
        {
            var db = await GetDatabaseAsync();
            
            // ✅ Если статус 0, меняем на 1
            if (newStatus == 0)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Попытка установить статус 0 для коробки {boxId}, меняем на 1");
                newStatus = 1;
            }
            
            await db.ExecuteAsync(
                "UPDATE boxes_cache SET status = ?, updated_at = ? WHERE box_id = ?",
                newStatus,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                boxId
            );
            
            System.Diagnostics.Debug.WriteLine($"✅ Статус коробки {boxId} обновлен на {newStatus}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления статуса: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверка существования коробки по штрихкоду в кэше
    /// </summary>
    public async Task<bool> IsBoxExistsByBarcode(string barcode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM boxes_cache WHERE barcode = ? AND status = 1",
                barcode
            );
            return count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка проверки существования коробки: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Проверка существования коробки по номеру
    /// </summary>
    public async Task<bool> IsBoxNumberExists(int boxNumber)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM boxes_cache WHERE box_number = ? AND status = 1",
                boxNumber
            );
            return count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка проверки номера коробки: {ex.Message}");
            return false;
        }
    }

    public async Task SyncBoxes(List<BoxCache> boxes)
    {
        try
        {
            var db = await GetDatabaseAsync();
            foreach (var box in boxes)
            {
                if (box.status == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Статус 0, меняем на 1 для коробки {box.barcode}");
                    box.status = 1;
                }
                
                box.updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await db.InsertOrReplaceAsync(box);
            }
            System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано {boxes.Count} коробок");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации коробок: {ex.Message}");
        }
    }

    // ============================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ============================================================

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