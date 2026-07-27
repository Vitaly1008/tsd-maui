using Android.Content;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Platforms.Android;

public static class AndroidContext
{
    public static Context? Current { get; set; }
}

public class BarcodeService : IBarcodeService
{
    private BarcodeBroadcastReceiver? _receiver;
    private bool _isListening;

    public event Action<string>? OnBarcodeScanned;

    public BarcodeService() // ✅ Добавлен конструктор
    {
        // Инициализация при создании
    }

    public void StartListening()
    {
        if (_isListening) return;

        try
        {
            var context = AndroidContext.Current;
            if (context == null) 
            {
                System.Diagnostics.Debug.WriteLine("❌ Контекст Android недоступен");
                return;
            }

            _receiver = new BarcodeBroadcastReceiver((barcode) =>
            {
                OnBarcodeScanned?.Invoke(barcode);
            });

            var filter = new IntentFilter();
            filter.AddAction("com.symbol.datawedge.api.ACTION_BARCODE");
            filter.AddAction("com.urovo.scanner.ACTION_BARCODE_RESULT");
            filter.AddAction("com.urovo.scanner.ACTION_SCAN_RESULT");
            filter.AddAction("com.android.scanner.ACTION_SCAN");

            context.RegisterReceiver(_receiver, filter);
            _isListening = true;
            
            System.Diagnostics.Debug.WriteLine("✅ Сканер запущен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка запуска сканера: {ex.Message}");
        }
    }

    public void StopListening()
    {
        if (!_isListening || _receiver == null) return;

        try
        {
            var context = AndroidContext.Current;
            if (context != null)
            {
                context.UnregisterReceiver(_receiver);
            }
            _receiver.Dispose();
            _receiver = null;
            _isListening = false;
            
            System.Diagnostics.Debug.WriteLine("✅ Сканер остановлен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка остановки сканера: {ex.Message}");
        }
    }

    public bool IsListening => _isListening;

    public void Dispose()
    {
        StopListening();
        GC.SuppressFinalize(this);
    }
}