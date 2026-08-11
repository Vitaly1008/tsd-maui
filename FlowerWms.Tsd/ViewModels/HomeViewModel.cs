using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly SyncQueueService _syncQueueService;
    private readonly SyncService _syncService;
    private readonly AuthService _authService;
    private readonly ServerDiscoveryService _discoveryService;
    private readonly NetworkService _networkService;
    private bool _isInitialized;
    private int _lastKnownPendingCount = -1;

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
    private string _syncStatusMessage = "Все данные синхронизированы ✅";

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
        _syncQueueService = new SyncQueueService();
        _syncService = new SyncService();
        _authService = new AuthService();
        _discoveryService = new ServerDiscoveryService();
        _networkService = new NetworkService();
        
        ServerAddress = Constants.ApiBaseUrl;

        _networkService.NetworkStatusChanged += OnNetworkStatusChanged;
        _networkService.ServerFound += OnServerFound;

        _syncQueueService.PendingCountChanged += (s, count) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PendingCount = count;
                UpdateSyncStatusMessage(count);
            });
        };

        _syncQueueService.SyncStatusChanged += (s, isSyncing) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsSyncing = isSyncing;
                if (!isSyncing && PendingCount == 0)
                {
                    SyncStatusMessage = "✅ Все данные синхронизированы";
                }
            });
        };

        _syncService.StatusChanged += (s, status) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOnline = status == SyncStatus.Online;
                ConnectionStatus = status switch
                {
                    SyncStatus.Online => "✅ Онлайн",
                    SyncStatus.Offline => "📴 Офлайн",
                    SyncStatus.Syncing => "🔄 Синхронизация...",
                    _ => "❓ Неизвестно"
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
                _ = RefreshPendingCount();
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

    private void UpdateSyncStatusMessage(int count)
    {
        if (count == 0)
        {
            SyncStatusMessage = "✅ Все данные синхронизированы";
        }
        else
        {
            SyncStatusMessage = $"⏳ Ожидает синхронизации: {count}";
        }
    }

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
            
            // Если есть интернет — делаем фоновую синхронизацию
            if (IsOnline && PendingCount > 0)
            {
                System.Diagnostics.Debug.WriteLine("🔄 Фоновая синхронизация при загрузке...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // ✅ Только обновление кэша, без обработки очереди (она обрабатывается отдельно)
                        await _syncService.SyncAllData();
                        await _syncQueueService.ProcessQueueAsync();
                        await RefreshPendingCount();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Фоновая синхронизация: {ex.Message}");
                    }
                });
            }
            
            _isInitialized = true;
            
            System.Diagnostics.Debug.WriteLine($"✅ HomePage инициализирован. Счетчик: {PendingCount}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка Initialize: {ex.Message}");
        }
    }

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
                System.Diagnostics.Debug.WriteLine($"📊 Счетчик обновлен: {currentCount}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления счетчика: {ex.Message}");
        }
    }

    // ============================================================
    // ✅ ИСПРАВЛЕННЫЙ МЕТОД СИНХРОНИЗАЦИИ
    // ============================================================

    [RelayCommand]
    private async Task Sync()
    {
        if (IsSyncing) 
        {
            System.Diagnostics.Debug.WriteLine("⚠️ Синхронизация уже выполняется");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine("🔄 Запуск ручной синхронизации");
        
        try
        {
            IsSyncing = true;
            SyncStatusMessage = "🔄 Проверка подключения...";
            
            // ✅ Проверяем интернет
            var hasInternet = await _syncService.CheckInternetManual();
            if (!hasInternet)
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "📴 Нет интернета",
                    "Нет подключения к серверу. Синхронизация недоступна.",
                    "OK"
                );
                return;
            }
            
            // ✅ Проверяем авторизацию
            var isAuthenticated = await _authService.ValidateToken();
            if (!isAuthenticated)
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "⚠️ Требуется авторизация",
                    "Сессия истекла. Пожалуйста, войдите заново.",
                    "OK"
                );
                await _authService.Logout();
                LogoutRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            
            // ✅ Синхронизация с индикацией прогресса
            SyncStatusMessage = "🔄 Синхронизация справочников...";
            await _syncService.SyncProducts();
            
            SyncStatusMessage = "🔄 Синхронизация локаций...";
            await _syncService.SyncLocations();
            
            SyncStatusMessage = "🔄 Синхронизация коробок...";
            await _syncService.SyncBoxes();
            
            // ✅ Обработка офлайн-транзакций (только один раз!)
            SyncStatusMessage = $"🔄 Обработка офлайн-транзакций...";
            await _syncQueueService.ProcessQueueAsync();
            
            // ✅ Обновляем счетчик
            await RefreshPendingCount();
            
            // ✅ Обновляем статус
            if (PendingCount == 0)
            {
                SyncStatusMessage = "✅ Все данные синхронизированы";
                
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "✅ Синхронизация завершена",
                    "Все данные успешно синхронизированы.",
                    "OK"
                );
            }
            else
            {
                SyncStatusMessage = $"⏳ Ожидает синхронизации: {PendingCount}";
                
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "⚠️ Частичная синхронизация",
                    $"Осталось {PendingCount} операций для синхронизации.\nПроверьте подключение и попробуйте снова.",
                    "OK"
                );
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ Синхронизация завершена. Осталось транзакций: {PendingCount}");
        }
        catch (UnauthorizedAccessException)
        {
            SyncStatusMessage = "❌ Ошибка авторизации";
            
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "⚠️ Требуется авторизация",
                "Сессия истекла. Пожалуйста, войдите заново.",
                "OK"
            );
            
            await _authService.Logout();
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации: {ex.Message}");
            SyncStatusMessage = $"❌ Ошибка: {ex.Message}";
            
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "❌ Ошибка синхронизации",
                $"Не удалось выполнить синхронизацию:\n{ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsSyncing = false;
        }
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
                
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "✅ Сервер найден",
                    $"Сервер доступен по адресу:\n{serverAddress}",
                    "OK"
                );
            }
            else
            {
                IsOnline = false;
                ConnectionStatus = "📴 Офлайн";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "❌ Сервер не найден",
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