using Android.Content;
using Android.OS;
using Android.Util;
using Android.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace FlowerWms.Tsd.Platforms.Android;

// BroadcastReceiver для обработки сканирования штрихкодов
public class BarcodeBroadcastReceiver : BroadcastReceiver
{
    private readonly Action<string>? _onBarcodeScanned;
    private long _lastScanTime;
    private string? _lastBarcode;

    public BarcodeBroadcastReceiver(Action<string>? onBarcodeScanned)
    {
        _onBarcodeScanned = onBarcodeScanned;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(BarcodeBroadcastReceiver))]
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == null) return;

        var barcode = ExtractBarcode(intent);

        if (!string.IsNullOrEmpty(barcode))
        {
            var now = SystemClock.UptimeMillis();
            if (now - _lastScanTime < 300)
            {
                Log.Debug("Barcode", $"Дебаунс: {barcode}");
                return;
            }

            if (barcode == _lastBarcode)
            {
                Log.Debug("Barcode", $"Повторный штрихкод: {barcode}");
                return;
            }

            _lastScanTime = now;
            _lastBarcode = barcode;

            var cleaned = CleanBarcode(barcode);
            Log.Debug("Barcode", $"Штрихкод: {cleaned}");

            Vibrate(context);
            _onBarcodeScanned?.Invoke(cleaned);
        }
    }

    // Вибрация при успешном сканировании
    private void Vibrate(Context? context)
    {
        try
        {
            if (context == null) return;

            var vibrator = context.GetSystemService(Context.VibratorService) as Vibrator;
            if (vibrator == null || !vibrator.HasVibrator) return;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var effect = VibrationEffect.CreateOneShot(100, VibrationEffect.DefaultAmplitude);
                vibrator.Vibrate(effect);
            }
            else
            {
#pragma warning disable CS0618
                vibrator.Vibrate(100);
#pragma warning restore CS0618
            }
        }
        catch
        {
            // Игнорируем ошибки вибрации
        }
    }

    // Извлекает штрихкод из Intent для разных типов сканеров
    private string ExtractBarcode(Intent intent)
    {
        var action = intent.Action;

        // DataWedge (Symbol/Zebra)
        if (action == "com.symbol.datawedge.api.ACTION_BARCODE")
        {
            var barcode = intent.GetStringExtra("com.ubx.datawedge.data_string");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("barcode_string");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("data");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
        }

        // Urovo сканер
        if (action == "com.urovo.scanner.ACTION_BARCODE_RESULT" ||
            action == "com.urovo.scanner.ACTION_SCAN_RESULT")
        {
            var barcode = intent.GetStringExtra("barcode");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
            
            barcode = intent.GetStringExtra("data");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
        }

        // Android стандартный сканер
        if (action == "com.android.scanner.ACTION_SCAN")
        {
            var barcode = intent.GetStringExtra("SCAN_RESULT");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
        }

        // RS Core (Realscan)
        if (action == "rs.core.hw.Barcode" || action == "rs.core.hw.ScanResult")
        {
            var barcode = intent.GetStringExtra("barcode");
            if (!string.IsNullOrEmpty(barcode)) return barcode;
        }

        // ACTION_VIEW (URI)
        if (action == Intent.ActionView)
        {
            var uri = intent.Data;
            if (uri != null && !string.IsNullOrEmpty(uri.ToString()))
            {
                return uri.ToString();
            }
        }

        return string.Empty;
    }

    // Очищает сырой штрихкод от префиксов и управляющих символов
    public static string CleanBarcode(string raw)
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