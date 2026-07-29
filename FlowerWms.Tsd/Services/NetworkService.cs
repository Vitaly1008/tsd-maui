using System.Net.NetworkInformation;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Services;

public class NetworkService : IDisposable
{
    private readonly ServerDiscoveryService _discoveryService;
    private readonly SyncService _syncService;
    private bool _isOnline;
    private bool _isChecking;

    public event EventHandler<bool>? NetworkStatusChanged;
    public event EventHandler<string>? ServerFound;

    public NetworkService()
    {
        _discoveryService = new ServerDiscoveryService();
        _syncService = new SyncService();
        
        // Подписываемся на изменения сети
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        
        // Проверяем текущее состояние
        _ = CheckNetworkAsync();
    }

    public bool IsOnline => _isOnline;

    private async void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {   
        if (e.IsAvailable)
        {
            // Сеть появилась - проверяем сервер
            await CheckNetworkAsync();
        }
        else
        {
            // Сеть пропала - переходим в офлайн
            _isOnline = false;
            NetworkStatusChanged?.Invoke(this, false);
        }
    }

    /// <summary>
    /// Проверка сети и поиск сервера при необходимости
    /// </summary>
    public async Task CheckNetworkAsync()
    {
        if (_isChecking) return;
        
        _isChecking = true;
        
        try
        {
            // Проверяем текущий адрес
            var currentAddress = Constants.ApiBaseUrl;
            
            var isAvailable = await _discoveryService.PingServer(currentAddress);
            
            if (isAvailable)
            {
                _isOnline = true;
                NetworkStatusChanged?.Invoke(this, true);
                
                // Если есть офлайн-транзакции - синхронизируем
                var pendingCount = await _syncService.GetPendingCount();
                if (pendingCount > 0)
                {
                    await _syncService.SyncManual();
                }
                return;
            }

            // Если текущий адрес не работает - ищем новый
            
            var newAddress = await _discoveryService.DiscoverServer();
            
            if (!string.IsNullOrEmpty(newAddress))
            {
                _isOnline = true;
                Constants.ApiBaseUrl = newAddress;
                NetworkStatusChanged?.Invoke(this, true);
                ServerFound?.Invoke(this, newAddress);
                
                // Синхронизируем данные
                var pendingCount = await _syncService.GetPendingCount();
                if (pendingCount > 0)
                {
                    await _syncService.SyncManual();
                }
            }
            else
            {
                _isOnline = false;
                NetworkStatusChanged?.Invoke(this, false);
            }
        }
        catch (Exception ex)
        {
            _isOnline = false;
            NetworkStatusChanged?.Invoke(this, false);
        }
        finally
        {
            _isChecking = false;
        }
    }

    /// <summary>
    /// Принудительная проверка сети (по кнопке)
    /// </summary>
    public async Task ForceCheckAsync()
    {
        await CheckNetworkAsync();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }
}