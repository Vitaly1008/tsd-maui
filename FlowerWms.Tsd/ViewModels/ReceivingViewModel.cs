using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.ViewModels;

public partial class ReceivingViewModel : ObservableObject, IDisposable
{
    private readonly IBarcodeService? _barcodeService;
    private readonly DatabaseHelper _dbHelper;
    private readonly ApiService _apiService;
    private readonly SyncQueueService _syncQueueService;
    private readonly SyncService _syncService;
    private bool _isScannerStarted;
    private bool _isInitialized;
    private bool _disposed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentLocation = "UNKNOWN";

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private string _scanStatusText = "Отсканируйте штрихкод коробки";

    [ObservableProperty]
    private string _boxInfoText = string.Empty;

    [ObservableProperty]
    private bool _isBoxScanned;

    [ObservableProperty]
    private Color _scanStatusColor = Colors.Gray;

    [ObservableProperty]
    private string _boxNumberDisplay = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _scanStatusIcon = "📷";

    [ObservableProperty]
    private int _scannedCount;

    [ObservableProperty]
    private bool _isBoxListExpanded = true; // по умолчанию развернут

    public ObservableCollection<Box> ScannedBoxes { get; } = new();

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public ReceivingViewModel(IBarcodeService? barcodeService = null)
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
        
