using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Views;

public partial class SyncQueuePage : BasePage
{
    private SyncQueueViewModel _viewModel;

    public SyncQueuePage(SyncQueueViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.BackRequested += OnBackRequested;
        _viewModel.ShowTransactionDetailRequested += OnShowTransactionDetail;
    }

    // ✅ ИСПРАВЛЕНО: убран Loaded + Initialize — Refresh выполняется в OnAppearing
    // (было двойная загрузка: Initialize в OnPageLoaded + Refresh в OnAppearing)

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel != null)
        {
            try
            {
                Logger.Info("📍 SyncQueuePage OnAppearing - загрузка/обновление");
                await _viewModel.Initialize();  // первый раз загрузит, повторно — быстро (IsLoading)
                Logger.Info($"📊 После загрузки: TotalCount = {_viewModel.TotalCount}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка загрузки SyncQueuePage: {ex.Message}");
                await DisplayAlertAsync("Ошибка", $"Не удалось загрузить страницу: {ex.Message}", "OK");
            }
        }
    }

    // ✅ ДОБАВЛЕНО: отписки и Dispose при уходе
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (_viewModel != null)
        {
            _viewModel.BackRequested -= OnBackRequested;
            _viewModel.ShowTransactionDetailRequested -= OnShowTransactionDetail;
            _viewModel.Dispose();
        }
    }

    private async void OnBackRequested(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnShowTransactionDetail(object? sender, OfflineTransaction transaction)
    {
        if (transaction == null) return;

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