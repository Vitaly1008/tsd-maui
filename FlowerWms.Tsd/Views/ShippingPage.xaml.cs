using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.ViewModels;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

// Страница отгрузки коробок
public partial class ShippingPage : BasePage
{
    private ShippingViewModel _viewModel;
    private readonly IBarcodeService _barcodeService;

    // ✅ ИЗМЕНЕННЫЙ КОНСТРУКТОР
    public ShippingPage(IBarcodeService barcodeService, ShippingViewModel viewModel)
    {
        InitializeComponent();
        
        _barcodeService = barcodeService;
        _viewModel = viewModel;
        BindingContext = _viewModel;
        
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    // Выполняется при загрузке страницы
    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        try
        {
            // ✅ Инициализируем ViewModel с barcodeService
            await _viewModel.Initialize(_barcodeService);
            
            _viewModel.OperationCompleted += OnOperationCompleted;
            _viewModel.OperationCancelled += OnOperationCancelled;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки ShippingPage: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось загрузить страницу: {ex.Message}", "OK");
            await Navigation.PopAsync();
        }
    }

    // Выполняется при выгрузке страницы
    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.OperationCompleted -= OnOperationCompleted;
            _viewModel.OperationCancelled -= OnOperationCancelled;
        }
        StopScannerAndDispose();
    }

    // Обработчик завершения операции
    private async void OnOperationCompleted(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.OperationCompleted -= OnOperationCompleted;
            _viewModel.OperationCancelled -= OnOperationCancelled;
        }
        StopScannerAndDispose();
        await Navigation.PopAsync();
    }

    // Обработчик отмены операции
    private async void OnOperationCancelled(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.OperationCompleted -= OnOperationCompleted;
            _viewModel.OperationCancelled -= OnOperationCancelled;
        }
        StopScannerAndDispose();
        await Navigation.PopAsync();
    }

    // Выполняется при исчезновении страницы
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopScannerAndDispose();
    }

    // Обработчик ввода количества по нажатию Enter
    private void OnQuantityEntryCompleted(object sender, EventArgs e)
    {
        if (sender is Entry entry && _viewModel != null)
        {
            // ✅ ИСПРАВЛЕНО: защита от MaxQuantity <= 0
            if (_viewModel.MaxQuantity <= 0)
            {
                _viewModel.ShipQuantityDisplay = _viewModel.ShipQuantity.ToString();
                entry.Unfocus();
                return;
            }

            if (int.TryParse(entry.Text, out int value))
            {
                _viewModel.ShipQuantity = Math.Clamp(value, 1, _viewModel.MaxQuantity);
                _viewModel.ShipQuantityDisplay = _viewModel.ShipQuantity.ToString();
            }
            else
            {
                _viewModel.ShipQuantityDisplay = _viewModel.ShipQuantity.ToString();
            }

            entry.Unfocus();
        }
    }

    // Перехват системной кнопки "Назад"
    protected override bool OnBackButtonPressed()
    {
        if (_viewModel != null && _viewModel.ScannedBoxes.Count > 0)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await _viewModel.CancelOperationCommand.ExecuteAsync(null);
            });
            return true;
        }

        return base.OnBackButtonPressed();
    }

    // Останавливает сканер и освобождает ресурсы
    private void StopScannerAndDispose()
    {
        _viewModel?.StopScanner();
        _viewModel?.Dispose();
    }
}