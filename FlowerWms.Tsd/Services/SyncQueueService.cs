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

    // ✅ ИСПРАВЛЕНО: удаляем транзакции после успешной синхронизации
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
            
            // ✅ 1. СНАЧАЛА ВСЕ ПРИЕМКИ (сортировка по времени)
            var receivingOps = operations
                .Where(o => o.operation_type == "Receiving")
                .OrderBy(o => o.created_at)
                .ToList();
            
            // ✅ 2. ПОТОМ ВСЕ ОТГРУЗКИ (сортировка по времени)
            var shippingOps = operations
                .Where(o => o.operation_type == "Shipping")
                .OrderBy(o => o.created_at)
                .ToList();
            
            // ✅ 3. ОСТАЛЬНЫЕ ОПЕРАЦИИ
            var otherOps = operations
                .Where(o => o.operation_type != "Receiving" && o.operation_type != "Shipping")
                .OrderBy(o => o.created_at)
                .ToList();
            
            // ✅ ОБРАБОТКА ПРИЕМОК
            foreach (var op in receivingOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessReceivingOperation(op, payload);
                    
                    // ✅ ИСПРАВЛЕНО: УДАЛЯЕМ, а не помечаем как синхронизированную
                    await _offlineService.DeleteTransaction(op.transaction_id);
                    successCount++;
                    System.Diagnostics.Debug.WriteLine($"✅ Приемка синхронизирована и удалена: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            // ✅ ОБРАБОТКА ОТГРУЗОК
            foreach (var op in shippingOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessShippingOperation(op, payload);
                    
                    // ✅ ИСПРАВЛЕНО: УДАЛЯЕМ, а не помечаем как синхронизированную
                    await _offlineService.DeleteTransaction(op.transaction_id);
                    successCount++;
                    System.Diagnostics.Debug.WriteLine($"✅ Отгрузка синхронизирована и удалена: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            // ✅ ОБРАБОТКА ОСТАЛЬНЫХ ОПЕРАЦИЙ
            foreach (var op in otherOps)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
                    await ProcessOtherOperation(op, payload);
                    
                    // ✅ ИСПРАВЛЕНО: УДАЛЯЕМ, а не помечаем как синхронизированную
                    await _offlineService.DeleteTransaction(op.transaction_id);
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
            // ✅ ИСПРАВЛЕНО: при превышении попыток УДАЛЯЕМ транзакцию
            System.Diagnostics.Debug.WriteLine($"⚠️ Превышено число попыток для {op.transaction_id}. Транзакция удалена.");
            await _offlineService.DeleteTransaction(op.transaction_id);
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
        
        if (boxNumber == 0 && !string.IsNullOrEmpty(barcode))
        {
            var parts = barcode.Split('-');
            if (parts.Length == 4 && int.TryParse(parts[3], out var n))
                boxNumber = n;
        }

        if (boxNumber <= 0)
            throw new Exception("Не удалось определить номер коробки");

        // ✅ 1. ПРОВЕРКА: коробка есть на сервере со статусом Draft?
        Box? serverBox = null;
        try
        {
            serverBox = await _apiService.GetBoxByBarcode(barcode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка проверки коробки: {ex.Message}");
        }

        if (serverBox == null)
        {
            throw new Exception($"Коробка #{boxNumber} не найдена на сервере! Сначала напечатайте штрихкод.");
        }

        System.Diagnostics.Debug.WriteLine($"Коробка найдена на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}");

        // ✅ 2. ПРОВЕРКА СТАТУСА: должен быть Draft
        if (serverBox.Status != BoxStatus.Draft)
        {
            throw new Exception($"Коробка #{boxNumber} имеет статус {serverBox.Status}, приемка невозможна");
        }

        // ✅ 3. АКТИВАЦИЯ на сервере
        var result = await _apiService.ActivateBox(
            boxId: serverBox.Id,
            locationCode: locationCode,
            comment: $"Приемка через ТСД, локация: {locationCode}"
        );

        if (!(result.TryGetValue("success", out var success) && success is bool s && s))
        {
            var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
            throw new Exception($"Ошибка активации: {errorMsg}");
        }

        // ✅ 4. Получаем обновленную коробку с сервера
        var updatedBox = await _apiService.GetBoxByBarcode(barcode);
        if (updatedBox != null)
        {
            // ✅ 5. Обновляем локальную БД ТОЛЬКО с сервера
            await UpdateBoxCache(updatedBox);
            System.Diagnostics.Debug.WriteLine($"✅ Коробка активирована и обновлена в кэше: {barcode}");
        }
        else
        {
            throw new Exception($"Не удалось получить обновленную коробку {barcode} с сервера");
        }

        System.Diagnostics.Debug.WriteLine($"✅ Коробка активирована: {barcode}");
    }

    // ✅ ИСПРАВЛЕНО: ProcessShippingOperation с правильным обновлением isPartial
    private async Task ProcessShippingOperation(OfflineTransaction op, Dictionary<string, object>? payload)
    {
        var barcode = op.barcode;
        var boxId = payload?.GetValueOrDefault("boxId")?.ToString();
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var isFullShipment = payload?.GetValueOrDefault("isFullShipment", false) is bool f && f;
        var currentQuantity = payload?.GetValueOrDefault("currentQuantity", 0) is int cq ? cq : 0;

        if (string.IsNullOrEmpty(boxId))
        {
            throw new Exception("Не указан boxId для операции отгрузки");
        }

        // ✅ 1. ПОЛУЧАЕМ АКТУАЛЬНОЕ СОСТОЯНИЕ КОРОБКИ С СЕРВЕРА (все проверки здесь!)
        var serverBox = await _apiService.GetBoxByBarcode(barcode);
        if (serverBox == null)
        {
            throw new Exception($"Коробка {barcode} не найдена на сервере");
        }

        // ✅ 2. ПРОВЕРЯЕМ СТАТУС
        if (serverBox.Status != BoxStatus.Active)
        {
            throw new Exception($"Коробка {barcode} имеет статус {serverBox.Status}, отгрузка невозможна");
        }

        // ✅ 3. ОПРЕДЕЛЯЕМ ДОСТУПНОЕ КОЛИЧЕСТВО
        int availableQuantity;
        bool isPartial = false;
        
        // Если коробка isPartial на сервере — количество берем из локальной БД
        if (serverBox.IsPartial)
        {
            isPartial = true;
            var localBox = await _dbHelper.GetBoxByBarcode(barcode);
            if (localBox != null)
            {
                availableQuantity = localBox.current_quantity;
                System.Diagnostics.Debug.WriteLine($"📦 Частичная коробка: количество из БД = {availableQuantity}");
            }
            else
            {
                availableQuantity = serverBox.CurrentQuantity;
                System.Diagnostics.Debug.WriteLine($"⚠️ Частичная коробка не найдена в БД, используем серверное значение: {availableQuantity}");
            }
        }
        else
        {
            // Целая коробка — количество из ШК (серверное значение)
            availableQuantity = serverBox.CurrentQuantity;
            System.Diagnostics.Debug.WriteLine($"📦 Целая коробка: количество из сервера = {availableQuantity}");
        }

        // ✅ 4. ПРОВЕРЯЕМ КОЛИЧЕСТВО (все проверки здесь!)
        if (!isFullShipment && quantity > 0 && quantity > availableQuantity)
        {
            throw new Exception($"Недостаточно товара. Доступно: {availableQuantity}, запрошено: {quantity}");
        }

        // Определяем количество для списания
        int quantityToShip;
        bool isFullShipmentFinal;
        
        if (isFullShipment || quantity <= 0 || quantity >= availableQuantity)
        {
            quantityToShip = availableQuantity;
            isFullShipmentFinal = true;
        }
        else
        {
            quantityToShip = quantity;
            isFullShipmentFinal = false;
        }

        // ✅ 5. ВЫПОЛНЯЕМ ОТГРУЗКУ НА СЕРВЕРЕ
        Dictionary<string, object> result;
        
        if (isFullShipmentFinal)
        {
            result = await _apiService.ShipBox(boxId, "Синхронизация из офлайн-режима");
        }
        else
        {
            result = await _apiService.ConsumeBox(boxId, quantityToShip, $"Частичная отгрузка, остаток: {availableQuantity - quantityToShip} шт.");
        }

        // ✅ 6. ПРОВЕРЯЕМ РЕЗУЛЬТАТ
        if (!(result.TryGetValue("success", out var success) && success is bool s && s))
        {
            var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
            throw new Exception(errorMsg);
        }

        // ✅ 7. ПОСЛЕ УСПЕШНОЙ ОТГРУЗКИ — ОБНОВЛЯЕМ ТОЛЬКО isPartial С СЕРВЕРА
        // ✅ ИСПРАВЛЕНО: используем UpdateBoxesPartialOnly, который НЕ трогает статус
        await RefreshLocalBoxesPartialFromServer();

        System.Diagnostics.Debug.WriteLine($"✅ Отгружена коробка: {barcode}, кол-во: {quantityToShip}, полная: {isFullShipmentFinal}");
    }

    /// <summary>
    /// ✅ НОВЫЙ МЕТОД: обновляет ТОЛЬКО isPartial и количество с сервера (НЕ трогает статус!)
    /// </summary>
    private async Task RefreshLocalBoxesPartialFromServer()
    {
        try
        {
            // ✅ Получаем ВСЕ частичные коробки с сервера
            var partialBoxes = await _apiService.GetPartialBoxes();
            if (partialBoxes != null && partialBoxes.Any())
            {
                // ✅ Обновляем ТОЛЬКО isPartial и количество (НЕ статус!)
                var updateList = partialBoxes.Select(box => (
                    barcode: box.Barcode,
                    isPartial: true,
                    currentQuantity: box.CurrentQuantity
                )).ToList();

                // ✅ Используем новый метод, который НЕ трогает статус
                await _dbHelper.UpdateBoxesPartialOnly(updateList);
                
                System.Diagnostics.Debug.WriteLine($"✅ Обновлено {partialBoxes.Count} коробок с сервера (только isPartial и количество)");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления коробок с сервера: {ex.Message}");
        }
    }
    
    // Обрабатывает другие типы операций
    private async Task ProcessOtherOperation(OfflineTransaction op, Dictionary<string, object>? payload)
    {
        System.Diagnostics.Debug.WriteLine($"Неизвестный тип операции: {op.operation_type}");
    }

    // Обновляет кэш коробки
    private async Task UpdateBoxCache(Box box)
    {
        // ✅ Сохраняем в локальный кэш ТОЛЬКО то, что пришло с сервера
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
            isPartial = box.IsPartial ? 1 : 0
        });
        
        System.Diagnostics.Debug.WriteLine($"Кэш обновлен с сервера: #{box.BoxNumber}, статус: {box.Status}, isPartial: {box.IsPartial}");
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

    // Удаляет транзакцию из очереди (после успешной синхронизации)
    public async Task DeleteTransaction(string transactionId)
    {
        try
        {
            await _offlineService.DeleteTransaction(transactionId);
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            System.Diagnostics.Debug.WriteLine($"✅ Транзакция удалена из очереди: {transactionId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка удаления транзакции: {ex.Message}");
            throw;
        }
    }

    // Возвращает все несинхронизированные транзакции для отображения
    public async Task<List<OfflineTransaction>> GetAllPendingTransactions()
    {
        return await _offlineService.GetAllUnsyncedTransactions();
    }

    // Удаляет конкретную транзакцию из очереди
    public async Task<bool> DeletePendingTransaction(string transactionId)
    {
        try
        {
            var transaction = await _offlineService.GetTransactionById(transactionId);
            if (transaction == null)
            {
                System.Diagnostics.Debug.WriteLine($"Транзакция {transactionId} не найдена");
                return false;
            }

            if (transaction.is_synced == 1)
            {
                System.Diagnostics.Debug.WriteLine($"Транзакция {transactionId} уже синхронизирована");
                return false;
            }

            await _offlineService.DeleteTransaction(transactionId);
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            System.Diagnostics.Debug.WriteLine($"✅ Транзакция удалена: {transactionId}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка удаления транзакции: {ex.Message}");
            return false;
        }
    }

    // Синхронизирует конкретную транзакцию
    public async Task<bool> SyncSingleTransaction(string transactionId)
    {
        try
        {
            var transaction = await _offlineService.GetTransactionById(transactionId);
            if (transaction == null || transaction.is_synced == 1)
            {
                return false;
            }

            var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(transaction.payload);
            
            if (transaction.operation_type == "Receiving")
            {
                await ProcessReceivingOperation(transaction, payload);
            }
            else if (transaction.operation_type == "Shipping")
            {
                await ProcessShippingOperation(transaction, payload);
            }
            else
            {
                await ProcessOtherOperation(transaction, payload);
            }

            // ✅ ИСПРАВЛЕНО: удаляем после успешной синхронизации
            await _offlineService.DeleteTransaction(transactionId);
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации транзакции {transactionId}: {ex.Message}");
            await _offlineService.MarkAsError(transactionId, ex.Message);
            return false;
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