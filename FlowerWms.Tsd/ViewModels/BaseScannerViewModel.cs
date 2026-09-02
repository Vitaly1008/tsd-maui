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

    [ObservableProperty]
    private bool _isBoxListExpanded = true;

    public BaseScannerViewModel(IBarcodeService? barcodeService = null)
    {
        _dbHelper = new DatabaseHelper();
        _apiService = new ApiService();
        _syncQueueService = new SyncQueueService();
        _syncService = new SyncService();
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
            // ✅ ИСПРАВЛЕНО: OnBarcodeScanned, StartListening
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
            // ✅ ИСПРАВЛЕНО: OnBarcodeScanned, StopListening
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

    // Удаление коробки из списка
    public virtual void RemoveBox(object parameter)
    {
        if (parameter is Box box && ScannedBoxes.Contains(box))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Remove(box);
                ScannedCount = ScannedBoxes.Count;
                if (ScannedCount == 0)
                {
                    ClearSession();
                }
            });
        }
    }

    // Парсинг штрихкода
    protected virtual (string ean13, int quantity, string grade, int boxNumber) ParseBarcode(string barcode)
    {
        var parts = barcode.Split('-');
        if (parts.Length == 4)
        {
            return (parts[0], int.Parse(parts[1]), parts[2], int.Parse(parts[3]));
        }
        return (barcode, 1, "Premium", 0);
    }

    // ✅ ИСПРАВЛЕНО: используем GetAllProducts() вместо GetProductByEan13
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
    protected virtual Box CreateLocalBox(string ean13, int quantity, string grade, int boxNumber, string productName, BoxStatus status)
    {
        var box = new Box
        {
            Id = $"LOCAL_{boxNumber}_{Guid.NewGuid():N}",
            Barcode = $"{ean13}-{quantity}-{grade}-{boxNumber}",
            BoxNumber = boxNumber,
            ProductName = productName,
            ProductEan13 = ean13,
            Quantity = quantity,
            CurrentQuantity = quantity,
            InitialQuantity = quantity,
            Grade = grade,
            LocationCode = "UNKNOWN",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        return box;
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

    // Проверка штрихкода локации
    protected virtual bool IsLocationBarcode(string barcode)
    {
        return barcode.StartsWith("LOC-") || 
               barcode.StartsWith("SHELF-") || 
               barcode.StartsWith("RACK-") ||
               barcode.StartsWith("ZONE-");
    }

    // Получение кода сорта
    protected virtual string GetGradeCode(string grade)
    {
        return grade switch
        {
            "Premium" => "P",
            "Standard" => "S",
            "Economy" => "E",
            _ => "P"
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