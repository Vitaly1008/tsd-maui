using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для главного экрана
public partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly SyncQueueService _syncQueueService;
    private readonly SyncService _syncService;
    private readonly AuthService _authService;
    private readonly ServerDiscoveryService _discoveryService;
    private readonly NetworkService _networkService;
    private bool _isInitialized;
    private int _lastKnownPendingCount = -1;
    private bool _disposed;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _connectionStatus = "Подключение...";

    [ObservableProperty]
    private string _connectionStatusIcon = "⏳";

    [ObservableProperty]
    private string _syncStatusMessage = "Все данные синхронизированы";

    [ObservableProperty]
    private string _serverAddress = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _searchProgressText = "Пожалуйста, подождите";

    [ObservableProperty]
    private string _version = VersionHelper.GetVersion();

    public event EventHandler? LogoutRequested;
    public event EventHandler? NavigateToReceivingRequested;
    public event EventHandler? NavigateToShippingRequested;
    public event EventHandler? NavigateToInventoryRequested;
    public event EventHandler? NavigateToPendingRequested;
    public event EventHandler? NavigateToAboutRequested;

    public HomeViewModel(
        SyncQueueService syncQueueService,
        SyncService syncService,
        AuthService authService,
        ServerDiscoveryService discoveryService,
        NetworkService networkService)
    {
        _syncQueueService = syncQueueService;
        _syncService = syncService;
        _authService = authService;
        _discoveryService = discoveryService;
        _networkService = networkService;

        ServerAddress = Constants.ApiBaseUrl;

        // ✅ Подписки через методы, а не лямбды — чтобы можно было отписаться
        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;
        _networkService.ServerFound += OnServerFound;

        _syncQueueService.PendingCountChanged += OnPendingCountChanged;
        _syncQueueService.SyncStatusChanged += OnSyncStatusChanged;
        _syncService.StatusChanged += OnSyncServiceStatusChanged;

        _discoveryService.ScanProgressChanged += OnScanProgressChanged;
    }

    // ============================================================
    // ✅ ОБРАБОТЧИКИ СОБЫТИЙ (вынесены из конструктора для отписки)
    // ============================================================

    private void OnPendingCountChanged(object? sender, int count)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PendingCount = count;
            UpdateSyncStatusMessage(count);
        });
    }

    private void OnSyncStatusChanged(object? sender, bool isSyncing)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsSyncing = isSyncing;
            if (!isSyncing && PendingCount == 0)
            {
                SyncStatusMessage = "Все данные синхронизированы";
            }
        });
    }

    private void OnSyncServiceStatusChanged(object? sender, SyncStatus status)
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
            ConnectionStatusIcon = status switch
            {
                SyncStatus.Online => "📶",
                SyncStatus.Offline => "📴",
                SyncStatus.Syncing => "🔄",
                _ => "❓"
            };
            IsSyncing = status == SyncStatus.Syncing;
        });
    }

    // Обработчик изменения статуса сети
    private void OnNetworkStatusChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsOnline = isOnline;
            ConnectionStatus = isOnline ? "Онлайн" : "Офлайн";

            if (isOnline)
            {
                ServerAddress = Constants.ApiBaseUrl;
                _ = RefreshPendingCount();
            }
        });
    }

    // Обработчик найденного сервера
    private void OnServerFound(object? sender, string newAddress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ServerAddress = newAddress;
            IsOnline = true;
            ConnectionStatus = "Онлайн";
        });
    }

    // Обработчик прогресса поиска сервера
    private void OnScanProgressChanged(object? sender, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SearchProgressText = message;
        });
    }

    // Обновляет сообщение статуса синхронизации
    private void UpdateSyncStatusMessage(int count)
    {
        if (count == 0)
        {
            SyncStatusMessage = "Все данные синхронизированы";
        }
        else
        {
            SyncStatusMessage = $"Ожидает синхронизации: {count}";
        }
    }

    // Инициализирует ViewModel
    public async Task Initialize()
    {
        if (_isInitialized) return;

        try
        {
            await _networkService.CheckNetworkAsync();

            IsOnline = await _syncService.CheckInternetManual();

            _lastKnownPendingCount = await _syncQueueService.GetPendingCount();
            PendingCount = _lastKnownPendingCount;
            UpdateSyncStatusMessage(PendingCount);

            ServerAddress = Constants.ApiBaseUrl;

            // ✅ УБРАНО: фоновая синхронизация при загрузке
            // Синхронизация выполняется:
            // - после логина (в LoginViewModel.SyncAfterLogin)
            // - вручную по кнопке "Синхр."

            _isInitialized = true;

            System.Diagnostics.Debug.WriteLine($"HomePage инициализирован. Счетчик: {PendingCount}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка Initialize: {ex.Message}");
        }
    }

    // Обновляет количество ожидающих синхронизации операций
    public async Task RefreshPendingCount()
    {
        try
        {
            var currentCount = await _syncQueueService.GetPendingCount();

            if (_lastKnownPendingCount != currentCount)
            {
                _lastKnownPendingCount = currentCount;
                PendingCount = currentCount;
                UpdateSyncStatusMessage(currentCount);
                System.Diagnostics.Debug.WriteLine($"Счетчик обновлен: {currentCount}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка обновления счетчика: {ex.Message}");
        }
    }

    // Выполняет синхронизацию
    [RelayCommand]
    private async Task Sync()
    {
        if (IsSyncing) return;

        try
        {
            IsSyncing = true;
            SyncStatusMessage = "Проверка подключения...";

            var hasInternet = await _syncService.CheckInternetManual();
            if (!hasInternet)
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Нет интернета",
                    "Нет подключения к серверу. Синхронизация недоступна.",
                    "OK"
                );
                return;
            }

            var isAuthenticated = await _authService.ValidateToken();
            if (!isAuthenticated)
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Требуется авторизация",
                    "Сессия истекла. Пожалуйста, войдите заново.",
                    "OK"
                );
                await _authService.Logout();
                LogoutRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            SyncStatusMessage = "Синхронизация данных...";
            await _syncService.SyncAllData();

            /* вызов await _syncQueueService.ProcessQueueAsync(); дублируется в await _syncService.SyncAllData();
            SyncStatusMessage = "Обработка офлайн-транзакций...";
            await _syncQueueService.ProcessQueueAsync();*/

            await RefreshPendingCount();

            if (PendingCount == 0)
            {
                SyncStatusMessage = "Все данные синхронизированы";

                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Синхронизация завершена",
                    "Все данные успешно синхронизированы.",
                    "OK"
                );
            }
            else
            {
                SyncStatusMessage = $"Ожидает синхронизации: {PendingCount}";

                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Частичная синхронизация",
                    $"Осталось {PendingCount} операций для синхронизации.\nПроверьте подключение и попробуйте снова.",
                    "OK"
                );
            }

            System.Diagnostics.Debug.WriteLine($"Синхронизация завершена. Осталось транзакций: {PendingCount}");
        }
        catch (UnauthorizedAccessException)
        {
            SyncStatusMessage = "Ошибка авторизации";

            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Требуется авторизация",
                "Сессия истекла. Пожалуйста, войдите заново.",
                "OK"
            );

            await _authService.Logout();
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации: {ex.Message}");
            SyncStatusMessage = $"Ошибка: {ex.Message}";

            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка синхронизации",
                $"Не удалось выполнить синхронизацию:\n{ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsSyncing = false;
        }
    }

    // Выполняет поиск сервера
    [RelayCommand]
    private async Task FindServer()
    {
        if (IsSearching) return;

        IsSearching = true;
        SearchProgressText = "🔍 Поиск сервера...";

        try
        {
            var serverAddress = await _discoveryService.GetServerAddress();

            if (!string.IsNullOrEmpty(serverAddress))
            {
                ServerAddress = serverAddress;
                Constants.ApiBaseUrl = serverAddress;
                IsOnline = true;
                ConnectionStatus = "Онлайн";

                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Сервер найден",
                    $"Сервер доступен по адресу:\n{serverAddress}",
                    "OK"
                );
            }
            else
            {
                IsOnline = false;
                ConnectionStatus = "Офлайн";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Сервер не найден",
                    "Не удалось найти сервер в сети.\nПроверьте подключение.",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                $"Ошибка поиска сервера: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsSearching = false;
            SearchProgressText = "Пожалуйста, подождите";
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

    [RelayCommand]
    private void NavigateToAbout()
    {
        NavigateToAboutRequested?.Invoke(this, EventArgs.Empty);
    }

    // ============================================================
    // ✅ IDisposable — отписки от событий
    // ============================================================
    public void Dispose()
    {
        if (_disposed) return;

        _networkService.NetworkStatusChanged -= OnNetworkStatusChanged;
        _networkService.ServerFound -= OnServerFound;

        _syncQueueService.PendingCountChanged -= OnPendingCountChanged;
        _syncQueueService.SyncStatusChanged -= OnSyncStatusChanged;
        _syncService.StatusChanged -= OnSyncServiceStatusChanged;

        _discoveryService.ScanProgressChanged -= OnScanProgressChanged;

        _disposed = true;
        GC.SuppressFinalize(this);

        System.Diagnostics.Debug.WriteLine("HomeViewModel.Dispose() вызван");
    }
}