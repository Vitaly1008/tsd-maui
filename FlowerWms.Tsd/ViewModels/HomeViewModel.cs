using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly SyncService _syncService;
    private readonly AuthService _authService;
    private readonly ServerDiscoveryService _discoveryService;
    private readonly NetworkService _networkService;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _connectionStatus = "Подключение...";
    
    [ObservableProperty]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    public event EventHandler? LogoutRequested;
    public event EventHandler? NavigateToReceivingRequested;
    public event EventHandler? NavigateToShippingRequested;
    public event EventHandler? NavigateToInventoryRequested;
    public event EventHandler? NavigateToPendingRequested;

    public HomeViewModel()
    {
        _syncService = new SyncService();
        _authService = new AuthService();
        _discoveryService = new ServerDiscoveryService();
        _networkService = new NetworkService();
        
        ServerAddress = Constants.ApiBaseUrl;

        // Подписываемся на события сети
        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;
        _networkService.ServerFound += OnServerFound;

        _syncService.StatusChanged += (s, status) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOnline = status == SyncStatus.Online;
                ConnectionStatus = status switch
                {
                    SyncStatus.Online => "Онлайн",
                    SyncStatus.Offline => "Офлайн",
                    SyncStatus.Syncing => "Синхронизация...",
                    _ => "Неизвестно"
                };
                IsSyncing = status == SyncStatus.Syncing;
            });
        };

        _syncService.PendingCountChanged += (s, count) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PendingCount = count;
            });
        };
    }

    private void OnNetworkStatusChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsOnline = isOnline;
            ConnectionStatus = isOnline ? "Онлайн" : "Офлайн";
            
            if (isOnline)
            {
                // Если появилась сеть - обновляем адрес
                ServerAddress = Constants.ApiBaseUrl;
            }
        });
    }

    private void OnServerFound(object? sender, string newAddress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ServerAddress = newAddress;
            IsOnline = true;
            ConnectionStatus = "Онлайн";
        });
    }

    public async Task Initialize()
    {
        try
        {
            // Запускаем проверку сети
            await _networkService.CheckNetworkAsync();
            
            await _syncService.Init();
            IsOnline = await _syncService.CheckInternetManual();
            PendingCount = await _syncService.GetPendingCount();
            ServerAddress = Constants.ApiBaseUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка Initialize: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Sync()
    {
        if (IsSyncing) return;
        await _syncService.SyncManual();
    }

    [RelayCommand]
    private async Task FindServer()
    {
        IsSearching = true;
        
        try
        {
            var serverAddress = await _discoveryService.GetServerAddress();
            
            if (!string.IsNullOrEmpty(serverAddress))
            {
                ServerAddress = serverAddress;
                Constants.ApiBaseUrl = serverAddress;
                IsOnline = true;
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Сервер найден",
                    $"Сервер доступен по адресу:\n{serverAddress}",
                    "OK"
                );
            }
            else
            {
                IsOnline = false;
                await Application.Current?.MainPage?.DisplayAlert(
                    "❌ Сервер не найден",
                    "Не удалось найти сервер в сети.\nПроверьте подключение.",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                $"Ошибка поиска сервера: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task Logout()
    {
        await _authService.Logout();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NavigateToReceiving()
    {
        NavigateToReceivingRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NavigateToShipping()
    {
        NavigateToShippingRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NavigateToInventory()
    {
        NavigateToInventoryRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NavigateToPending()
    {
        NavigateToPendingRequested?.Invoke(this, EventArgs.Empty);
    }
}