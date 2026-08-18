using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using System.Timers;

namespace FlowerWms.Tsd.ViewModels;

/// <summary>
/// Базовый ViewModel для операций (приемка/отгрузка)
/// </summary>
public abstract partial class BaseOperationViewModel : BaseScannerViewModel
{
    protected readonly string _operationType;
    protected readonly System.Timers.Timer _autoSaveTimer;
    protected int _scanCountSinceLastSave;

    [ObservableProperty]
    private string _currentLocation = "UNKNOWN";

    [ObservableProperty]
    private string? _orderNumber;

    [ObservableProperty]
    private string? _orderId;

    [ObservableProperty]
    private string _boxInfoText = string.Empty;

    [ObservableProperty]
    private bool _isBoxScanned;

    [ObservableProperty]
    private string _boxNumberDisplay = string.Empty;

    [ObservableProperty]
    private int _scannedCount;

    [ObservableProperty]
    private bool _isBoxListExpanded;

    public ObservableCollection<Box> ScannedBoxes { get; } = new();

    // ===== СОБЫТИЯ =====
    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    // ===== ЗАЩИЩЕННЫЕ МЕТОДЫ ДЛЯ ВЫЗОВА СОБЫТИЙ =====
    protected virtual void OnOperationCompleted()
    {
        OperationCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnOperationCancelled()
    {
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    protected BaseOperationViewModel(string operationType, IBarcodeService? barcodeService = null)
        : base(barcodeService)
    {
        _operationType = operationType;
        _autoSaveTimer = new System.Timers.Timer(30000);
        _autoSaveTimer.Elapsed += OnAutoSaveTimerElapsed;
        _autoSaveTimer.AutoReset = true;
    }

    public override async Task Initialize()
    {
        await base.Initialize();
        _autoSaveTimer.Start();
    }

    private async void OnAutoSaveTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await AutoSaveIfNeeded();
    }

    protected virtual async Task AutoSaveIfNeeded()
    {
        if (_scanCountSinceLastSave == 0 || ScannedBoxes.Count == 0)
            return;

        try
        {
            var boxes = ScannedBoxes.ToList();
            
            // Добавляем больше информации для восстановления
            var payload = new
            {
                operationType = _operationType,
                boxes = boxes.Select(b => b.ToDictionary()),
                locationCode = CurrentLocation,
                orderNumber = OrderNumber,
                orderId = OrderId,
                isAutoSave = true,
                deviceId = Constants.DeviceId,
                timestamp = DateTime.UtcNow,
                boxCount = boxes.Count,
                totalQuantity = boxes.Sum(b => b.CurrentQuantity)
            };

            await new OfflineService().SaveTransaction(
                operationType: $"{_operationType}_autosave",
                barcode: string.Join(",", boxes.Select(b => b.Barcode)),
                payload: payload,
                deviceId: Constants.DeviceId
            );

            _scanCountSinceLastSave = 0;
            System.Diagnostics.Debug.WriteLine($"Автосохранение: {boxes.Count} коробок");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка автосохранения: {ex.Message}");
        }
    }

    protected override async Task ProcessBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;

        if (IsLocationBarcode(barcode))
        {
            await ProcessLocationScan(barcode);
            return;
        }

        await ProcessBoxScan(barcode);
    }

    protected virtual async Task ProcessLocationScan(string locationCode)
    {
        try
        {
            var location = await _dbHelper.GetLocationByCode(locationCode);

            if (location == null && IsOnline)
            {
                var synced = await _apiService.SyncLocations();
                if (synced)
                {
                    location = await _dbHelper.GetLocationByCode(locationCode);
                }
            }

            if (location == null)
            {
                SetError($"Локация '{locationCode}' не найдена");
                return;
            }

            if (location.is_active != 1)
            {
                SetError($"Локация '{locationCode}' неактивна", "⚠️", Colors.Orange);
                return;
            }

            CurrentLocation = locationCode;
            LastScannedBarcode = locationCode;
            SetStatus($"Локация: {locationCode} ({location.name})", "📍", Colors.Blue);

            // Обновляем локацию для всех коробок в сессии
            foreach (var box in ScannedBoxes)
            {
                box.LocationCode = locationCode;
            }
        }
        catch (Exception ex)
        {
            SetError($"Ошибка проверки локации: {ex.Message}");
        }
    }

