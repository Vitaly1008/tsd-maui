using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для страницы инвентаризации
public partial class InventoryViewModel : ObservableObject, IDisposable
{
    private readonly IBarcodeService? _barcodeService;
    private readonly DatabaseHelper _dbHelper;
    private readonly ApiService _apiService;
    private readonly SyncQueueService _syncQueueService;
    private readonly SyncService _syncService;
    private bool _isScannerStarted;
    private bool _isInitialized;
    private bool _disposed;
    private Box? _currentSelectedBox;

    // Основные свойства
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentLocation = string.Empty;

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private string _scanStatusText = "Сканируйте локацию или коробку";

    [ObservableProperty]
    private string _scanStatusIcon = "📷";

    [ObservableProperty]
    private Color _scanStatusColor = Colors.Gray;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // Режимы
    [ObservableProperty]
    private bool _isLocationMode = true;

    [ObservableProperty]
    private string _modeText = "Режим: просмотр локации";

    // Данные локации
    [ObservableProperty]
    private string _locationInfo = "Сканируйте локацию для просмотра коробок";

    [ObservableProperty]
    private int _totalBoxesInLocation;

    [ObservableProperty]
    private int _totalQuantityInLocation;

    [ObservableProperty]
    private ObservableCollection<Box> _locationBoxes = new();

    // Данные для перемещения
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

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public InventoryViewModel(IBarcodeService? barcodeService = null)
    {
        _barcodeService = barcodeService;
        _dbHelper = new DatabaseHelper();
        _apiService = new ApiService();
        _syncQueueService = new SyncQueueService();
        _syncService = new SyncService();
        
        if (_barcodeService != null)
        {
            _barcodeService.OnBarcodeScanned += OnBarcodeScanned;
        }

        LocationBoxes = new ObservableCollection<Box>();
    }

