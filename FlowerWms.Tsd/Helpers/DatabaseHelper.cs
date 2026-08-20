using FlowerWms.Tsd.Models;
using SQLite;

namespace FlowerWms.Tsd.Helpers;

// Работа с локальной SQLite БД
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

    // ============================================================
    // ИНИЦИАЛИЗАЦИЯ БД
    // ============================================================

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
    // МЕТОДЫ ДЛЯ РАБОТЫ С КОРОБКАМИ
    // ============================================================

    public async Task SaveBox(BoxCache box)
    {
        try
        {
            var db = await GetDatabaseAsync();
            box.updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            await db.ExecuteAsync(
                "DELETE FROM boxes_cache WHERE barcode = ?",
                box.barcode
            );
            
            await db.InsertAsync(box);
            System.Diagnostics.Debug.WriteLine($"✅ Коробка сохранена: {box.barcode}, статус={box.status}");
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
            return await db.Table<BoxCache>().FirstOrDefaultAsync(b => b.barcode == barcode);
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
                .Where(b => b.location_code == locationCode && b.status == BoxStatus.Active)
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
                .Where(b => b.status == BoxStatus.Active)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения активных коробок: {ex.Message}");
            return new List<BoxCache>();
        }
    }

    public async Task<List<BoxCache>> GetAllBoxes()
    {
        try
        {
            var db = await GetDatabaseAsync();
            return await db.Table<BoxCache>().ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения всех коробок: {ex.Message}");
            return new List<BoxCache>();
        }
    }

    public async Task<List<BoxCache>> GetBoxesByStatus(BoxStatus status)
    {
        try
        {
            var db = await GetDatabaseAsync();
            return await db.Table<BoxCache>()
                .Where(b => b.status == status)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения коробок по статусу: {ex.Message}");
            return new List<BoxCache>();
        }
    }

    public async Task UpdateBoxStatus(string boxId, int newStatus)
    {
        try
        {
            var db = await GetDatabaseAsync();
            
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

    public async Task UpdateBoxQuantity(string barcode, int newQuantity)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE boxes_cache SET current_quantity = ?, updated_at = ? WHERE barcode = ?",
                newQuantity,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                barcode
            );
            
            System.Diagnostics.Debug.WriteLine($"✅ Количество коробки {barcode} обновлено на {newQuantity}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления количества: {ex.Message}");
        }
    }

    public async Task DeleteBoxByBarcode(string barcode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.ExecuteAsync(
                "DELETE FROM boxes_cache WHERE barcode = ?",
                barcode
            );
            System.Diagnostics.Debug.WriteLine($"✅ Коробка удалена из кэша: {barcode}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка удаления коробки: {ex.Message}");
        }
    }

    public async Task SyncBoxes(List<BoxCache> boxes)
    {
        try
        {
            var db = await GetDatabaseAsync();
            foreach (var box in boxes)
            {
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

    public async Task<List<BoxCache>> GetDirtyBoxes()
    {
        try
        {
            var db = await GetDatabaseAsync();
            var tableInfo = await db.QueryAsync<TableInfo>("PRAGMA table_info(boxes_cache)");
            var hasDirtyColumn = tableInfo.Any(c => c.name == "is_dirty");
            
            if (!hasDirtyColumn)
            {
                await db.ExecuteAsync("ALTER TABLE boxes_cache ADD COLUMN is_dirty INTEGER DEFAULT 0");
            }
            
            return await db.Table<BoxCache>()
                .Where(b => b.isPartial == 1)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения dirty коробок: {ex.Message}");
            return new List<BoxCache>();
        }
    }

    public async Task MarkBoxSynced(string barcode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE boxes_cache SET is_dirty = 0 WHERE barcode = ?",
                barcode
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка отметки коробки: {ex.Message}");
        }
    }

    public async Task CleanOldBoxes(int daysToKeep = 30)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-daysToKeep).ToUnixTimeMilliseconds();
            
            var deleted = await db.ExecuteAsync(
                "DELETE FROM boxes_cache WHERE status = 3 AND updated_at < ?",
                cutoff
            );
            
            System.Diagnostics.Debug.WriteLine($"✅ Удалено {deleted} старых коробок из кэша");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка очистки кэша: {ex.Message}");
        }
    }

    // ============================================================
    // МЕТОДЫ ПРОВЕРКИ СУЩЕСТВОВАНИЯ КОРОБОК
    // ============================================================

    // Проверка существования АКТИВНОЙ коробки по штрихкоду (status == 1)
    public async Task<bool> IsActiveBoxExistsByBarcode(string barcode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var box = await db.Table<BoxCache>()
                .FirstOrDefaultAsync(b => b.barcode == barcode && b.status == BoxStatus.Active);
            return box != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка проверки активной коробки: {ex.Message}");
            return false;
        }
    }

    // Проверка существования ЛЮБОЙ коробки по штрихкоду (включая Draft и Empty)
    public async Task<bool> IsAnyBoxExistsByBarcode(string barcode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var box = await db.Table<BoxCache>().FirstOrDefaultAsync(b => b.barcode == barcode);
            return box != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка проверки существования коробки: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IsActiveBoxNumberExists(int boxNumber)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM boxes_cache WHERE box_number = ? AND status = 1",
                boxNumber,
                BoxStatus.Active
            );
            return count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка проверки номера коробки: {ex.Message}");
            return false;
        }
    }

    // ============================================================
    // МЕТОДЫ ДЛЯ РАБОТЫ С ЛОКАЦИЯМИ
    // ============================================================

    public async Task<LocationCache?> GetLocationByCode(string code)
    {
        try
        {
            var db = await GetDatabaseAsync();
            return await db.Table<LocationCache>().FirstOrDefaultAsync(l => l.code == code);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения локации: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> IsLocationExists(string code)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var location = await db.Table<LocationCache>().FirstOrDefaultAsync(l => l.code == code);
            return location != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка проверки локации: {ex.Message}");
            return false;
        }
    }

    public async Task SaveLocation(LocationCache location)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.InsertOrReplaceAsync(location);
            System.Diagnostics.Debug.WriteLine($"✅ Локация сохранена: {location.code}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения локации: {ex.Message}");
        }
    }

    public async Task SyncLocations(List<LocationCache> locations)
    {
        try
        {
            var db = await GetDatabaseAsync();
            foreach (var location in locations)
            {
                await db.InsertOrReplaceAsync(location);
            }
            System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано {locations.Count} локаций");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации локаций: {ex.Message}");
        }
    }

    // ============================================================
    // МЕТОДЫ ДЛЯ РАБОТЫ С ИСТОРИЕЙ ОПЕРАЦИЙ
    // ============================================================

    public async Task SaveBoxOperation(BoxOperationCache operation)
    {
        try
        {
            var db = await GetDatabaseAsync();
            
            var tableExists = await TableExists("box_operations_cache");
            if (!tableExists)
            {
                await db.CreateTableAsync<BoxOperationCache>();
            }
            
            operation.created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await db.InsertAsync(operation);
            
            System.Diagnostics.Debug.WriteLine($"✅ Операция сохранена: {operation.operation_type} для коробки {operation.box_barcode}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения операции: {ex.Message}");
        }
    }

    public async Task<List<BoxOperationCache>> GetBoxOperationsByBarcode(string barcode)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var tableExists = await TableExists("box_operations_cache");
            if (!tableExists)
            {
                await db.CreateTableAsync<BoxOperationCache>();
                return new List<BoxOperationCache>();
            }
            
            return await db.Table<BoxOperationCache>()
                .Where(o => o.box_barcode == barcode)
                .OrderByDescending(o => o.created_at)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения истории: {ex.Message}");
            return new List<BoxOperationCache>();
        }
    }

    public async Task<List<BoxOperationCache>> GetBoxOperationsByBoxId(string boxId)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var tableExists = await TableExists("box_operations_cache");
            if (!tableExists)
            {
                await db.CreateTableAsync<BoxOperationCache>();
                return new List<BoxOperationCache>();
            }
            
            return await db.Table<BoxOperationCache>()
                .Where(o => o.box_id == boxId)
                .OrderByDescending(o => o.created_at)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения истории: {ex.Message}");
            return new List<BoxOperationCache>();
        }
    }

    public async Task<List<BoxOperationCache>> GetUnsyncedOperations(string? operationType = null)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var tableExists = await TableExists("box_operations_cache");
            if (!tableExists)
            {
                await db.CreateTableAsync<BoxOperationCache>();
                return new List<BoxOperationCache>();
            }
            
            var query = "SELECT * FROM box_operations_cache WHERE is_synced = 0";
            if (!string.IsNullOrEmpty(operationType))
            {
                query += $" AND operation_type = '{operationType}'";
            }
            query += " ORDER BY created_at ASC LIMIT 100";
            
            return await db.QueryAsync<BoxOperationCache>(query);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения операций: {ex.Message}");
            return new List<BoxOperationCache>();
        }
    }

    public async Task MarkOperationSynced(string operationId)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE box_operations_cache SET is_synced = 1, synced_at = ? WHERE operation_id = ?",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                operationId
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка отметки операции: {ex.Message}");
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

    // Обновление статуса локальной коробки
    public async Task UpdateBoxStatusByBarcode(string barcode, BoxStatus newStatus)
    {
        try
        {
            var db = await GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE boxes_cache SET status = ?, updated_at = ? WHERE barcode = ?",
                (int)newStatus,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                barcode
            );
            System.Diagnostics.Debug.WriteLine($"✅ Статус коробки {barcode} обновлен на {newStatus}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления статуса: {ex.Message}");
        }
    }

    // массовое обновление статусов
    public async Task UpdateBoxesStatus(List<string> barcodes, BoxStatus newStatus)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var barcodeList = string.Join(",", barcodes.Select(b => $"'{b}'"));
            await db.ExecuteAsync(
                $"UPDATE boxes_cache SET status = ?, updated_at = ? WHERE barcode IN ({barcodeList})",
                (int)newStatus,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
            System.Diagnostics.Debug.WriteLine($"✅ Обновлено {barcodes.Count} коробок до статуса {newStatus}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка массового обновления: {ex.Message}");
        }
    }

    /// <summary>
    /// Принудительно обновляет статус и количество коробки в локальной БД
    /// Используется для гарантированного обновления после отгрузки/приемки
    /// </summary>
    public async Task ForceUpdateBoxStatus(string barcode, BoxStatus newStatus, int newQuantity)
    {
        try
        {
            var db = await GetDatabaseAsync();
            
            // Проверяем существование колонки is_dirty
            var tableInfo = await db.QueryAsync<TableInfo>("PRAGMA table_info(boxes_cache)");
            var hasDirtyColumn = tableInfo.Any(c => c.name == "is_dirty");
            
            // Получаем текущую коробку для сохранения остальных полей
            var existingBox = await GetBoxByBarcode(barcode);
            
            if (existingBox != null)
            {
                // Обновляем существующую запись
                var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                
                if (hasDirtyColumn)
                {
                    await db.ExecuteAsync(
                        @"UPDATE boxes_cache 
                        SET status = ?, 
                            current_quantity = ?, 
                            updated_at = ?,
                            is_dirty = 0
                        WHERE barcode = ?",
                        (int)newStatus,
                        newQuantity,
                        updatedAt,
                        barcode
                    );
                }
                else
                {
                    await db.ExecuteAsync(
                        @"UPDATE boxes_cache 
                        SET status = ?, 
                            current_quantity = ?, 
                            updated_at = ?
                        WHERE barcode = ?",
                        (int)newStatus,
                        newQuantity,
                        updatedAt,
                        barcode
                    );
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ Принудительно обновлен статус: {barcode} -> {newStatus}, кол-во: {newQuantity}");
            }
            else
            {
                // Если коробки нет в кэше, создаем новую запись
                var newBox = new BoxCache
                {
                    barcode = barcode,
                    box_id = barcode, // временный ID
                    box_number = 0,
                    grade = "Premium",
                    initial_quantity = newQuantity,
                    current_quantity = newQuantity,
                    product_id = string.Empty,
                    product_name = "Неизвестный продукт",
                    product_ean13 = string.Empty,
                    location_code = "UNKNOWN",
                    status = newStatus,
                    created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    isPartial = 0
                };
                
                await db.InsertAsync(newBox);
                System.Diagnostics.Debug.WriteLine($"✅ Создана новая запись коробки: {barcode} -> {newStatus}, кол-во: {newQuantity}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка принудительного обновления статуса: {ex.Message}");
            throw; // Пробрасываем исключение для обработки на верхнем уровне
        }
    }

    /// <summary>
    /// Принудительно обновляет статус для нескольких коробок
    /// </summary>
    public async Task ForceUpdateBoxesStatus(List<string> barcodes, BoxStatus newStatus, int newQuantity = 0)
    {
        try
        {
            var db = await GetDatabaseAsync();
            var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            foreach (var barcode in barcodes)
            {
                await db.ExecuteAsync(
                    @"UPDATE boxes_cache 
                    SET status = ?, 
                        current_quantity = ?, 
                        updated_at = ?,
                        is_dirty = 0
                    WHERE barcode = ?",
                    (int)newStatus,
                    newQuantity,
                    updatedAt,
                    barcode
                );
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ Принудительно обновлено {barcodes.Count} коробок -> {newStatus}, кол-во: {newQuantity}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка массового обновления статусов: {ex.Message}");
            throw;
        }
    }

    // Вспомогательный класс для PRAGMA table_info
    public class TableInfo
    {
        public string name { get; set; } = string.Empty;
    }
}