using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly ServerDiscoveryService _discoveryService;
    private readonly LoggerService _logger;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    private string _serverStatusText = "Проверка подключения...";

    [ObservableProperty]
    private string _serverStatusIcon = "⏳";

    [ObservableProperty]
    private Color _serverStatusColor = Colors.Orange;

    [ObservableProperty]
    private string _searchButtonText = "🔍 Поиск сервера";

    public event EventHandler<LoginResponse>? LoginSuccess;

    public LoginViewModel()
    {
        _logger = new LoggerService();
        _authService = new AuthService();
        _discoveryService = new ServerDiscoveryService();
        
        ServerAddress = Constants.ApiBaseUrl;
        
        // Проверяем сервер при создании
        _ = CheckServerAsync();
    }

    /// <summary>
    /// Проверка сервера (без поиска)
    /// </summary>
    public async Task CheckServerAsync()
    {
        _logger.Info($"🔍 Проверка сервера: {Constants.ApiBaseUrl}");
        
        try
        {
            // Используем обычный Ping, не PingServerWithInfo
            var isAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (isAvailable)
                {
                    ServerStatusText = "Сервер доступен ✓";
                    ServerStatusIcon = "✅";
                    ServerStatusColor = Colors.Green;
                }
                else
                {
                    ServerStatusText = "Сервер не найден ✗";
                    ServerStatusIcon = "❌";
                    ServerStatusColor = Colors.Red;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка проверки сервера: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ServerStatusText = "Ошибка проверки";
                ServerStatusIcon = "⚠️";
                ServerStatusColor = Colors.Orange;
            });
        }
    }

    [RelayCommand]
    private async Task FindServer()
    {
        _logger.Info("🔍 ===== РУЧНОЙ ПОИСК СЕРВЕРА =====");
        
        IsLoading = true;
        SearchButtonText = "⏳ Поиск...";
        ErrorMessage = string.Empty;
        ServerStatusText = "Поиск сервера...";
        ServerStatusIcon = "🔍";
        ServerStatusColor = Colors.Orange;

        try
        {
            var serverAddress = await _discoveryService.DiscoverServer();
            
            if (!string.IsNullOrEmpty(serverAddress))
            {
                ServerAddress = serverAddress;
                Constants.ApiBaseUrl = serverAddress;
                
                ServerStatusText = "✅ Сервер найден!";
                ServerStatusIcon = "✅";
                ServerStatusColor = Colors.Green;
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Сервер найден",
                    $"Сервер доступен по адресу:\n{serverAddress}",
                    "OK"
                );
            }
            else
            {
                ServerStatusText = "❌ Сервер не найден";
                ServerStatusIcon = "❌";
                ServerStatusColor = Colors.Red;
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "❌ Сервер не найден",
                    "Не удалось найти сервер в сети.\n\n" +
                    "Проверьте:\n" +
                    "• Подключение к Wi-Fi\n" +
                    "• Что сервер запущен\n" +
                    "• Что устройства в одной сети (маска 255.255.255.0)",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка поиска сервера: {ex.Message}");
            
            ServerStatusText = "Ошибка поиска";
            ServerStatusIcon = "⚠️";
            ServerStatusColor = Colors.Red;
            
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                $"Ошибка поиска сервера: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
            SearchButtonText = "🔍 Поиск сервера";
            _logger.Info("🔍 ===== КОНЕЦ ПОИСКА СЕРВЕРА =====");
        }
    }

    [RelayCommand]
    private async Task Login()
    {
        _logger.Info("🔑 Попытка входа...");
        
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Введите имя пользователя и пароль";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // Проверяем сервер перед входом
            var serverAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            if (!serverAvailable)
            {
                ErrorMessage = "Сервер не доступен. Нажмите 'Поиск сервера'";
                IsLoading = false;
                return;
            }

            var response = await _authService.Login(Username, Password);
            LoginSuccess?.Invoke(this, response);
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка входа: {ex.Message}");
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearError()
    {
        ErrorMessage = string.Empty;
    }
}