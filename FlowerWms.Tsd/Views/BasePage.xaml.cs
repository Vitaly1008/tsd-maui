using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.Views;

public partial class BasePage : ContentPage
{
    public BasePage()
    {
        InitializeComponent();
        
        // Устанавливаем отступ для статус-бара на Android
        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            var statusBarHeight = GetStatusBarHeight();
            // ✅ Убедимся, что паддинг не перекрывает контент
            // Используем безопасную область
            Padding = new Thickness(0, statusBarHeight, 0, 0);
        }
    }

    private int GetStatusBarHeight()
    {
        try
        {
            var context = Android.App.Application.Context;
            var resourceId = context.Resources.GetIdentifier("status_bar_height", "dimen", "android");
            if (resourceId > 0)
            {
                return context.Resources.GetDimensionPixelSize(resourceId);
            }
            return 30;
        }
        catch
        {
            return 30;
        }
    }
}