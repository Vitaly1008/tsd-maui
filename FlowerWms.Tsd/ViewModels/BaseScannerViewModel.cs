using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.ViewModels;

/// <summary>
/// Базовый ViewModel для страниц, использующих сканер штрихкодов
/// </summary>
public abstract partial class BaseScannerViewModel : ObservableObject, IDisposable
{
    protected readonly DatabaseHelper _dbHelper;
    protected readonly ApiService _apiService;
    protected readonly SyncQueueService _syncQueueService;
    protected readonly SyncService _syncService;
    protected IBarcodeService? _barcodeService;
    protected bool _isInitialized;
    protected bool _isScannerStarted;
    private bool _disposed;

    [ObservableProperty]
    private string _scanStatusText = "Готов к сканированию";

    [ObservableProperty]
    private Color _scanStatusColor = Colors.Gray;

    [ObservableProperty]
    private string _scanStatusIcon = "📷";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _lastScannedBarcode = string.Empty;

    [ObservableProperty]
    private bool _isBoxScanned;

    [ObservableProperty]
    private string _boxInfoText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<Box> _scannedBoxes = new();

    [ObservableProperty]
    private int _scannedCount;

    // ✅ ЕДИНСТВЕННОЕ объявление IsBoxListExpanded на всю иерархию.
    //    Раньше дублировалось в BaseOperationViewModel — из-за этого
    //    ToggleBoxListCommand переключал одно свойство, а XAML читал другое.
    [ObservableProperty]
    private bool _isBoxListExpanded = false;

    public BaseScannerViewModel(
        DatabaseHelper dbHelper,
        ApiService apiService,
        SyncQueueService syncQueueService,
        SyncService syncService,
        IBarcodeService? barcodeService = null)
    {
        _dbHelper = dbHelper;
        _apiService = apiService;
        _syncQueueService = syncQueueService;
        _syncService = syncService;
        _barcodeService = barcodeService;
        _isInitialized = false;
        _isScannerStarted = false;

        ScannedBoxes = new ObservableCollection<Box>();
    }

    // ✅ ИНИЦИАЛИЗАЦИЯ С ПЕРЕДАЧЕЙ BARCODESERVICE
    public virtual async Task Initialize(IBarcodeService? barcodeService = null)
    {
        if (_isInitialized) return;
        
        if (barcodeService != null)
        {
            _barcodeService = barcodeService;
        }
        
        if (_barcodeService != null && !_isScannerStarted)
        {
            _barcodeService.OnBarcodeScanned += OnBarcodeScanned;
            _barcodeService.StartListening();
            _isScannerStarted = true;
            SetStatus("Сканер готов", "📷", Colors.Gray);
        }
        
        IsOnline = await _syncService.CheckInternetManual();
        _isInitialized = true;
        
        await OnInitialized();
    }

    protected virtual Task OnInitialized()
    {
        return Task.CompletedTask;
    }

