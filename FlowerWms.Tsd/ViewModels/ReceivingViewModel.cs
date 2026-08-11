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
    private bool _isBoxListExpanded = false;

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
        
        // Проверяем корректность формата
        if (parts.Length != 4)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Некорректный формат штрихкода: {barcode}");
            return ("", 0, "", 0);
        }

        // Проверяем EAN13
        if (!IsValidEan13(parts[0]))
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Некорректный EAN13: {parts[0]}");
            return ("", 0, "", 0);
        }

        string ean13 = parts[0];
        int quantity = int.TryParse(parts[1], out var q) ? q : 0;
        string grade = parts.Length > 2 ? GetGradeName(parts[2]) : "Premium";
        int boxNumber = parts.Length > 3 && int.TryParse(parts[3], out var n) ? n : 0;

        // Проверяем, что номер коробки > 0
        if (boxNumber <= 0)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Некорректный номер коробки: {boxNumber}");
            return (ean13, quantity, grade, 0);
        }

        return (ean13, quantity, grade, boxNumber);
    }

    private bool IsValidEan13(string code)
    {
        return !string.IsNullOrEmpty(code) && code.Length == 13 && code.All(char.IsDigit);
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
        if (string.IsNullOrEmpty(ean13))
            return "Неизвестный продукт";

        try
        {
            // Сначала проверяем локальный кэш
            var product = await _dbHelper.GetProductByEan13(ean13);
            if (product != null && !string.IsNullOrEmpty(product.name))
            {
                return product.name;
            }
            
            // Если нет локально и есть интернет — синхронизируем
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

    private bool IsLocationBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return false;
        
        // Если это EAN13-формат коробки (13 цифр) — не локация
        if (IsValidEan13(barcode)) return false;
        
        // Проверяем формат коробки: EAN13-Кол-Сорт-Номер
        var parts = barcode.Split('-');
        if (parts.Length == 4)
        {
            // Если первая часть — EAN13 и последняя — число, это коробка
            if (IsValidEan13(parts[0]) && int.TryParse(parts[3], out _))
            {
                return false;
            }
        }
        
        // Если это чисто число — не локация
        if (int.TryParse(barcode, out _))
        {
            return false;
        }
        
        // В остальных случаях считаем локацией
        return true;
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
            // 1. Проверка на дубликат в текущей сессии
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

            // Проверяем валидность парсинга
            if (string.IsNullOrEmpty(ean13) || boxNumber <= 0)
            {
                HasError = true;
                ErrorMessage = "⚠️ Некорректный формат штрихкода";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }

            // 2. Проверяем локальный кэш (всегда сначала)
            var cachedBox = await _dbHelper.GetBoxByBarcode(barcode);
            if (cachedBox != null)
            {
                // Проверяем статус в кэше
                if (cachedBox.status == 1) // Active
                {
                    HasError = true;
                    ErrorMessage = $"⚠️ Коробка №{cachedBox.box_number} уже активирована локально!";
                    ScanStatusIcon = "⚠️";
                    ScanStatusColor = Colors.Orange;
                    ScanStatusText = ErrorMessage;
                    Vibration.Vibrate(200);
                    return;
                }
                else
                {
                    // Если статус не Active, обновляем его
                    System.Diagnostics.Debug.WriteLine($"⚠️ Коробка найдена в кэше со статусом {cachedBox.status}, обновляем...");
                }
            }

            // Получаем информацию о продукте
            var productName = await GetProductName(ean13);

            Box box;
            bool isActivated = false;

            if (IsOnline)
            {
                // 3. Проверяем на сервере
                var existingBox = await _apiService.GetBoxByBarcode(barcode);
                
                if (existingBox == null)
                {
                    HasError = true;
                    ErrorMessage = $"⚠️ Коробка №{boxNumber} не найдена на сервере! Сначала напечатайте штрихкод.";
                    ScanStatusIcon = "❌";
                    ScanStatusColor = Colors.Red;
                    ScanStatusText = ErrorMessage;
                    Vibration.Vibrate(200);
                    return;
                }

                // Проверяем статус
                if (existingBox.Status == 1) // Active
                {
                    HasError = true;
                    ErrorMessage = $"⚠️ Коробка №{boxNumber} уже активирована!";
                    ScanStatusIcon = "⚠️";
                    ScanStatusColor = Colors.Orange;
                    ScanStatusText = ErrorMessage;
                    Vibration.Vibrate(200);
                    return;
                }

                if (existingBox.Status != 0) // Не Draft
                {
                    HasError = true;
                    ErrorMessage = $"⚠️ Коробка №{boxNumber} имеет статус {existingBox.Status}, активация невозможна";
                    ScanStatusIcon = "❌";
                    ScanStatusColor = Colors.Red;
                    ScanStatusText = ErrorMessage;
                    Vibration.Vibrate(200);
                    return;
                }

                // 4. Активируем коробку
                try
                {
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
                            isActivated = true;
                            
                            // ✅ Сохраняем в локальный кэш (обновляем или создаем)
                            await SaveBoxToCache(box, isLocal: false);
                            
                            System.Diagnostics.Debug.WriteLine($"✅ Коробка активирована: #{box.BoxNumber}");
                        }
                        else
                        {
                            throw new Exception("Не удалось получить данные коробки");
                        }
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
                    
                    // Если не удалось активировать — пробуем сохранить локально для офлайн-синхронизации
                    box = CreateLocalBoxFromExisting(existingBox, productName);
                    isActivated = false;
                    
                    await AddToOfflineQueue(box);
                    await SaveBoxToCache(box, isLocal: true);
                    
                    await Application.Current?.MainPage?.DisplayAlert(
                        "⚠️ Внимание",
                        $"Коробка сохранена локально для синхронизации.\n{ex.Message}",
                        "OK"
                    );
                }
            }
            else
            {
                // Офлайн-режим
                // Если коробка уже есть в кэше, используем её
                if (cachedBox != null)
                {
                    box = new Box
                    {
                        Id = cachedBox.box_id,
                        Barcode = cachedBox.barcode,
                        BoxNumber = cachedBox.box_number,
                        ProductName = cachedBox.product_name ?? productName,
                        ProductEan13 = cachedBox.product_ean13 ?? ean13,
                        Quantity = cachedBox.current_quantity,
                        Grade = cachedBox.grade ?? grade,
                        LocationCode = CurrentLocation,
                        Status = 1, // Active
                        CreatedAt = cachedBox.created_at,
                        UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    
                    // Обновляем статус в кэше
                    await SaveBoxToCache(box, isLocal: true);
                }
                else
                {
                    // Создаем новую локальную запись
                    box = CreateLocalBox(ean13, quantity, grade, boxNumber, productName);
                    await AddToOfflineQueue(box);
                    await SaveBoxToCache(box, isLocal: true);
                }
                
                isActivated = false;
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "📴 Офлайн-режим",
                    $"Коробка №{boxNumber} сохранена локально. Будет активирована при синхронизации.",
                    "OK"
                );
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
                ScanStatusIcon = isActivated ? "✅" : "📴";
                ScanStatusColor = isActivated ? Colors.Green : Colors.Orange;
                ScanStatusText = isActivated 
                    ? $"✅ Активирована: #{box.BoxNumber}" 
                    : $"📴 Локально: #{box.BoxNumber}";
                
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
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private Box CreateLocalBoxFromExisting(Box existingBox, string productName)
    {
        return new Box
        {
            Id = existingBox.Id,
            Barcode = existingBox.Barcode,
            BoxNumber = existingBox.BoxNumber,
            ProductName = productName,
            ProductEan13 = existingBox.ProductEan13,
            Quantity = existingBox.Quantity,
            Grade = existingBox.Grade,
            LocationCode = CurrentLocation,
            Status = 1, // Active
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private Box CreateLocalBox(string ean13, int quantity, string grade, int boxNumber, string productName)
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
            Status = 1, // Active
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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
            initial_quantity = box.Quantity,
            current_quantity = box.Quantity,
            product_id = box.ProductId,
            product_name = box.ProductName,
            product_ean13 = box.ProductEan13,
            location_code = box.LocationCode ?? CurrentLocation,
            status = 1, // Active
            created_at = box.CreatedAt,
            updated_at = box.UpdatedAt,
            is_dirty = isLocal ? 1 : 0
        };
        await _dbHelper.SaveBox(boxCache);
        System.Diagnostics.Debug.WriteLine($"✅ Коробка сохранена в кэш: #{box.BoxNumber}, статус=1, is_dirty={isLocal}");
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
        
        System.Diagnostics.Debug.WriteLine($"📴 Коробка добавлена в офлайн-очередь: #{box.BoxNumber}");
    }

    [RelayCommand]
    public async Task ScanLocation(string locationCode)
    {
        if (string.IsNullOrEmpty(locationCode)) return;
        
        IsLoading = true;
        
        try
        {
            // Сначала проверяем локальную БД
            var location = await _dbHelper.GetLocationByCode(locationCode);
            
            // Если нет локально и есть интернет — синхронизируем
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
            
            // Устанавливаем локацию для новых коробок
            CurrentLocation = locationCode;
            LastScannedBarcode = locationCode;
            HasError = false;
            ErrorMessage = string.Empty;
            ScanStatusIcon = "📍";
            ScanStatusColor = Colors.Blue;
            ScanStatusText = $"📍 Локация: {locationCode} ({location.name})";
            
            System.Diagnostics.Debug.WriteLine($"✅ Локация установлена: {locationCode}");
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"❌ Ошибка проверки локации: {ex.Message}";
            ScanStatusIcon = "❌";
            ScanStatusColor = Colors.Red;
            ScanStatusText = ErrorMessage;
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка установки локации: {ex.Message}");
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
        int onlineCount = 0;
        int localCount = 0;
        
        try
        {
            // Подсчитываем локальные коробки
            var localBoxes = boxes.Where(b => b.Id.StartsWith("local_")).ToList();
            localCount = localBoxes.Count;
            onlineCount = boxes.Count - localCount;
            
            if (localBoxes.Any())
            {
                if (IsOnline)
                {
                    // Показываем прогресс
                    await Application.Current?.MainPage?.DisplayAlert(
                        "Синхронизация",
                        $"Найдено {localCount} локальных коробок. Синхронизация...",
                        "OK"
                    );
                    
                    // Синхронизируем
                    await _syncQueueService.ProcessQueueAsync();
                    
                    // Проверяем результат
                    var pending = await _syncQueueService.GetPendingCount();
                    if (pending == 0)
                    {
                        await Application.Current?.MainPage?.DisplayAlert(
                            "✅ Успешно",
                            $"Все {localCount} коробок синхронизированы с сервером.",
                            "OK"
                        );
                    }
                    else
                    {
                        await Application.Current?.MainPage?.DisplayAlert(
                            "⚠️ Внимание",
                            $"Синхронизировано {localCount - pending} из {localCount} коробок.\n{pending} ожидают синхронизации.",
                            "OK"
                        );
                    }
                }
                else
                {
                    await Application.Current?.MainPage?.DisplayAlert(
                        "📴 Офлайн-режим",
                        $"{localCount} коробок сохранены локально и будут синхронизированы при подключении.",
                        "OK"
                    );
                }
            }
            else
            {
                // Все коробки уже синхронизированы
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Успешно",
                    $"Принято {onlineCount} коробок.",
                    "OK"
                );
            }
            
            ClearSession();
            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка подтверждения операции: {ex.Message}");
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
            // Не сбрасываем CurrentLocation при очистке сессии
            LastScannedBarcode = null;
            IsBoxScanned = false;
            ScanStatusText = "Отсканируйте штрихкод коробки";
            ScanStatusColor = Colors.Gray;
            BoxInfoText = string.Empty;
            BoxNumberDisplay = string.Empty;
            ScanStatusIcon = "📷";
            HasError = false;
            ErrorMessage = string.Empty;
        });
    }

    [RelayCommand]
    public async Task CancelOperation()
    {
        if (ScannedBoxes.Count > 0)
        {
            // Удаляем только локальные коробки из кэша
            var localBoxes = ScannedBoxes.Where(b => b.Id.StartsWith("local_")).ToList();
            foreach (var box in localBoxes)
            {
                await _dbHelper.DeleteBoxByBarcode(box.Barcode);
                System.Diagnostics.Debug.WriteLine($"🗑️ Удалена локальная коробка: #{box.BoxNumber}");
            }
            
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
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveBox(object parameter)
    {
        if (parameter is Box box && ScannedBoxes.Contains(box))
        {
            // Если коробка локальная — удаляем из кэша
            if (box.Id.StartsWith("local_"))
            {
                _ = Task.Run(async () =>
                {
                    await _dbHelper.DeleteBoxByBarcode(box.Barcode);
                    System.Diagnostics.Debug.WriteLine($"🗑️ Удалена локальная коробка из кэша: #{box.BoxNumber}");
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