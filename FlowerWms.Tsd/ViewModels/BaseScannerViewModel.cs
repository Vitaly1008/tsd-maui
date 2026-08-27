using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.ViewModels;

// Базовый ViewModel для страниц, использующих сканер штрихкодов
public abstract partial class BaseScannerViewModel : ObservableObject, IDisposable
{
    protected readonly IBarcodeService? _barcodeService;
    protected readonly DatabaseHelper _dbHelper;
    protected readonly ApiService _apiService;
    protected readonly SyncQueueService _syncQueueService;
    protected readonly SyncService _syncService;
    protected bool _isScannerStarted;
    protected bool _isInitialized;
    protected bool _disposed;

    // ===== Общие свойства =====
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private string _scanStatusText = string.Empty;

    [ObservableProperty]
    private string _scanStatusIcon = "📷";

    [ObservableProperty]
    private Color _scanStatusColor = Colors.Gray;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public BaseScannerViewModel(IBarcodeService? barcodeService = null)
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

    // ===== Общие методы =====
    protected virtual void OnBarcodeScanned(string barcode)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ProcessBarcode(barcode);
        });
    }

    // Обработка сканированного штрихкода (должен быть переопределён в наследниках)
    protected abstract Task ProcessBarcode(string barcode);

    public virtual void StartScanner()
    {
        if (_barcodeService == null || _isScannerStarted) return;

        try
        {
            _barcodeService.StartListening();
            _isScannerStarted = true;
            System.Diagnostics.Debug.WriteLine($"Сканер запущен в {GetType().Name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка запуска сканера: {ex.Message}");
        }
    }

    public virtual void StopScanner()
    {
        if (_barcodeService == null || !_isScannerStarted) return;

        try
        {
            _barcodeService.StopListening();
            _isScannerStarted = false;
            System.Diagnostics.Debug.WriteLine($"Сканер остановлен в {GetType().Name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка остановки сканера: {ex.Message}");
        }
    }

    public virtual async Task Initialize()
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
            System.Diagnostics.Debug.WriteLine($"{GetType().Name} инициализирован");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации {GetType().Name}: {ex.Message}");
        }
    }

    // Проверка, является ли штрихкод локацией
    protected virtual bool IsLocationBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return false;

        // EAN13 - всегда продукт/коробка
        if (System.Text.RegularExpressions.Regex.IsMatch(barcode, @"^\d{13}$"))
            return false;

        // Формат коробки: EAN13-Количество-Сорт-Номер
        var parts = barcode.Split('-');
        if (parts.Length == 4)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^\d{13}") &&
                int.TryParse(parts[3], out _))
            {
                return false;
            }
        }

        // Если это чисто число - скорее всего коробка
        if (int.TryParse(barcode, out _))
            return false;

        return true;
    }

    protected void SetError(string message, string icon = "❌", Color? color = null)
    {
        HasError = true;
        ErrorMessage = message;
        ScanStatusIcon = icon;
        ScanStatusColor = color ?? Colors.Red;
        ScanStatusText = message;
        Vibration.Vibrate(200);
    }

    protected void SetSuccess(string message, string icon = "✅", Color? color = null)
    {
        HasError = false;
        ErrorMessage = string.Empty;
        ScanStatusIcon = icon;
        ScanStatusColor = color ?? Colors.Green;
        ScanStatusText = message;
    }

    protected void SetStatus(string message, string icon = "📷", Color? color = null)
    {
        HasError = false;
        ErrorMessage = string.Empty;
        ScanStatusIcon = icon;
        ScanStatusColor = color ?? Colors.Gray;
        ScanStatusText = message;
    }

    protected void SetWarning(string message, string icon = "⚠️", Color? color = null)
    {
        HasError = false;
        ErrorMessage = string.Empty;
        ScanStatusIcon = icon;
        ScanStatusColor = color ?? Colors.Orange;
        ScanStatusText = message;
    }

    // ===== Вспомогательные методы =====
    protected string GetGradeName(string gradeCode) => gradeCode switch
    {
        "1" => "First",
        "2" => "Second",
        "3" => "Decorated",
        "5" => "Rejected",
        "9" => "Premium",
        _ => gradeCode
    };

    protected string GetGradeCode(string gradeName) => gradeName switch
    {
        "First" => "1",
        "Second" => "2",
        "Decorated" => "3",
        "Rejected" => "5",
        "Premium" => "9",
        _ => gradeName
    };

    protected (string ean13, int quantity, string grade, int boxNumber) ParseBarcode(string barcode)
    {
        var parts = barcode.Split('-');

        string ean13 = parts.Length > 0 ? parts[0] : "0000000000000";
        int quantity = parts.Length > 1 && int.TryParse(parts[1], out var q) ? q : 0;
        string grade = parts.Length > 2 ? GetGradeName(parts[2]) : "Premium";
        int boxNumber = parts.Length > 3 && int.TryParse(parts[3], out var n) ? n : 0;

        return (ean13, quantity, grade, boxNumber);
    }

    protected async Task<string> GetProductName(string ean13)
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
            System.Diagnostics.Debug.WriteLine($"Ошибка получения продукта: {ex.Message}");
        }

        return "Неизвестный продукт";
    }

    public virtual void Dispose()
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