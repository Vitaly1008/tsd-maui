using System.Reflection;

namespace FlowerWms.Tsd.Helpers;

// Вспомогательный класс для получения информации о версии приложения
public static class VersionHelper
{
    private static string? _cachedVersion;
    private static string? _cachedBuildDate;

    // Получает версию приложения в формате v1.x.x.x
    public static string GetVersion()
    {
        if (!string.IsNullOrEmpty(_cachedVersion))
            return _cachedVersion;

        try
        {
            // Приоритет 1: Используем VersionInfo (удобно для ручного изменения)
            var versionFromInfo = VersionInfo.Version;
            if (!string.IsNullOrEmpty(versionFromInfo) && versionFromInfo != "1.0.0.0")
            {
                _cachedVersion = $"v{versionFromInfo}";
                return _cachedVersion;
            }

            // Приоритет 2: Пробуем получить из Assembly
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            
            if (version != null && version.ToString() != "0.0.0.0")
            {
                _cachedVersion = $"v{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                return _cachedVersion;
            }

            // Приоритет 3: Пробуем получить из AssemblyFileVersion
            var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            if (fileVersion != null && !string.IsNullOrEmpty(fileVersion.Version))
            {
                var parts = fileVersion.Version.Split('.');
                if (parts.Length >= 4)
                {
                    _cachedVersion = $"v{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}";
                    return _cachedVersion;
                }
            }

            // Значение по умолчанию
            _cachedVersion = "v1.0.0.0";
            return _cachedVersion;
        }
        catch
        {
            _cachedVersion = "v1.0.0.0";
            return _cachedVersion;
        }
    }

    // Получает дату и время сборки приложения
    public static string GetBuildDate()
    {
        if (!string.IsNullOrEmpty(_cachedBuildDate))
            return _cachedBuildDate;

        try
        {
            // Приоритет 1: Используем VersionInfo
            var buildDateFromInfo = VersionInfo.BuildDate;
            if (!string.IsNullOrEmpty(buildDateFromInfo) && buildDateFromInfo != "2026-08-14 12:00:00")
            {
                _cachedBuildDate = buildDateFromInfo;
                return _cachedBuildDate;
            }

            // Приоритет 2: Пробуем получить из атрибута сборки
            var assembly = Assembly.GetExecutingAssembly();
            var buildAttribute = assembly.GetCustomAttribute<BuildDateAttribute>();
            if (buildAttribute != null)
            {
                _cachedBuildDate = buildAttribute.BuildDate.ToString("yyyy-MM-dd HH:mm:ss");
                return _cachedBuildDate;
            }

            // Приоритет 3: Используем дату изменения файла сборки
            var buildTime = File.GetLastWriteTime(assembly.Location);
            if (buildTime.Year > 2000)
            {
                _cachedBuildDate = buildTime.ToString("yyyy-MM-dd HH:mm:ss");
                return _cachedBuildDate;
            }

            // Значение по умолчанию
            _cachedBuildDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return _cachedBuildDate;
        }
        catch
        {
            _cachedBuildDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return _cachedBuildDate;
        }
    }
}

// Атрибут для хранения даты сборки
[AttributeUsage(AttributeTargets.Assembly)]
public class BuildDateAttribute : Attribute
{
    public DateTime BuildDate { get; }

    public BuildDateAttribute(string buildDate)
    {
        BuildDate = DateTime.Parse(buildDate);
    }
}