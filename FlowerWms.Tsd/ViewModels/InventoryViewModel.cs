using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.ViewModels;

public partial class InventoryViewModel : ObservableObject
{
    private readonly ApiService _apiService;
    private readonly OfflineService _offlineService;
    private Box? _selectedBox;
    private string? _scannedBarcodeForMove;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    // ✅ Для режима "Информация о коробке"
    [ObservableProperty]
    private Box? _currentBox;

    [ObservableProperty]
    private string _currentBoxInfo = "Сканируйте коробку для получения информации";

    [ObservableProperty]
    private string _currentLocationInfo = "Сканируйте локацию для просмотра коробок";

    [ObservableProperty]
    private string _selectedBoxNumber = "Не выбрана";

    [ObservableProperty]
    private string _targetLocation = string.Empty;

    [ObservableProperty]
    private string _actionButtonText = "🔍 Информация о коробке";

    // ✅ Два списка для инвентаризации локации
    [ObservableProperty]
    private ObservableCollection<Box> _scannedBoxes = new();

    [ObservableProperty]
    private ObservableCollection<Box> _notScannedBoxes = new();

    [ObservableProperty]
    private string _scanModeText = "Режим: Информация о коробке";

    [ObservableProperty]
    private bool _isBoxMode = true;

    [ObservableProperty]
    private bool _isLocationMode;

    [ObservableProperty]
    private int _scannedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _progressText = "0 / 0";

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public InventoryViewModel()
    {
        _apiService = new ApiService();
        _offlineService = new OfflineService();
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            var syncService = new SyncService();
            IsOnline = await syncService.CheckInternetManual();
            UpdateModeUI();
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ✅ Переключение между режимами
    [RelayCommand]
    private void SwitchMode()
    {
        IsBoxMode = !IsBoxMode;
        IsLocationMode = !IsLocationMode;
        
        // Очищаем состояние
        CurrentBox = null;
        CurrentBoxInfo = IsBoxMode ? "Сканируйте коробку для получения информации" : "Сканируйте локацию для просмотра коробок";
        SelectedBoxNumber = "Не выбрана";
        TargetLocation = string.Empty;
        ScannedBoxes.Clear();
        NotScannedBoxes.Clear();
        ScannedCount = 0;
        TotalCount = 0;
        ProgressText = "0 / 0";
        LastScannedBarcode = null;
        
        UpdateModeUI();
    }

    private void UpdateModeUI()
    {
        if (IsBoxMode)
        {
            ScanModeText = "📦 Режим: Информация о коробке";
            ActionButtonText = "🔍 Информация о коробке";
        }
        else
        {
            ScanModeText = "📍 Режим: Сканирование локации";
            ActionButtonText = "✅ Завершить инвентаризацию";
        }
    }

    // ✅ Обработка сканирования
    [RelayCommand]
    private async Task ScanBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;

        IsLoading = true;
        LastScannedBarcode = barcode;

        try
        {
            if (IsBoxMode)
            {
                await ProcessBoxScan(barcode);
            }
            else
            {
                await ProcessLocationScan(barcode);
            }
        }
        catch (Exception ex)
        {
            await Application.Current?.MainPage?.DisplayAlert("Ошибка", ex.Message, "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ✅ Обработка сканирования коробки
    private async Task ProcessBoxScan(string barcode)
    {
        // Пытаемся получить информацию о коробке
        var box = await _apiService.FindBoxByBarcode(barcode);
        
        if (box == null)
        {
            // Если не найдена - создаем локальную
            box = CreateLocalBox(barcode);
            CurrentBoxInfo = $"⚠️ Локальная коробка #{box.BoxNumber}\nШтрихкод: {barcode}";
        }
        else
        {
            CurrentBox = box;
            SelectedBoxNumber = $"#{box.BoxNumber}";
            CurrentBoxInfo = $"📦 Коробка #{box.BoxNumber}\n" +
                            $"Продукт: {box.ProductName}\n" +
                            $"Количество: {box.Quantity} шт.\n" +
                            $"Локация: {box.LocationCode ?? "Не указана"}\n" +
                            $"Сорт: {box.Grade}";
            
            // Если есть локация - предлагаем переместить
            if (!string.IsNullOrEmpty(box.LocationCode))
            {
                TargetLocation = box.LocationCode;
            }
        }
    }

    // ✅ Обработка сканирования локации
    private async Task ProcessLocationScan(string locationCode)
    {
        TargetLocation = locationCode;
        CurrentLocationInfo = $"📍 Локация: {locationCode}";
        LastScannedBarcode = locationCode;

        // Получаем список коробок в локации
        var boxes = await _apiService.GetBoxesByLocation(locationCode);
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            NotScannedBoxes.Clear();
            
            if (boxes.Count == 0)
            {
                CurrentLocationInfo = $"📍 Локация: {locationCode}\nКоробок не найдено";
                TotalCount = 0;
                ScannedCount = 0;
                ProgressText = "0 / 0";
                return;
            }

            // Все коробки начинаются как "не отсканированные"
            foreach (var box in boxes)
            {
                NotScannedBoxes.Add(box);
            }
            
            TotalCount = boxes.Count;
            ScannedCount = 0;
            ProgressText = $"0 / {TotalCount}";
            CurrentLocationInfo = $"📍 Локация: {locationCode}\nВсего коробок: {TotalCount}";
        });
    }

    // ✅ Сканирование коробки в режиме инвентаризации локации
    [RelayCommand]
    private async Task ScanBoxInLocation(string barcode)
    {
        if (string.IsNullOrEmpty(barcode) || !IsLocationMode) return;

        // Ищем коробку в списке "не отсканированных"
        var box = NotScannedBoxes.FirstOrDefault(b => b.Barcode == barcode);
        if (box != null)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                NotScannedBoxes.Remove(box);
                ScannedBoxes.Add(box);
                ScannedCount++;
                ProgressText = $"{ScannedCount} / {TotalCount}";
                LastScannedBarcode = barcode;
            });
            
