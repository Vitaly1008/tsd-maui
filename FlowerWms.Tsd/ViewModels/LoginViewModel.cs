using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для экрана входа
public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly ServerDiscoveryService _discoveryService;
    private readonly SyncService _syncService; //  ДОБАВЛЕНО
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
    private string _searchButtonText = "Поиск сервера";

    [ObservableProperty]
    private string _deviceIp = string.Empty;

    [ObservableProperty]
    private string _titleText = "ALPHA WMS";

    [ObservableProperty]
    private string _subtitleText = "Терминал сбора данных";

    [ObservableProperty]
    private string _serverAddressDisplay = string.Empty;

    [ObservableProperty]
    private bool _isLoginEnabled = true;

    [ObservableProperty]
    private bool _isSyncingPartialBoxes; //  ДОБАВЛЕНО: индикатор загрузки частичных коробок

    public event EventHandler<LoginResponse>? LoginSuccess;

    public LoginViewModel()
    {
        _authService = new AuthService();
        _discoveryService = new ServerDiscoveryService();
        _syncService = new SyncService(); //  ДОБАВЛЕНО
        
        _discoveryService.ScanProgressChanged += OnScanProgressChanged;

        ServerAddress = Constants.ApiBaseUrl;
        ServerAddressDisplay = $"{ServerAddress}";
        DeviceIp = _discoveryService.GetLocalIpAddress() ?? "не определен";
        
        _ = CheckServerAsync();
    }

    // Обработчик прогресса сканирования
    private void OnScanProgressChanged(object? sender, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ServerAddressDisplay = message;
        });
    }

    // Проверяет доступность сервера
    public async Task CheckServerAsync()
    {
        try
        {
            var isAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (isAvailable)
                {
                    ServerStatusText = "Сервер доступен";
                    ServerStatusIcon = "✅";
                    ServerStatusColor = Colors.Green;
                }
                else
                {
                    ServerStatusText = "Сервер не найден";
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

    // Выполняет поиск сервера
    [RelayCommand]
    private async Task FindServer()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsLoading = true;
            SearchButtonText = "Поиск...";
            ErrorMessage = string.Empty;
            ServerStatusText = "Поиск сервера...";
            ServerStatusIcon = "🔍";
            ServerStatusColor = Colors.Orange;
            TitleText = "Идет поиск сервера";
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
                    ServerAddressDisplay = $"{serverAddress}";
                    
                    ServerStatusText = "Сервер найден";
                    ServerStatusIcon = "✅";
                    ServerStatusColor = Colors.Green;
                    
                    Application.Current?.MainPage?.DisplayAlert(
                        "Сервер найден",
                        $"Сервер доступен по адресу:\n{serverAddress}",
                        "OK"
                    );
                }
                else
                {
                    ServerStatusText = "Сервер не найден";
                    ServerStatusIcon = "❌";
                    ServerStatusColor = Colors.Red;
                    ServerAddressDisplay = $"{ServerAddress}";
                    
                    Application.Current?.MainPage?.DisplayAlert(
                        "Сервер не найден",
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
                ServerAddressDisplay = $"{ServerAddress}";
                
                Application.Current?.MainPage?.DisplayAlert(
                    "Ошибка",
                    $"Ошибка поиска сервера: {ex.Message}",
                    "OK"
                );
            });
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsLoading = false;
                SearchButtonText = "Поиск сервера";
                TitleText = "ALPHA WMS";
                SubtitleText = "Терминал сбора данных";
                IsLoginEnabled = true;
                
                if (ServerStatusColor != Colors.Green)
                {
                    ServerAddressDisplay = $"{ServerAddress}";
                }
            });
        }
    }

    //  ИСПРАВЛЕНО: добавлен вызов SyncPartialBoxes после успешного логина
    [RelayCommand]
    private async Task Login()
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Введите имя пользователя и пароль";
            return;
        }

        IsLoginEnabled = false;
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // 1. Проверяем доступность сервера
            var serverAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            if (!serverAvailable)
            {
                ErrorMessage = "Сервер не доступен. Нажмите 'Поиск сервера'";
                return;
            }

            // 2. Выполняем аутентификацию
            var response = await _authService.Login(Username, Password);
            
            // 3. Проверяем успешность входа
            if (response == null || string.IsNullOrEmpty(response.Token))
            {
                ErrorMessage = "Неверный логин или пароль";
                return;
            }

            //  4. ПО АЛГОРИТМУ (п.1): загружаем частичные коробки с сервера
            await SyncAfterLogin();

            // 5. Уведомляем об успешном входе
            LoginSuccess?.Invoke(this, response);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            IsLoginEnabled = true;
        }
    }

    /// <summary>
    /// СИНХРОНИЗАЦИЯ ПОСЛЕ ЛОГИНА (по алгоритму п.1)
    /// </summary>
    private async Task SyncAfterLogin()
    {
        try
        {
            IsSyncingPartialBoxes = true;
            
            System.Diagnostics.Debug.WriteLine("🔄 Синхронизация после логина...");
            
            // ✅ ВЫЗЫВАЕМ СИНХРОНИЗАЦИЮ ПОСЛЕ ЛОГИНА
            // Она проверит очередь и обновит кэш
            await _syncService.SyncAfterLogin();
            
            System.Diagnostics.Debug.WriteLine("✅ Синхронизация после логина завершена");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка синхронизации после логина: {ex.Message}");
            
            // Показываем предупреждение, но не прерываем вход
            await Application.Current?.MainPage?.DisplayAlert(
                "Предупреждение",
                $"Не удалось выполнить синхронизацию:\n{ex.Message}\n\n" +
                "Вы сможете синхронизировать данные позже вручную.",
                "OK"
            );
        }
        finally
        {
            IsSyncingPartialBoxes = false;
        }
    }

    // Очищает сообщение об ошибке
    [RelayCommand]
    private void ClearError()
    {
        ErrorMessage = string.Empty;
    }
}