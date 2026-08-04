using Android.Content;
using Android.OS;
using Android.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace FlowerWms.Tsd.Platforms.Android;

// ✅ Убираем DynamicDependency с класса, ставим на методы
public class UrovoScannerService : Java.Lang.Object, IDisposable
{
    private readonly Context _context;
    private BroadcastReceiver? _receiver;
    private bool _isListening;
    private readonly Action<string>? _onBarcodeScanned;
    private bool _disposed;

    public UrovoScannerService(Context context, Action<string>? onBarcodeScanned)
    {
        _context = context;
        _onBarcodeScanned = onBarcodeScanned;
    }

    public void StartListening()
    {
        if (_isListening) return;

        try
        {
            var filter = new IntentFilter();
            
            filter.AddAction("com.urovo.scanner.ACTION_BARCODE_RESULT");
            filter.AddAction("com.urovo.scanner.ACTION_SCAN_RESULT");
            filter.AddAction("com.urovo.scanner.ACTION_DECODE");
            filter.AddAction("com.urovo.scanner.ACTION_SCAN");
            filter.AddAction("android.intent.action.VIEW");
            filter.AddAction("com.symbol.datawedge.api.ACTION_BARCODE");

            _receiver = new UrovoBroadcastReceiver(_onBarcodeScanned);
            _context.RegisterReceiver(_receiver, filter);
            _isListening = true;

            ActivateScanner();
            
            System.Diagnostics.Debug.WriteLine("✅ Urovo сканер активирован");
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка активации сканера: {ex.Message}");
        }
    }

    private void ActivateScanner()
    {
        try
        {
            var intent = new Intent("com.urovo.scanner.ACTION_START_SCAN");
            _context.SendBroadcast(intent);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Не удалось активировать сканер: {ex.Message}");
        }
    }

    public void StopListening()
    {
        if (!_isListening || _receiver == null) return;

        try
        {
            _context.UnregisterReceiver(_receiver);
            _receiver.Dispose();
            _receiver = null;
            _isListening = false;
            
            System.Diagnostics.Debug.WriteLine("✅ Urovo сканер остановлен");
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка остановки сканера: {ex.Message}");
        }
    }

    public bool IsListening => _isListening;

    public new void Dispose()
    {
        if (_disposed) return;
        
        StopListening();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

// ✅ Убираем DynamicDependency с класса, ставим на методы
public class UrovoBroadcastReceiver : BroadcastReceiver
{
    private readonly Action<string>? _onBarcodeScanned;
    private long _lastScanTime;
    private string? _lastBarcode;

    public UrovoBroadcastReceiver(Action<string>? onBarcodeScanned)
    {
        _onBarcodeScanned = onBarcodeScanned;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(UrovoBroadcastReceiver))]
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == null) return;

        var barcode = ExtractBarcode(intent);

        if (!string.IsNullOrEmpty(barcode))
        {
            var now = SystemClock.UptimeMillis();
            if (now - _lastScanTime < 300)
            {
                System.Diagnostics.Debug.WriteLine($"⏳ Дебаунс: {barcode}");
                return;
            }

            if (barcode == _lastBarcode)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Повторный штрихкод: {barcode}");
                return;
            }

            _lastScanTime = now;
            _lastBarcode = barcode;

            var cleaned = CleanBarcode(barcode);
            System.Diagnostics.Debug.WriteLine($"📷 Штрихкод Urovo: {cleaned}");
            _onBarcodeScanned?.Invoke(cleaned);
        }
    }

    private string ExtractBarcode(Intent intent)
    {
        var action = intent.Action;

        if (action == "com.urovo.scanner.ACTION_BARCODE_RESULT" ||
            action == "com.urovo.scanner.ACTION_SCAN_RESULT")
        {
            var barcode = intent.GetStringExtra("barcode");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("data");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("Barcode");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("DATA");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
        }

        if (action == Intent.ActionView)
        {
            var uri = intent.Data;
            if (uri != null && !string.IsNullOrEmpty(uri.ToString()))
            {
                return uri.ToString();
            }
        }

        if (action == "com.symbol.datawedge.api.ACTION_BARCODE")
        {
            var barcode = intent.GetStringExtra("com.ubx.datawedge.data_string");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("barcode_string");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("data");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
        }

        return string.Empty;
    }

    private string CleanBarcode(string raw)
    {
        var cleaned = raw;

        var prefixes = new[] { "] ", "]", "\\", "/", "--", "??" };
        foreach (var prefix in prefixes)
        {
            if (cleaned.StartsWith(prefix))
            {
                cleaned = cleaned.Substring(prefix.Length);
                break;
            }
        }

        cleaned = cleaned.Trim();
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[\x00-\x1F\x7F]", "");

        return cleaned;
    }
}