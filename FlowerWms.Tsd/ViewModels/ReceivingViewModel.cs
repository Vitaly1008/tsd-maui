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
    private bool _isBoxListExpanded = true;

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
            
            // Проверяем наличие локации UNKNOWN в локальной БД
            var unknownLocation = await _dbHelper.GetLocationByCode("UNKNOWN");
            if (unknownLocation == null)
            {
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
        string grade = parts.Length > 2 ? GetGradeName(parts[2]) : "Premium";
        int boxNumber = parts.Length > 3 && int.TryParse(parts[3], out var n) ? n : 0;

        return (ean13, quantity, grade, boxNumber);
    }

    private string GetGradeName(string gradeCode)
    {
        return gradeCode switch
        {
            "1" => "First",
            "2" => "Second",
            "3" => "Decorated",
            "5" => "Rejected",
            "9" => "Premium",
            _ => gradeCode
        };
    }

    private string GetGradeCode(string gradeName)
    {
        return gradeName switch
        {
            "First" => "1",
            "Second" => "2",
            "Decorated" => "3",
            "Rejected" => "5",
            "Premium" => "9",
            _ => gradeName
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

        // Проверяем, не является ли штрихкод локацией
        if (IsLocationBarcode(barcode))
        {
            await ScanLocation(barcode);
            return;
        }

        HasError = false;
        ErrorMessage = string.Empty;
        IsLoading = true;
        
        try
        {
            // Проверка на дубликат в текущей сессии
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

            // Парсим штрихкод
            var (ean13, quantity, grade, boxNumber) = ParseBarcode(barcode);

            // ============================================================
            // ✅ ПРОВЕРЯЕМ, СУЩЕСТВУЕТ ЛИ УЖЕ КОРОБКА С ТАКИМ НОМЕРОМ
            // ============================================================
            
            // 1. Проверяем в локальном кэше (все статусы)
            var existsInCache = await _dbHelper.IsBoxExistsByBarcode(barcode);
            if (existsInCache)
            {
                HasError = true;
                ErrorMessage = $"⚠️ Коробка №{boxNumber} уже существует на складе!";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }

            // 2. Если есть интернет — проверяем на сервере
            if (IsOnline)
            {
                try
                {
                    var checkResult = await _apiService.CheckBoxNumber(boxNumber);
                    if (checkResult.TryGetValue("success", out var success) && success is bool s && s)
                    {
                        var data = checkResult.GetValueOrDefault("data") as Dictionary<string, object>;
                        if (data != null && data.GetValueOrDefault("isFree") is bool isFree && !isFree)
                        {
                            HasError = true;
                            ErrorMessage = $"⚠️ Номер {boxNumber} уже занят на сервере!";
                            ScanStatusIcon = "❌";
                            ScanStatusColor = Colors.Red;
                            ScanStatusText = ErrorMessage;
                            Vibration.Vibrate(200);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка проверки номера: {ex.Message}");
                }
            }

            // Получаем информацию о продукте
            var productName = await GetProductName(ean13);

            // ============================================================
            // ✅ СОЗДАЕМ DRAFT-КОРОБКУ НА СЕРВЕРЕ
            // ============================================================
            Box box;
            bool isDraftCreated = false;

            if (IsOnline)
            {
                try
                {
                    var gradeCode = GetGradeCode(grade);
                    var draftResult = await _apiService.CreateDraftBox(
                        ean13: ean13,
                        quantity: quantity > 0 ? quantity : 100,
                        grade: gradeCode,
                        boxNumber: boxNumber
                    );

                    if (draftResult.TryGetValue("success", out var draftSuccess) && draftSuccess is bool ds && ds)
                    {
                        var data = draftResult.GetValueOrDefault("data") as Dictionary<string, object>;
                        if (data != null)
                        {
                            box = Box.FromJson(data);
                            box.LocationCode = CurrentLocation;
                            isDraftCreated = true;
                            
                            System.Diagnostics.Debug.WriteLine($"✅ Draft-коробка создана: #{box.BoxNumber}");
                        }
                        else
                        {
                            throw new Exception("Не удалось получить данные коробки");
                        }
                    }
                    else
                    {
                        var errorMsg = draftResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                        throw new Exception(errorMsg);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Ошибка создания Draft: {ex.Message}");
                    
                    // Если не удалось создать на сервере — создаем локально
                    box = await CreateLocalBox(ean13, quantity, grade, boxNumber, productName);
                    isDraftCreated = false;
                    
                    await Application.Current?.MainPage?.DisplayAlert(
                        "⚠️ Внимание",
                        $"Коробка создана локально. Будет синхронизирована позже.\n{ex.Message}",
                        "OK"
                    );
                }
            }
            else
            {
                // Офлайн-режим — создаем локально
                box = await CreateLocalBox(ean13, quantity, grade, boxNumber, productName);
                isDraftCreated = false;
            }

            // Добавляем в список
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Add(box);
                ScannedCount = ScannedBoxes.Count;
                LastScannedBarcode = barcode;
                
                IsBoxScanned = true;
                HasError = false;
                ErrorMessage = string.Empty;
                ScanStatusIcon = isDraftCreated ? "✅" : "📴";
                ScanStatusColor = isDraftCreated ? Colors.Green : Colors.Orange;
                ScanStatusText = isDraftCreated 
                    ? $"✅ Создан Draft: {barcode}" 
                    : $"📴 Локально: {barcode}";
                
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

    private async Task<Box> CreateLocalBox(string ean13, int quantity, string grade, int boxNumber, string productName)
    {
        return new Box
        {
            Id = $"local_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Barcode = $"{ean13}-{quantity}-{GetGradeCode(grade)}-{boxNumber}",
            BoxNumber = boxNumber,
            ProductName = productName,
            ProductEan13 = ean13,
            Quantity = quantity > 0 ? quantity : 100,
            Grade = grade,
            LocationCode = CurrentLocation,
            Status = 0 // Draft!
        };
    }

    [RelayCommand]
    public async Task ScanLocation(string locationCode)
    {
        if (string.IsNullOrEmpty(locationCode)) return;
        
        IsLoading = true;
        
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
                HasError = true;
                ErrorMessage = $"❌ Локация '{locationCode}' не найдена";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                CurrentLocation = "UNKNOWN";
                return;
            }
            
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
            
            CurrentLocation = locationCode;
            LastScannedBarcode = locationCode;
            HasError = false;
            ErrorMessage = string.Empty;
            ScanStatusIcon = "📍";
            ScanStatusColor = Colors.Blue;
            ScanStatusText = $"📍 Локация: {locationCode} ({location.name})";
            
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
        int activatedCount = 0;
        int localCount = 0;
        
        try
        {
            // ============================================================
            // ✅ АКТИВАЦИЯ КОРОБОК НА СЕРВЕРЕ
            // ============================================================
            foreach (var box in boxes)
            {
                // Если коробка локальная (не Draft на сервере)
                if (box.Id.StartsWith("local_"))
                {
                    // Сохраняем в локальный кэш
                    var boxCache = new BoxCache
                    {
                        barcode = box.Barcode,
                        box_id = box.Id,
                        box_number = box.BoxNumber,
                        grade = box.Grade,
                        initial_quantity = box.Quantity,
                        current_quantity = box.Quantity,
                        product_id = box.ProductId,
                        product_name = box.ProductName,
                        product_ean13 = box.ProductEan13,
                        location_code = box.LocationCode ?? CurrentLocation,
                        status = 0, // Draft
                        created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    await _dbHelper.SaveBox(boxCache);
                    
                    // Добавляем в очередь синхронизации
                    var payload = new
                    {
                        boxes = new[]
                        {
                            new
                            {
                                id = box.Id,
                                barcode = box.Barcode,
                                boxNumber = box.BoxNumber,
                                productName = box.ProductName,
                                productEan13 = box.ProductEan13,
                                quantity = box.Quantity,
                                grade = GetGradeCode(box.Grade),
                                locationCode = box.LocationCode ?? CurrentLocation,
                                status = 0 // Draft
                            }
                        },
                        locationCode = CurrentLocation,
                        operationType = "Receiving",
                        timestamp = DateTime.UtcNow
                    };
                    
                    await _syncQueueService.EnqueueAsync(
                        operationType: "Receiving",
                        barcode: box.Barcode,
                        payload: payload,
                        deviceId: Constants.DeviceId
                    );
                    
                    localCount++;
                    continue;
                }

                // Если коробка создана как Draft на сервере
                if (IsOnline)
                {
                    try
                    {
                        var activateResult = await _apiService.ActivateBox(
                            box.Id,
                            comment: $"Приемка через ТСД, локация: {CurrentLocation}"
                        );

                        if (activateResult.TryGetValue("success", out var success) && success is bool s && s)
                        {
                            activatedCount++;
                            
                            // ✅ ПОЛУЧАЕМ ОБНОВЛЕННЫЕ ДАННЫЕ КОРОБКИ С СЕРВЕРА
                            // (чтобы получить правильный CreatedAt и UpdatedAt)
                            var updatedBox = await _apiService.GetBoxByBarcode(box.Barcode);
                            if (updatedBox != null)
                            {
                                box.CreatedAt = updatedBox.CreatedAt;
                                box.UpdatedAt = updatedBox.UpdatedAt;
                                box.Status = updatedBox.Status;
                                
                                System.Diagnostics.Debug.WriteLine($"✅ Коробка активирована: #{box.BoxNumber}, CreatedAt: {box.CreatedAt}");
                            }
                            
                            // Обновляем локальный кэш
                            var boxCache = new BoxCache
                            {
                                barcode = box.Barcode,
                                box_id = box.Id,
                                box_number = box.BoxNumber,
                                grade = box.Grade,
                                initial_quantity = box.Quantity,
                                current_quantity = box.Quantity,
                                product_id = box.ProductId,
                                product_name = box.ProductName,
                                product_ean13 = box.ProductEan13,
                                location_code = box.LocationCode ?? CurrentLocation,
                                status = 1, // Active
                                created_at = box.CreatedAt, // ✅ БЕРЕМ С СЕРВЕРА
                                updated_at = box.UpdatedAt  // ✅ БЕРЕМ С СЕРВЕРА
                            };
                            await _dbHelper.SaveBox(boxCache);
                            
                            System.Diagnostics.Debug.WriteLine($"✅ Коробка активирована: #{box.BoxNumber}");
                        }
                        else
                        {
                            var errorMsg = activateResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                            throw new Exception(errorMsg);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Ошибка активации: {ex.Message}");
                        // Если не удалось активировать — сохраняем локально
                        var boxCache = new BoxCache
                        {
                            barcode = box.Barcode,
                            box_id = box.Id,
                            box_number = box.BoxNumber,
                            grade = box.Grade,
                            initial_quantity = box.Quantity,
                            current_quantity = box.Quantity,
                            product_id = box.ProductId,
                            product_name = box.ProductName,
                            product_ean13 = box.ProductEan13,
                            location_code = box.LocationCode ?? CurrentLocation,
                            status = 0, // Draft
                            created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                        await _dbHelper.SaveBox(boxCache);
                        
                        // Добавляем в очередь синхронизации
                        var payload = new
                        {
                            boxes = new[]
                            {
                                new
                                {
                                    id = box.Id,
                                    barcode = box.Barcode,
                                    boxNumber = box.BoxNumber,
                                    productName = box.ProductName,
                                    productEan13 = box.ProductEan13,
                                    quantity = box.Quantity,
                                    grade = GetGradeCode(box.Grade),
                                    locationCode = box.LocationCode ?? CurrentLocation,
                                    status = 0
                                }
                            },
                            locationCode = CurrentLocation,
                            operationType = "Receiving",
                            timestamp = DateTime.UtcNow
                        };
                        
                        await _syncQueueService.EnqueueAsync(
                            operationType: "Receiving",
                            barcode: box.Barcode,
                            payload: payload,
                            deviceId: Constants.DeviceId
                        );
                        
                        localCount++;
                    }
                }
                else
                {
                    // Офлайн — сохраняем локально
                    var boxCache = new BoxCache
                    {
                        barcode = box.Barcode,
                        box_id = box.Id,
                        box_number = box.BoxNumber,
                        grade = box.Grade,
                        initial_quantity = box.Quantity,
                        current_quantity = box.Quantity,
                        product_id = box.ProductId,
                        product_name = box.ProductName,
                        product_ean13 = box.ProductEan13,
                        location_code = box.LocationCode ?? CurrentLocation,
                        status = 0,
                        created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    await _dbHelper.SaveBox(boxCache);
                    
                    var payload = new
                    {
                        boxes = new[]
                        {
                            new
                            {
                                id = box.Id,
                                barcode = box.Barcode,
                                boxNumber = box.BoxNumber,
                                productName = box.ProductName,
                                productEan13 = box.ProductEan13,
                                quantity = box.Quantity,
                                grade = GetGradeCode(box.Grade),
                                locationCode = box.LocationCode ?? CurrentLocation,
                                status = 0
                            }
                        },
                        locationCode = CurrentLocation,
                        operationType = "Receiving",
                        timestamp = DateTime.UtcNow
                    };
                    
                    await _syncQueueService.EnqueueAsync(
                        operationType: "Receiving",
                        barcode: box.Barcode,
                        payload: payload,
                        deviceId: Constants.DeviceId
                    );
                    
                    localCount++;
                }
            }

            // ============================================================
            // ✅ ПОКАЗЫВАЕМ РЕЗУЛЬТАТ
            // ============================================================
            var hasInternet = await _syncService.CheckInternetManual();
            var totalCount = activatedCount + localCount;
            
            var message = $"Принято {totalCount} коробок.\n";
            if (activatedCount > 0)
                message += $"✅ Активировано: {activatedCount}\n";
            if (localCount > 0)
                message += $"📴 Сохранено локально: {localCount}\n";
            if (hasInternet && localCount == 0)
                message += "Данные синхронизированы с сервером.";
            else if (hasInternet && localCount > 0)
                message += "Часть данных будет синхронизирована позже.";
            else
                message += "Данные сохранены локально и будут синхронизированы при подключении.";

            await Application.Current?.MainPage?.DisplayAlert(
                localCount == 0 ? "✅ Успешно" : "⚠️ Внимание",
                message,
                "OK"
            );
            
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
        // Если есть Draft-коробки — пробуем их удалить
        var draftBoxes = ScannedBoxes.Where(b => !b.Id.StartsWith("local_")).ToList();
        if (draftBoxes.Any() && IsOnline)
        {
            try
            {
                foreach (var box in draftBoxes)
                {
                    await _apiService.DeleteDraftBox(box.Id);
                }
                System.Diagnostics.Debug.WriteLine($"✅ Удалено {draftBoxes.Count} Draft-коробок");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка удаления Draft: {ex.Message}");
            }
        }

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
            // Если это Draft-коробка — удаляем с сервера
            if (!box.Id.StartsWith("local_") && IsOnline)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _apiService.DeleteDraftBox(box.Id);
                        System.Diagnostics.Debug.WriteLine($"✅ Draft удален: #{box.BoxNumber}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка удаления Draft: {ex.Message}");
                    }
                });
            }
            
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
            await ScanLocation(result);
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
            $"{i + 1}. #{b.BoxNumber} | {b.ProductName} | {b.Quantity} шт. | {b.Grade} | {(b.Id.StartsWith("local_") ? "📴" : "✅")}")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"📦 Список коробок ({ScannedBoxes.Count})",
            boxList,
            "OK"
        );
    }

    private bool IsLocationBarcode(string barcode)
    {
        var hasEan13 = System.Text.RegularExpressions.Regex.IsMatch(barcode, @"^\d{13}");
        if (hasEan13) return false;
        
        var parts = barcode.Split('-');
        if (parts.Length == 4)
        {
            if (parts[0].Length == 13 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^\d{13}"))
            {
                if (int.TryParse(parts[3], out _))
                {
                    return false;
                }
            }
        }
        
        if (int.TryParse(barcode, out _))
        {
            return false;
        }
        
        return true;
    }

    [RelayCommand]
    private void ToggleBoxList()
    {
        IsBoxListExpanded = !IsBoxListExpanded;
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