        // Подписка на изменение статуса синхронизации
        _syncQueueService.PendingCountChanged += (s, count) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Обновляем UI если нужно
            });
        };
    }

    private void OnBarcodeScanned(string barcode)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ScanBoxCommand.ExecuteAsync(barcode);
        });
    }

    public void StartScanner()
    {
        if (_barcodeService == null || _isScannerStarted) return;
        
        try
        {
            _barcodeService.StartListening();
            _isScannerStarted = true;
            System.Diagnostics.Debug.WriteLine("✅ Сканер запущен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка запуска сканера: {ex.Message}");
        }
    }

    public void StopScanner()
    {
        if (_barcodeService == null || !_isScannerStarted) return;
        
        try
        {
            _barcodeService.StopListening();
            _isScannerStarted = false;
            System.Diagnostics.Debug.WriteLine("✅ Сканер остановлен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка остановки сканера: {ex.Message}");
        }
    }

    public async Task Initialize()
    {
        if (_isInitialized) return;
        
        try
        {
            IsOnline = await _syncService.CheckInternetManual();
            
            // ✅ Проверяем наличие локации UNKNOWN в локальной БД
            var unknownLocation = await _dbHelper.GetLocationByCode("UNKNOWN");
            if (unknownLocation == null)
            {
                // Если нет - создаем
                var location = new LocationCache
                {
                    location_id = Guid.NewGuid().ToString(),
                    code = "UNKNOWN",
                    name = "Неизвестная локация",
                    is_active = 1,
                    created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _dbHelper.SaveLocation(location);
                System.Diagnostics.Debug.WriteLine("✅ Создана локация UNKNOWN в локальной БД");
            }
            
            if (_barcodeService != null)
            {
                StartScanner();
            }
            
            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine("✅ Страница приемки инициализирована");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка инициализации: {ex.Message}");
        }
    }

    private (string ean13, int quantity, string grade, int boxNumber) ParseBarcode(string barcode)
    {
        var parts = barcode.Split('-');
        
        string ean13 = parts.Length > 0 ? parts[0] : "0000000000000";
        int quantity = parts.Length > 1 && int.TryParse(parts[1], out var q) ? q : 0;
        string grade = parts.Length > 2 ? GetGradeName(parts[2]) : "Unknown";
        int boxNumber = parts.Length > 3 && int.TryParse(parts[3], out var n) ? n : 0;

        return (ean13, quantity, grade, boxNumber);
    }

    private string GetGradeName(string gradeCode)
    {
        return gradeCode switch
        {
            "1" => "Premium",
            "2" => "Extra",
            "3" => "Standard",
            "5" => "Decorated",
            "9" => "Rejected",
            _ => gradeCode
        };
    }

    private async Task<string> GetProductName(string ean13)
    {
        try
        {
            var product = await _dbHelper.GetProductByEan13(ean13);
            if (product != null && !string.IsNullOrEmpty(product.name))
            {
                return product.name;
            }
            
            // Если не нашли в кэше - пробуем синхронизировать продукты
            if (IsOnline)
            {
                var synced = await _apiService.SyncProducts();
                if (synced)
                {
                    product = await _dbHelper.GetProductByEan13(ean13);
                    if (product != null && !string.IsNullOrEmpty(product.name))
                    {
                        return product.name;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения продукта: {ex.Message}");
        }
        
        return "Неизвестный продукт";
    }

    [RelayCommand]
    public async Task ScanBox(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;
        if (IsLoading) return;

        // ============================================================
        // ✅ ОПРЕДЕЛЯЕМ ТИП ШТРИХКОДА
        // ============================================================
        if (IsLocationBarcode(barcode))
        {
            await ScanLocation(barcode);
            return;
        }

        // Сбрасываем ошибку
        HasError = false;
        ErrorMessage = string.Empty;
        IsLoading = true;
        
        try
        {
            // ============================================================
            // ✅ ПРОВЕРКА НА ДУБЛИКАТ В ТЕКУЩЕЙ СЕССИИ
            // ============================================================
            if (ScannedBoxes.Any(b => b.Barcode == barcode))
            {
                HasError = true;
                ErrorMessage = "⚠️ Коробка уже отсканирована в этой сессии";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }
            
            // ============================================================
            // ✅ ПРОВЕРКА В ЛОКАЛЬНОЙ БАЗЕ (уже существует на складе)
            // ============================================================
            var existsInCache = await _dbHelper.IsBoxExistsByBarcode(barcode);
            if (existsInCache)
            {
                HasError = true;
                ErrorMessage = "⚠️ Коробка уже существует на складе!";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }
            
            // ============================================================
            // ✅ ПАРСИНГ ШТРИХКОДА
            // ============================================================
            var (ean13, quantity, grade, boxNumber) = ParseBarcode(barcode);
            
            // Проверка номера коробки
            if (boxNumber > 0)
            {
                var numberExists = await _dbHelper.IsBoxNumberExists(boxNumber);
                if (numberExists)
                {
                    HasError = true;
                    ErrorMessage = $"⚠️ Коробка №{boxNumber} уже существует!";
                    ScanStatusIcon = "❌";
                    ScanStatusColor = Colors.Red;
                    ScanStatusText = ErrorMessage;
                    Vibration.Vibrate(200);
                    return;
                }
            }
            
            // ============================================================
            // ✅ ПОЛУЧЕНИЕ ИНФОРМАЦИИ О ПРОДУКТЕ
            // ============================================================
            var productName = await GetProductName(ean13);
            
            // ============================================================
            // ✅ СОЗДАНИЕ КОРОБКИ
            // ============================================================
            var box = new Box
            {
                Id = Guid.NewGuid().ToString(),
                Barcode = barcode,
                BoxNumber = boxNumber,
                ProductName = productName,
                ProductEan13 = ean13,
                Quantity = quantity > 0 ? quantity : 100,
                Grade = grade,
                LocationCode = CurrentLocation,
                Status = 1 // Active
            };
            
            // Добавляем в список
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Add(box);
                ScannedCount = ScannedBoxes.Count;
                LastScannedBarcode = barcode;
                
                IsBoxScanned = true;
                HasError = false;
                ErrorMessage = string.Empty;
                ScanStatusIcon = "✅";
                ScanStatusColor = Colors.Green;
                ScanStatusText = $"✅ Отсканировано: {barcode}";
                
                BoxInfoText = $"{productName} | {box.Quantity} шт. | {grade} | №{boxNumber}";
                BoxNumberDisplay = $"№{boxNumber}";
            });
            
            Vibration.Vibrate(100);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"❌ Ошибка: {ex.Message}";
            ScanStatusIcon = "❌";
            ScanStatusColor = Colors.Red;
            ScanStatusText = ErrorMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ScanLocation(string locationCode)
    {
        if (string.IsNullOrEmpty(locationCode)) return;
        
        IsLoading = true;
        
        try
        {
            // 1. Проверяем в локальном кэше
            var location = await _dbHelper.GetLocationByCode(locationCode);
            
            // 2. Если нет в кэше и есть интернет - пробуем с сервера
            if (location == null && IsOnline)
            {
                var synced = await _apiService.SyncLocations();
                if (synced)
                {
                    location = await _dbHelper.GetLocationByCode(locationCode);
                }
            }
            
            // 3. Если локация не найдена - показываем ошибку
            if (location == null)
            {
                HasError = true;
                ErrorMessage = $"❌ Локация '{locationCode}' не найдена";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                CurrentLocation = "UNKNOWN";
                return;
            }
            
            // 4. Проверяем активна ли локация
            if (location.is_active != 1)
            {
                HasError = true;
                ErrorMessage = $"⚠️ Локация '{locationCode}' неактивна";
                ScanStatusIcon = "⚠️";
                ScanStatusColor = Colors.Orange;
                ScanStatusText = ErrorMessage;
                CurrentLocation = "UNKNOWN";
                return;
            }
            
            // 5. Успешно - устанавливаем локацию
            CurrentLocation = locationCode;
            LastScannedBarcode = locationCode;
            HasError = false;
            ErrorMessage = string.Empty;
            ScanStatusIcon = "📍";
            ScanStatusColor = Colors.Blue;
            ScanStatusText = $"📍 Локация: {locationCode} ({location.name})";
            
            // Обновляем локацию у всех отсканированных коробок
            foreach (var box in ScannedBoxes)
            {
                box.LocationCode = locationCode;
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ Локация установлена: {locationCode}");
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"❌ Ошибка проверки локации: {ex.Message}";
            ScanStatusIcon = "❌";
            ScanStatusColor = Colors.Red;
            ScanStatusText = ErrorMessage;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ConfirmOperation()
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
        
        try
        {
            // ============================================================
            // ✅ СОХРАНЕНИЕ В ЛОКАЛЬНУЮ БАЗУ (ВСЕГДА)
            // ============================================================
            
            // Сохраняем коробки в кэш
            foreach (var box in boxes)
            {
                var location = await _dbHelper.GetLocationByCode(CurrentLocation);
                var locationId = location?.location_id ?? "";

                var boxCache = new BoxCache
                {
                    barcode = box.Barcode, // ✅ теперь это PrimaryKey
                    box_id = box.Id,
                    box_number = box.BoxNumber,
                    grade = box.Grade,
                    initial_quantity = box.Quantity,
                    current_quantity = box.Quantity,
                    product_id = box.ProductId,
                    product_name = box.ProductName,
                    product_ean13 = box.ProductEan13,
                    location_code = box.LocationCode ?? CurrentLocation,
                    location_id = locationId, // ✅ добавляем ID локации
                    status = 1,
                    created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _dbHelper.SaveBox(boxCache);
            }
            
            // ============================================================
            // ✅ ДОБАВЛЕНИЕ В ОЧЕРЕДЬ СИНХРОНИЗАЦИИ
            // ============================================================
            var payload = new
            {
                boxes = boxes.Select(b => new
                {
                    id = b.Id,
                    barcode = b.Barcode,
                    boxNumber = b.BoxNumber,
                    productName = b.ProductName,
                    productEan13 = b.ProductEan13,
                    quantity = b.Quantity,
                    grade = b.Grade,
                    locationCode = b.LocationCode ?? CurrentLocation
                }),
                locationCode = CurrentLocation,
                operationType = "Receiving",
                timestamp = DateTime.UtcNow
            };
            
            var transactionId = await _syncQueueService.EnqueueAsync(
                operationType: "Receiving",
                barcode: string.Join(",", boxes.Select(b => b.Barcode)),
                payload: payload,
                deviceId: Constants.DeviceId
            );
            
            System.Diagnostics.Debug.WriteLine($"✅ Транзакция добавлена: {transactionId}");
            
            // ============================================================
            // ✅ ПОКАЗЫВАЕМ РЕЗУЛЬТАТ
            // ============================================================
            var hasInternet = await _syncService.CheckInternetManual();
            
            if (hasInternet)
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Успешно",
                    $"Принято {boxes.Count} коробок.\nДанные синхронизированы с сервером.",
                    "OK"
                );
            }
            else
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "📴 Офлайн-режим",
                    $"Принято {boxes.Count} коробок.\nДанные сохранены локально и будут синхронизированы автоматически при подключении к сети.",
                    "OK"
                );
            }
            
            // Очищаем сессию
            ClearSession();
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "❌ Ошибка",
                $"Не удалось сохранить операцию: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ClearSession()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            ScannedCount = 0;
            CurrentLocation = "UNKNOWN";
            LastScannedBarcode = null;
            IsBoxScanned = false;
            ScanStatusText = "Отсканируйте штрихкод коробки";
            ScanStatusColor = Colors.Gray;
            BoxInfoText = string.Empty;
            BoxNumberDisplay = string.Empty;
            ScanStatusIcon = "📷";
        });
    }

    [RelayCommand]
    public async Task CancelOperation()
    {
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
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveBox(object parameter)
    {
        if (parameter is Box box && ScannedBoxes.Contains(box))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Remove(box);
                ScannedCount = ScannedBoxes.Count;
                
                if (ScannedBoxes.Count == 0)
                {
                    IsBoxScanned = false;
                    ScanStatusText = "Отсканируйте штрихкод коробки";
                    ScanStatusColor = Colors.Gray;
                    BoxInfoText = string.Empty;
                    LastScannedBarcode = null;
                }
            });
        }
    }

    [RelayCommand]
    public async Task ShowLocationInput()
    {
        var result = await Application.Current?.MainPage?.DisplayPromptAsync(
            "Введите код локации",
            "Например: A-01, B-02-03",
            "Подтвердить",
            "Отмена",
            CurrentLocation
        );

        if (!string.IsNullOrEmpty(result))
        {
            ScanLocation(result);
        }
    }

    [RelayCommand]
    public async Task ShowBoxesList()
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
            $"{i + 1}. #{b.BoxNumber} | {b.ProductName} | {b.Quantity} шт. | {b.Grade}")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"📦 Список коробок ({ScannedBoxes.Count})",
            boxList,
            "OK"
        );
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

    [RelayCommand]
    private void ToggleBoxList()
    {
        IsBoxListExpanded = !IsBoxListExpanded;
    }

    /// <summary>
    /// Определяет, является ли штрихкод локацией или коробкой
    /// </summary>
    private bool IsLocationBarcode(string barcode)
    {
        // Проверяем, что штрихкод не содержит 13 цифр подряд (EAN-13)
        var hasEan13 = System.Text.RegularExpressions.Regex.IsMatch(barcode, @"^\d{13}");
        if (hasEan13) return false;
        
        // Проверяем формат коробки: EAN13-Quantity-Grade-BoxNumber
        var parts = barcode.Split('-');
        if (parts.Length == 4)
        {
            // Проверяем, что первая часть - 13 цифр (EAN-13)
            if (parts[0].Length == 13 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^\d{13}"))
            {
                // Проверяем, что последняя часть - число (номер коробки)
                if (int.TryParse(parts[3], out _))
                {
                    return false; // это коробка
                }
            }
        }
        
        // Проверяем, может это просто номер коробки (только цифры)
        if (int.TryParse(barcode, out _))
        {
            return false; // это коробка
        }
        
        // Если не похоже на коробку - считаем локацией
        return true;
    }
}