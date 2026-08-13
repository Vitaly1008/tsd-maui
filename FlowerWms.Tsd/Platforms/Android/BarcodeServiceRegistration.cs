using Android.Content;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Platforms.Android;

// Статический контекст Android для доступа из сервисов
public static class AndroidContext
{
    public static Context? Current { get; set; }
}

// Реализация IBarcodeService для Android
public class BarcodeService : IBarcodeService
{
    private UrovoScannerService? _scannerService;
    private bool _isListening;
    private readonly object _lock = new();
    private bool _disposed;

    public event Action<string>? OnBarcodeScanned;

    public BarcodeService()
    {
        System.Diagnostics.Debug.WriteLine("BarcodeService создан");
    }

    // Гарантирует инициализацию UrovoScannerService
    private void EnsureScannerService()
    {
        if (_scannerService != null) return;

        var context = AndroidContext.Current;
        if (context == null)
        {
            System.Diagnostics.Debug.WriteLine("Контекст Android недоступен");
            return;
        }

        _scannerService = new UrovoScannerService(context, (barcode) =>
        {
            OnBarcodeScanned?.Invoke(barcode);
        });
    }

    // Начинает прослушивание сканера
    public void StartListening()
    {
        lock (_lock)
        {
            if (_isListening || _disposed) return;
        }

        try
        {
            EnsureScannerService();
            if (_scannerService == null) return;

            _scannerService.StartListening();
            
            lock (_lock)
            {
                _isListening = true;
            }

            System.Diagnostics.Debug.WriteLine("Сканер запущен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка запуска сканера: {ex.Message}");
        }
    }

    // Останавливает прослушивание сканера
    public void StopListening()
    {
        lock (_lock)
        {
            if (!_isListening || _scannerService == null || _disposed) return;
        }

        try
        {
            _scannerService.StopListening();
            
            lock (_lock)
            {
                _isListening = false;
            }

            System.Diagnostics.Debug.WriteLine("Сканер остановлен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка остановки сканера: {ex.Message}");
        }
    }

    // Возвращает статус прослушивания
    public bool IsListening
    {
        get
        {
            lock (_lock)
            {
                return _isListening && !_disposed;
            }
        }
    }

    // Освобождает ресурсы
    public void Dispose()
    {
        if (_disposed) return;
        
        StopListening();
        _scannerService?.Dispose();
        _scannerService = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}