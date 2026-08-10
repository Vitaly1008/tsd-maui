using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.ViewModels;

public partial class ShippingViewModel : ObservableObject, IDisposable
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

    // ============================================================
    // ОСНОВНЫЕ СВОЙСТВА (как в ReceivingViewModel)
    // ============================================================

    [ObservableProperty]
    private bool _isLoading;

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

    // ============================================================
    // СВОЙСТВА ДЛЯ УПРАВЛЕНИЯ ОТГРУЗКОЙ
    // ============================================================

    [ObservableProperty]
    private int _shipQuantity = 0;

    [ObservableProperty]
    private int _maxQuantity;

    [ObservableProperty]
    private bool _isFullShipmentMode = true;  // По умолчанию - полная отгрузка

    [ObservableProperty]
    private bool _isPartialShipmentMode;

    [ObservableProperty]
    private bool _canPartialShip;

    [ObservableProperty]
    private string _shipQuantityDisplay = "0";

    public ObservableCollection<Box> ScannedBoxes { get; } = new();

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public ShippingViewModel(IBarcodeService? barcodeService = null)
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
            
            if (_barcodeService != null)
            {
                StartScanner();
                System.Diagnostics.Debug.WriteLine("✅ Сканер запущен из ShippingViewModel.Initialize()");
            }
            
            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine("✅ Страница отгрузки инициализирована");
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

    // ============================================================
    // ОСНОВНАЯ ЛОГИКА СКАНИРОВАНИЯ
    // ============================================================

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
            // ✅ ПОЛУЧАЕМ КОРОБКУ С СЕРВЕРА ИЛИ ИЗ КЭША
            // ============================================================
            Box? box = null;

            if (IsOnline)
            {
                try
                {
                    var serverBox = await _apiService.GetBoxByBarcode(barcode);
                    if (serverBox != null)
                    {
                        box = serverBox;
                        System.Diagnostics.Debug.WriteLine($"✅ Коробка найдена на сервере: #{box.BoxNumber}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка получения коробки с сервера: {ex.Message}");
                }
            }

            // Если не нашли на сервере — ищем в локальном кэше
            if (box == null)
            {
                var cachedBox = await _dbHelper.GetBoxByBarcode(barcode);
                if (cachedBox != null)
                {
                    box = new Box
                    {
                        Id = cachedBox.box_id,
                        Barcode = cachedBox.barcode,
                        BoxNumber = cachedBox.box_number,
                        Grade = cachedBox.grade,
                        CurrentQuantity = cachedBox.current_quantity,
                        InitialQuantity = cachedBox.initial_quantity,
                        ProductId = cachedBox.product_id,
                        ProductName = cachedBox.product_name,
                        ProductEan13 = cachedBox.product_ean13,
                        LocationCode = cachedBox.location_code,
                        Status = cachedBox.status,
                        CreatedAt = cachedBox.created_at,
                        UpdatedAt = cachedBox.updated_at
                    };
                    System.Diagnostics.Debug.WriteLine($"📦 Коробка найдена в кэше: #{box.BoxNumber}");
                }
            }

            if (box == null)
            {
                HasError = true;
                ErrorMessage = $"⚠️ Коробка №{boxNumber} не найдена на складе!";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }

            // Проверяем статус коробки
            if (box.Status == 3) // Shipped
            {
                HasError = true;
                ErrorMessage = $"⚠️ Коробка №{box.BoxNumber} уже отгружена!";
                ScanStatusIcon = "❌";
                ScanStatusColor = Colors.Red;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }

            if (box.Status == 2) // Empty
            {
                HasError = true;
                ErrorMessage = $"⚠️ Коробка №{box.BoxNumber} пуста!";
                ScanStatusIcon = "📭";
                ScanStatusColor = Colors.Orange;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }

            if (box.Status == 5) // Reserved
            {
                HasError = true;
                ErrorMessage = $"⚠️ Коробка №{box.BoxNumber} зарезервирована!";
                ScanStatusIcon = "🔒";
                ScanStatusColor = Colors.Orange;
                ScanStatusText = ErrorMessage;
                Vibration.Vibrate(200);
                return;
            }

            // Получаем название продукта если его нет
            if (string.IsNullOrEmpty(box.ProductName))
            {
                box.ProductName = await GetProductName(box.ProductEan13);
            }

            // ✅ Запоминаем выбранную коробку
            _currentSelectedBox = box;
            MaxQuantity = box.CurrentQuantity;
            
            // ✅ Обновляем режимы
            UpdateModes();

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
                ScanStatusText = $"✅ Найдена: #{box.BoxNumber} ({box.CurrentQuantity} шт.)";
                
                BoxInfoText = $"{box.ProductName} | {box.CurrentQuantity} шт. | {box.Grade} | №{box.BoxNumber}";
                BoxNumberDisplay = $"№{box.BoxNumber}";
                
                // Автоматически устанавливаем количество
                ShipQuantity = box.CurrentQuantity;
                ShipQuantityDisplay = ShipQuantity.ToString();
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

    // ============================================================
    // УПРАВЛЕНИЕ РЕЖИМАМИ ОТГРУЗКИ
    // ============================================================

    private void UpdateModes()
    {
        if (ScannedBoxes.Count > 1)
        {
            // Несколько коробок - только полная отгрузка
            CanPartialShip = false;
            IsFullShipmentMode = true;
            IsPartialShipmentMode = false;
            ShipQuantity = ScannedBoxes.Sum(b => b.CurrentQuantity);
            ShipQuantityDisplay = ShipQuantity.ToString();
        }
        else if (ScannedBoxes.Count == 1)
        {
            // Одна коробка - можно выбрать режим
            CanPartialShip = true;
            
            // Если частичная отгрузка уже была включена, оставляем
            if (!IsPartialShipmentMode)
            {
                IsFullShipmentMode = true;
                ShipQuantity = _currentSelectedBox?.CurrentQuantity ?? 0;
                ShipQuantityDisplay = ShipQuantity.ToString();
            }
            else
            {
                IsFullShipmentMode = false;
                // Количество уже установлено
            }
        }
        else
        {
            CanPartialShip = false;
            IsFullShipmentMode = true;
            IsPartialShipmentMode = false;
            ShipQuantity = 0;
            ShipQuantityDisplay = "0";
        }
    }

    [RelayCommand]
    public void SetFullShipment()
    {
        if (!CanPartialShip && ScannedBoxes.Count > 1)
        {
            return;
        }

        IsFullShipmentMode = true;
        IsPartialShipmentMode = false;
        ShipQuantity = _currentSelectedBox?.CurrentQuantity ?? 0;
        ShipQuantityDisplay = ShipQuantity.ToString();
    }

    [RelayCommand]
    public void SetPartialShipment()
    {
        if (!CanPartialShip)
        {
            Application.Current?.MainPage?.DisplayAlert(
                "Информация",
                "Частичная отгрузка доступна только для одной коробки",
                "OK"
            );
            return;
        }

        IsFullShipmentMode = false;
        IsPartialShipmentMode = true;
        ShipQuantity = _currentSelectedBox?.CurrentQuantity ?? 0;
        ShipQuantityDisplay = ShipQuantity.ToString();
    }

    [RelayCommand]
    public void IncreaseQuantity()
    {
        if (!IsPartialShipmentMode || _currentSelectedBox == null) return;
        if (ShipQuantity < _currentSelectedBox.CurrentQuantity)
        {
            ShipQuantity++;
            ShipQuantityDisplay = ShipQuantity.ToString();
        }
    }

    [RelayCommand]
    public void DecreaseQuantity()
    {
        if (!IsPartialShipmentMode || _currentSelectedBox == null) return;
        if (ShipQuantity > 1)
        {
            ShipQuantity--;
            ShipQuantityDisplay = ShipQuantity.ToString();
        }
    }

    // ============================================================
    // ЛОГИКА ОТГРУЗКИ
    // ============================================================

    [RelayCommand]
    public async Task ConfirmOperation()
    {
        if (ScannedBoxes.Count == 0)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Внимание",
                "Нет коробок для отгрузки",
                "OK"
            );
            return;
        }

        IsLoading = true;
        var boxes = ScannedBoxes.ToList();
        int shippedCount = 0;      // Полностью отгружены (Shipped)
        int emptyCount = 0;        // Стали пустыми (Empty)
        int partialCount = 0;      // Частично отгружены (Active)
        int localCount = 0;        // Сохранены локально
        int totalShippedQuantity = 0;
        
        try
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                
                // ✅ Определяем количество для отгрузки
                int quantityToShip;
                bool isFullShipment; // true = клиент забирает всю коробку
                
                // Если это последняя коробка и включен режим частичной отгрузки
                if (i == boxes.Count - 1 && IsPartialShipmentMode && CanPartialShip)
                {
                    quantityToShip = ShipQuantity;
                    // ✅ Флаг полной отгрузки = клиент забирает ВСЁ количество
                    isFullShipment = quantityToShip >= box.CurrentQuantity;
                }
                else
                {
                    // Для всех остальных коробок - клиент забирает всё
                    quantityToShip = box.CurrentQuantity;
                    isFullShipment = true;
                }

                if (quantityToShip <= 0)
                    continue;

                // ✅ Нельзя отгрузить больше, чем есть
                if (quantityToShip > box.CurrentQuantity)
                    quantityToShip = box.CurrentQuantity;

                int newQuantity = box.CurrentQuantity - quantityToShip;
                totalShippedQuantity += quantityToShip;

                // ============================================================
                // ✅ ПРАВИЛЬНАЯ ЛОГИКА ОТГРУЗКИ
                // ============================================================
                if (IsOnline && !box.Id.StartsWith("local_"))
                {
                    try
                    {
                        if (isFullShipment)
                        {
                            // ✅ ПОЛНАЯ ОТГРУЗКА (клиент забирает коробку целиком)
                            // → ShipBox → статус Shipped (3)
                            var shipResult = await _apiService.ShipBox(
                                boxId: box.Id,
                                comment: $"Полная отгрузка через ТСД"
                            );

                            if (shipResult.TryGetValue("success", out var success) && success is bool s && s)
                            {
                                var updatedBox = await _apiService.GetBoxByBarcode(box.Barcode);
                                if (updatedBox != null)
                                {
                                    updatedBox.Status = 3; // Shipped
                                    await UpdateLocalBox(updatedBox);
                                    shippedCount++;
                                }
                            }
                            else
                            {
                                var errorMsg = shipResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                                throw new Exception(errorMsg);
                            }
                        }
                        else
                        {
                            // ✅ ЧАСТИЧНАЯ ОТГРУЗКА (клиент забирает часть, коробка остается на складе)
                            // → ConsumeBox → статус зависит от остатка
                            var consumeResult = await _apiService.ConsumeBox(
                                boxId: box.Id,
                                quantity: quantityToShip,
                                comment: $"Частичная отгрузка, остаток: {newQuantity} шт."
                            );

                            if (consumeResult.TryGetValue("success", out var success) && success is bool s && s)
                            {
                                var updatedBox = await _apiService.GetBoxByBarcode(box.Barcode);
                                if (updatedBox != null)
                                {
                                    // ✅ После Consume статус определяется автоматически:
                                    // - Active (1) если остаток > 0
                                    // - Empty (2) если остаток = 0
                                    updatedBox.Status = newQuantity > 0 ? 1 : 2;
                                    await UpdateLocalBox(updatedBox);
                                    
                                    if (newQuantity == 0)
                                        emptyCount++;  // Коробка пуста, но на складе
                                    else
                                        partialCount++; // Частично отгружена
                                }
                            }
                            else
                            {
                                var errorMsg = consumeResult.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                                throw new Exception(errorMsg);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Ошибка отгрузки: {ex.Message}");
                        await SaveLocalShipment(box, quantityToShip, isFullShipment);
                        localCount++;
                    }
                }
                else
                {
                    // Офлайн-режим
                    await SaveLocalShipment(box, quantityToShip, isFullShipment);
                    localCount++;
                }
            }

            // ============================================================
            // ✅ ПОКАЗЫВАЕМ РЕЗУЛЬТАТ
            // ============================================================
            var hasInternet = await _syncService.CheckInternetManual();
            var totalCount = shippedCount + emptyCount + partialCount + localCount;
            
            var message = $"Обработано {totalCount} коробок.\n";
            message += $"📦 Всего отгружено: {totalShippedQuantity} шт.\n\n";
            
            if (shippedCount > 0)
                message += $"✅ Полностью отгружено (Shipped): {shippedCount}\n";
            if (partialCount > 0)
                message += $"📦 Частично отгружено (Active): {partialCount}\n";
            if (emptyCount > 0)
                message += $"📭 Остаток 0 (Empty): {emptyCount}\n";
            if (localCount > 0)
                message += $"📴 Сохранено локально: {localCount}\n";
            
            if (localCount == 0 && hasInternet)
                message += "\n✅ Данные синхронизированы с сервером.";
            else if (localCount > 0 && hasInternet)
                message += "\n⚠️ Часть данных будет синхронизирована позже.";
            else
                message += "\n📴 Данные сохранены локально и будут синхронизированы при подключении.";

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
                $"Не удалось выполнить отгрузку: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ============================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ============================================================

    private async Task UpdateLocalBox(Box updatedBox)
    {
        var boxCache = new BoxCache
        {
            barcode = updatedBox.Barcode,
            box_id = updatedBox.Id,
            box_number = updatedBox.BoxNumber,
            grade = updatedBox.Grade,
            initial_quantity = updatedBox.InitialQuantity,
            current_quantity = updatedBox.CurrentQuantity,
            product_id = updatedBox.ProductId,
            product_name = updatedBox.ProductName,
            product_ean13 = updatedBox.ProductEan13,
            location_code = updatedBox.LocationCode ?? "UNKNOWN",
            status = updatedBox.Status,
            created_at = updatedBox.CreatedAt,
            updated_at = updatedBox.UpdatedAt
        };
        await _dbHelper.SaveBox(boxCache);
    }

    private async Task SaveLocalShipment(Box box, int quantityToShip, bool isFullShipment)
    {
        int newQuantity = box.CurrentQuantity - quantityToShip;
        
        // ✅ Правильное определение статуса для локального сохранения
        int newStatus;
        if (isFullShipment)
        {
            // Полная отгрузка - коробка покидает склад
            newStatus = 3; // Shipped
        }
        else if (newQuantity == 0)
        {
            // Частичная отгрузка, но остаток 0 - коробка пуста, но на складе
            newStatus = 2; // Empty
        }
        else
        {
            // Частичная отгрузка, остаток > 0 - коробка активна
            newStatus = 1; // Active
        }
        
        await SaveBoxOperation(
            box, 
            isFullShipment ? "Ship" : "PartialShip",
            $"Отгружено локально: {quantityToShip} шт. Остаток: {newQuantity} шт."
        );
        
        var boxCache = new BoxCache
        {
            barcode = box.Barcode,
            box_id = box.Id,
            box_number = box.BoxNumber,
            grade = box.Grade,
            initial_quantity = box.InitialQuantity,
            current_quantity = newQuantity,
            product_id = box.ProductId,
            product_name = box.ProductName,
            product_ean13 = box.ProductEan13,
            location_code = box.LocationCode ?? "UNKNOWN",
            status = newStatus,
            created_at = box.CreatedAt,
            updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await _dbHelper.SaveBox(boxCache);
        
        var payload = new
        {
            boxId = box.Id,
            barcode = box.Barcode,
            boxNumber = box.BoxNumber,
            quantity = quantityToShip,
            newQuantity = newQuantity,
            status = newStatus,
            isFullShipment = isFullShipment,
            operationType = "Shipping",
            timestamp = DateTime.UtcNow
        };
        
        await _syncQueueService.EnqueueAsync(
            operationType: "Shipping",
            barcode: box.Barcode,
            payload: payload,
            deviceId: Constants.DeviceId
        );
        
        string statusText = newStatus switch
        {
            1 => "Active",
            2 => "Empty",
            3 => "Shipped",
            _ => "Unknown"
        };
        
        System.Diagnostics.Debug.WriteLine($"📴 Коробка сохранена локально: #{box.BoxNumber}, остаток: {newQuantity}, статус: {statusText}");
}

    private async Task SaveBoxOperation(Box box, string operationType, string comment)
    {
        try
        {
            var operation = new BoxOperationCache
            {
                operation_id = $"op_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid().ToString().Substring(0, 8)}",
                box_id = box.Id,
                box_barcode = box.Barcode,
                operation_type = operationType,
                quantity = box.CurrentQuantity,
                from_location_code = box.LocationCode,
                to_location_code = operationType == "Ship" || operationType == "PartialShip" ? "SHIPPED" : null,
                device_id = Constants.DeviceId,
                comment = comment,
                created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                is_synced = IsOnline ? 1 : 0
            };
            
            await _dbHelper.SaveBoxOperation(operation);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка сохранения операции: {ex.Message}");
        }
    }

    private void ClearSession()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            ScannedCount = 0;
            LastScannedBarcode = null;
            IsBoxScanned = false;
            ScanStatusText = "Отсканируйте штрихкод коробки";
            ScanStatusColor = Colors.Gray;
            BoxInfoText = string.Empty;
            BoxNumberDisplay = string.Empty;
            ScanStatusIcon = "📷";
            _currentSelectedBox = null;
            ShipQuantity = 0;
            MaxQuantity = 0;
            IsFullShipmentMode = true;
            IsPartialShipmentMode = false;
            CanPartialShip = false;
            ShipQuantityDisplay = "0";
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
    public void RemoveBox(object parameter)
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
                    _currentSelectedBox = null;
                    CanPartialShip = false;
                }
                else if (ScannedBoxes.Count == 1)
                {
                    _currentSelectedBox = ScannedBoxes.First();
                    MaxQuantity = _currentSelectedBox.CurrentQuantity;
                    UpdateModes();
                }
                else
                {
                    // Несколько коробок - только полная отгрузка
                    CanPartialShip = false;
                    IsFullShipmentMode = true;
                    IsPartialShipmentMode = false;
                    ShipQuantity = ScannedBoxes.Sum(b => b.CurrentQuantity);
                    ShipQuantityDisplay = ShipQuantity.ToString();
                }
            });
        }
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
                return;
            }
            
            if (location.is_active != 1)
            {
                HasError = true;
                ErrorMessage = $"⚠️ Локация '{locationCode}' неактивна";
                ScanStatusIcon = "⚠️";
                ScanStatusColor = Colors.Orange;
                ScanStatusText = ErrorMessage;
                return;
            }
            
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
            $"{i + 1}. #{b.BoxNumber} | {b.ProductName} | {b.CurrentQuantity} шт. | {b.Grade} | {(b.Status == 1 ? "✅" : "📭")}")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"📦 Список коробок ({ScannedBoxes.Count})",
            boxList,
            "OK"
        );
    }

    [RelayCommand]
    public async Task ShowBoxHistory(object parameter)
    {
        if (parameter is Box box)
        {
            try
            {
                string historyText = $"📋 История коробки #{box.BoxNumber}\n" +
                                    $"📦 Остаток: {box.CurrentQuantity} шт.\n" +
                                    $"📅 Поступление: {DateTimeOffset.FromUnixTimeMilliseconds(box.CreatedAt).LocalDateTime:dd.MM.yyyy HH:mm}\n\n";
                
                if (IsOnline)
                {
                    try
                    {
                        var client = new HttpClient();
                        var token = await new SecureStorageService().GetToken();
                        if (!string.IsNullOrEmpty(token))
                        {
                            client.DefaultRequestHeaders.Authorization = 
                                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                        }
                        
                        var response = await client.GetAsync(
                            $"{Constants.ApiBaseUrl}/api/boxes/{box.Id}/history"
                        );
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            var history = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(content);
                            
                            if (history != null && history.Count > 0)
                            {
                                foreach (var op in history)
                                {
                                    var type = op.GetValueOrDefault("operationType", "")?.ToString() ?? "Unknown";
                                    var qty = op.GetValueOrDefault("quantity", 0);
                                    var comment = op.GetValueOrDefault("comment", "")?.ToString() ?? "";
                                    var createdAt = op.GetValueOrDefault("createdAt", 0) is long ts ? 
                                        DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString("dd.MM.yy HH:mm") : 
                                        "Неизвестно";
                                    
                                    historyText += $"🕐 {createdAt} | {type} | {qty} шт.";
                                    if (!string.IsNullOrEmpty(comment))
                                        historyText += $" | {comment}";
                                    historyText += "\n";
                                }
                            }
                            else
                            {
                                historyText += "📭 Нет операций в истории на сервере.\n";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка получения истории с сервера: {ex.Message}");
                        historyText += "⚠️ Не удалось получить историю с сервера.\n";
                    }
                }
                
                var localOps = await _dbHelper.GetBoxOperationsByBarcode(box.Barcode);
                if (localOps.Any())
                {
                    historyText += "\n📴 Локальные операции:\n";
                    foreach (var op in localOps)
                    {
                        var date = DateTimeOffset.FromUnixTimeMilliseconds(op.created_at).LocalDateTime.ToString("dd.MM.yy HH:mm");
                        historyText += $"📱 {date} | {op.operation_type} | {op.quantity} шт.";
                        if (!string.IsNullOrEmpty(op.comment))
                            historyText += $" | {op.comment}";
                        historyText += "\n";
                    }
                }
                
                await Application.Current?.MainPage?.DisplayAlert(
                    $"📋 История коробки #{box.BoxNumber}",
                    historyText,
                    "OK"
                );
            }
            catch (Exception ex)
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "❌ Ошибка",
                    $"Не удалось получить историю: {ex.Message}",
                    "OK"
                );
            }
        }
    }

    [RelayCommand]
    private void ToggleBoxList()
    {
        IsBoxListExpanded = !IsBoxListExpanded;
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