using Android.Content;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Platforms.Android;

public static class AndroidContext
{
    public static Context? Current { get; set; }
}

public class BarcodeService : IBarcodeService
{
    private UrovoScannerService? _scannerService;
    private bool _isListening;
    private readonly object _lock = new();

    public event Action<string>? OnBarcodeScanned;

    public BarcodeService()
    {
        // Инициализация будет при первом запуске
    }

    private void EnsureScannerService()
    {
        if (_scannerService != null) return;

        var context = AndroidContext.Current;
        if (context == null)
        {
            System.Diagnostics.Debug.WriteLine("❌ Контекст Android недоступен");
            return;
        }

        _scannerService = new UrovoScannerService(context, (barcode) =>
        {
            OnBarcodeScanned?.Invoke(barcode);
        });
    }

    public void StartListening()
    {
        lock (_lock)
        {
            if (_isListening) return;
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

            System.Diagnostics.Debug.WriteLine("✅ Сканер запущен (Urovo RT40)");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка запуска сканера: {ex.Message}");
        }
    }

    public void StopListening()
    {
        lock (_lock)
        {
            if (!_isListening || _scannerService == null) return;
        }

        try
        {
            _scannerService.StopListening();
            
            lock (_lock)
            {
                _isListening = false;
            }

            System.Diagnostics.Debug.WriteLine("✅ Сканер остановлен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка остановки сканера: {ex.Message}");
        }
    }

    public bool IsListening
    {
        get
        {
            lock (_lock)
            {
                return _isListening;
            }
        }
    }

    public void Dispose()
    {
        StopListening();
        _scannerService?.Dispose();
        _scannerService = null;
        GC.SuppressFinalize(this);
    }
}