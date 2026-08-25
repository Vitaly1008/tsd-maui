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
            // ✅ 3.3. Проверка на дублирование сканирования в сессии
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

            var productName = await GetProductName(ean13);

            // ✅ 3.2. Добавление в сессию (без проверок)
            // Создаем локальную коробку со статусом DRAFT
            var box = CreateLocalBox(ean13, quantity, grade, boxNumber, productName, BoxStatus.Draft);
            
            // ✅ Добавляем в очередь синхронизации (НЕ сохраняем в локальную БД вручную!)
            await AddToOfflineQueue(box);

            // ✅ Добавляем коробку в список сессии
            AddBoxToList(box);
            
            SetStatus($"Коробка #{box.BoxNumber} добавлена (ожидает синхронизации)", "📴", Colors.Orange);
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
            boxNumber = box.BoxNumber,
            ean13 = box.ProductEan13,
            quantity = box.Quantity,
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
            // ✅ 3.4. Синхронизация (запускаем обработку очереди)
            await _syncQueueService.ProcessQueueAsync();
            
            // Проверяем, сколько коробок еще в очереди
            var pendingCount = await _syncQueueService.GetPendingCount();

            var message = pendingCount > 0
                ? $"{ScannedBoxes.Count} коробок добавлено. Ожидает синхронизации: {pendingCount} коробок."
                : $"Принято {ScannedBoxes.Count} коробок. Все синхронизированы! ✅";

            await Application.Current?.MainPage?.DisplayAlert(
                pendingCount > 0 ? "Офлайн-режим" : "Успешно",
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