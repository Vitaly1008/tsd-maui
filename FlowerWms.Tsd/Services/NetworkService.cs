using System.Net.NetworkInformation;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Services;

// Мониторинг состояния сети
public class NetworkService : IDisposable
{
    private readonly ServerDiscoveryService _discoveryService;
    private readonly SyncService _syncService;
    private readonly OfflineService _offlineService;
    private bool _isOnline;
    private bool _isChecking;

    public event EventHandler<bool>? NetworkStatusChanged;
    public event EventHandler<string>? ServerFound;

    public NetworkService()
    {
        _discoveryService = new ServerDiscoveryService();
        _syncService = new SyncService();
        
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        
        _ = CheckNetworkAsync();
    }

    public bool IsOnline => _isOnline;

    // Обработчик изменения доступности сети
    private async void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {   
        if (e.IsAvailable)
        {
            await CheckNetworkAsync();
        }
        else
        {
            _isOnline = false;
            NetworkStatusChanged?.Invoke(this, false);
        }
    }

    // Проверяет сеть и ищет сервер при необходимости
    public async Task CheckNetworkAsync()
    {
        if (_isChecking) return;
        
        _isChecking = true;
        
        try
        {
            var currentAddress = Constants.ApiBaseUrl;
            var isAvailable = await _discoveryService.PingServer(currentAddress);
            
            if (isAvailable)
            {
                _isOnline = true;
                NetworkStatusChanged?.Invoke(this, true);
                // ✅ УБРАН ВЫЗОВ PerformSyncIfNeeded()
                return;
            }

            var newAddress = await _discoveryService.DiscoverServer();
            
            if (!string.IsNullOrEmpty(newAddress))
            {
                _isOnline = true;
                Constants.ApiBaseUrl = newAddress;
                NetworkStatusChanged?.Invoke(this, true);
                ServerFound?.Invoke(this, newAddress);
                // ✅ УБРАН ВЫЗОВ PerformSyncIfNeeded()
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

    // Принудительная проверка сети
    public async Task ForceCheckAsync()
    {
        await CheckNetworkAsync();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }
}