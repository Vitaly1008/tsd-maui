using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для страницы "О программе"
public partial class AboutViewModel : ObservableObject
{
    private readonly AuthService _authService;

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

    public AboutViewModel()
    {
        _authService = new AuthService();
    }

    // Проверяет пароль пользователя
    private async Task<bool> ValidateUserPassword(string password)
    {
        try
        {
            // Получаем текущего пользователя из SecureStorage
            var userJson = await SecureStorage.GetAsync("user");
            if (string.IsNullOrEmpty(userJson))
                return false;

            // Парсим данные пользователя
            var user = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(userJson);
            var username = user?.GetValueOrDefault("username") ?? string.Empty;

            if (string.IsNullOrEmpty(username))
                return false;

            // Пытаемся выполнить логин с введенным паролем
            var authService = new AuthService();
            var loginResult = await authService.Login(username, password);
            
            return loginResult != null && !string.IsNullOrEmpty(loginResult.Token);
        }
        catch
        {
            return false;
        }
    }
}