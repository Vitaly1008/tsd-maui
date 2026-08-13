using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Views;

// Базовая страница для всех страниц приложения
public partial class BasePage : ContentPage
{
    public BasePage()
    {
        // InitializeComponent() вызывается в каждой наследующей странице
        
        if (DeviceInfo.Current.Platform == DevicePlatform.Android)
        {
            var statusBarHeight = StatusBarHelper.GetStatusBarHeight();
            Padding = new Thickness(0, statusBarHeight, 0, 0);
        }
    }
}