            // Вибрируем для подтверждения
            Vibration.Vibrate(100);
        }
        else
        {
            // Проверяем, может уже отсканирована
            var alreadyScanned = ScannedBoxes.Any(b => b.Barcode == barcode);
            if (alreadyScanned)
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "Информация",
                    "Эта коробка уже отсканирована",
                    "OK"
                );
            }
            else
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "Не найдено",
                    $"Коробка с штрихкодом {barcode} не найдена в этой локации",
                    "OK"
                );
            }
        }
    }

    // ✅ Подтвердить перемещение коробки
    [RelayCommand]
    private async Task MoveBox()
    {
        if (CurrentBox == null)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                "Сначала отсканируйте коробку",
                "OK"
            );
            return;
        }

        if (string.IsNullOrEmpty(TargetLocation) || TargetLocation == CurrentBox.LocationCode)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                "Укажите новую локацию для перемещения",
                "OK"
            );
            return;
        }

        IsLoading = true;

        try
        {
            var result = await _apiService.MoveBox(CurrentBox.Id, TargetLocation);
            
            if (result.TryGetValue("success", out var success) && (bool)success)
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Успешно",
                    $"Коробка #{CurrentBox.BoxNumber} перемещена в {TargetLocation}",
                    "OK"
                );
                
                // Обновляем информацию
                CurrentBox.LocationCode = TargetLocation;
                CurrentBoxInfo = $"📦 Коробка #{CurrentBox.BoxNumber}\n" +
                                $"Продукт: {CurrentBox.ProductName}\n" +
                                $"Количество: {CurrentBox.Quantity} шт.\n" +
                                $"Локация: {TargetLocation}\n" +
                                $"Сорт: {CurrentBox.Grade}";
            }
            else
            {
                // Если API не доступен - сохраняем офлайн
                await _offlineService.SaveTransaction(
                    operationType: "Move",
                    barcode: CurrentBox.Barcode,
                    payload: new
                    {
                        boxId = CurrentBox.Id,
                        boxNumber = CurrentBox.BoxNumber,
                        targetLocation = TargetLocation,
                        currentLocation = CurrentBox.LocationCode,
                        operation = "MoveBox"
                    },
                    deviceId: Constants.DeviceId
                );
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "📴 Офлайн-режим",
                    $"Перемещение сохранено для синхронизации",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            await Application.Current?.MainPage?.DisplayAlert("Ошибка", ex.Message, "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ✅ Завершить инвентаризацию локации
    [RelayCommand]
    private async Task CompleteLocationInventory()
    {
        if (ScannedCount < TotalCount)
        {
            var confirm = await Application.Current?.MainPage?.DisplayAlert(
                "Не все коробки отсканированы",
                $"Отсканировано {ScannedCount} из {TotalCount} коробок. Завершить?",
                "Да",
                "Нет"
            );
            
            if (confirm == false) return;
        }

        await Application.Current?.MainPage?.DisplayAlert(
            "✅ Инвентаризация завершена",
            $"Отсканировано {ScannedCount} из {TotalCount} коробок",
            "OK"
        );

        // Очищаем состояние
        ScannedBoxes.Clear();
        NotScannedBoxes.Clear();
        ScannedCount = 0;
        TotalCount = 0;
        ProgressText = "0 / 0";
        TargetLocation = string.Empty;
        CurrentLocationInfo = "Сканируйте локацию для просмотра коробок";
        LastScannedBarcode = null;
    }

    // ✅ Кнопка "Назад"
    [RelayCommand]
    private async Task Back()
    {
        if (ScannedCount > 0 || NotScannedBoxes.Count > 0)
        {
            var confirm = await Application.Current?.MainPage?.DisplayAlert(
                "Выход",
                "Инвентаризация не завершена. Выйти?",
                "Да",
                "Нет"
            );
            
            if (confirm == false) return;
        }
        
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    private Box CreateLocalBox(string barcode)
    {
        var parts = barcode.Split('-');
        
        var ean13 = parts.Length > 0 ? parts[0] : "0000000000000";
        var quantity = parts.Length > 1 && int.TryParse(parts[1], out var q) ? q : 100;
        var grade = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : "Premium";
        var boxNumber = parts.Length > 3 && int.TryParse(parts[3], out var n) ? n : 0;

        if (boxNumber == 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(barcode, @"-(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var n2))
                boxNumber = n2;
            else
                boxNumber = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 10000);
        }

        return new Box
        {
            Id = $"local_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Barcode = barcode,
            BoxNumber = boxNumber,
            ProductName = $"Локальная коробка #{boxNumber}",
            ProductEan13 = ean13,
            Quantity = quantity,
            Grade = grade,
            LocationCode = "UNKNOWN",
            Status = 1 //"Active"
        };
    }

    public bool IsActionEnabled => CurrentBox != null && !string.IsNullOrEmpty(TargetLocation);
}