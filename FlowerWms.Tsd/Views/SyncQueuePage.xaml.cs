using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Views;

public partial class SyncQueuePage : BasePage
{
    private SyncQueueViewModel? _viewModel;

    public SyncQueuePage()
    {
        InitializeComponent();
        
        _viewModel = BindingContext as SyncQueueViewModel;
        
        if (_viewModel != null)
        {
            _viewModel.BackRequested += OnBackRequested;
            _viewModel.ShowTransactionDetailRequested += OnShowTransactionDetail;
            Loaded += OnPageLoaded;
        }
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        try
        {
            if (_viewModel != null)
            {
                await _viewModel.Initialize();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки SyncQueuePage: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось загрузить страницу: {ex.Message}", "OK");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Logger.Info("📍 SyncQueuePage OnAppearing - НАЧАЛО");
        
        if (_viewModel != null)
        {
            // Принудительно загружаем транзакции
            Logger.Info("📊 Загружаем транзакции...");
            await _viewModel.ForceRefresh();
            
            Logger.Info($"📊 После загрузки: HasPendingTransactions = {_viewModel.HasPendingTransactions}");
            Logger.Info($"📊 После загрузки: TotalCount = {_viewModel.TotalCount}");
        }
        
        Logger.Info("📍 SyncQueuePage OnAppearing - КОНЕЦ");
    }

    private async void OnBackRequested(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnShowTransactionDetail(object? sender, OfflineTransaction transaction)
    {
        if (transaction == null) return;

        // Формируем детальную информацию
        var typeDisplay = transaction.operation_type == "Receiving" ? "Приемка" : "Отгрузка";
        var statusDisplay = transaction.is_synced == 1 ? "✅ Синхронизирована" : "⏳ Ожидает";
        if (!string.IsNullOrEmpty(transaction.error_message))
        {
            statusDisplay = $"❌ Ошибка: {transaction.error_message}";
        }

        var date = DateTimeOffset.FromUnixTimeMilliseconds(transaction.created_at).LocalDateTime;
        var dateStr = date.ToString("dd.MM.yyyy HH:mm:ss");

        var message = $"📋 Детали операции\n\n" +
                      $"Тип: {typeDisplay}\n" +
                      $"Штрих-код: {transaction.barcode}\n" +
                      $"Статус: {statusDisplay}\n" +
                      $"Попыток: {transaction.retry_count}\n" +
                      $"Дата: {dateStr}\n" +
                      $"ID: {transaction.transaction_id}";

        if (!string.IsNullOrEmpty(transaction.payload))
        {
            message += $"\n\n📦 Данные: {transaction.payload}";
        }

        await DisplayAlertAsync("Информация об операции", message, "OK");
    }
}