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
    private bool _isLoginExecuting;

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

    [ObservableProperty]
    private string _deviceIp = string.Empty;

    // ✅ Новые свойства для UI
    [ObservableProperty]
    private string _titleText = "ALPHA WMS";

    [ObservableProperty]
    private string _subtitleText = "Терминал сбора данных";

    [ObservableProperty]
    private string _serverAddressDisplay = string.Empty;

    [ObservableProperty]
    private bool _isLoginEnabled = true;

    public event EventHandler<LoginResponse>? LoginSuccess;

    public LoginViewModel()
    {
        _authService = new AuthService();
        _discoveryService = new ServerDiscoveryService();
        
          // ✅ Подписываемся на событие прогресса
        _discoveryService.ScanProgressChanged += OnScanProgressChanged;

        ServerAddress = Constants.ApiBaseUrl;
        ServerAddressDisplay = $"🌐 {ServerAddress}";
        DeviceIp = _discoveryService.GetLocalIpAddress() ?? "не определен";
        
        
        _ = CheckServerAsync();
    }

    // ✅ Обработчик изменения диапазона сканирования
    private void OnScanProgressChanged(object? sender, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ServerAddressDisplay = message;
        });
    }

    public async Task CheckServerAsync()
    {
        try
        {
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
        catch
        {
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
        // ✅ Меняем UI перед поиском
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsLoading = true;
            SearchButtonText = "⏳ Поиск...";
            ErrorMessage = string.Empty;
            ServerStatusText = "Поиск сервера...";
            ServerStatusIcon = "🔍";
            ServerStatusColor = Colors.Orange;
            TitleText = "🔍 Идет поиск сервера";
            SubtitleText = "Пожалуйста, подождите...";
            IsLoginEnabled = false;
        });

        try
        {
            var serverAddress = await _discoveryService.GetServerAddress();
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!string.IsNullOrEmpty(serverAddress))
                {
                    ServerAddress = serverAddress;
                    Constants.ApiBaseUrl = serverAddress;
                    ServerAddressDisplay = $"🌐 {serverAddress}";
                    
                    ServerStatusText = "✅ Сервер найден!";
                    ServerStatusIcon = "✅";
                    ServerStatusColor = Colors.Green;
                    
                    Application.Current?.MainPage?.DisplayAlert(
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
                    ServerAddressDisplay = $"🌐 {ServerAddress}";
                    
                    Application.Current?.MainPage?.DisplayAlert(
                        "❌ Сервер не найден",
                        "Не удалось найти сервер в сети.\n\n" +
                        "Проверьте:\n" +
                        "• Подключение к Wi-Fi\n" +
                        "• Что сервер запущен\n" +
                        "• Что устройства в одной сети\n" +
                        $"IP устройства: {DeviceIp}",
                        "OK"
                    );
                }
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ServerStatusText = "Ошибка поиска";
                ServerStatusIcon = "⚠️";
                ServerStatusColor = Colors.Red;
                ServerAddressDisplay = $"🌐 {ServerAddress}";
                
                Application.Current?.MainPage?.DisplayAlert(
                    "Ошибка",
                    $"Ошибка поиска сервера: {ex.Message}",
                    "OK"
                );
            });
        }
        finally
        {
            // ✅ Возвращаем UI в исходное состояние
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsLoading = false;
                SearchButtonText = "🔍 Поиск сервера";
                TitleText = "ALPHA WMS";
                SubtitleText = "Терминал сбора данных";
                IsLoginEnabled = true;
                
                // Если сервер не найден — показываем адрес
                if (ServerStatusColor != Colors.Green)
                {
                    ServerAddressDisplay = $"🌐 {ServerAddress}";
                }
            });
        }
    }

    [RelayCommand]
    private async Task Login()
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Введите имя пользователя и пароль";
            return;
        }

        // ✅ Блокируем кнопку входа
        IsLoginEnabled = false;
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var serverAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            if (!serverAvailable)
            {
                ErrorMessage = "Сервер не доступен. Нажмите 'Поиск сервера'";
                return;
            }

            var response = await _authService.Login(Username, Password);
            LoginSuccess?.Invoke(this, response);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            // ✅ Разблокируем кнопку входа (всегда)
            IsLoading = false;
            IsLoginEnabled = true;
        }
    }

    [RelayCommand]
    private void ClearError()
    {
        ErrorMessage = string.Empty;
    }
}