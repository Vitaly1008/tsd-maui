using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для страницы приемки коробок
public partial class ReceivingViewModel : BaseOperationViewModel
{
    public ReceivingViewModel(IBarcodeService? barcodeService = null)
        : base("Receiving", barcodeService)
    {
    }

    protected override async Task ProcessBoxScan(string barcode)
    {
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

            if (existingBox != null)
            {
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
                if (existingBox.status == BoxStatus.Shipped || existingBox.status == BoxStatus.Empty)
                {
                    SetError($"Коробка #{boxNumber} отгружена/пуста");
                    return;
                }

                // ✅ Draft → изменить на Active
                if (existingBox.status == BoxStatus.Draft)
                {
                    System.Diagnostics.Debug.WriteLine($"📦 Коробка #{boxNumber} в статусе Draft, меняем на Active");
                    
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
                System.Diagnostics.Debug.WriteLine($"📦 Создание новой коробки #{boxNumber} локально");
                
                var productName = await GetProductName(ean13);
                box = CreateLocalBox(ean13, quantity, grade, boxNumber, productName, BoxStatus.Active);
                
                // ✅ 2.4. Сохранить в локальную БД
                await SaveBoxToCache(box);
            }

            // ✅ 2.5. Добавить в сессию
            AddBoxToList(box);
            
            // ✅ 2.6. Создать транзакцию в Queue (тип: Receiving)
            await AddToOfflineQueue(box);
            
            SetSuccess($"Коробка #{boxNumber} принята");
        }
        catch (Exception ex)
        {
            SetError($"Ошибка: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AddToOfflineQueue(Box box)
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
            operationType = "Receiving"
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
            await Application.Current?.MainPage?.DisplayAlert(
                "Внимание",
                "Нет коробок для подтверждения",
                "OK"
            );
            return;
        }

        IsLoading = true;

        try
        {
            var pendingCount = await _syncQueueService.GetPendingCount();

            var message = pendingCount > 0
                ? $"{ScannedBoxes.Count} коробок принято. " +
                $"Ожидает синхронизации: {pendingCount} коробок.\n\n" +
                "Синхронизация будет выполнена автоматически."
                : $"Принято {ScannedBoxes.Count} коробок. Все синхронизированы! ✅";

            await Application.Current?.MainPage?.DisplayAlert(
                pendingCount > 0 ? "Операция сохранена" : "Успешно",
                message,
                "OK"
            );

            ClearSession();
            OnOperationCompleted();
        }
        catch (Exception ex)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                $"Не удалось сохранить операцию: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }
    }
}