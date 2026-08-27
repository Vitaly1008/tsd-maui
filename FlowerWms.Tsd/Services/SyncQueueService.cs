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
        
        Logger.Info("SyncQueueService инициализирован");
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
            
            Logger.Info($"Транзакция добавлена в очередь: {transactionId}, тип: {operationType}");
        }
        catch (Exception ex)
        {
            Logger.Info($"Ошибка добавления в очередь: {ex.Message}");
            throw;
        }
    }

    // Возвращает количество ожидающих операций
    public async Task<int> GetPendingCount()
    {
        return await _offlineService.GetPendingCount();
    }

    // ✅ ИСПРАВЛЕНО: строго по алгоритму п.4
    public async Task ProcessQueueAsync()
    {
        if (_isProcessing)
        {
            Logger.Info("Обработка очереди уже выполняется");
            return;
        }

        try
        {
            _isProcessing = true;
            SyncStatusChanged?.Invoke(this, true);
            
            // ✅ 4.1 Получить все несинхронизированные транзакции
            var operations = await _offlineService.GetUnsyncedTransactions();
            
            if (!operations.Any())
            {
                Logger.Info("Нет операций для синхронизации");
                return;
            }
            
            Logger.Info($"Обработка {operations.Count} офлайн-операций");
            
            int successCount = 0;
            int errorCount = 0;
            
            // ✅ 4.2 Разделить: Группа A (Receiving) и Группа B (Shipping)
            var receivingOps = operations
                .Where(o => o.operation_type == "Receiving")
                .OrderBy(o => o.created_at)
                .ToList();
            
            var shippingOps = operations
                .Where(o => o.operation_type == "Shipping")
                .OrderBy(o => o.created_at)
                .ToList();
            
            // ✅ 4.3 ОБРАБОТАТЬ ГРУППУ A (ВСЕ ПРИЕМКИ)
            foreach (var op in receivingOps)
            {
                try
                {
                    await ProcessReceivingOperation(op);
                    await _offlineService.DeleteTransaction(op.transaction_id);
                    successCount++;
                    Logger.Info($"✅ Приемка обработана: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            // ✅ 4.4 ОБРАБОТАТЬ ГРУППУ B (ВСЕ ОТГРУЗКИ)
            foreach (var op in shippingOps)
            {
                try
                {
                    await ProcessShippingOperation(op);
                    await _offlineService.DeleteTransaction(op.transaction_id);
                    successCount++;
                    Logger.Info($"✅ Отгрузка обработана: {op.barcode}");
                }
                catch (Exception ex)
                {
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            
            // ✅ 4.5 ОБНОВИТЬ ЛОКАЛЬНУЮ БД
            await RefreshLocalCacheFromServer();
            
            Logger.Info($"Синхронизировано: {successCount}, ошибок: {errorCount}");
            
            var pending = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pending);
        }
        catch (Exception ex)
        {
            Logger.Info($"Ошибка обработки очереди: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
            SyncStatusChanged?.Invoke(this, false);
        }
    }

    // ✅ 4.3. ОБРАБОТКА ПРИЕМКИ (строго по алгоритму)
    private async Task ProcessReceivingOperation(OfflineTransaction op)
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
        var barcode = op.barcode;
        var locationCode = payload?.GetValueOrDefault("locationCode")?.ToString() ?? "UNKNOWN";
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var grade = payload?.GetValueOrDefault("grade")?.ToString() ?? "Premium";
        var productName = payload?.GetValueOrDefault("productName")?.ToString() ?? "Неизвестный товар";
        var productEan13 = payload?.GetValueOrDefault("ean13")?.ToString() ?? "";
        var boxNumber = payload?.GetValueOrDefault("boxNumber", 0) is int bn ? bn : 0;

        if (boxNumber <= 0)
        {
            var parts = barcode.Split('-');
            if (parts.Length == 4 && int.TryParse(parts[3], out var n))
                boxNumber = n;
        }

        if (boxNumber <= 0)
            throw new Exception("Не удалось определить номер коробки");

        // ✅ 4.3.1-4.3.2. Получить коробку на сервере
        Box? serverBox;
        try
        {
            serverBox = await _apiService.GetBoxByBarcode(barcode);
        }
        catch (Exception ex)
        {
            Logger.Info($"Ошибка запроса коробки: {ex.Message}");
            throw new Exception($"Не удалось проверить коробку на сервере: {ex.Message}");
        }

        // ✅ 4.3.3. Если коробки нет → ПРИНУДИТЕЛЬНАЯ ПРИЕМКА
        if (serverBox == null)
        {
            Logger.Info($"⚠️ Коробка #{boxNumber} не найдена на сервере. Принудительная приемка...");
            
            // ✅ СОЗДАЕМ КОРОБКУ НА СЕРВЕРЕ
            var createResult = await _apiService.ForceCreateBox(
                ean13: productEan13,
                quantity: quantity,
                grade: grade,
                boxNumber: boxNumber,
                locationCode: locationCode,
                comment: $"Коробка принята принудительно: не найдена на сервере"
            );

            if (createResult.TryGetValue("success", out var success) && success is bool s && s)
            {
                Logger.Info($"✅ Принудительно создана коробка #{boxNumber}");
                
                // ✅ Обновляем локальную БД
                var createdBox = await _apiService.GetBoxByBarcode(barcode);
                if (createdBox != null)
                {
                    await _dbHelper.SaveBox(createdBox.ToCache());
                    Logger.Info($"✅ Локальная БД обновлена для #{boxNumber}");
                }
                return; // ✅ Удаляем транзакцию (она успешно обработана)
            }
            else
            {
                var errorMsg = createResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                throw new Exception($"Принудительная приемка не удалась: {errorMsg}");
            }
        }

        Logger.Info($"Коробка найдена на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}");

        // ✅ 4.3.4. Если статус == Draft → Активировать
        if (serverBox.Status == BoxStatus.Draft)
        {
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

            Logger.Info($"✅ Коробка активирована: {barcode}");
        }
        // ✅ 4.3.5. Если статус != Draft → ОШИБКА + PROBLEM BOXES
        else
        {
            var errorMsg = $"Попытка приемки коробки: серверный статус {serverBox.Status}";
            
            // ✅ ДОБАВЛЯЕМ В PROBLEM BOXES
            await _apiService.AddToProblemBoxes(
                barcode: barcode,
                boxId: serverBox.Id,
                errorType: "ReceivingError",
                comment: errorMsg,
                boxNumber: serverBox.BoxNumber,
                productName: serverBox.ProductName
            );
            
            throw new Exception($"Коробка #{serverBox.BoxNumber} имеет статус {serverBox.Status}, приемка невозможна");
        }
    }

    // ✅ 4.4. ОБРАБОТКА ОТГРУЗКИ (строго по алгоритму)
    private async Task ProcessShippingOperation(OfflineTransaction op)
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
        var barcode = op.barcode;
        var boxId = payload?.GetValueOrDefault("boxId")?.ToString();
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var isFullShipment = payload?.GetValueOrDefault("isFullShipment", false) is bool f && f;

        if (string.IsNullOrEmpty(boxId))
        {
            throw new Exception("Не указан boxId для операции отгрузки");
        }

        // ✅ 4.4.2. Запросить коробку на сервере
        Box? serverBox;
        try
        {
            serverBox = await _apiService.GetBoxById(boxId);
            if (serverBox == null)
            {
                serverBox = await _apiService.GetBoxByBarcode(barcode);
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"Ошибка запроса коробки: {ex.Message}");
            throw new Exception($"Не удалось проверить коробку на сервере: {ex.Message}");
        }

        // ✅ 4.4.3. Если коробки нет → ОШИБКА
        if (serverBox == null)
        {
            throw new Exception($"Коробка {barcode} не найдена на сервере");
        }

        Logger.Info($"Коробка на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}, кол-во: {serverBox.CurrentQuantity}");

        // ✅ 4.4.4. Если статус == Draft → ОШИБКА
        if (serverBox.Status == BoxStatus.Draft)
        {
            throw new Exception($"Коробка #{serverBox.BoxNumber} не активирована (Draft)");
        }

        // ✅ 4.4.5. Если статус == Shipped || Empty → ОШИБКА
        if (serverBox.Status == BoxStatus.Shipped)
        {
            throw new Exception($"Коробка #{serverBox.BoxNumber} уже отгружена");
        }
        if (serverBox.Status == BoxStatus.Empty)
        {
            throw new Exception($"Коробка #{serverBox.BoxNumber} пуста");
        }

        // ✅ 4.4.6. Если статус == Active || Reserved
        if (serverBox.Status == BoxStatus.Active || serverBox.Status == BoxStatus.Reserved)
        {
            // ✅ Количество ТОЛЬКО из локальной БД (алгоритм п.3.4)
            var localBox = await _dbHelper.GetBoxByBarcode(barcode);
            if (localBox == null)
            {
                throw new Exception($"Коробка {barcode} не найдена в локальной БД");
            }

            int localQuantity = localBox.current_quantity;
            
            // Проверяем, что количество в локальной БД > 0
            if (localQuantity <= 0)
            {
                throw new Exception($"Коробка #{serverBox.BoxNumber} пуста (остаток: {localQuantity})");
            }

            // ✅ Определяем количество для списания (ТОЛЬКО из локальной БД)
            int quantityToShip;
            bool isFullShipmentFinal;
            
            if (isFullShipment || quantity <= 0 || quantity >= localQuantity)
            {
                quantityToShip = localQuantity; // ✅ ТОЛЬКО из локальной БД
                isFullShipmentFinal = true;
            }
            else
            {
                quantityToShip = quantity; // quantity уже проверено <= localQuantity
                isFullShipmentFinal = false;
            }

            // ✅ 4.4.6.1. Выполняем отгрузку на сервере
            Dictionary<string, object> result;
            if (isFullShipmentFinal)
            {
                // ✅ POST /api/boxes/{boxId}/ship
                result = await _apiService.ShipBox(boxId, $"Полная отгрузка через ТСД");
            }
            else
            {
                // ✅ POST /api/boxes/consume (по ID)
                result = await _apiService.ConsumeBox(boxId, quantityToShip, $"Частичная отгрузка: {quantityToShip} шт.");
            }

            // ✅ 4.4.6.2. Проверяем результат
            if (!(result.TryGetValue("success", out var success) && success is bool s && s))
            {
                var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                
                // ✅ Добавляем в ProblemBoxes
                await _apiService.AddToProblemBoxes(
                    barcode: barcode,
                    boxId: boxId,
                    errorType: "ShippingError",
                    comment: errorMsg,
                    boxNumber: serverBox.BoxNumber,
                    productName: serverBox.ProductName
                );
                
                throw new Exception(errorMsg);
            }

            Logger.Info($"✅ Отгружена коробка: {barcode}, кол-во: {quantityToShip}, полная: {isFullShipmentFinal}");
        }
        // ✅ 4.4.7. Если статус == Discarded → ОШИБКА
        else if (serverBox.Status == BoxStatus.Discarded)
        {
            throw new Exception($"Коробка #{serverBox.BoxNumber} списана");
        }
        else
        {
            throw new Exception($"Неизвестный статус коробки: {serverBox.Status}");
        }
    }

    // Обрабатывает ошибку синхронизации
    private async Task HandleError(OfflineTransaction op, Exception ex)
    {
        op.retry_count++;
        op.error_message = ex.Message;
        
        Logger.Info($"Ошибка синхронизации {op.transaction_id}: {ex.Message}");
        
        await _offlineService.MarkAsError(op.transaction_id, ex.Message);
    }

    // ✅ 4.5. ОБНОВЛЕНИЕ ЛОКАЛЬНОЙ БД (по алгоритму)
    private async Task RefreshLocalCacheFromServer()
    {
        try
        {
            // ✅ 4.5.1. Получить LastChanged с сервера
            var serverLastChanged = await _apiService.GetServerLastChanged();
            
            // ✅ Безопасное преобразование в Unix timestamp
            long serverTimestamp;
            try
            {
                serverTimestamp = new DateTimeOffset(serverLastChanged).ToUnixTimeMilliseconds();
            }
            catch (ArgumentOutOfRangeException)
            {
                // Если дата вне диапазона, используем текущее время
                Logger.Info("⚠️ ServerLastChanged вне допустимого диапазона, используем текущее время");
                serverTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            
            // ✅ 4.5.2. Получить ServerLastChanged из локальной БД
            var localLastChanged = await _dbHelper.GetServerLastChanged();
            
            Logger.Info($"ServerLastChanged: {serverTimestamp}, LocalLastChanged: {localLastChanged}");
            
            // ✅ 4.5.3. Если LastChanged > ServerLastChanged
            if (serverTimestamp > localLastChanged)
            {
                Logger.Info("Обновление локального кэша с сервера...");
                
                // ✅ 4.5.3.1. Загрузить все коробки с сервера (Draft, Active, Reserved)
                var boxes = await _apiService.GetAllBoxesForSync();
                
                if (boxes != null && boxes.Any())
                {
                    // ✅ 4.5.3.2. Очистить локальную boxes_cache
                    var db = await _dbHelper.GetDatabaseAsync();
                    await db.ExecuteAsync("DELETE FROM boxes_cache");
                    
                    // ✅ 4.5.3.3. Записать загруженные коробки
                    var boxCacheList = new List<BoxCache>();
                    foreach (var box in boxes)
                    {
                        // Сохраняем только Draft, Active, Reserved (по алгоритму п.1.4.1)
                        if (box.Status == BoxStatus.Draft || 
                            box.Status == BoxStatus.Active || 
                            box.Status == BoxStatus.Reserved)
                        {
                            boxCacheList.Add(new BoxCache
                            {
                                barcode = box.Barcode,
                                box_id = box.Id,
                                box_number = box.BoxNumber,
                                grade = box.Grade,
                                initial_quantity = box.InitialQuantity > 0 ? box.InitialQuantity : box.CurrentQuantity,
                                current_quantity = box.CurrentQuantity,
                                product_id = box.ProductId,
                                product_name = box.ProductName,
                                product_ean13 = box.ProductEan13,
                                location_code = box.LocationCode ?? "UNKNOWN",
                                status = box.Status,
                                created_at = box.CreatedAt,
                                updated_at = box.UpdatedAt
                            });
                        }
                    }
                    
                    if (boxCacheList.Any())
                    {
                        await _dbHelper.SyncBoxes(boxCacheList);
                        Logger.Info($"✅ Загружено {boxCacheList.Count} коробок с сервера");
                    }
                    
                    // ✅ 4.5.3.4. Обновить ServerLastChanged
                    await _dbHelper.UpdateServerLastChanged(serverTimestamp);
                }
            }
            else
            {
                Logger.Info("Локальный кэш актуален");
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"❌ Ошибка обновления локального кэша: {ex.Message}");
            throw;
        }
    }

    // Очищает таблицу синхронизации
    public async Task<int> ClearSyncTable()
    {
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
            
            if (count == 0)
            {
                Logger.Info("Нет несинхронизированных транзакций для очистки");
                return 0;
            }
            
            var deleted = await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE is_synced = 0"
            );
            
            Logger.Info($"✅ Очищено {deleted} несинхронизированных транзакций");
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            return deleted;
        }
        catch (Exception ex)
        {
            Logger.Info($"❌ Ошибка очистки таблицы синхронизации: {ex.Message}");
            throw;
        }
    }

    // Удаляет транзакцию
    public async Task DeleteTransaction(string transactionId)
    {
        try
        {
            await _offlineService.DeleteTransaction(transactionId);
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            Logger.Info($"✅ Транзакция удалена: {transactionId}");
        }
        catch (Exception ex)
        {
            Logger.Info($"❌ Ошибка удаления транзакции: {ex.Message}");
            throw;
        }
    }

    // Возвращает все несинхронизированные транзакции
    public async Task<List<OfflineTransaction>> GetAllPendingTransactions()
    {
        return await _offlineService.GetAllUnsyncedTransactions();
    }

    // Удаляет конкретную транзакцию
    public async Task<bool> DeletePendingTransaction(string transactionId)
    {
        try
        {
            var transaction = await _offlineService.GetTransactionById(transactionId);
            if (transaction == null)
            {
                Logger.Info($"Транзакция {transactionId} не найдена");
                return false;
            }

            if (transaction.is_synced == 1)
            {
                Logger.Info($"Транзакция {transactionId} уже синхронизирована");
                return false;
            }

            await _offlineService.DeleteTransaction(transactionId);
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            Logger.Info($"✅ Транзакция удалена: {transactionId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Info($"❌ Ошибка удаления транзакции: {ex.Message}");
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

            if (transaction.operation_type == "Receiving")
            {
                await ProcessReceivingOperation(transaction);
            }
            else if (transaction.operation_type == "Shipping")
            {
                await ProcessShippingOperation(transaction);
            }
            else
            {
                Logger.Info($"Неизвестный тип операции: {transaction.operation_type}");
                return false;
            }

            await _offlineService.DeleteTransaction(transactionId);
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Info($"❌ Ошибка синхронизации транзакции {transactionId}: {ex.Message}");
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