    protected abstract Task ProcessBoxScan(string barcode);

    protected virtual async Task SaveBoxToCache(Box box, bool isLocal)
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
            location_code = box.LocationCode ?? CurrentLocation,
            status = box.Status,
            created_at = box.CreatedAt,
            updated_at = box.UpdatedAt,
            is_dirty = isLocal ? 1 : 0
        };
        await _dbHelper.SaveBox(boxCache);
    }

    protected virtual async Task UpdateLocalBox(Box updatedBox)
    {
        await SaveBoxToCache(updatedBox, isLocal: false);
    }

    protected virtual void AddBoxToList(Box box)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Add(box);
            ScannedCount = ScannedBoxes.Count;
            LastScannedBarcode = box.Barcode;
            _scanCountSinceLastSave++;

            IsBoxScanned = true;
            BoxInfoText = $"{box.ProductName} | {box.Quantity} шт. | {box.Grade} | №{box.BoxNumber}";
            BoxNumberDisplay = $"№{box.BoxNumber}";

            SetSuccess($"Коробка добавлена: #{box.BoxNumber}");
            Vibration.Vibrate(100);

            if (_scanCountSinceLastSave >= 5)
            {
                _ = AutoSaveIfNeeded();
            }
        });
    }

    protected virtual void ClearSession()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            ScannedCount = 0;
            LastScannedBarcode = null;
            IsBoxScanned = false;
            BoxInfoText = string.Empty;
            BoxNumberDisplay = string.Empty;
            _scanCountSinceLastSave = 0;
            SetStatus(string.Empty);
        });
    }

    [RelayCommand]
    public virtual void RemoveBox(object parameter)
    {
        if (parameter is Box box && ScannedBoxes.Contains(box))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Remove(box);
                ScannedCount = ScannedBoxes.Count;

                if (ScannedBoxes.Count == 0)
                {
                    ClearSession();
                }
            });
        }
    }

    [RelayCommand]
    public virtual async Task ConfirmOperation()
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

        _autoSaveTimer.Stop();
        await AutoSaveIfNeeded();

        // Должен быть переопределён в наследниках
        await Task.CompletedTask;
    }

    [RelayCommand]
    public virtual async Task CancelOperation()
    {
        _autoSaveTimer.Stop();

        if (ScannedBoxes.Count > 0)
        {
            var confirm = await Application.Current?.MainPage?.DisplayAlert(
                "Выход",
                $"Вы отсканировали {ScannedBoxes.Count} коробок. Выйти без сохранения?",
                "Да",
                "Нет"
            );

            if (confirm == false) return;
        }

        StopScanner();
        ClearSession();
        
        // 👇 ИСПОЛЬЗУЕМ ЗАЩИЩЕННЫЙ МЕТОД ВМЕСТО ПРЯМОГО ВЫЗОВА СОБЫТИЯ
        OnOperationCancelled();
    }

    [RelayCommand]
    public virtual async Task ShowBoxesList()
    {
        if (ScannedBoxes.Count == 0)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Список коробок",
                "Нет отсканированных коробок",
                "OK"
            );
            return;
        }

        var boxList = string.Join("\n", ScannedBoxes.Select((b, i) =>
            $"{i + 1}. #{b.BoxNumber} | {b.ProductName} | {b.Quantity} шт. | {b.Grade} | {(b.Status == BoxStatus.Active ? "✅" : "📭")}")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"Список коробок ({ScannedBoxes.Count})",
            boxList,
            "OK"
        );
    }

    [RelayCommand]
    private void ToggleBoxList()
    {
        IsBoxListExpanded = !IsBoxListExpanded;
    }

    protected Box CreateLocalBox(string ean13, int quantity, string grade, int boxNumber, string productName, BoxStatus status = BoxStatus.Draft)
    {
        return new Box
        {
            Id = $"local_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString().Substring(0, 8)}",
            Barcode = $"{ean13}-{quantity}-{GetGradeCode(grade)}-{boxNumber}",
            BoxNumber = boxNumber,
            ProductName = productName,
            ProductEan13 = ean13,
            Quantity = quantity > 0 ? quantity : 100,
            Grade = grade,
            LocationCode = CurrentLocation,
            Status = status, // ✅ Теперь можно задать любой статус
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public override void Dispose()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Dispose();
        base.Dispose();
    }
}