using System.Diagnostics;

namespace FlowerWms.Tsd.Helpers;

/// <summary>
/// Сервис логирования с поддержкой adb logcat для Android
/// </summary>
public static class Logger
{
    private static readonly object _lock = new object();
    private static string _logFilePath = string.Empty;
    private static bool _useAndroidLog = false;

    static Logger()
    {
        try
        {
            // Определяем, запущены ли мы на Android
            if (DeviceInfo.Current.Platform == DevicePlatform.Android)
            {
                _useAndroidLog = true;
                
                // Также сохраняем в файл для истории
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(documentsPath, "FlowerWms.Tsd");
                
                if (!Directory.Exists(appFolder))
                    Directory.CreateDirectory(appFolder);
                
                _logFilePath = Path.Combine(appFolder, $"log_{DateTime.Now:yyyyMMdd}.txt");
            }
            else
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appFolder = Path.Combine(documentsPath, "FlowerWms.Tsd");
                
                if (!Directory.Exists(appFolder))
                    Directory.CreateDirectory(appFolder);
                
                _logFilePath = Path.Combine(appFolder, $"log_{DateTime.Now:yyyyMMdd}.txt");
            }
        }
        catch
        {
            _logFilePath = Path.Combine(FileSystem.AppDataDirectory, "error.log");
        }
    }

    public static void Info(string message)
    {
        WriteLog("INFO", message);
        System.Diagnostics.Debug.WriteLine($"ℹ️ {message}");
        
        if (_useAndroidLog)
            Android.Util.Log.Info("FlowerWms", $"[INFO] {message}");
        else
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [INFO] {message}");
    }

    public static void Warning(string message)
    {
        WriteLog("WARN", message);
        System.Diagnostics.Debug.WriteLine($"⚠️ {message}");
        
        if (_useAndroidLog)
            Android.Util.Log.Warn("FlowerWms", $"[WARN] {message}");
        else
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [WARN] {message}");
    }

    public static void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message}\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        WriteLog("ERROR", fullMessage);
        System.Diagnostics.Debug.WriteLine($"❌ {fullMessage}");
        
        if (_useAndroidLog)
            Android.Util.Log.Error("FlowerWms", $"[ERROR] {fullMessage}");
        else
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [ERROR] {fullMessage}");
    }

    public static void Debug(string message)
    {
        WriteLog("DEBUG", message);
        System.Diagnostics.Debug.WriteLine($"🔍 {message}");
        
        if (_useAndroidLog)
            Android.Util.Log.Debug("FlowerWms", $"[DEBUG] {message}");
        else
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [DEBUG] {message}");
    }

    public static void Verbose(string message)
    {
        WriteLog("VERBOSE", message);
        
        if (_useAndroidLog)
            Android.Util.Log.Verbose("FlowerWms", $"[VERBOSE] {message}");
        else
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [VERBOSE] {message}");
    }

    private static void WriteLog(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                if (string.IsNullOrEmpty(_logFilePath)) return;
                
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, logLine);
            }
        }
        catch
        {
            // Игнорируем ошибки записи в файл
        }
    }

    public static string GetLogFilePath()
    {
        return _logFilePath;
    }

    public static async Task<string> ReadLogAsync(int lines = 100)
    {
        try
        {
            if (!File.Exists(_logFilePath))
                return "Лог-файл не найден";
            
            var allLines = await File.ReadAllLinesAsync(_logFilePath);
            var lastLines = allLines.Length > lines 
                ? allLines.Skip(allLines.Length - lines) 
                : allLines;
            
            return string.Join(Environment.NewLine, lastLines);
        }
        catch (Exception ex)
        {
            return $"Ошибка чтения лога: {ex.Message}";
        }
    }

    public static void ClearLog()
    {
        try
        {
            if (File.Exists(_logFilePath))
                File.Delete(_logFilePath);
        }
        catch { }
    }
}