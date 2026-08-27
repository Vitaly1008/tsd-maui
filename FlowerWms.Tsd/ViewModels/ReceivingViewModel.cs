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

            // ✅ ЕСЛИ ОФЛАЙН → СОХРАНЯЕМ ЛОКАЛЬНО
            if (!IsOnline)
            {
                System.Diagnostics.Debug.WriteLine($"📴 Офлайн-режим: сохранение коробки #{boxNumber} локально");
                
                // Создаем локальную коробку
                var productName = await GetProductName(ean13);
                var localBox = CreateLocalBox(ean13, quantity, grade, boxNumber, productName, BoxStatus.Active);
                
                // Сохраняем в локальную БД
                await SaveBoxToCache(localBox);
                
                // Добавляем в сессию
                AddBoxToList(localBox);
                
                // Создаем транзакцию для синхронизации
                await AddToOfflineQueue(localBox);
                
                SetWarning($"Коробка #{boxNumber} сохранена локально (офлайн-режим)");
                return;
            }

            // ✅ ОНЛАЙН: проверка на сервере
            Box? serverBox = null;
            try
            {
                serverBox = await _apiService.GetBoxByBarcode(barcode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка проверки коробки: {ex.Message}");
                // Если ошибка сети, переходим в офлайн-режим
                if (!await _apiService.PingServer())
                {
                    IsOnline = false;
                    // Повторяем сканирование в офлайн-режиме
                    await ProcessBoxScan(barcode);
                    return;
                }
                throw;
            }

            // ✅ Если коробки нет на сервере → ошибка
            if (serverBox == null)
            {
                SetError($"Коробка #{boxNumber} не найдена. Сначала напечатайте штрихкод.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Коробка на сервере: #{serverBox.BoxNumber}, статус: {serverBox.Status}");

            // ✅ Проверка статуса
            if (serverBox.Status == BoxStatus.Draft)
            {
                // ✅ Draft → активируем
                var result = await _apiService.ActivateBox(
                    boxId: serverBox.Id,
                    locationCode: CurrentLocation,
                    comment: $"Приемка через ТСД, локация: {CurrentLocation}"
                );

                if (!(result.TryGetValue("success", out var success) && success is bool s && s))
                {
                    var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                    SetError($"Ошибка активации: {errorMsg}");
                    return;
                }

                // ✅ Получаем обновленную коробку с сервера
                var updatedBox = await _apiService.GetBoxByBarcode(barcode);
                if (updatedBox != null)
                {
                    serverBox = updatedBox;
                }
                
                SetStatus($"Коробка #{boxNumber} активирована", "✅", Colors.Green);
            }
            else if (serverBox.Status == BoxStatus.Active)
            {
                SetError($"Коробка #{boxNumber} уже активна");
                return;
            }
            else if (serverBox.Status == BoxStatus.Reserved)
            {
                SetError($"Коробка #{boxNumber} зарезервирована");
                return;
            }
            else if (serverBox.Status == BoxStatus.Shipped || serverBox.Status == BoxStatus.Empty)
            {
                SetError($"Коробка #{boxNumber} отгружена/пуста");
                return;
            }
            else
            {
                SetError($"Недопустимый статус коробки: {serverBox.Status}");
                return;
            }

            // ✅ Обновляем локальную БД с сервера
            await UpdateLocalBox(serverBox);

            // ✅ Добавляем в сессию
            AddBoxToList(serverBox);
            
            // ✅ Создаем транзакцию для синхронизации
            await AddToOfflineQueue(serverBox);
            
            SetStatus($"Коробка #{boxNumber} принята", "✅", Colors.Green);
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

            if (pendingCount > 0 && IsOnline)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _syncQueueService.ProcessQueueAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Фоновая синхронизация: {ex.Message}");
                    }
                });
            }

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