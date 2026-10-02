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
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            if (ScannedBoxes.Any(b => b.Barcode == barcode))
            {
                SetError("Коробка уже отсканирована в этой сессии");
                return;
            }

            // ✅ TryParseBoxBarcode сам гарантирует: ean13 непустой,
            //    quantity > 0, grade ∈ {1,2,3,5,9}, boxNumber > 0.
            if (!TryParseBoxBarcode(barcode, out var ean13, out var quantity,
                                    out var gradeCode, out var gradeName, out var boxNumber))
            {
                SetError("Некорректный формат штрихкода");
                return;
            }

            var operationTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // ✅ 2.3. Проверка наличия в локальной БД.
            var existingBox = await _dbHelper.GetActiveBoxByBarcode(barcode);
            Box box;
            bool wasCreatedLocally = false;
            BoxStatus? previousStatus = null;

            if (existingBox != null)
            {
                previousStatus = existingBox.status;

                // Архивные статусы в локальной БД быть не должны.
                // Если встретились — трактуем как «нет коробки».
                if (existingBox.status == BoxStatus.Empty ||
                    existingBox.status == BoxStatus.Shipped ||
                    existingBox.status == BoxStatus.Discarded)
                {
                    Logger.Warning($"⚠️ Архивный статус в кэше: {barcode}, status={existingBox.status}. Трактуем как «нет коробки».");
                    existingBox = null;
                }
                else if (existingBox.status == BoxStatus.Active)
                {
                    SetError($"Коробка #{boxNumber} уже активна");
                    return;
                }
                else if (existingBox.status == BoxStatus.Reserved)
                {
                    SetError($"Коробка #{boxNumber} зарезервирована");
                    return;
                }
                else if (existingBox.status != BoxStatus.Draft)
                {
                    SetError($"Недопустимый статус коробки: {existingBox.status}");
                    return;
                }
            }

            if (existingBox != null && existingBox.status == BoxStatus.Draft)
            {
                // ✅ Draft → изменить на Active
                Logger.Info($"📦 Коробка #{boxNumber} в статусе Draft, меняем на Active");

                await _dbHelper.ForceUpdateBoxStatus(
                    barcode: barcode,
                    newStatus: BoxStatus.Active,
                    newQuantity: existingBox.current_quantity,
                    operationTime: operationTimeMs,
                    newCreatedAt: operationTimeMs
                );

                var updatedBox = await _dbHelper.GetBoxByBarcode(barcode);
                if (updatedBox != null)
                {
                    box = Box.FromCache(updatedBox);
                    wasCreatedLocally = false;
                }
                else
                {
                    // Аномалия: только что обновили статус — а записи нет.
                    // Считаем, что коробку создаём заново (откат = удалить).
                    Logger.Warning($"⚠️ После ForceUpdateBoxStatus запись {barcode} не найдена. Создаём заново.");
                    var productName = await GetProductName(ean13);
                    box = CreateLocalBox(barcode, ean13, quantity, gradeName, boxNumber, productName, BoxStatus.Active);
                    wasCreatedLocally = true;
                }
            }
            else
            {
                // ✅ 2.3.1. Нет → создать коробку со статусом Active
                wasCreatedLocally = true;
                Logger.Info($"📦 Создание новой коробки #{boxNumber} локально");

                var productName = await GetProductName(ean13);
                box = CreateLocalBox(barcode, ean13, quantity, gradeName, boxNumber, productName, BoxStatus.Active);

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
                    created_at = operationTimeMs,
                    updated_at = operationTimeMs
                });
            }

            // ✅ КРИТИЧНО: проставляем локацию в объект box.
            //    CreateLocalBox жёстко ставит UNKNOWN; здесь переопределяем
            //    актуальной локацией, чтобы payload.locationCode был корректным.
            box.LocationCode = CurrentLocation ?? "UNKNOWN";

            // ✅ 2.5. Добавить в сессию
            AddBoxToList(box);

            // ✅ 2.6. Создать транзакцию в Queue (тип: Receiving)
            await AddToOfflineQueue(box, wasCreatedLocally, previousStatus, operationTimeMs);

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

    private async Task AddToOfflineQueue(Box box, bool wasCreatedLocally, BoxStatus? previousStatus, long operationTimeMs)
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
            deviceId: Constants.DeviceId,
            createdAt: operationTimeMs
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