    // Обработчик сканирования штрихкода
    protected virtual void OnBarcodeScanned(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;
        if (IsLoading) return;
        
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await ProcessBarcode(barcode);
            }
            catch (Exception ex)
            {
                SetError($"Ошибка: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Ошибка обработки штрихкода: {ex.Message}");
            }
        });
    }

    // Абстрактный метод обработки штрихкода
    protected abstract Task ProcessBarcode(string barcode);

    // Установка статуса
    protected void SetStatus(string text, string icon, Color color)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScanStatusText = text;
            ScanStatusIcon = icon;
            ScanStatusColor = color;
            HasError = false;
            ErrorMessage = string.Empty;
        });
    }

    // Установка ошибки
    protected void SetError(string error)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ErrorMessage = error;
            HasError = true;
            ScanStatusText = "Ошибка";
            ScanStatusIcon = "❌";
            ScanStatusColor = Colors.Red;
        });
        Vibration.Vibrate(TimeSpan.FromMilliseconds(200));
    }

    // Установка предупреждения
    protected void SetWarning(string text, string icon, Color color)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScanStatusText = text;
            ScanStatusIcon = icon;
            ScanStatusColor = color;
            HasError = false;
            ErrorMessage = string.Empty;
        });
        Vibration.Vibrate(TimeSpan.FromMilliseconds(100));
    }

    // Установка успеха
    protected void SetSuccess(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScanStatusText = text;
            ScanStatusIcon = "✅";
            ScanStatusColor = Colors.Green;
            HasError = false;
            ErrorMessage = string.Empty;
        });
        Vibration.Vibrate(TimeSpan.FromMilliseconds(50));
    }

    // Остановка сканера
    public virtual void StopScanner()
    {
        if (_barcodeService != null && _isScannerStarted)
        {
            _barcodeService.OnBarcodeScanned -= OnBarcodeScanned;
            _barcodeService.StopListening();
            _isScannerStarted = false;
            System.Diagnostics.Debug.WriteLine("Сканер остановлен");
        }
    }

    // Очистка сессии
    public virtual void ClearSession()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            ScannedCount = 0;
            IsBoxScanned = false;
            BoxInfoText = string.Empty;
            ErrorMessage = string.Empty;
            HasError = false;
            SetStatus("Готов к сканированию", "📷", Colors.Gray);
        });
    }

    // Переключение списка коробок
    [RelayCommand]
    public void ToggleBoxList()
    {
        IsBoxListExpanded = !IsBoxListExpanded;
    }

    // ✅ Удаление коробки из списка с откатом pending-транзакции.
    //    [RelayCommand] стоит здесь — в наследниках override без атрибута.
    [RelayCommand]
    public virtual async Task RemoveBox(object parameter)
    {
        Logger.Info($"🔘 RemoveBox вызван: parameter={parameter?.GetType().Name ?? "null"}");
        if (parameter is not Box box || !ScannedBoxes.Contains(box))
        {
            Logger.Warning($"⚠️ RemoveBox: parameter не Box, а {parameter?.GetType().FullName ?? "null"}");
            return;
        }
        Logger.Info($"🔘 RemoveBox: box.Barcode={box.Barcode}, в коллекции={ScannedBoxes.Contains(box)}");
        // 1. Откат pending-транзакции (если есть).
        //    DeletePendingTransaction внутри вызывает OfflineService.RevertTransaction,
        //    который корректно восстанавливает локальную БД:
        //    - для Receiving: удаляет новую коробку ИЛИ возвращает previousStatus;
        //    - для Shipping: восстанавливает статус/количество или пересоздаёт запись.
        try
        {
            var pending = await _syncQueueService.GetAllPendingTransactions();
            var tx = pending.FirstOrDefault(t => t.barcode == box.Barcode);

            if (tx != null)
            {
                Logger.Info($"🔄 Удаление коробки {box.Barcode}: откат транзакции {tx.transaction_id} ({tx.operation_type})");
                var ok = await _syncQueueService.DeletePendingTransaction(tx.transaction_id);
                if (!ok)
                {
                    Logger.Warning($"⚠️ Не удалось откатить транзакцию {tx.transaction_id} для {box.Barcode}");
                }
            }
            else
            {
                // Маловероятный сценарий (транзакция уже синхронизирована или потеряна).
                // Просто убираем из UI, локальную БД не трогаем — при следующей
                // синхронизации сервер вернёт актуальное состояние.
                Logger.Warning($"⚠️ Pending-транзакция для {box.Barcode} не найдена. Удаляем только из UI.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка отката транзакции при удалении коробки {box.Barcode}: {ex.Message}");
            // Продолжаем — из UI удаляем в любом случае.
        }

        // 2. Убираем из UI.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Remove(box);
            ScannedCount = ScannedBoxes.Count;
            if (ScannedCount == 0)
                ClearSession();
        });

        // 3. Хук для наследников (специфика отгрузки).
        OnBoxRemoved(box);
    }

    /// <summary>
    /// Вызывается после удаления коробки из списка.
    /// Наследники могут переопределить для сброса специфичного состояния
    /// (например, режимов частичной отгрузки в ShippingViewModel).
    /// </summary>
    protected virtual void OnBoxRemoved(Box box) { }

    /// <summary>
    /// Парсит ШК локации: ровно 2 разделителя '-' (3 части).
    /// Может содержать буквы и цифры. Регистр нормализуется в UPPER.
    /// </summary>
    protected virtual bool TryParseLocationBarcode(string barcode, out string locationCode)
    {
        locationCode = string.Empty;
        if (string.IsNullOrWhiteSpace(barcode))
            return false;

        var parts = barcode.Split('-');
        if (parts.Length != 3)
            return false;

        // Все части непустые
        if (parts.Any(string.IsNullOrWhiteSpace))
            return false;

        locationCode = barcode.ToUpperInvariant();
        return true;
    }

    /// <summary>
    /// Парсит ШК коробки: ровно 3 разделителя '-' (4 части).
    /// Все части — только цифры. quantity > 0, grade ∈ {1,2,3,5,9}, boxNumber > 0.
    /// </summary>
    protected virtual bool TryParseBoxBarcode(
        string barcode,
        out string ean13,
        out int quantity,
        out int gradeCode,
        out string gradeName,
        out int boxNumber)
    {
        ean13 = string.Empty;
        quantity = 0;
        gradeCode = 0;
        gradeName = string.Empty;
        boxNumber = 0;

        if (string.IsNullOrWhiteSpace(barcode))
            return false;

        var parts = barcode.Split('-');
        if (parts.Length != 4)
            return false;

        // Все части — только цифры
        if (parts.Any(p => string.IsNullOrEmpty(p) || !p.All(char.IsDigit)))
            return false;

        ean13 = parts[0];

        if (!int.TryParse(parts[1], out quantity) || quantity <= 0)
            return false;

        if (!int.TryParse(parts[2], out gradeCode))
            return false;

        // Сорта строго фиксированы: 1, 2, 3, 5, 9
        if (gradeCode != 1 && gradeCode != 2 && gradeCode != 3 &&
            gradeCode != 5 && gradeCode != 9)
            return false;

        if (!int.TryParse(parts[3], out boxNumber) || boxNumber <= 0)
            return false;

        gradeName = gradeCode switch
        {
            9 => "Premium",
            1 => "First",
            2 => "Second",
            3 => "Decorated",
            5 => "Rejected",
            _ => string.Empty
        };

        return !string.IsNullOrEmpty(gradeName);
    }

    // Получение имени товара
    protected virtual async Task<string> GetProductName(string ean13)
    {
        if (string.IsNullOrEmpty(ean13)) return "Неизвестный товар";

        var product = await _dbHelper.GetProductByEan13(ean13);
        if (product != null && !string.IsNullOrEmpty(product.name))
        {
            return product.name;
        }

        if (IsOnline)
        {
            try
            {
                var allProducts = await _apiService.GetAllProducts();
                var serverProduct = allProducts.FirstOrDefault(p => p.Ean13 == ean13);
                
                if (serverProduct != null && !string.IsNullOrEmpty(serverProduct.Name))
                {
                    await _dbHelper.SaveProduct(new ProductCache
                    {
                        product_id = serverProduct.Id,
                        ean13 = serverProduct.Ean13,
                        name = serverProduct.Name,
                        short_name = serverProduct.ShortName,
                        onec_guid = serverProduct.OneCGuid,
                        barcode = serverProduct.Barcode,
                        updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                    return serverProduct.Name;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка получения продукта: {ex.Message}");
            }
        }

        return "Неизвестный товар";
    }

    // Создание локальной коробки
    protected virtual Box CreateLocalBox(
        string barcode, string ean13, int quantity,
        string gradeName, int boxNumber, string productName, BoxStatus status)
    {
        return new Box
        {
            Id = $"LOCAL_{boxNumber}_{Guid.NewGuid():N}",
            Barcode = barcode,
            BoxNumber = boxNumber,
            ProductName = productName,
            ProductEan13 = ean13,
            Quantity = quantity,
            CurrentQuantity = quantity,
            InitialQuantity = quantity,
            Grade = gradeName,
            LocationCode = "UNKNOWN",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    // Сохранение коробки в кэш
    protected virtual async Task SaveBoxToCache(Box box)
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
            updated_at = box.UpdatedAt
        };
        await _dbHelper.SaveBox(boxCache);
    }

    // Добавление коробки в список
    protected virtual void AddBoxToList(Box box)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!ScannedBoxes.Any(b => b.Barcode == box.Barcode))
            {
                ScannedBoxes.Add(box);
                ScannedCount = ScannedBoxes.Count;
                IsBoxScanned = true;
                BoxInfoText = $"Коробка #{box.BoxNumber} добавлена";
                SetSuccess($"Коробка #{box.BoxNumber} добавлена");
            }
        });
    }

    // Получение кода сорта
    protected virtual int GetGradeCode(string gradeName)
    {
        return gradeName switch
        {
            "Premium"   => 9,
            "First"     => 1,
            "Second"    => 2,
            "Decorated" => 3,
            "Rejected"  => 5,
            _ => int.TryParse(gradeName, out var g) ? g : 9
        };
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        
        StopScanner();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}