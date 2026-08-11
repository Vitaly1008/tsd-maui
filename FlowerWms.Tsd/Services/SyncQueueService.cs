using System.Text.Json;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Services;

public class SyncQueueService : IDisposable
{
    private readonly DatabaseHelper _dbHelper;
    private readonly ApiService _apiService;
    private readonly System.Timers.Timer? _autoSyncTimer;
    private bool _disposed;
    private bool _isProcessing;

    public event EventHandler<int>? PendingCountChanged;
    public event EventHandler<bool>? SyncStatusChanged;

    public SyncQueueService()
    {
        _dbHelper = new DatabaseHelper();
        _apiService = new ApiService();
        
        // ✅ Автосинхронизация каждые 30 секунд
        _autoSyncTimer = new System.Timers.Timer(30000);
        _autoSyncTimer.Elapsed += async (sender, e) => await AutoSync();
        _autoSyncTimer.AutoReset = true;
        _autoSyncTimer.Start();
        
        System.Diagnostics.Debug.WriteLine("✅ SyncQueueService инициализирован");
    }

    private async Task AutoSync()
    {
        try
        {
            var hasInternet = await _apiService.PingServer();
            if (hasInternet)
            {
                var pendingCount = await GetPendingCount();
                if (pendingCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 Автосинхронизация: {pendingCount} операций");
                    await ProcessQueueAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка автосинхронизации: {ex.Message}");
        }
    }

    public async Task EnqueueAsync(string operationType, string barcode, object payload, string deviceId)
    {
        try
        {
            var transaction = new OfflineTransaction
            {
                transaction_id = $"txn_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString().Substring(0, 8)}",
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
            
            var pendingCount = await GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            System.Diagnostics.Debug.WriteLine($"✅ Транзакция добавлена в очередь: {transaction.transaction_id}, тип: {operationType}");
            
            // ✅ Если есть интернет, пытаемся синхронизировать сразу
            var hasInternet = await _apiService.PingServer();
            if (hasInternet)
            {
                _ = Task.Run(async () => await ProcessQueueAsync());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка добавления в очередь: {ex.Message}");
            throw;
        }
    }

    public async Task<int> GetPendingCount()
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
            return count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения счетчика: {ex.Message}");
            return 0;
        }
    }

    public async Task ProcessQueueAsync()
    {
        if (_isProcessing)
        {
            System.Diagnostics.Debug.WriteLine("⚠️ Обработка очереди уже выполняется");
            return;
        }

        try
        {
            _isProcessing = true;
            SyncStatusChanged?.Invoke(this, true);
            
            // ✅ Получаем все несинхронизированные операции
            var operations = await GetUnsyncedOperations();
            
            if (!operations.Any())
            {
                System.Diagnostics.Debug.WriteLine("📭 Нет операций для синхронизации");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"🔄 Обработка {operations.Count} офлайн-операций");
            
            int successCount = 0;
            int errorCount = 0;
            
            // ✅ ВАЖНО: сначала обрабатываем ВСЕ приемки
            var receivingOps = operations.Where(o => o.operation_type == "Receiving").OrderBy(o => o.created_at);
            var shippingOps = operations.Where(o => o.operation_type == "Shipping").OrderBy(o => o.created_at);
            var otherOps = operations.Where(o => o.operation_type != "Receiving" && o.operation_type != "Shipping").OrderBy(o => o.created_at);
            
            // 1. Приемки
            foreach (var op in receivingOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessReceivingOperation(op, payload);
                    await MarkOperationSynced(op.transaction_id);
                    successCount++;
                    System.Diagnostics.Debug.WriteLine($"✅ Приемка синхронизирована: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            // 2. Отгрузки
            foreach (var op in shippingOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessShippingOperation(op, payload);
                    await MarkOperationSynced(op.transaction_id);
                    successCount++;
                    System.Diagnostics.Debug.WriteLine($"✅ Отгрузка синхронизирована: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            // 3. Остальные операции
            foreach (var op in otherOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessOtherOperation(op, payload);
                    await MarkOperationSynced(op.transaction_id);
                    successCount++;
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано: {successCount}, ошибок: {errorCount}");
            
            // ✅ Обновляем счетчик ожидающих
            var pending = await GetPendingCount();
            PendingCountChanged?.Invoke(this, pending);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обработки очереди: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            SyncStatusChanged?.Invoke(this, false);
        }
    }

    private async Task<List<OfflineTransaction>> GetUnsyncedOperations()
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            return await db.Table<OfflineTransaction>()
                .Where(t => t.is_synced == 0)
                .OrderBy(t => t.created_at)
                .Take(100)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения операций: {ex.Message}");
            return new List<OfflineTransaction>();
        }
    }

    private async Task HandleError(OfflineTransaction op, Exception ex)
    {
        op.retry_count++;
        op.error_message = ex.Message;
        
        System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации {op.transaction_id}: {ex.Message}");
        
        // ✅ После 3 попыток помечаем как завершенную с ошибкой
        if (op.retry_count >= 3)
        {
            op.is_synced = 1;
            op.error_message = $"Превышено число попыток: {ex.Message}";
            await MarkOperationSynced(op.transaction_id);
        }
        else
        {
            await UpdateOperationRetryCount(op.transaction_id, op.retry_count, ex.Message);
        }
    }

    private async Task ProcessReceivingOperation(OfflineTransaction op, Dictionary<string, object>? payload)
    {
        var barcode = op.barcode;
        var locationCode = payload?.GetValueOrDefault("locationCode")?.ToString() ?? "UNKNOWN";
        var boxNumber = 0;
        
        // Извлекаем boxNumber из payload или штрихкода
        if (payload != null)
        {
            var boxNumberObj = payload.GetValueOrDefault("boxNumber");
            if (boxNumberObj is int bi) boxNumber = bi;
            else if (boxNumberObj is string bs && int.TryParse(bs, out var bn)) boxNumber = bn;
        }
        
        if (boxNumber == 0 && !string.IsNullOrEmpty(barcode))
        {
            var parts = barcode.Split('-');
            if (parts.Length == 4 && int.TryParse(parts[3], out var n))
                boxNumber = n;
        }
        
        // ============================================================
        // ✅ 1. ПРОВЕРЯЕМ, СУЩЕСТВУЕТ ЛИ КОРОБКА НА СЕРВЕРЕ
        // ============================================================
        Box? serverBox = null;
        try
        {
            serverBox = await _apiService.GetBoxByBarcode(barcode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка проверки коробки: {ex.Message}");
        }
        
        if (serverBox != null)
        {
            // ============================================================
            // ✅ КОРОБКА СУЩЕСТВУЕТ НА СЕРВЕРЕ
            // ============================================================
            
            System.Diagnostics.Debug.WriteLine($"📦 Коробка найдена на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}");
            
            // Проверяем статус
            if (serverBox.Status == 1) // Уже Active
            {
                System.Diagnostics.Debug.WriteLine($"ℹ️ Коробка уже активна: {barcode}");
                // Обновляем локальный кэш
                await UpdateBoxCache(serverBox);
                return;
            }
            
            if (serverBox.Status == 0) // Draft
            {
                // ✅ Активируем коробку
                var result = await _apiService.ActivateBox(
                    boxId: serverBox.Id,
                    locationCode: locationCode,
                    comment: "Синхронизация из офлайн-режима"
                );
                
                if (!(result.TryGetValue("success", out var success) && success is bool s && s))
                {
                    var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                    throw new Exception($"Ошибка активации: {errorMsg}");
                }
                
                // Обновляем локальный кэш
                var updatedBox = await _apiService.GetBoxByBarcode(barcode);
                if (updatedBox != null)
                {
                    await UpdateBoxCache(updatedBox);
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ Коробка активирована: {barcode}");
                return;
            }
            
            // Другие статусы (Shipped, Empty, Discarded) — ошибка
            throw new Exception($"Коробка имеет статус {serverBox.Status}, активация невозможна");
        }
        
        // ============================================================
        // ✅ КОРОБКИ НЕТ НА СЕРВЕРЕ — СОЗДАЕМ НОВУЮ
        // ============================================================
        
        System.Diagnostics.Debug.WriteLine($"📦 Коробка не найдена на сервере, создаем новую: {barcode}");
        
        // Извлекаем данные из payload
        var ean13 = payload?.GetValueOrDefault("ean13")?.ToString() ?? "";
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var grade = payload?.GetValueOrDefault("grade")?.ToString() ?? "Premium";
        
        // Если ean13 не найден, пытаемся извлечь из штрихкода
        if (string.IsNullOrEmpty(ean13) && !string.IsNullOrEmpty(barcode))
        {
            var parts = barcode.Split('-');
            if (parts.Length > 0)
                ean13 = parts[0];
        }
        
        if (string.IsNullOrEmpty(ean13))
            throw new Exception("Не удалось определить EAN-13 продукта");
        
        if (quantity <= 0)
            quantity = 100; // Значение по умолчанию
        
        if (boxNumber == 0)
            throw new Exception("Не удалось определить номер коробки");
        
        // ✅ Создаем коробку на сервере (сразу Active!)
        var createResult = await _apiService.CreateBox(
            ean13: ean13,
            quantity: quantity,
            grade: grade,
            boxNumber: boxNumber,
            locationCode: locationCode
        );
        
        if (!(createResult.TryGetValue("success", out var createSuccess) && createSuccess is bool cs && cs))
        {
            var errorMsg = createResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
            throw new Exception($"Ошибка создания коробки: {errorMsg}");
        }
        
        // Обновляем локальный кэш
        var createdBox = await _apiService.GetBoxByBarcode(barcode);
        if (createdBox != null)
        {
            await UpdateBoxCache(createdBox);
        }
        
        System.Diagnostics.Debug.WriteLine($"✅ Коробка создана на сервере: {barcode}, номер: {boxNumber}");
    }

    private async Task ProcessShippingOperation(OfflineTransaction op, Dictionary<string, object>? payload)
    {
        var boxId = payload?.GetValueOrDefault("boxId")?.ToString();
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var isFullShipment = payload?.GetValueOrDefault("isFullShipment", false) is bool f && f;
        
        if (string.IsNullOrEmpty(boxId))
        {
            throw new Exception("Не указан boxId для операции отгрузки");
        }
        
        object result;
        if (isFullShipment || quantity <= 0)
        {
            // Полная отгрузка
            result = await _apiService.ShipBox(boxId, "Синхронизация из офлайн-режима");
        }
        else
        {
            // Частичная отгрузка
            result = await _apiService.ConsumeBox(boxId, quantity, "Синхронизация из офлайн-режима");
        }
        
        var resultDict = result as Dictionary<string, object>;
        if (!(resultDict?.TryGetValue("success", out var success) == true && success is bool s && s))
        {
            var errorMsg = resultDict?.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
            throw new Exception(errorMsg);
        }
        
        // ✅ После успешной отгрузки, обновляем локальный кэш
        var updatedBox = await _apiService.GetBoxByBarcode(op.barcode);
        if (updatedBox != null)
        {
            await UpdateBoxCache(updatedBox);
        }
        else
        {
            // Если коробка полностью отгружена, удаляем из кэша
            await _dbHelper.DeleteBoxByBarcode(op.barcode);
        }
        
        System.Diagnostics.Debug.WriteLine($"✅ Отгружена коробка: {op.barcode}, кол-во: {quantity}, полная: {isFullShipment}");
    }

    private async Task ProcessOtherOperation(OfflineTransaction op, Dictionary<string, object>? payload)
    {
        // Обработка других типов операций (Move, Inventory и т.д.)
        System.Diagnostics.Debug.WriteLine($"⚠️ Неизвестный тип операции: {op.operation_type}");
        // Помечаем как обработанную, чтобы не блокировать очередь
    }

    private async Task UpdateBoxCache(Box box)
    {
        // ✅ Сохраняем коробку в кэш ТОЛЬКО если она Active (1) или Empty (2)
        // Shipped (3) и Discarded (4) не нужны в кэше ТСД
        if (box.Status == 1 || box.Status == 2)
        {
            await _dbHelper.SaveBox(new BoxCache
            {
                barcode = box.Barcode,
                box_id = box.Id,
                box_number = box.BoxNumber,
                grade = box.Grade,
                initial_quantity = box.InitialQuantity,
                current_quantity = box.CurrentQuantity,
                product_id = box.ProductId,
                product_name = box.ProductName,
                product_ean13 = box.ProductEan13,
                location_code = box.LocationCode ?? "UNKNOWN",
                status = box.Status,
                created_at = box.CreatedAt,
                updated_at = box.UpdatedAt,
                is_dirty = 0
            });
            System.Diagnostics.Debug.WriteLine($"✅ Кэш обновлен: #{box.BoxNumber}, статус: {box.Status}");
        }
        else
        {
            // Если коробка не Active и не Empty — удаляем из кэша
            await _dbHelper.DeleteBoxByBarcode(box.Barcode);
            System.Diagnostics.Debug.WriteLine($"🗑️ Коробка удалена из кэша: #{box.BoxNumber}, статус: {box.Status}");
        }
    }

    private async Task MarkOperationSynced(string transactionId)
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
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка отметки операции: {ex.Message}");
        }
    }

    private async Task UpdateOperationRetryCount(string transactionId, int retryCount, string errorMessage)
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            await db.ExecuteAsync(
                "UPDATE offline_transactions SET retry_count = ?, error_message = ? WHERE transaction_id = ?",
                retryCount, errorMessage, transactionId
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления retry_count: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _autoSyncTimer?.Stop();
        _autoSyncTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}