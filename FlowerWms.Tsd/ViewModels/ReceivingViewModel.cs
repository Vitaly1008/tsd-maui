using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для страницы приемки коробок
public partial class ReceivingViewModel : BaseOperationViewModel
{
    public ReceivingViewModel(
        DatabaseHelper dbHelper,
        ApiService apiService,
        SyncQueueService syncQueueService,
        SyncService syncService,
        IBarcodeService barcodeService)
        : base("Receiving", dbHelper, apiService, syncQueueService, syncService, barcodeService)
    {
    }

    protected override async Task ProcessBoxScan(string barcode)
    {
        // ✅ ИСПРАВЛЕНО: защита от параллельного сканирования
        if (IsLoading) return;

        IsLoading = true;

        try
        {
            // ✅ 2.2. Проверка дублирования в сессии
            if (ScannedBoxes.Any(b => b.Barcode == barcode))
            {
                SetError("Коробка уже отсканирована в этой сессии");
                return;
            }

            var (ean13, quantity, grade, boxNumber) = ParseBarcode(barcode);

            if (string.IsNullOrEmpty(ean13) || boxNumber <= 0)
            {
                SetError("Некорректный формат штрихкода");
                return;
            }

            // ✅ 2.3. Проверка наличия в локальной БД
            var existingBox = await _dbHelper.GetBoxByBarcode(barcode);
            Box box;
            bool wasCreatedLocally = false;
            BoxStatus? previousStatus = null;

            if (existingBox != null)
            {
                previousStatus = existingBox.status;

                // ✅ 2.3.2. Есть → проверить статус
                if (existingBox.status == BoxStatus.Active)
                {
                    SetError($"Коробка #{boxNumber} уже активна");
                    return;
                }
                if (existingBox.status == BoxStatus.Reserved)
                {
                    SetError($"Коробка #{boxNumber} зарезервирована");
                    return;
                }
                if (existingBox.status == BoxStatus.Shipped)
                {
                    SetError($"Коробка #{boxNumber} отгружена");
                    return;
                }
                if (existingBox.status == BoxStatus.Empty)
                {
                    SetError($"Коробка #{boxNumber} пуста");
                    return;
                }
                // ✅ ДОБАВЛЕНО: явная ветка для Discarded
                if (existingBox.status == BoxStatus.Discarded)
                {
                    SetError($"Коробка #{boxNumber} списана");
                    return;
                }

                // ✅ Draft → изменить на Active
                if (existingBox.status == BoxStatus.Draft)
                {
                    Logger.Info($"📦 Коробка #{boxNumber} в статусе Draft, меняем на Active");

                    await _dbHelper.ForceUpdateBoxStatus(
                        barcode: barcode,
                        newStatus: BoxStatus.Active,
                        newQuantity: existingBox.current_quantity
                    );

                    var updatedBox = await _dbHelper.GetBoxByBarcode(barcode);
                    if (updatedBox != null)
                    {
                        box = Box.FromCache(updatedBox);
                    }
                    else
                    {
                        // Неожиданная ситуация — создаем заново
                        var productName = await GetProductName(ean13);
                        box = CreateLocalBox(ean13, quantity, grade, boxNumber, productName, BoxStatus.Active);
                    }
                    wasCreatedLocally = false;
                }
                else
                {
                    // Неизвестный статус — ошибка
                    SetError($"Недопустимый статус коробки: {existingBox.status}");
                    return;
                }
            }
            else
            {
                // ✅ 2.3.1. Нет → создать коробку со статусом Active
                wasCreatedLocally = true;
                Logger.Info($"📦 Создание новой коробки #{boxNumber} локально");

                var productName = await GetProductName(ean13);
                box = CreateLocalBox(ean13, quantity, grade, boxNumber, productName, BoxStatus.Active);

                // ✅ 2.4. Сохранить в локальную БД
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
                    location_code = CurrentLocation ?? "UNKNOWN",
                    status = BoxStatus.Active,
                    created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }

            // ✅ 2.5. Добавить в сессию
            AddBoxToList(box);

            // ✅ 2.6. Создать транзакцию в Queue (тип: Receiving)
            await AddToOfflineQueue(box, wasCreatedLocally, previousStatus);

            SetSuccess($"Коробка #{boxNumber} принята");
        }
        catch (Exception ex)
        {
            SetError($"Ошибка: {ex.Message}");
            Logger.Error($"Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AddToOfflineQueue(Box box, bool wasCreatedLocally, BoxStatus? previousStatus)
    {
        var payload = new
        {
            barcode = box.Barcode,
            boxId = box.Id,
            boxNumber = box.BoxNumber,
            ean13 = box.ProductEan13,
            quantity = box.CurrentQuantity,
            grade = GetGradeCode(box.Grade),
            locationCode = box.LocationCode ?? CurrentLocation,
            productName = box.ProductName,
            operationType = "Receiving",
            // ✅ ДОБАВЛЕНО: для корректного отката
            // true = коробка была создана заново (откат = удалить)
            // false = коробка уже была (откат = вернуть предыдущий статус)
            wasCreatedLocally = wasCreatedLocally,
            previousStatus = (int)(previousStatus ?? BoxStatus.Draft)
        };

        await _syncQueueService.EnqueueAsync(
            operationType: "Receiving",
            barcode: box.Barcode,
            payload: payload,
            deviceId: Constants.DeviceId
        );
    }

    public override async Task ConfirmOperation()
    {
        if (ScannedBoxes.Count == 0)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Внимание",
                "Нет коробок для подтверждения",
                "OK"
            );
            return;
        }

        IsLoading = true;
        bool operationSucceeded = false;

        try
        {
            var pendingCount = await _syncQueueService.GetPendingCount();

            // ✅ ИСПРАВЛЕНО: вариант B — синхронизация вручную
            var message = pendingCount > 0
                ? $"{ScannedBoxes.Count} коробок принято.\n\n" +
                  $"📤 Ожидает синхронизации: {pendingCount} операций.\n\n" +
                  "Синхронизируйте вручную в разделе «Очередь»."
                : $"Принято {ScannedBoxes.Count} коробок. Все синхронизированы! ✅";

            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                pendingCount > 0 ? "Операция сохранена" : "Успешно",
                message,
                "OK"
            );

            operationSucceeded = true;
        }
        catch (Exception ex)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                $"Не удалось сохранить операцию: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }

        // ✅ ИСПРАВЛЕНО: вынесено после try/catch,
        // чтобы ошибки в ClearSession/OnOperationCompleted не маскировались
        if (operationSucceeded)
        {
            ClearSession();
            OnOperationCompleted();
        }
    }
}