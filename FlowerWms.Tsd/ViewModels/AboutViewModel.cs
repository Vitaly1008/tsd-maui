using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для страницы "О программе"
public partial class AboutViewModel : ObservableObject
{

    [ObservableProperty]
    private string _appVersion = VersionHelper.GetVersion();

    [ObservableProperty]
    private string _buildDate = $"📅 Дата сборки: {VersionHelper.GetBuildDate()}";

    [ObservableProperty]
    private string _appName = "ALPHA WMS";

    [ObservableProperty]
    private string _appIcon = "📦";

    [ObservableProperty]
    private string _appMode = "Full Offline Mode";

    [ObservableProperty]
    private string _copyright = "© 2026 Alpha Flowers";

    [ObservableProperty]
    private string _copyrightDetail = "Все права защищены";
}