using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.ViewModels;

/// <summary>
/// ViewModel для страницы инвентаризации
/// </summary>
public partial class InventoryViewModel : BaseScannerViewModel
{
    // Специфичные свойства для инвентаризации
    [ObservableProperty]
    private string _currentLocation = string.Empty;

    [ObservableProperty]
    private string _locationInfo = "Сканируйте локацию для просмотра коробок";

    [ObservableProperty]
    private int _totalBoxesInLocation;

    [ObservableProperty]
    private int _totalQuantityInLocation;

    [ObservableProperty]
    private ObservableCollection<Box> _locationBoxes = new();

    [ObservableProperty]
    private bool _isLocationMode = true;

    [ObservableProperty]
    private string _modeText = "Режим: просмотр локации";

    [ObservableProperty]
    private Box? _selectedBox;

    [ObservableProperty]
    private string _selectedBoxInfo = "Коробка не выбрана";

    [ObservableProperty]
    private string _targetLocation = string.Empty;

    [ObservableProperty]
    private bool _isMoveMode;

    [ObservableProperty]
    private string _moveButtonText = "Указать локацию";

    [ObservableProperty]
    private bool _isMoveButtonEnabled;

    private Box? _currentSelectedBox;

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public InventoryViewModel(IBarcodeService? barcodeService = null)
        : base(barcodeService)
    {
        LocationBoxes = new ObservableCollection<Box>();
        SetStatus("Сканируйте локацию или коробку", "📷", Colors.Gray);
    }

    protected override async Task ProcessBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;
        if (IsLoading) return;

        IsLoading = true;
        LastScannedBarcode = barcode;

        try
        {
            if (IsLocationBarcode(barcode))
            {
                await ProcessLocationScan(barcode);
            }
            else
            {
                await ProcessBoxScan(barcode);
            }
        }
        catch (Exception ex)
        {
            SetError($"Ошибка: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ProcessLocationScan(string locationCode)
    {
        CurrentLocation = locationCode;
        SetStatus($"Локация: {locationCode}", "📍", Colors.Blue);

        var cachedBoxes = await _dbHelper.GetBoxesByLocation(locationCode);
        var boxes = cachedBoxes.Select(BoxCacheToBox).ToList();

        // Если локально нет коробок и есть интернет - пытаемся получить с сервера
        if (boxes.Count == 0 && IsOnline)
        {
            try
            {
                var serverBoxes = await _apiService.GetBoxesByLocation(locationCode);
                if (serverBoxes.Count > 0)
                {
                    foreach (var box in serverBoxes)
                    {
                        await SaveBoxToCache(box, isLocal: false);
                    }
                    boxes = serverBoxes;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения коробок с сервера: {ex.Message}");
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LocationBoxes.Clear();
            foreach (var box in boxes)
            {
                LocationBoxes.Add(box);
            }

            TotalBoxesInLocation = boxes.Count;
            TotalQuantityInLocation = boxes.Sum(b => b.CurrentQuantity > 0 ? b.CurrentQuantity : b.Quantity);

            LocationInfo = $"Локация: {locationCode}\n" +
                          $"Коробок: {TotalBoxesInLocation}\n" +
                          $"Количество: {TotalQuantityInLocation} шт.";
        });
    }

    private async Task ProcessBoxScan(string barcode)
    {
        var box = await FindBoxByBarcode(barcode);

        if (box == null)
        {
            SetError("Коробка не найдена на складе");
            return;
        }

        // Если коробка уже в списке - показываем информацию
        if (LocationBoxes.Any(b => b.Barcode == box.Barcode))
        {
            SelectedBox = box;
            _currentSelectedBox = box;
            UpdateBoxInfo();
            return;
        }

        // Переход в режим перемещения
        IsLocationMode = false;
        IsMoveMode = true;
        SelectedBox = box;
        _currentSelectedBox = box;

        UpdateBoxInfo();

        SetStatus($"Коробка #{box.BoxNumber} выбрана для перемещения", "📦", Colors.Green);

        ModeText = "Режим: перемещение коробки";
        MoveButtonText = "Указать новую локацию";
        IsMoveButtonEnabled = true;

        TargetLocation = string.Empty;
        Vibration.Vibrate(100);
    }

    private async Task<Box?> FindBoxByBarcode(string barcode)
    {
        var cachedBox = await _dbHelper.GetBoxByBarcode(barcode);
        if (cachedBox != null)
        {
            return BoxCacheToBox(cachedBox);
        }

        if (IsOnline)
        {
            try
            {
                var serverBox = await _apiService.FindBoxByBarcode(barcode);
                if (serverBox != null)
                {
                    await SaveBoxToCache(serverBox, isLocal: false);
                    return serverBox;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка поиска на сервере: {ex.Message}");
            }
        }

        return null;
    }

    private void UpdateBoxInfo()
    {
        if (_currentSelectedBox == null) return;

        SelectedBoxInfo = $"Коробка #{_currentSelectedBox.BoxNumber}\n" +
                         $"Продукт: {_currentSelectedBox.ProductName}\n" +
                         $"Количество: {_currentSelectedBox.CurrentQuantity} шт.\n" +
                         $"Текущая локация: {_currentSelectedBox.LocationCode ?? "Не указана"}\n" +
                         $"Сорт: {_currentSelectedBox.Grade}";
    }

    [RelayCommand]
    public async Task SetTargetLocation()
    {
        if (SelectedBox == null)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                "Сначала выберите коробку для перемещения",
                "OK"
            );
            return;
        }

        var result = await Application.Current?.MainPage?.DisplayPromptAsync(
            "Введите целевую локацию",
            $"Коробка #{SelectedBox.BoxNumber}\nТекущая: {SelectedBox.LocationCode ?? "Не указана"}",
            "Подтвердить",
            "Отмена",
            SelectedBox.LocationCode
        );

        if (!string.IsNullOrEmpty(result) && result != SelectedBox.LocationCode)
        {
            TargetLocation = result;
            MoveButtonText = $"Переместить в {result}";
            await MoveBox();
        }
        else if (result == SelectedBox.LocationCode)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Внимание",
                "Вы указали ту же локацию. Перемещение не требуется.",
                "OK"
            );
        }
    }

