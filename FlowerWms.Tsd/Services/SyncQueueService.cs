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
        Logger.Info($"📥 EnqueueAsync: operationType={operationType}, barcode={barcode}, deviceId={deviceId}");
        Logger.Info($"📦 Payload: {JsonSerializer.Serialize(payload)}");
        
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
            
            Logger.Info($"✅ Транзакция добавлена в очередь: {transactionId}, тип: {operationType}, pending={pendingCount}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка добавления в очередь: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    // Возвращает количество ожидающих операций
    public async Task<int> GetPendingCount()
    {
        Logger.Info($"🔍 GetPendingCount: вызов");
        var count = await _offlineService.GetPendingCount();
        Logger.Info($"📊 GetPendingCount: {count}");
        return count;
    }

    // ✅ ИСПРАВЛЕНО: строго по алгоритму п.4
    public async Task ProcessQueueAsync()
    {
        Logger.Info($"🔄 НАЧАЛО ProcessQueueAsync");
        
        if (_isProcessing)
        {
            Logger.Info("⚠️ Обработка очереди уже выполняется");
            return;
        }

        try
        {
            _isProcessing = true;
            SyncStatusChanged?.Invoke(this, true);
            
            Logger.Info("🔍 Получение несинхронизированных транзакций...");
            var operations = await _offlineService.GetUnsyncedTransactions();
            Logger.Info($"📋 Получено {operations.Count} транзакций");
            
            if (!operations.Any())
            {
                Logger.Info("✅ Нет операций для синхронизации");
                return;
            }
            
            Logger.Info($"📊 Обработка {operations.Count} офлайн-операций");
            
            int successCount = 0;
            int errorCount = 0;
            
            // 4.2 Разделить на группы
            var receivingOps = operations
                .Where(o => o.operation_type == "Receiving")
                .OrderBy(o => o.created_at)
                .ToList();
            
            var shippingOps = operations
                .Where(o => o.operation_type == "Shipping")
                .OrderBy(o => o.created_at)
                .ToList();
            
            Logger.Info($"📦 Разделение: Receiving={receivingOps.Count}, Shipping={shippingOps.Count}");
            
            // 4.3 Обработка приемок
            Logger.Info($"🔄 НАЧАЛО обработки приемок ({receivingOps.Count})");
            foreach (var op in receivingOps)
            {
                try
                {
                    Logger.Info($"📥 Обработка приемки: ID={op.transaction_id}, barcode={op.barcode}");
                    await ProcessReceivingOperation(op);
                    Logger.Info($"🗑️ Удаление транзакции приемки: {op.transaction_id}");
                    await _offlineService.DeleteTransaction(op.transaction_id);
                    successCount++;
                    Logger.Info($"✅ Приемка обработана: {op.barcode}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"❌ Ошибка приемки {op.transaction_id}: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            Logger.Info($"🏁 КОНЕЦ обработки приемок: success={successCount}, errors={errorCount}");
            
            // 4.4 Обработка отгрузок
            Logger.Info($"🔄 НАЧАЛО обработки отгрузок ({shippingOps.Count})");
            foreach (var op in shippingOps)
            {
                try
                {
                    Logger.Info($"📤 Обработка отгрузки: ID={op.transaction_id}, barcode={op.barcode}");
                    await ProcessShippingOperation(op);
                    Logger.Info($"🗑️ Удаление транзакции отгрузки: {op.transaction_id}");
                    await _offlineService.DeleteTransaction(op.transaction_id);
                    successCount++;
                    Logger.Info($"✅ Отгрузка обработана: {op.barcode}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"❌ Ошибка отгрузки {op.transaction_id}: {ex.Message}");
                    Logger.Error($"StackTrace: {ex.StackTrace}");
                    await HandleError(op, ex);
                    errorCount++;
                }
            }
            Logger.Info($"🏁 КОНЕЦ обработки отгрузок: success={successCount}, errors={errorCount}");
            
            // ✅ 4.5 Обновление БД — УДАЛЕНО, теперь вызывается из SyncService!
            // Обновление БД выполняется в SyncService после вызова ProcessQueueAsync()
            
            Logger.Info($"📊 ИТОГО: синхронизировано: {successCount}, ошибок: {errorCount}");
            
            var pending = await _offlineService.GetPendingCount();
            Logger.Info($"📊 Осталось в очереди: {pending}");
            PendingCountChanged?.Invoke(this, pending);
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ КРИТИЧЕСКАЯ ОШИБКА ProcessQueueAsync: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
        }
        finally
        {
            _isProcessing = false;
            SyncStatusChanged?.Invoke(this, false);
            Logger.Info($"🏁 КОНЕЦ ProcessQueueAsync");
        }
    }

    // ✅ 4.3. ОБРАБОТКА ПРИЕМКИ (строго по алгоритму)
    private async Task ProcessReceivingOperation(OfflineTransaction op)
    {
        Logger.Info($"🚚 НАЧАЛО ProcessReceivingOperation: ID={op.transaction_id}, barcode={op.barcode}");
        
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
        Logger.Info($"📦 Payload: {JsonSerializer.Serialize(payload)}");
        
        var barcode = op.barcode;
        var locationCode = payload?.GetValueOrDefault("locationCode")?.ToString() ?? "UNKNOWN";
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var grade = payload?.GetValueOrDefault("grade")?.ToString() ?? "Premium";
        var productName = payload?.GetValueOrDefault("productName")?.ToString() ?? "Неизвестный товар";
        var productEan13 = payload?.GetValueOrDefault("ean13")?.ToString() ?? "";
        var boxNumber = payload?.GetValueOrDefault("boxNumber", 0) is int bn ? bn : 0;

        Logger.Info($"📊 Параметры: boxNumber={boxNumber}, quantity={quantity}, grade={grade}, location={locationCode}");

        if (boxNumber <= 0)
        {
            var parts = barcode.Split('-');
            if (parts.Length == 4 && int.TryParse(parts[3], out var n))
                boxNumber = n;
            Logger.Info($"🔍 Извлечен boxNumber из barcode: {boxNumber}");
        }

        if (boxNumber <= 0)
        {
            Logger.Error($"❌ Не удалось определить номер коробки для barcode={barcode}");
            throw new Exception("Не удалось определить номер коробки");
        }

        // ✅ 4.3.1-4.3.2. Получить коробку на сервере
        Logger.Info($"🔍 Запрос коробки на сервере: barcode={barcode}");
        Box? serverBox;
        try
        {
            serverBox = await _apiService.GetBoxByBarcode(barcode);
            Logger.Info($"📡 Результат GetBoxByBarcode: {(serverBox != null ? $"найдена #{serverBox.BoxNumber}" : "NULL")}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка запроса коробки: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw new Exception($"Не удалось проверить коробку на сервере: {ex.Message}");
        }

        // ✅ 4.3.3. Если коробки нет → ПРИНУДИТЕЛЬНАЯ ПРИЕМКА
        if (serverBox == null)
        {
            Logger.Info($"⚠️ Коробка #{boxNumber} не найдена на сервере. Принудительная приемка...");
            
            // ✅ СОЗДАЕМ КОРОБКУ НА СЕРВЕРЕ
            Logger.Info($"📤 Вызов ForceCreateBox: ean13={productEan13}, quantity={quantity}, boxNumber={boxNumber}");
            var createResult = await _apiService.ForceCreateBox(
                ean13: productEan13,
                quantity: quantity,
                grade: grade,
                boxNumber: boxNumber,
                locationCode: locationCode,
                comment: $"Коробка принята принудительно: не найдена на сервере"
            );
            Logger.Info($"📡 Результат ForceCreateBox: {JsonSerializer.Serialize(createResult)}");

            if (createResult.TryGetValue("success", out var success) && success is bool s && s)
            {
                Logger.Info($"✅ Принудительно создана коробка #{boxNumber}");
                
                // ✅ Обновляем локальную БД
                Logger.Info($"🔍 Получение созданной коробки: barcode={barcode}");
                var createdBox = await _apiService.GetBoxByBarcode(barcode);
                if (createdBox != null)
                {
                    Logger.Info($"💾 Сохранение коробки в локальную БД: {barcode}");
                    await _dbHelper.SaveBox(createdBox.ToCache());
                    Logger.Info($"🗑️ Удаление транзакции приемки: {op.transaction_id}");
                    await _offlineService.DeleteTransaction(op.transaction_id);
                    Logger.Info($"✅ Принудительная приемка завершена");
                    return;
                }
                else
                {
                    Logger.Error($"❌ Не удалось получить созданную коробку: {barcode}");
                }
            }
            else
            {
                var errorMsg = createResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                Logger.Error($"❌ Принудительная приемка не удалась: {errorMsg}");
                throw new Exception($"Принудительная приемка не удалась: {errorMsg}");
            }
        }

        Logger.Info($"✅ Коробка найдена на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}");

        // ✅ 4.3.4. Если статус == Draft → Активировать
        if (serverBox.Status == BoxStatus.Draft)
        {
            Logger.Info($"📤 Активация коробки: boxId={serverBox.Id}, location={locationCode}");
            var result = await _apiService.ActivateBox(
                boxId: serverBox.Id,
                locationCode: locationCode,
                comment: $"Приемка через ТСД, локация: {locationCode}"
            );
            Logger.Info($"📡 Результат активации: {JsonSerializer.Serialize(result)}");

            if (!(result.TryGetValue("success", out var success) && success is bool s && s))
            {
                var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                Logger.Error($"❌ Ошибка активации: {errorMsg}");
                throw new Exception($"Ошибка активации: {errorMsg}");
            }

            Logger.Info($"✅ Коробка активирована: {barcode}");
        }
        // ✅ 4.3.5. Если статус != Draft → ОШИБКА + PROBLEM BOXES
        else
        {
            var errorMsg = $"Попытка приемки коробки: серверный статус {serverBox.Status}";
            Logger.Warning($"⚠️ {errorMsg}");
            
            // ✅ ДОБАВЛЯЕМ В PROBLEM BOXES
            Logger.Info($"📤 Добавление в ProblemBoxes: barcode={barcode}, boxId={serverBox.Id}");
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
        
        Logger.Info($"🏁 КОНЕЦ ProcessReceivingOperation: {op.transaction_id}");
    }

    // ✅ 4.4. ОБРАБОТКА ОТГРУЗКИ (строго по алгоритму)
    private async Task ProcessShippingOperation(OfflineTransaction op)
    {
        Logger.Info($"🚚 НАЧАЛО обработки отгрузки: ID={op.transaction_id}, Barcode={op.barcode}");
        
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
        Logger.Info($"📦 Payload: {JsonSerializer.Serialize(payload)}");

        var barcode = op.barcode;
        var boxId = payload?.GetValueOrDefault("boxId")?.ToString();
       // ✅ Берем quantity из payload, но если его нет или он 0 — используем currentQuantity
        var quantity = payload?.GetValueOrDefault("quantity", 0) is int q ? q : 0;
        var currentQuantity = payload?.GetValueOrDefault("currentQuantity", 0) is int cq ? cq : 0;
        var isFullShipment = payload?.GetValueOrDefault("isFullShipment", false) is bool f && f;

        // ✅ Если quantity = 0, но currentQuantity > 0 — это полная отгрузка
        if (quantity <= 0 && currentQuantity > 0)
        {
            quantity = currentQuantity;
            isFullShipment = true;
            Logger.Info($"📦 Коррекция: quantity=0 -> {quantity}, isFullShipment=true");
        }

        Logger.Info($"📊 Параметры: boxId={boxId}, quantity={quantity}, isFullShipment={isFullShipment}");

        if (string.IsNullOrEmpty(boxId))
        {
            Logger.Error($"❌ Не указан boxId для операции отгрузки");
            throw new Exception("Не указан boxId для операции отгрузки");
        }

        // ✅ 4.4.2. Запросить коробку на сервере
        Logger.Info($"🔍 Запрос коробки на сервере: boxId={boxId}, barcode={barcode}");

        Box? serverBox;
        try
        {
            Logger.Info($"📡 Вызов GetBoxById: {boxId}");
            serverBox = await _apiService.GetBoxById(boxId);
            Logger.Info($"📡 Результат GetBoxById: {(serverBox != null ? $"найдена #{serverBox.BoxNumber}" : "NULL")}");

            if (serverBox == null)
            {
                Logger.Info($"📡 Вызов GetBoxByBarcode: {barcode}");
                serverBox = await _apiService.GetBoxByBarcode(barcode);
                Logger.Info($"📡 Результат GetBoxByBarcode: {(serverBox != null ? $"найдена #{serverBox.BoxNumber}" : "NULL")}");
            }

            if (serverBox == null)
            {
                Logger.Error($"❌ Коробка {barcode} не найдена на сервере");
                throw new Exception($"Коробка {barcode} не найдена на сервере");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка запроса коробки: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw new Exception($"Не удалось проверить коробку на сервере: {ex.Message}");
        }

        // ✅ 4.4.3. Если коробки нет → ОШИБКА
        if (serverBox == null)
        {
            Logger.Error($"❌ Коробка {barcode} не найдена на сервере");
            throw new Exception($"Коробка {barcode} не найдена на сервере");
        }

        Logger.Info($"✅ Коробка на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}, кол-во: {serverBox.CurrentQuantity}");

        // ✅ 4.4.4. Если статус == Draft → ОШИБКА
        if (serverBox.Status == BoxStatus.Draft)
        {
            Logger.Error($"❌ Коробка #{serverBox.BoxNumber} не активирована (Draft)");
            throw new Exception($"Коробка #{serverBox.BoxNumber} не активирована (Draft)");
        }

        // ✅ 4.4.5. Если статус == Shipped || Empty → ОШИБКА
        if (serverBox.Status == BoxStatus.Shipped)
        {
            Logger.Error($"❌ Коробка #{serverBox.BoxNumber} уже отгружена");
            throw new Exception($"Коробка #{serverBox.BoxNumber} уже отгружена");
        }
        if (serverBox.Status == BoxStatus.Empty)
        {
            Logger.Error($"❌ Коробка #{serverBox.BoxNumber} пуста");
            throw new Exception($"Коробка #{serverBox.BoxNumber} пуста");
        }

        // ✅ 4.4.6. Если статус == Active || Reserved
        if (serverBox.Status == BoxStatus.Active || serverBox.Status == BoxStatus.Reserved)
        {
            Logger.Info($"✅ Статус коробки допустим для отгрузки: {serverBox.Status}");
            Logger.Info($"🔍 Проверка локальной БД для barcode={barcode}");
            
            // ✅ Количество ТОЛЬКО из локальной БД
            var localBox = await _dbHelper.GetBoxByBarcode(barcode);
            if (localBox == null)
            {
                Logger.Error($"❌ Коробка {barcode} не найдена в локальной БД");
                throw new Exception($"Коробка {barcode} не найдена в локальной БД");
            }

            int localQuantity = localBox.current_quantity;
            int serverQuantity = serverBox.CurrentQuantity;

            Logger.Info($"📊 Локальное количество: {localQuantity}, серверное: {serverBox.CurrentQuantity}");
            
            // ✅ Если локальное количество расходится с серверным — обновляем локальную БД
            if (localQuantity != serverQuantity)
            {
                Logger.Warning($"⚠️ Расхождение: локальное={localQuantity}, серверное={serverQuantity}. Обновляем локальную БД...");
                
                // Обновляем локальную БД
                await _dbHelper.ForceUpdateBoxStatus(
                    barcode: barcode,
                    newStatus: serverBox.Status,
                    newQuantity: serverQuantity
                );
                
                localQuantity = serverQuantity;
            }

            if (localQuantity <= 0)
            {
                Logger.Error($"❌ Коробка #{serverBox.BoxNumber} пуста (остаток: {localQuantity})");
                throw new Exception($"Коробка #{serverBox.BoxNumber} пуста (остаток: {localQuantity})");
            }

            int quantityToShip;
            bool isFullShipmentFinal;
            
            if (isFullShipment || quantity <= 0 || quantity >= localQuantity)
            {
                quantityToShip = localQuantity;
                isFullShipmentFinal = true;
                Logger.Info($"📦 ПОЛНАЯ отгрузка: quantityToShip={quantityToShip}");
            }
            else
            {
                quantityToShip = quantity;
                isFullShipmentFinal = false;
                Logger.Info($"📦 ЧАСТИЧНАЯ отгрузка: quantityToShip={quantityToShip}");
            }

            // ✅ 4.4.6.1. Выполняем отгрузку на сервере
            // ✅ ИСПОЛЬЗУЕМ Internal-методы (БЕЗ создания транзакций!)
            Logger.Info($"🚀 Вызов API отгрузки: boxId={boxId}, quantity={quantityToShip}, full={isFullShipmentFinal}");
            
            Dictionary<string, object> result;
            try
            {
                if (isFullShipmentFinal)
                {
                    Logger.Info($"📡 Вызов ShipBoxInternal для boxId={boxId}");
                    result = await _apiService.ShipBoxInternal(
                        boxId, 
                        $"Полная отгрузка через ТСД"
                    );
                    Logger.Info($"📡 Результат ShipBoxInternal: {JsonSerializer.Serialize(result)}");
                }
                else
                {
                    Logger.Info($"📡 Вызов ConsumeBoxInternal для boxId={boxId}, quantity={quantityToShip}");
                    result = await _apiService.ConsumeBoxInternal(
                        boxId, 
                        quantityToShip, 
                        $"Частичная отгрузка: {quantityToShip} шт."
                    );
                    Logger.Info($"📡 Результат ConsumeBoxInternal: {JsonSerializer.Serialize(result)}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"❌ Исключение при вызове API: {ex.Message}");
                Logger.Error($"StackTrace: {ex.StackTrace}");
                
                // Добавляем в ProblemBoxes
                await _apiService.AddToProblemBoxes(
                    barcode: barcode,
                    boxId: boxId,
                    errorType: "ShippingError",
                    comment: $"API Error: {ex.Message}",
                    boxNumber: serverBox.BoxNumber,
                    productName: serverBox.ProductName
                );
                throw;
            }

            // ✅ 4.4.6.2. Проверяем результат
            if (!(result.TryGetValue("success", out var success) && success is bool s && s))
            {
                var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                Logger.Error($"❌ API вернул ошибку: {errorMsg}");
                
                Logger.Info($"📤 Добавление в ProblemBoxes: barcode={barcode}, boxId={boxId}");
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
            Logger.Error($"❌ Коробка #{serverBox.BoxNumber} списана (Discarded)");
            throw new Exception($"Коробка #{serverBox.BoxNumber} списана");
        }
        else
        {
            Logger.Error($"❌ Неизвестный статус коробки: {serverBox.Status}");
            throw new Exception($"Неизвестный статус коробки: {serverBox.Status}");
        }
        
        Logger.Info($"🏁 КОНЕЦ обработки отгрузки: {op.transaction_id}");
    }

    // Обрабатывает ошибку синхронизации
    private async Task HandleError(OfflineTransaction op, Exception ex)
    {
        Logger.Warning($"⚠️ HandleError: transaction={op.transaction_id}, error={ex.Message}");
        
        op.retry_count++;
        op.error_message = ex.Message;
        Logger.Info($"📝 Обновление транзакции: retry_count={op.retry_count}");
        await _offlineService.MarkAsError(op.transaction_id, ex.Message);
        
        // ✅ ДОБАВЛЯЕМ В PROBLEM BOXES
        try
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(op.payload);
            var boxId = payload?.GetValueOrDefault("boxId")?.ToString();
            
            Logger.Info($"📤 Добавление в ProblemBoxes: barcode={op.barcode}, boxId={boxId}");
            await _apiService.AddToProblemBoxes(
                barcode: op.barcode,
                boxId: boxId ?? string.Empty,
                errorType: op.operation_type == "Receiving" ? "ReceivingError" : "ShippingError",
                comment: ex.Message,
                boxNumber: 0,
                productName: "Неизвестный продукт"
            );
            Logger.Info($"✅ ProblemBox создан");
        }
        catch (Exception innerEx)
        {
            Logger.Error($"❌ Ошибка создания ProblemBox: {innerEx.Message}");
            Logger.Error($"StackTrace: {innerEx.StackTrace}");
        }
    }


    // Очищает таблицу синхронизации
    public async Task<int> ClearSyncTable()
    {
        Logger.Info($"🗑️ ClearSyncTable: вызов");
        try
        {
            var db = await _dbHelper.GetDatabaseAsync();
            
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
            Logger.Info($"📊 Найдено несинхронизированных транзакций: {count}");
            
            if (count == 0)
            {
                Logger.Info("✅ Нет несинхронизированных транзакций для очистки");
                return 0;
            }
            
            var deleted = await db.ExecuteAsync(
                "DELETE FROM offline_transactions WHERE is_synced = 0"
            );
            
            Logger.Info($"✅ Очищено {deleted} несинхронизированных транзакций");
            
            var pendingCount = await _offlineService.GetPendingCount();
            Logger.Info($"📊 Осталось в очереди: {pendingCount}");
            PendingCountChanged?.Invoke(this, pendingCount);
            
            return deleted;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка очистки таблицы синхронизации: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    // Удаляет транзакцию
    public async Task DeleteTransaction(string transactionId)
    {
        Logger.Info($"🗑️ DeleteTransaction: {transactionId}");
        try
        {
            await _offlineService.DeleteTransaction(transactionId);
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            Logger.Info($"✅ Транзакция удалена: {transactionId}, осталось: {pendingCount}");
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка удаления транзакции: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    // Возвращает все несинхронизированные транзакции
    public async Task<List<OfflineTransaction>> GetAllPendingTransactions()
    {
        Logger.Info($"🔍 GetAllPendingTransactions: вызов");
        var result = await _offlineService.GetAllUnsyncedTransactions();
        Logger.Info($"📋 Получено {result.Count} транзакций");
        foreach (var tx in result)
        {
            Logger.Info($"   - {tx.transaction_id}: {tx.operation_type}, {tx.barcode}, retry={tx.retry_count}");
        }
        return result;
    }

    // Удаляет конкретную транзакцию
    public async Task<bool> DeletePendingTransaction(string transactionId)
    {
        Logger.Info($"🗑️ DeletePendingTransaction: {transactionId}");
        try
        {
            var transaction = await _offlineService.GetTransactionById(transactionId);
            if (transaction == null)
            {
                Logger.Warning($"⚠️ Транзакция {transactionId} не найдена");
                return false;
            }

            if (transaction.is_synced == 1)
            {
                Logger.Warning($"⚠️ Транзакция {transactionId} уже синхронизирована");
                return false;
            }

            Logger.Info($"🔄 Откат транзакции: {transactionId}");
            // ✅ ОТКАТЫВАЕМ ИЗМЕНЕНИЯ
            await _offlineService.RevertTransaction(transactionId);

            // ✅ УДАЛЯЕМ ТРАНЗАКЦИЮ
            Logger.Info($"🗑️ Удаление транзакции: {transactionId}");
            await _offlineService.DeleteTransaction(transactionId);
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            Logger.Info($"✅ Транзакция удалена и откачена: {transactionId}, осталось: {pendingCount}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка удаления транзакции: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    // Синхронизирует конкретную транзакцию
    public async Task<bool> SyncSingleTransaction(string transactionId)
    {
        Logger.Info($"🔄 SyncSingleTransaction: {transactionId}");
        try
        {
            var transaction = await _offlineService.GetTransactionById(transactionId);
            if (transaction == null)
            {
                Logger.Warning($"⚠️ Транзакция {transactionId} не найдена");
                return false;
            }
            
            if (transaction.is_synced == 1)
            {
                Logger.Warning($"⚠️ Транзакция {transactionId} уже синхронизирована");
                return false;
            }

            Logger.Info($"📤 Обработка транзакции: type={transaction.operation_type}, barcode={transaction.barcode}");
            
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
                Logger.Warning($"⚠️ Неизвестный тип операции: {transaction.operation_type}");
                return false;
            }

            Logger.Info($"🗑️ Удаление транзакции: {transactionId}");
            await _offlineService.DeleteTransaction(transactionId);
            
            var pendingCount = await _offlineService.GetPendingCount();
            PendingCountChanged?.Invoke(this, pendingCount);
            
            Logger.Info($"✅ Транзакция синхронизирована: {transactionId}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка синхронизации транзакции {transactionId}: {ex.Message}");
            Logger.Error($"StackTrace: {ex.StackTrace}");
            await _offlineService.MarkAsError(transactionId, ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        Logger.Info($"🗑️ Dispose SyncQueueService");
        _autoSyncTimer?.Stop();
        _autoSyncTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}