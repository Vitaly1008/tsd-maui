namespace FlowerWms.Tsd.Services;

public class LoggerService
{
    private readonly string _logFilePath;
    private readonly object _lock = new object();

    public LoggerService()
    {
        var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
        
        _logFilePath = Path.Combine(logDir, $"app_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        Info($"📝 Лог-файл создан: {_logFilePath}");
    }

    private void WriteToFile(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] {level} {message}";
                
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                
                // Также выводим в консоль для отладки
                Console.WriteLine(logLine);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка записи лога: {ex.Message}");
        }
    }

    public void Info(string message)
    {
        WriteToFile("ℹ️", message);
    }

    public void Success(string message)
    {
        WriteToFile("✅", message);
    }

    public void Warning(string message)
    {
        WriteToFile("⚠️", message);
    }

    public void Error(string message, Exception? ex = null)
    {
        WriteToFile("❌", message);
        if (ex != null)
        {
            WriteToFile("❌", $"  {ex.Message}");
            if (ex.StackTrace != null)
            {
                WriteToFile("❌", $"  {ex.StackTrace}");
            }
        }
    }

    public void Debug(string message)
    {
#if DEBUG
        WriteToFile("🔍", message);
#endif
    }

    /// <summary>
    /// Получить путь к файлу лога
    /// </summary>
    public string GetLogFilePath()
    {
        return _logFilePath;
    }

    /// <summary>
    /// Получить содержимое лога
    /// </summary>
    public string GetLogContent()
    {
        try
        {
            if (File.Exists(_logFilePath))
            {
                return File.ReadAllText(_logFilePath);
            }
            return "Лог-файл не найден";
        }
        catch (Exception ex)
        {
            return $"Ошибка чтения лога: {ex.Message}";
        }
    }

    /// <summary>
    /// Очистить лог
    /// </summary>
    public void ClearLog()
    {
        try
        {
            if (File.Exists(_logFilePath))
            {
                File.Delete(_logFilePath);
                Info("🗑️ Лог очищен");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка очистки лога: {ex.Message}");
        }
    }
}