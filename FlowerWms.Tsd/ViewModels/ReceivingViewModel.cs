using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;

namespace FlowerWms.Tsd.ViewModels;

public partial class ReceivingViewModel : ObservableObject
{
    private readonly OperationViewModel _operationViewModel;
    private readonly IBarcodeService? _barcodeService;
    private readonly DatabaseHelper _dbHelper;
    private readonly ApiService _apiService;
    private bool _isScannerStarted;
    private bool _isInitialized;

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

    public ObservableCollection<Box> ScannedBoxes => _operationViewModel.ScannedBoxes;

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public ReceivingViewModel(IBarcodeService? barcodeService = null)
    {
        _operationViewModel = new OperationViewModel("Receiving");
        _barcodeService = barcodeService;
        _dbHelper = new DatabaseHelper();
        _apiService = new ApiService();
        
        if (_barcodeService != null)
        {
            _barcodeService.OnBarcodeScanned += OnBarcodeScanned;
        }
        
        _operationViewModel.OperationCompleted += (s, e) => OperationCompleted?.Invoke(this, EventArgs.Empty);
        _operationViewModel.OperationCancelled += (s, e) => OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnBarcodeScanned(string barcode)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScanBox(barcode);
        });
    }

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

    public async Task Initialize()
    {
        if (_isInitialized) return;
        
        try
        {
            // Проверяем интернет в фоне
            _ = Task.Run(async () =>
            {
                try
                {
                    var syncService = new SyncService();
                    var isOnline = await syncService.CheckInternetManual();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        IsOnline = isOnline;
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка проверки интернета: {ex.Message}");
                }
            });
            
            // Сразу запускаем сканер
            if (_barcodeService != null)
            {
                StartScanner();
            }
            
            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine("Страница приемки инициализирована");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации: {ex.Message}");
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
            "1" => "Extra",
            "2" => "Premium",
            "3" => "Standard",
            "4" => "Economy",
            "5" => "Business",
            "6" => "Luxury",
            "7" => "Elite",
            "8" => "Select",
            "9" => "Premium",
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка получения продукта: {ex.Message}");
        }
        
        return "Неизвестный продукт";
    }

    private async Task<bool> SyncProductsIfNeeded()
    {
        try
        {
            var syncService = new SyncService();
            var isOnline = await syncService.CheckInternetManual();
            
            if (!isOnline) return false;
            
            return await _apiService.SyncProducts();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации продуктов: {ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    public async Task ScanBox(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;

        // ✅ Сбрасываем ошибку
        HasError = false;
        ErrorMessage = string.Empty;
        IsLoading = true;
        
        try
        {
            var (ean13, quantity, grade, boxNumber) = ParseBarcode(barcode);
            
            // ============================================================
            // ✅ ВСЕ ПРОВЕРКИ НА ДУБЛИКАТЫ
            // ============================================================
            
            // Проверка в текущей сессии
            if (ScannedBoxes.Any(b => b.Barcode == barcode))
            {
                HasError = true;
                ErrorMessage = "⚠️ Коробка уже отсканирована в этой сессии";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                
                // Вибрируем для предупреждения
                Vibration.Vibrate(200);
                return;
            }
            
            // Проверка в кэше БД
            var dbHelper = new DatabaseHelper();
            var existsInCache = await dbHelper.IsBoxExistsByBarcode(barcode);
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
            
            // Проверка номера коробки
            if (boxNumber > 0)
            {
                var numberExists = await dbHelper.IsBoxNumberExists(boxNumber);
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
            // ✅ УСПЕШНОЕ СКАНИРОВАНИЕ
            // ============================================================
            
            // Получаем информацию о продукте
            var productName = await GetProductName(ean13);
            if (productName == "Неизвестный продукт")
            {
                var synced = await SyncProductsIfNeeded();
                if (synced)
                {
                    productName = await GetProductName(ean13);
                }
            }
            
            // Создаем коробку
            var box = new Box
            {
                Id = $"local_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                Barcode = barcode,
                BoxNumber = boxNumber,
                ProductName = productName,
                ProductEan13 = ean13,
                Quantity = quantity,
                Grade = grade,
                LocationCode = CurrentLocation,
                Status = 1
            };
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Add(box);
                LastScannedBarcode = barcode;
                
                IsBoxScanned = true;
                HasError = false;
                ErrorMessage = string.Empty;
                ScanStatusIcon = "✅";
                ScanStatusColor = Colors.Green;
                ScanStatusText = $"✅ Отсканировано: {barcode}";
                
                BoxInfoText = $"{productName} | {quantity} шт. | {grade} | №{boxNumber}";
                BoxNumberDisplay = $"№{boxNumber}";
            });
            
            // Вибрируем для подтверждения
            Vibration.Vibrate(100);
            
            await _operationViewModel.ScanBox(barcode);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"❌ Ошибка: {ex.Message}";
            ScanStatusIcon = "❌";
            ScanStatusColor = Colors.Red;
            ScanStatusText = ErrorMessage;
            
            await Application.Current?.MainPage?.DisplayAlert(
                "❌ Ошибка",
                $"Не удалось обработать штрихкод: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ScanLocation(string locationCode)
    {
        _operationViewModel.ScanLocation(locationCode);
        CurrentLocation = locationCode;
        LastScannedBarcode = locationCode;
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
            if (IsOnline)
            {
                bool allSuccess = true;
                string lastError = "";

                foreach (var box in boxes)
                {
                    var result = await _apiService.SyncOfflineTransaction(
                        transactionId: $"online_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}",
                        operationType: "Receiving",
                        barcode: box.Barcode,
                        payload: new Dictionary<string, object>
                        {
                            ["boxId"] = box.Id,
                            ["boxNumber"] = box.BoxNumber,
                            ["productName"] = box.ProductName,
                            ["productEan13"] = box.ProductEan13,
                            ["quantity"] = box.Quantity,
                            ["locationCode"] = CurrentLocation,
                            ["grade"] = box.Grade,
                            ["operationType"] = "Receiving",
                            ["status"] = 1 // ✅ int, 1 = Active
                        }
                    );
                    
                    if (result.TryGetValue("success", out var successObj) && successObj is bool success && !success)
                    {
                        allSuccess = false;
                        lastError = result.ContainsKey("message") 
                            ? result["message"]?.ToString() ?? "Неизвестная ошибка"
                            : "Неизвестная ошибка";
                        break;
                    }
                }

                if (allSuccess)
                {
                    await Application.Current?.MainPage?.DisplayAlert(
                        "Успешно",
                        $"Принято {boxes.Count} коробок",
                        "OK"
                    );
                    
                    ClearSession();
                    OperationCompleted?.Invoke(this, EventArgs.Empty);
                    return;
                }
                else
                {
                    await SaveOfflineTransaction(boxes);
                    await Application.Current?.MainPage?.DisplayAlert(
                        "Внимание",
                        $"Операция сохранена офлайн. Будет синхронизирована позже.\nОшибка: {lastError}",
                        "OK"
                    );
                }
            }
            else
            {
                await SaveOfflineTransaction(boxes);
                await Application.Current?.MainPage?.DisplayAlert(
                    "Офлайн-режим",
                    $"Операция сохранена. Будет синхронизирована при подключении к сети.",
                    "OK"
                );
            }
            
            ClearSession();
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            try
            {
                await SaveOfflineTransaction(boxes);
                await Application.Current?.MainPage?.DisplayAlert(
                    "Внимание",
                    $"Операция сохранена офлайн. Будет синхронизирована позже.\nОшибка: {ex.Message}",
                    "OK"
                );
                ClearSession();
                OperationCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception innerEx)
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "Ошибка",
                    $"Не удалось сохранить операцию: {innerEx.Message}",
                    "OK"
                );
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveOfflineTransaction(List<Box> boxes)
    {
        var offlineService = new OfflineService();
        var dbHelper = new DatabaseHelper();
        
        foreach (var box in boxes)
        {
            // ✅ Сохраняем в кэш коробок
            var boxCache = new BoxCache
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
                location_code = box.LocationCode ?? CurrentLocation,
                status = 1, // ✅ ЯВНО УСТАНАВЛИВАЕМ 1
                created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            await dbHelper.SaveBox(boxCache);
            
            // Сохраняем транзакцию
            await offlineService.SaveTransaction(
                operationType: "Receiving",
                barcode: box.Barcode,
                payload: new
                {
                    boxId = box.Id,
                    boxNumber = box.BoxNumber,
                    productName = box.ProductName,
                    productEan13 = box.ProductEan13,
                    quantity = box.Quantity,
                    locationCode = CurrentLocation,
                    grade = box.Grade,
                    operationType = "Receiving",
                    status = 1 // ✅ ЯВНО УСТАНАВЛИВАЕМ 1
                },
                deviceId: Constants.DeviceId
            );
        }
    }
    private void ClearSession()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            CurrentLocation = "UNKNOWN";
            LastScannedBarcode = null;
            IsBoxScanned = false;
            ScanStatusText = "Отсканируйте штрихкод коробки";
            ScanStatusColor = Colors.Gray;
            BoxInfoText = string.Empty;
            BoxNumberDisplay = string.Empty;
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
        
        await _operationViewModel.CancelOperation();
        StopScanner();
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void RemoveBox(int index)
    {
        if (index >= 0 && index < ScannedBoxes.Count)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.RemoveAt(index);
                
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
            $"{i + 1}. {b.Barcode} | {b.ProductName} | {b.Quantity} шт. | {b.Grade}")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"Список коробок ({ScannedBoxes.Count})",
            boxList,
            "OK"
        );
    }

    public void Dispose()
    {
        StopScanner();
        if (_barcodeService != null)
        {
            _barcodeService.OnBarcodeScanned -= OnBarcodeScanned;
        }
    }
}