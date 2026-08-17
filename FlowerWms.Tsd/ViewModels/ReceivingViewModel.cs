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
            // Проверка на дублирование в сессии
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

            // ✅ ПРОВЕРКА ЛОКАЛЬНОГО КЭША - ищем любую коробку
            var cachedBox = await _dbHelper.GetBoxByBarcode(barcode);
            
            // Если коробка уже АКТИВНА в локальной БД — блокируем
            if (cachedBox != null && cachedBox.status == BoxStatus.Active)
            {
                SetError($"Коробка №{cachedBox.box_number} уже активирована!", "⚠️", Colors.Orange);
                return;
            }

            var productName = await GetProductName(ean13);
            Box box;
            bool isActivated = false;

            if (IsOnline)
            {
                // ✅ ОНЛАЙН-РЕЖИМ: проверяем на сервере
                var existingBox = await _apiService.GetBoxByBarcode(barcode);

                if (existingBox == null)
                {
                    SetError($"Коробка №{boxNumber} не найдена на сервере! Сначала напечатайте штрихкод.");
                    return;
                }

                // Проверка статуса на сервере
                if (existingBox.Status == BoxStatus.Active)
                {
                    SetError($"Коробка №{boxNumber} уже активирована на сервере!", "⚠️", Colors.Orange);
                    return;
                }

                if (existingBox.Status != BoxStatus.Draft)
                {
                    SetError($"Коробка №{boxNumber} имеет статус {existingBox.Status}, активация невозможна");
                    return;
                }

                try
                {
                    // ✅ АКТИВИРУЕМ НА СЕРВЕРЕ
                    var activateResult = await _apiService.ActivateBox(
                        boxId: existingBox.Id,
                        locationCode: CurrentLocation,
                        comment: $"Приемка через ТСД, локация: {CurrentLocation}"
                    );

                    if (activateResult.TryGetValue("success", out var success) && success is bool s && s)
                    {
                        var data = activateResult.GetValueOrDefault("data") as Dictionary<string, object>;
                        if (data != null)
                        {
                            box = Box.FromJson(data);
                            box.LocationCode = CurrentLocation;
                            
                            // ✅ СТАТУС УЖЕ ACTIVE (пришел с сервера)
                            isActivated = true;

                            // Сохраняем в локальный кэш с ACTIVE статусом
                            await SaveBoxToCache(box, isLocal: false);
                        }
                        else
                        {
                            throw new Exception("Не удалось получить данные коробки");
                        }
                    }
                    else
                    {
                        throw new Exception(activateResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка");
                    }
                }
                catch (Exception ex)
                {
                    // ✅ ОШИБКА АКТИВАЦИИ - переходим в офлайн-режим
                    // Проверяем, есть ли уже локальная Draft-коробка
                    if (cachedBox != null && cachedBox.status == BoxStatus.Draft)
                    {
                        // Используем существующую локальную коробку
                        box = Box.FromCache(cachedBox);
                        box.LocationCode = CurrentLocation;
                        isActivated = false;
                    }
                    else
                    {
                        // Создаем новую локальную коробку со статусом DRAFT
                        box = CreateLocalBox(ean13, quantity, grade, boxNumber, productName, BoxStatus.Draft);
                        await AddToOfflineQueue(box);
                        await SaveBoxToCache(box, isLocal: true);
                        isActivated = false;
                    }

                    await Application.Current?.MainPage?.DisplayAlert(
                        "Внимание",
                        $"Коробка сохранена локально для синхронизации.\n{ex.Message}",
                        "OK"
                    );
                }
            }
            else
            {
                // ✅ ОФЛАЙН-РЕЖИМ
                if (cachedBox != null && cachedBox.status == BoxStatus.Draft)
                {
                    // Используем существующую локальную коробку
                    box = Box.FromCache(cachedBox);
                    box.LocationCode = CurrentLocation;
                    isActivated = false;
                }
                else
                {
                    // Создаем новую локальную коробку со статусом DRAFT
                    box = CreateLocalBox(ean13, quantity, grade, boxNumber, productName, BoxStatus.Draft);
                    await AddToOfflineQueue(box);
                    await SaveBoxToCache(box, isLocal: true);
                    isActivated = false;
                }

                await Application.Current?.MainPage?.DisplayAlert(
                    "Офлайн-режим",
                    $"Коробка №{boxNumber} сохранена локально. Будет активирована при синхронизации.",
                    "OK"
                );
            }

            // ✅ Добавляем коробку в список сессии
            if (!ScannedBoxes.Any(b => b.Barcode == box.Barcode))
            {
                AddBoxToList(box);
            }
            
            // ✅ Обновляем статус в соответствии с режимом активации
            if (!isActivated)
            {
                SetStatus($"Локально: #{box.BoxNumber} (ожидает синхронизации)", "📴", Colors.Orange);
            }
            else
            {
                SetStatus($"Активирована: #{box.BoxNumber}", "✅", Colors.Green);
            }
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
            boxId = box.Id,
            barcode = box.Barcode,
            boxNumber = box.BoxNumber,
            ean13 = box.ProductEan13,
            quantity = box.Quantity,
            grade = GetGradeCode(box.Grade),
            locationCode = box.LocationCode ?? CurrentLocation,
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
        var boxes = ScannedBoxes.ToList();
        var localBoxes = boxes.Where(b => b.Id.StartsWith("local_")).ToList();

        try
        {
            if (localBoxes.Any() && IsOnline)
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "Синхронизация",
                    $"Найдено {localBoxes.Count} локальных коробок. Синхронизация...",
                    "OK"
                );

                // ✅ Синхронизируем очередь
                await _syncQueueService.ProcessQueueAsync();
                
                // ✅ ПОСЛЕ СИНХРОНИЗАЦИИ - обновляем статусы всех локальных коробок на ACTIVE
                var localBarcodes = localBoxes.Select(b => b.Barcode).ToList();
                await _dbHelper.UpdateBoxesStatus(localBarcodes, BoxStatus.Active);
                
                // Обновляем статусы в текущей сессии
                foreach (var box in localBoxes)
                {
                    box.Status = BoxStatus.Active;
                }
            }

            var message = localBoxes.Any() && !IsOnline
                ? $"{localBoxes.Count} коробок сохранены локально и будут синхронизированы при подключении."
                : $"Принято {boxes.Count} коробок.";

            await Application.Current?.MainPage?.DisplayAlert(
                IsOnline ? "Успешно" : "Офлайн-режим",
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