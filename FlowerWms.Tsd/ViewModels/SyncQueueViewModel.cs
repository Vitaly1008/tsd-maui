using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Services;
using System.Collections.ObjectModel;

namespace FlowerWms.Tsd.ViewModels;

public partial class SyncQueueViewModel : ObservableObject
{
    private readonly SyncQueueService _syncQueueService;
    private readonly SyncService _syncService;
    private readonly AuthService _authService;

    [ObservableProperty]
    private ObservableCollection<OfflineTransaction> _pendingTransactions = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _statusMessage = "Загрузка...";

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _receivingCount;

    [ObservableProperty]
    private int _shippingCount;

    [ObservableProperty]
    private bool _isRefreshing;

    public event EventHandler? BackRequested;

    public SyncQueueViewModel()
    {
        _syncQueueService = new SyncQueueService();
        _syncService = new SyncService();
        _authService = new AuthService();

        _syncQueueService.PendingCountChanged += async (s, count) =>
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                UpdateCounts();
                if (count == 0)
                {
                    StatusMessage = "Нет операций для синхронизации";
                }
            });
        };
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            IsOnline = await _syncService.CheckInternetManual();
            await LoadPendingTransactions();
            UpdateCounts();

            if (PendingTransactions.Count == 0)
            {
                StatusMessage = "Нет операций для синхронизации";
            }
            else
            {
                StatusMessage = $"Ожидает синхронизации: {PendingTransactions.Count}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPendingTransactions()
    {
        try
        {
            var transactions = await _syncQueueService.GetAllPendingTransactions();
            
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PendingTransactions.Clear();
                foreach (var tx in transactions)
                {
                    PendingTransactions.Add(tx);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки транзакций: {ex.Message}");
        }
    }

    private void UpdateCounts()
    {
        TotalCount = PendingTransactions.Count;
        ReceivingCount = PendingTransactions.Count(t => t.operation_type == "Receiving");
        ShippingCount = PendingTransactions.Count(t => t.operation_type == "Shipping");
    }

    public async Task Refresh()
    {
        IsRefreshing = true;
        await LoadPendingTransactions();
        UpdateCounts();
        StatusMessage = PendingTransactions.Count > 0 
            ? $"Ожидает синхронизации: {PendingTransactions.Count}"
            : "Нет операций для синхронизации";
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task SyncAll()
    {
        if (IsSyncing) return;
        if (PendingTransactions.Count == 0)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Информация",
                "Нет операций для синхронизации",
                "OK"
            );
            return;
        }

        IsSyncing = true;
        StatusMessage = "Синхронизация...";

        try
        {
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
                BackRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            await _syncQueueService.ProcessQueueAsync();
            await Refresh();

            if (PendingTransactions.Count == 0)
            {
                StatusMessage = "Все операции синхронизированы! ✅";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Синхронизация завершена",
                    "Все операции успешно синхронизированы.",
                    "OK"
                );
            }
            else
            {
                StatusMessage = $"Осталось: {PendingTransactions.Count}";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Частичная синхронизация",
                    $"Осталось {PendingTransactions.Count} операций.\nПроверьте подключение и попробуйте снова.",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
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
    private async Task DeleteTransaction(object parameter)
    {
        if (parameter is not string transactionId) return;

        var confirm = await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
            "Удаление",
            "Вы уверены, что хотите удалить эту операцию из очереди?",
            "Да",
            "Нет"
        );

        if (confirm != true) return;

        try
        {
            var success = await _syncQueueService.DeletePendingTransaction(transactionId);
            if (success)
            {
                await Refresh();
                StatusMessage = PendingTransactions.Count > 0 
                    ? $"Ожидает синхронизации: {PendingTransactions.Count}"
                    : "Нет операций для синхронизации";
            }
            else
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Ошибка",
                    "Не удалось удалить операцию",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                $"Ошибка удаления: {ex.Message}",
                "OK"
            );
        }
    }

    [RelayCommand]
    private async Task SyncSingleTransaction(object parameter)
    {
        if (parameter is not string transactionId) return;
        if (IsSyncing) return;

        var confirm = await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
            "Синхронизация",
            "Выполнить синхронизацию для этой операции?",
            "Да",
            "Нет"
        );

        if (confirm != true) return;

        IsSyncing = true;
        try
        {
            var hasInternet = await _syncService.CheckInternetManual();
            if (!hasInternet)
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Нет интернета",
                    "Нет подключения к серверу.",
                    "OK"
                );
                return;
            }

            var success = await _syncQueueService.SyncSingleTransaction(transactionId);
            
            await Refresh();
            
            if (success)
            {
                StatusMessage = "Операция синхронизирована ✅";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Успешно",
                    "Операция успешно синхронизирована.",
                    "OK"
                );
            }
            else
            {
                StatusMessage = "Ошибка синхронизации";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Ошибка",
                    "Не удалось синхронизировать операцию.",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                ex.Message,
                "OK"
            );
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (PendingTransactions.Count == 0) return;

        var confirm = await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
            "Очистка очереди",
            $"Вы уверены, что хотите удалить все {PendingTransactions.Count} операций из очереди?",
            "Да, удалить всё",
            "Нет"
        );

        if (confirm != true) return;

        try
        {
            var deleted = await _syncQueueService.ClearSyncTable();
            await Refresh();
            StatusMessage = "Очередь очищена";
            
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Очистка выполнена",
                $"Удалено {deleted} операций",
                "OK"
            );
        }
        catch (Exception ex)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                $"Ошибка очистки: {ex.Message}",
                "OK"
            );
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    // Вспомогательные методы для UI
    public string GetOperationIcon(string operationType)
    {
        return operationType switch
        {
            "Receiving" => "📥",
            "Shipping" => "📤",
            _ => "📋"
        };
    }

    public string GetOperationTypeDisplay(string operationType)
    {
        return operationType switch
        {
            "Receiving" => "Приемка",
            "Shipping" => "Отгрузка",
            _ => operationType
        };
    }

    public string GetStatusDisplay(OfflineTransaction transaction)
    {
        if (transaction.is_synced == 1)
            return "✅ Синхронизирована";
        
        if (!string.IsNullOrEmpty(transaction.error_message))
            return $"❌ Ошибка: {transaction.error_message}";
        
        return "⏳ Ожидает";
    }

    public Color GetStatusColor(OfflineTransaction transaction)
    {
        if (transaction.is_synced == 1)
            return Colors.Green;
        
        if (!string.IsNullOrEmpty(transaction.error_message))
            return Colors.Red;
        
        return Colors.Orange;
    }

    public string GetRetryCountDisplay(OfflineTransaction transaction)
    {
        if (transaction.retry_count == 0)
            return "Попыток: 0";
        
        return $"Попыток: {transaction.retry_count}";
    }

    public string GetCreatedAtDisplay(OfflineTransaction transaction)
    {
        var date = DateTimeOffset.FromUnixTimeMilliseconds(transaction.created_at).LocalDateTime;
        return date.ToString("dd.MM.yyyy HH:mm");
    }
}