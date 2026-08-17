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
    private bool _isProcessing;

    public event EventHandler<int>? PendingCountChanged;
    public event EventHandler<bool>? SyncStatusChanged;

    public SyncQueueService()
    {
        _dbHelper = new DatabaseHelper();
        _apiService = new ApiService();
        _offlineService = new OfflineService();
        
        _autoSyncTimer = new System.Timers.Timer(30000);
        _autoSyncTimer.Elapsed += async (sender, e) => await AutoSync();
        _autoSyncTimer.AutoReset = true;
        _autoSyncTimer.Start();
        
        System.Diagnostics.Debug.WriteLine("SyncQueueService инициализирован");
    }

    // Автоматическая синхронизация
    private async Task AutoSync()
    {
        try
        {
            var hasInternet = await _apiService.PingServer();
            if (hasInternet)
            {
                var pendingCount = await _offlineService.GetPendingCount();
                if (pendingCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Автосинхронизация: {pendingCount} операций");
                    await ProcessQueueAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка автосинхронизации: {ex.Message}");
        }
    }

    // Добавляет операцию в очередь
    public async Task EnqueueAsync(string operationType, string barcode, object payload, string deviceId)
    {
        try
        {
            var transactionId = await _offlineService.SaveTransaction(
                operationType,
                barcode,
                payload,
                deviceId
            );
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            System.Diagnostics.Debug.WriteLine($"Транзакция добавлена в очередь: {transactionId}, тип: {operationType}");
            
            var hasInternet = await _apiService.PingServer();
            if (hasInternet)
            {
                _ = Task.Run(async () => await ProcessQueueAsync());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка добавления в очередь: {ex.Message}");
            throw;
        }
    }

    // Возвращает количество ожидающих операций
    public async Task<int> GetPendingCount()
    {
        return await _offlineService.GetPendingCount();
    }

    // Обрабатывает очередь операций
    public async Task ProcessQueueAsync()
    {
        if (_isProcessing)
        {
            System.Diagnostics.Debug.WriteLine("Обработка очереди уже выполняется");
            return;
        }

        try
        {
            _isProcessing = true;
            SyncStatusChanged?.Invoke(this, true);
            
            var operations = await _offlineService.GetUnsyncedTransactions();
            
            if (!operations.Any())
            {
                System.Diagnostics.Debug.WriteLine("Нет операций для синхронизации");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"Обработка {operations.Count} офлайн-операций");
            
            int successCount = 0;
            int errorCount = 0;
            
            var receivingOps = operations.Where(o => o.operation_type == "Receiving").OrderBy(o => o.created_at);
            var shippingOps = operations.Where(o => o.operation_type == "Shipping").OrderBy(o => o.created_at);
            var otherOps = operations.Where(o => o.operation_type != "Receiving" && o.operation_type != "Shipping").OrderBy(o => o.created_at);
            
            foreach (var op in receivingOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessReceivingOperation(op, payload);
                    await _offlineService.MarkAsSynced(op.transaction_id);
                    successCount++;
                    System.Diagnostics.Debug.WriteLine($"Приемка синхронизирована: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            foreach (var op in shippingOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessShippingOperation(op, payload);
                    await _offlineService.MarkAsSynced(op.transaction_id);
                    successCount++;
                    System.Diagnostics.Debug.WriteLine($"Отгрузка синхронизирована: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            foreach (var op in otherOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessOtherOperation(op, payload);
                    await _offlineService.MarkAsSynced(op.transaction_id);
                    successCount++;
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"Синхронизировано: {successCount}, ошибок: {errorCount}");
            
            var pending = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pending);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка обработки очереди: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            SyncStatusChanged?.Invoke(this, false);
        }
    }

    // Обрабатывает ошибку синхронизации
    private async Task HandleError(OfflineTransaction op, Exception ex)
    {
        op.retry_count++;
        op.error_message = ex.Message;
        
        System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации {op.transaction_id}: {ex.Message}");
        
        if (op.retry_count >= 3)
        {
            op.is_synced = 1;
            op.error_message = $"Превышено число попыток: {ex.Message}";
            await _offlineService.MarkAsSynced(op.transaction_id);
        }
        else
        {
            await _offlineService.MarkAsError(op.transaction_id, ex.Message);
        }
    }

    // Обрабатывает операцию приемки
    private async Task ProcessReceivingOperation(OfflineTransaction op, Dictionary<string, object>? payload)
    {
        var barcode = op.barcode;
        var locationCode = payload?.GetValueOrDefault("locationCode")?.ToString() ?? "UNKNOWN";
        var boxNumber = 0;
        
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
        
        Box? serverBox = null;
        try
        {
            serverBox = await _apiService.GetBoxByBarcode(barcode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка проверки коробки: {ex.Message}");
        }
        
        if (serverBox != null)
        {
            System.Diagnostics.Debug.WriteLine($"Коробка найдена на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}");
            
            if (serverBox.Status == BoxStatus.Active)
            {
                System.Diagnostics.Debug.WriteLine($"Коробка уже активна: {barcode}");
                await UpdateBoxCache(serverBox);
                return;
            }
            
            if (serverBox.Status == BoxStatus.Draft)
            {
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
                
                var updatedBox = await _apiService.GetBoxByBarcode(barcode);
                if (updatedBox != null)
                {
                    await UpdateBoxCache(updatedBox);
                    // обновляем статус в локальной БД
                    await UpdateBoxStatusAfterSync(barcode, BoxStatus.Active);
                }
                
                System.Diagnostics.Debug.WriteLine($"Коробка активирована: {barcode}");
                return;
            }
            
            throw new Exception($"Коробка имеет статус {serverBox.Status}, активация невозможна");
        }
        
        System.Diagnostics.Debug.WriteLine($"Коробка не найдена на сервере, создаем новую: {barcode}");
        
        var ean13 = payload?.GetValueOrDefault("ean13")?.ToString() ?? "";
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var grade = payload?.GetValueOrDefault("grade")?.ToString() ?? "Premium";
        
        if (string.IsNullOrEmpty(ean13) && !string.IsNullOrEmpty(barcode))
        {
            var parts = barcode.Split('-');
            if (parts.Length > 0)
                ean13 = parts[0];
        }
        
        if (string.IsNullOrEmpty(ean13))
            throw new Exception("Не удалось определить EAN-13 продукта");
        
        if (quantity <= 0)
            quantity = 100;
        
        if (boxNumber == 0)
            throw new Exception("Не удалось определить номер коробки");
        
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
        
        var createdBox = await _apiService.GetBoxByBarcode(barcode);
        if (createdBox != null)
        {
            await UpdateBoxCache(createdBox);
        }
        
        System.Diagnostics.Debug.WriteLine($"Коробка создана на сервере: {barcode}, номер: {boxNumber}");
    }

    // Обрабатывает операцию отгрузки
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
            result = await _apiService.ShipBox(boxId, "Синхронизация из офлайн-режима");
        }
        else
        {
            result = await _apiService.ConsumeBox(boxId, quantity, "Синхронизация из офлайн-режима");
        }
        
        var resultDict = result as Dictionary<string, object>;
        if (!(resultDict?.TryGetValue("success", out var success) == true && success is bool s && s))
        {
            var errorMsg = resultDict?.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
            throw new Exception(errorMsg);
        }
        
        var updatedBox = await _apiService.GetBoxByBarcode(op.barcode);
        if (updatedBox != null)
        {
            await UpdateBoxCache(updatedBox);
        }
        else
        {
            await _dbHelper.DeleteBoxByBarcode(op.barcode);
        }
        
        System.Diagnostics.Debug.WriteLine($"Отгружена коробка: {op.barcode}, кол-во: {quantity}, полная: {isFullShipment}");
    }

    // Обрабатывает другие типы операций
    private async Task ProcessOtherOperation(OfflineTransaction op, Dictionary<string, object>? payload)
    {
        System.Diagnostics.Debug.WriteLine($"Неизвестный тип операции: {op.operation_type}");
    }

    // Обновляет кэш коробки
    private async Task UpdateBoxCache(Box box)
    {
        if (box.Status == BoxStatus.Active || box.Status == BoxStatus.Empty)
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
            System.Diagnostics.Debug.WriteLine($"Кэш обновлен: #{box.BoxNumber}, статус: {box.Status}");
        }
        else
        {
            await _dbHelper.DeleteBoxByBarcode(box.Barcode);
            System.Diagnostics.Debug.WriteLine($"Коробка удалена из кэша: #{box.BoxNumber}, статус: {box.Status}");
        }
    }

    // Очищает таблицу синхронизации (удаляет все несинхронизированные транзакции)
    public async Task<int> ClearSyncTable()
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            
            // Получаем количество записей для очистки
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
            
            if (count == 0)
            {
                System.Diagnostics.Debug.WriteLine("Нет несинхронизированных транзакций для очистки");
                return 0;
            }
            
            // Удаляем несинхронизированные транзакции
            var deleted = await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE is_synced = 0"
            );
            
            System.Diagnostics.Debug.WriteLine($"✅ Очищено {deleted} несинхронизированных транзакций");
            
            // Обновляем счетчик ожидающих операций
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            return deleted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка очистки таблицы синхронизации: {ex.Message}");
            throw;
        }
    }


    // Обновление статуса после синхронизации
    private async Task UpdateBoxStatusAfterSync(string barcode, BoxStatus newStatus)
    {
        try
        {
            var box = await _dbHelper.GetBoxByBarcode(barcode);
            if (box != null)
            {
                box.status = newStatus;
                box.is_dirty = 0;
                await _dbHelper.SaveBox(box);
                System.Diagnostics.Debug.WriteLine($"✅ Статус коробки {barcode} обновлен на {newStatus} после синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления статуса: {ex.Message}");
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