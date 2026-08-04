using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.Views;

// ✅ Делаем класс abstract, чтобы не вызывать InitializeComponent напрямую
public abstract partial class BasePage : ContentPage
{
    public BasePage()
    {
        // ✅ Убираем InitializeComponent() - он вызывается в наследниках
        // Устанавливаем отступ для статус-бара на Android
        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            var statusBarHeight = GetStatusBarHeight();
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