    // Преобразует BoxCache в Box
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
            Status = cached.status
        };
    }

    // Преобразует Box в BoxCache
    private static BoxCache BoxToBoxCache(Box box, string locationCode)
    {
        return new BoxCache
        {
            box_id = box.Id,
            barcode = box.Barcode,
            box_number = box.BoxNumber,
            grade = box.Grade,
            initial_quantity = box.InitialQuantity,
            current_quantity = box.CurrentQuantity,
            product_id = box.ProductId,
            product_name = box.ProductName,
            product_ean13 = box.ProductEan13,
            location_code = locationCode,
            status = box.Status,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private void OnBarcodeScanned(string barcode)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ScanBarcodeCommand.ExecuteAsync(barcode);
        });
    }

    // Запускает сканер
    public void StartScanner()
    {
        if (_barcodeService == null || _isScannerStarted) return;
        
        try
        {
            _barcodeService.StartListening();
            _isScannerStarted = true;
            System.Diagnostics.Debug.WriteLine("Сканер запущен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка запуска сканера: {ex.Message}");
        }
    }

    // Останавливает сканер
    public void StopScanner()
    {
        if (_barcodeService == null || !_isScannerStarted) return;
        
        try
        {
            _barcodeService.StopListening();
            _isScannerStarted = false;
            System.Diagnostics.Debug.WriteLine("Сканер остановлен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка остановки сканера: {ex.Message}");
        }
    }

    // Инициализирует ViewModel
    public async Task Initialize()
    {
        if (_isInitialized) return;
        
        try
        {
            IsOnline = await _syncService.CheckInternetManual();
            
            if (_barcodeService != null)
            {
                StartScanner();
            }
            
            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine("Страница инвентаризации инициализирована");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации: {ex.Message}");
        }
    }

    // Обрабатывает сканирование штрихкода
    [RelayCommand]
    public async Task ScanBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;
        if (IsLoading) return;

        HasError = false;
        ErrorMessage = string.Empty;
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
            HasError = true;
            ErrorMessage = $"Ошибка: {ex.Message}";
            ScanStatusIcon = "❌";
            ScanStatusColor = Colors.Red;
            ScanStatusText = ErrorMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Определяет, является ли штрихкод локацией
    private bool IsLocationBarcode(string barcode)
    {
        var hasEan13 = System.Text.RegularExpressions.Regex.IsMatch(barcode, @"^\d{13}");
        if (hasEan13) return false;
        
        var parts = barcode.Split('-');
        if (parts.Length == 4 && parts[0].Length == 13)
        {
            return false;
        }
        
        return true;
    }

    // Обрабатывает сканирование локации
    private async Task ProcessLocationScan(string locationCode)
    {
        CurrentLocation = locationCode;
        ScanStatusIcon = "📍";
        ScanStatusColor = Colors.Blue;
        ScanStatusText = $"Локация: {locationCode}";

        var cachedBoxes = await _dbHelper.GetBoxesByLocation(locationCode);
        var boxes = cachedBoxes.Select(BoxCacheToBox).ToList();
        
        if (boxes.Count == 0 && IsOnline)
        {
            var serverBoxes = await _apiService.GetBoxesByLocation(locationCode);
            if (serverBoxes.Count > 0)
            {
                foreach (var box in serverBoxes)
                {
                    await _dbHelper.SaveBox(BoxToBoxCache(box, locationCode));
                }
                boxes = serverBoxes;
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
            TotalQuantityInLocation = boxes.Sum(b => b.CurrentQuantity);
            
            LocationInfo = $"Локация: {locationCode}\n" +
                        $"Коробок: {TotalBoxesInLocation}\n" +
                        $"Количество: {TotalQuantityInLocation} шт.";
        });

        System.Diagnostics.Debug.WriteLine($"Найдено {boxes.Count} коробок в локации {locationCode}");
    }

    // Обрабатывает сканирование коробки
    private async Task ProcessBoxScan(string barcode)
    {
        var box = await FindBoxByBarcode(barcode);
        
        if (box == null)
        {
            HasError = true;
            ErrorMessage = "Коробка не найдена на складе";
            ScanStatusIcon = "❌";
            ScanStatusColor = Colors.Red;
            ScanStatusText = ErrorMessage;
            Vibration.Vibrate(200);
            return;
        }

        IsLocationMode = false;
        IsMoveMode = true;
        SelectedBox = box;
        _currentSelectedBox = box;
        
        SelectedBoxInfo = $"Коробка #{box.BoxNumber}\n" +
                         $"Продукт: {box.ProductName}\n" +
                         $"Количество: {box.CurrentQuantity} шт.\n" +
                         $"Текущая локация: {box.LocationCode ?? "Не указана"}\n" +
                         $"Сорт: {box.Grade}";
        
        ScanStatusIcon = "📦";
        ScanStatusColor = Colors.Green;
        ScanStatusText = $"Коробка #{box.BoxNumber} выбрана для перемещения";
        
        ModeText = "Режим: перемещение коробки";
        MoveButtonText = "Указать новую локацию";
        IsMoveButtonEnabled = true;
        
        TargetLocation = string.Empty;
        
        Vibration.Vibrate(100);
    }

    // Находит коробку по штрихкоду
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
                    await _dbHelper.SaveBox(BoxToBoxCache(serverBox, serverBox.LocationCode ?? "UNKNOWN"));
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

    // Устанавливает целевую локацию для перемещения
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

    // Выполняет перемещение коробки
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
            SelectedBoxInfo = $"Коробка #{SelectedBox.BoxNumber}\n" +
                             $"Продукт: {SelectedBox.ProductName}\n" +
                             $"Количество: {SelectedBox.CurrentQuantity} шт.\n" +
                             $"Перемещена в: {TargetLocation}\n" +
                             $"Сорт: {SelectedBox.Grade}";

            if (!string.IsNullOrEmpty(CurrentLocation))
            {
                await ProcessLocationScan(CurrentLocation);
            }

            var hasInternet = await _syncService.CheckInternetManual();
            var message = hasInternet 
                ? $"Коробка #{SelectedBox.BoxNumber} перемещена в {TargetLocation}\nДанные синхронизированы"
                : $"Коробка #{SelectedBox.BoxNumber} перемещена в {TargetLocation}\nДанные сохранены локально";
            
            await Application.Current?.MainPage?.DisplayAlert(
                hasInternet ? "Успешно" : "Офлайн-режим",
                message,
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

    // Возвращает в режим просмотра локации
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
            ScanStatusText = "Сканируйте локацию для просмотра коробок";
            ScanStatusIcon = "📷";
            ScanStatusColor = Colors.Gray;
        });
    }

    // Отменяет операцию
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
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    // Обновляет текущую локацию
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

    // Отменяет перемещение
    [RelayCommand]
    public void CancelMove()
    {
        if (IsMoveMode)
        {
            ResetToLocationMode();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        StopScanner();
        if (_barcodeService != null)
        {
            _barcodeService.OnBarcodeScanned -= OnBarcodeScanned;
        }
        _syncQueueService.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}