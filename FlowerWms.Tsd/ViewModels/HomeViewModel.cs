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
    private bool _isInitialized;
    private int _lastKnownPendingCount = -1; // ✅ Для отслеживания изменений

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

        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;
        _networkService.ServerFound += OnServerFound;

        _syncService.StatusChanged += (s, status) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOnline = status == FlowerWms.Tsd.Models.SyncStatus.Online;
                ConnectionStatus = status switch
                {
                    FlowerWms.Tsd.Models.SyncStatus.Online => "✅ Онлайн",
                    FlowerWms.Tsd.Models.SyncStatus.Offline => "📴 Офлайн",
                    FlowerWms.Tsd.Models.SyncStatus.Syncing => "🔄 Синхронизация...",
                    _ => "❓ Неизвестно"
                };
                IsSyncing = status == FlowerWms.Tsd.Models.SyncStatus.Syncing;
            });
        };

        _syncService.PendingCountChanged += (s, count) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // ✅ Обновляем только если изменилось
                if (_lastKnownPendingCount != count)
                {
                    _lastKnownPendingCount = count;
                    PendingCount = count;
                    System.Diagnostics.Debug.WriteLine($"📊 Счетчик обновлен: {count}");
                }
            });
        };
    }

    private void OnNetworkStatusChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsOnline = isOnline;
            ConnectionStatus = isOnline ? "✅ Онлайн" : "📴 Офлайн";
            
            if (isOnline)
            {
                ServerAddress = Constants.ApiBaseUrl;
                // ✅ При появлении сети проверяем счетчик
                RefreshPendingCount();
            }
        });
    }

    private void OnServerFound(object? sender, string newAddress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ServerAddress = newAddress;
            IsOnline = true;
            ConnectionStatus = "✅ Онлайн";
        });
    }

    public async Task Initialize()
    {
        // ✅ Предотвращаем повторную инициализацию
        if (_isInitialized) return;
        
        try
        {
            await _networkService.CheckNetworkAsync();
            
            IsOnline = await _syncService.CheckInternetManual();
            
            // ✅ Загружаем счетчик только один раз при инициализации
            _lastKnownPendingCount = await _syncService.GetPendingCount();
            PendingCount = _lastKnownPendingCount;
            
            ServerAddress = Constants.ApiBaseUrl;
            
            _isInitialized = true;
            
            System.Diagnostics.Debug.WriteLine($"✅ HomePage инициализирован. Счетчик: {PendingCount}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка Initialize: {ex.Message}");
        }
    }

    // ✅ Метод для обновления счетчика (вызывается при возврате на главную)
    public async Task RefreshPendingCount()
    {
        try
        {
            var currentCount = await _syncService.GetPendingCount();
            
            // ✅ Обновляем только если изменилось
            if (_lastKnownPendingCount != currentCount)
            {
                _lastKnownPendingCount = currentCount;
                PendingCount = currentCount;
                System.Diagnostics.Debug.WriteLine($"📊 Счетчик обновлен при возврате: {currentCount}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления счетчика: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Sync()
    {
        if (IsSyncing) 
        {
            System.Diagnostics.Debug.WriteLine("⚠️ Синхронизация уже выполняется");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine("🔄 Запуск синхронизации");
        await _syncService.SyncManual();
        
        // ✅ Обновляем счетчик после синхронизации
        await RefreshPendingCount();
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
                ConnectionStatus = "✅ Онлайн";
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Сервер найден",
                    $"Сервер доступен по адресу:\n{serverAddress}",
                    "OK"
                );
            }
            else
            {
                IsOnline = false;
                ConnectionStatus = "📴 Офлайн";
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