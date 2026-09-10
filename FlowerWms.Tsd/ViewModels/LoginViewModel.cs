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
    private readonly SyncService _syncService;
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
    private bool _isSyncingPartialBoxes;

    [ObservableProperty]
    private string _loadingStatusText = "Подключение к серверу...";

    public event EventHandler<LoginResponse>? LoginSuccess;

    public LoginViewModel()
    {
        _authService = new AuthService();
        _discoveryService = new ServerDiscoveryService();
        _syncService = new SyncService();

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
        if (IsLoading) return; // защита от повторного нажатия

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsLoading = true;
            LoadingStatusText = "🔍 Поиск сервера...";
            SearchButtonText = "Поиск...";
            ErrorMessage = string.Empty;
            ServerStatusText = "Поиск сервера...";
            ServerStatusIcon = "🔍";
            ServerStatusColor = Colors.Orange;
            TitleText = "Идет поиск сервера";
            SubtitleText = "Пожалуйста, подождите...";
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

                if (ServerStatusColor != Colors.Green)
                {
                    ServerAddressDisplay = $"{ServerAddress}";
                }
            });
        }
    }

    // Вход в систему
    [RelayCommand]
    private async Task Login()
    {
        if (_isLoginExecuting) return; // защита от двойного нажатия

        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Введите имя пользователя и пароль";
            return;
        }

        _isLoginExecuting = true;
        var loginSucceeded = false;

        // ✅ Сразу показываем оверлей и первый статус
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsLoading = true;
            LoadingStatusText = "⏳ Подключение к серверу...";
            ErrorMessage = string.Empty;
        });

        // Даём UI один кадр, чтобы оверлей успел отрисоваться
        await Task.Delay(50);

        try
        {
            // 1. Проверяем доступность сервера
            var serverAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            if (!serverAvailable)
            {
                ErrorMessage = "Сервер не доступен. Нажмите 'Поиск сервера'";
                return;
            }

            // 2. Авторизация
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingStatusText = "🔐 Авторизация...";
            });

            var response = await _authService.Login(Username, Password);

            if (response == null || string.IsNullOrEmpty(response.Token))
            {
                ErrorMessage = "Неверный логин или пароль";
                return;
            }

            // 3. Синхронизация после логина (по алгоритму п.1)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingStatusText = "🔄 Синхронизация данных...";
            });

            await SyncAfterLogin();

            // 4. Финал
            loginSucceeded = true;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingStatusText = "✅ Готово!";
            });

            // Даём пользователю увидеть "Готово" перед навигацией
            await Task.Delay(400);

            LoginSuccess?.Invoke(this, response);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _isLoginExecuting = false;

            // ✅ Если логин успешен — оставляем оверлей, пока идёт навигация.
            // Он снимется вместе с удалением страницы (Navigation.RemovePage).
            if (!loginSucceeded)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsLoading = false;
                });
            }
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