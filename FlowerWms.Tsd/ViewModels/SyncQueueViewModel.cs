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
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _receivingCount;

    [ObservableProperty]
    private int _shippingCount;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _hasPendingTransactions;

    public event EventHandler? BackRequested;
    public event EventHandler<OfflineTransaction>? ShowTransactionDetailRequested;

    public SyncQueueViewModel(
        SyncQueueService syncQueueService,
        SyncService syncService,
        AuthService authService)
    {
        _syncQueueService = syncQueueService;
        _syncService = syncService;
        _authService = authService;

        _syncQueueService.PendingCountChanged += async (s, count) =>
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                UpdateCounts();
                HasPendingTransactions = PendingTransactions.Count > 0;
                
                if (PendingTransactions.Count == 0)
                {
                    StatusMessage = "Все данные синхронизированы";
                }
                else
                {
                    StatusMessage = $"Ожидает синхронизации: {PendingTransactions.Count}";
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
            HasPendingTransactions = PendingTransactions.Count > 0;

            if (PendingTransactions.Count == 0)
            {
                StatusMessage = "Все данные синхронизированы";
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
            Logger.Info("🔍 НАЧАЛО загрузки транзакций...");
            
            var transactions = await _syncQueueService.GetAllPendingTransactions();
            
            Logger.Info($"📋 Загружено {transactions.Count} транзакций для синхронизации");
            
            // Выводим детали каждой транзакции
            foreach (var tx in transactions)
            {
                Logger.Info($"  - ID: {tx.transaction_id}, Тип: {tx.operation_type}, ШК: {tx.barcode}, Synced: {tx.is_synced}, Ошибок: {tx.retry_count}");
            }
            
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Logger.Info($"🔄 Очистка коллекции, текущее кол-во: {PendingTransactions.Count}");
                PendingTransactions.Clear();
                
                Logger.Info($"➕ Добавление {transactions.Count} транзакций в коллекцию");
                foreach (var tx in transactions)
                {
                    PendingTransactions.Add(tx);
                    Logger.Info($"  ✅ Добавлена: {tx.transaction_id}");
                }
                
                HasPendingTransactions = PendingTransactions.Count > 0;
                Logger.Info($"📊 HasPendingTransactions = {HasPendingTransactions}");
                Logger.Info($"📊 TotalCount = {TotalCount}");
                
                // Принудительно обновляем UI
                OnPropertyChanged(nameof(PendingTransactions));
                OnPropertyChanged(nameof(HasPendingTransactions));
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(ReceivingCount));
                OnPropertyChanged(nameof(ShippingCount));
            });
            
            Logger.Info("✅ КОНЕЦ загрузки транзакций");
        }
        catch (Exception ex)
        {
            Logger.Info($"❌ ОШИБКА загрузки транзакций: {ex.Message}");
            Logger.Info($"StackTrace: {ex.StackTrace}");
        }
    }

    private void UpdateCounts()
    {
        TotalCount = PendingTransactions.Count;
        ReceivingCount = PendingTransactions.Count(t => t.operation_type == "Receiving");
        ShippingCount = PendingTransactions.Count(t => t.operation_type == "Shipping");
        HasPendingTransactions = PendingTransactions.Count > 0;
    }

    public async Task Refresh()
    {
        IsRefreshing = true;
        await LoadPendingTransactions();
        UpdateCounts();
        
        if (PendingTransactions.Count == 0)
        {
            StatusMessage = "Все данные синхронизированы";
        }
        else
        {
            StatusMessage = $"Ожидает синхронизации: {PendingTransactions.Count}";
        }
        
        IsRefreshing = false;
    }

    public async Task ForceRefresh()
    {
        Logger.Info("🔄 ForceRefresh - НАЧАЛО");
        
        try
        {
            // Загружаем транзакции напрямую через OfflineService
            var offlineService = new OfflineService();
            var transactions = await offlineService.GetAllUnsyncedTransactions();
            
            Logger.Info($"📋 ForceRefresh: найдено {transactions.Count} транзакций");
            
            foreach (var tx in transactions)
            {
                Logger.Info($"  - {tx.transaction_id}: {tx.operation_type}, {tx.barcode}, is_synced={tx.is_synced}");
            }
            
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PendingTransactions.Clear();
                foreach (var tx in transactions)
                {
                    PendingTransactions.Add(tx);
                }
                
                TotalCount = PendingTransactions.Count;
                ReceivingCount = PendingTransactions.Count(t => t.operation_type == "Receiving");
                ShippingCount = PendingTransactions.Count(t => t.operation_type == "Shipping");
                HasPendingTransactions = PendingTransactions.Count > 0;
                
                StatusMessage = PendingTransactions.Count > 0 
                    ? $"Ожидает синхронизации: {PendingTransactions.Count}" 
                    : "Все данные синхронизированы";
                
                // ПРИНУДИТЕЛЬНО обновляем UI
                OnPropertyChanged(nameof(PendingTransactions));
                OnPropertyChanged(nameof(HasPendingTransactions));
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(ReceivingCount));
                OnPropertyChanged(nameof(ShippingCount));
                OnPropertyChanged(nameof(StatusMessage));
                
                Logger.Info($"📊 UI обновлен: HasPendingTransactions={HasPendingTransactions}, TotalCount={TotalCount}");
            });
        }
        catch (Exception ex)
        {
            Logger.Info($"❌ ForceRefresh ошибка: {ex.Message}");
        }
        
        Logger.Info("🔄 ForceRefresh - КОНЕЦ");
    }

    [RelayCommand]
    private void ShowTransactionDetail(OfflineTransaction transaction)
    {
        if (transaction != null)
        {
            ShowTransactionDetailRequested?.Invoke(this, transaction);
        }
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
        StatusMessage = "Выполняется синхронизация...";

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

            // ✅ ИСПРАВЛЕНО: используем полную синхронизацию
            await _syncService.SyncAllData();
            
            // ✅ Обновляем список
            await Refresh();

            if (PendingTransactions.Count == 0)
            {
                StatusMessage = "✅ Все данные синхронизированы";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Синхронизация завершена",
                    "Все операции успешно синхронизированы и локальная БД обновлена.",
                    "OK"
                );
            }
            else
            {
                StatusMessage = $"⏳ Осталось: {PendingTransactions.Count}";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Частичная синхронизация",
                    $"Осталось {PendingTransactions.Count} операций.\nПроверьте подключение и попробуйте снова.",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ошибка: {ex.Message}";
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
                    : "Все данные синхронизированы";
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
            
            // ✅ После синхронизации одной транзакции обновляем локальную БД
            if (success)
            {
                await _syncService.RefreshLocalCacheFromServer();
            }
            
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
                StatusMessage = "❌ Ошибка синхронизации";
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Ошибка",
                    "Не удалось синхронизировать операцию.",
                    "OK"
                );
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ошибка: {ex.Message}";
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
            StatusMessage = "✅ Очередь очищена";
            
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
}