    [RelayCommand]
    public async Task MoveBox()
    {
        if (SelectedBox == null || string.IsNullOrEmpty(TargetLocation))
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                "Укажите целевую локацию для перемещения",
                "OK"
            );
            return;
        }

        IsLoading = true;

        try
        {
            var oldLocation = SelectedBox.LocationCode;

            var boxCache = await _dbHelper.GetBoxByBarcode(SelectedBox.Barcode);
            if (boxCache != null)
            {
                boxCache.location_code = TargetLocation;
                boxCache.updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _dbHelper.SaveBox(boxCache);
            }

            var payload = new
            {
                boxId = SelectedBox.Id,
                boxNumber = SelectedBox.BoxNumber,
                oldLocation = oldLocation,
                targetLocation = TargetLocation,
                barcode = SelectedBox.Barcode,
                operationType = "Move",
                timestamp = DateTime.UtcNow
            };

            await _syncQueueService.EnqueueAsync(
                operationType: "Move",
                barcode: SelectedBox.Barcode,
                payload: payload,
                deviceId: Constants.DeviceId
            );

            SelectedBox.LocationCode = TargetLocation;
            UpdateBoxInfo();

            // Обновляем список коробок в текущей локации
            if (!string.IsNullOrEmpty(CurrentLocation))
            {
                await ProcessLocationScan(CurrentLocation);
            }

            await Application.Current?.MainPage?.DisplayAlert(
                "Успешно",
                $"Коробка #{SelectedBox.BoxNumber} перемещена в {TargetLocation}",
                "OK"
            );

            ResetToLocationMode();
        }
        catch (Exception ex)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                $"Не удалось выполнить перемещение: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ResetToLocationMode()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsLocationMode = true;
            IsMoveMode = false;
            SelectedBox = null;
            _currentSelectedBox = null;
            SelectedBoxInfo = "Коробка не выбрана";
            TargetLocation = string.Empty;
            MoveButtonText = "Указать локацию";
            IsMoveButtonEnabled = false;
            ModeText = "Режим: просмотр локации";
            SetStatus("Сканируйте локацию для просмотра коробок", "📷", Colors.Gray);
        });
    }

    [RelayCommand]
    public async Task CancelOperation()
    {
        if (IsMoveMode)
        {
            var confirm = await Application.Current?.MainPage?.DisplayAlert(
                "Выход",
                "Перемещение не завершено. Выйти?",
                "Да",
                "Нет"
            );
            if (confirm == false) return;
        }
        else if (LocationBoxes.Count > 0)
        {
            var confirm = await Application.Current?.MainPage?.DisplayAlert(
                "Выход",
                "Выйти из инвентаризации?",
                "Да",
                "Нет"
            );
            if (confirm == false) return;
        }

        StopScanner();
        
        // 👇 ИСПОЛЬЗУЕМ СОБЫТИЕ (InventoryViewModel не наследует BaseOperationViewModel)
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public async Task RefreshLocation()
    {
        if (!string.IsNullOrEmpty(CurrentLocation))
        {
            await ProcessLocationScan(CurrentLocation);
        }
        else
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Информация",
                "Сначала отсканируйте локацию",
                "OK"
            );
        }
    }

    [RelayCommand]
    public void CancelMove()
    {
        if (IsMoveMode)
        {
            ResetToLocationMode();
        }
    }

    private static Box BoxCacheToBox(BoxCache cached)
    {
        return new Box
        {
            Id = cached.box_id,
            Barcode = cached.barcode,
            BoxNumber = cached.box_number,
            ProductName = cached.product_name,
            ProductEan13 = cached.product_ean13,
            CurrentQuantity = cached.current_quantity,
            InitialQuantity = cached.initial_quantity,
            Grade = cached.grade,
            LocationCode = cached.location_code,
            Status = cached.status,
            CreatedAt = cached.created_at,
            UpdatedAt = cached.updated_at
        };
    }

    private async Task SaveBoxToCache(Box box, bool isLocal)
    {
        var boxCache = new BoxCache
        {
            barcode = box.Barcode,
            box_id = box.Id,
            box_number = box.BoxNumber,
            grade = box.Grade,
            initial_quantity = box.InitialQuantity > 0 ? box.InitialQuantity : box.Quantity,
            current_quantity = box.CurrentQuantity > 0 ? box.CurrentQuantity : box.Quantity,
            product_id = box.ProductId,
            product_name = box.ProductName,
            product_ean13 = box.ProductEan13,
            location_code = box.LocationCode ?? "UNKNOWN",
            status = box.Status,
            created_at = box.CreatedAt,
            updated_at = box.UpdatedAt,
            is_dirty = isLocal ? 1 : 0
        };
        await _dbHelper.SaveBox(boxCache);
    }
}