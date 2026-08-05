using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace FlowerWms.Tsd.Views;

public partial class BasePage : ContentPage
{
    public BasePage()
    {
        // ❌ НЕ ВЫЗЫВАЕМ InitializeComponent() здесь!
        // Он будет вызван в каждой наследующей странице
        
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
            if (context == null) return 30;
            
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