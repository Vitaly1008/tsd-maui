using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.Helpers;

public static class StatusBarHelper
{
    private static int? _cachedHeight;
    private static Thickness? _cachedPadding;

    /// <summary>
    /// Получает высоту статус-бара в пикселях
    /// </summary>
    public static int GetStatusBarHeight()
    {
        if (_cachedHeight.HasValue)
            return _cachedHeight.Value;

        try
        {
            var context = Android.App.Application.Context;
            
            // Пытаемся получить из ресурсов Android
            var resourceId = context.Resources.GetIdentifier("status_bar_height", "dimen", "android");
            if (resourceId > 0)
            {
                _cachedHeight = context.Resources.GetDimensionPixelSize(resourceId);
                return _cachedHeight.Value;
            }

            // Альтернативный способ через WindowManager
            var windowManager = context.GetSystemService(Context.WindowService) as IWindowManager;
            if (windowManager != null)
            {
                var displayMetrics = new Android.Util.DisplayMetrics();
                windowManager.DefaultDisplay.GetMetrics(displayMetrics);
                
                // На некоторых устройствах статус-бар имеет фиксированную высоту
                _cachedHeight = (int)(25 * displayMetrics.Density); // 25dp
                return _cachedHeight.Value;
            }

            _cachedHeight = 30; // Значение по умолчанию
            return _cachedHeight.Value;
        }
        catch
        {
            _cachedHeight = 30; // Значение по умолчанию
            return _cachedHeight.Value;
        }
    }

    /// <summary>
    /// Получает высоту статус-бара в dp (device independent pixels)
    /// </summary>
    public static double GetStatusBarHeightDp()
    {
        var heightPx = GetStatusBarHeight();
        var density = DeviceDisplay.Current.MainDisplayInfo.Density;
        return heightPx / density;
    }

    /// <summary>
    /// Возвращает Thickness для Padding с отступом сверху под статус-бар
    /// </summary>
    public static Thickness GetStatusBarPadding()
    {
        if (_cachedPadding.HasValue)
            return _cachedPadding.Value;

        var height = GetStatusBarHeight();
        _cachedPadding = new Thickness(0, height, 0, 0);
        return _cachedPadding.Value;
    }

    /// <summary>
    /// Сбрасывает кэш (использовать при изменении конфигурации)
    /// </summary>
    public static void ClearCache()
    {
        _cachedHeight = null;
        _cachedPadding = null;
    }
}