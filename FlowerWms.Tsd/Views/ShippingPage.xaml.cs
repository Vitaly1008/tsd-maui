using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.ViewModels;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

// Страница отгрузки коробок
public partial class ShippingPage : BasePage
{
    private ShippingViewModel? _viewModel;
    private readonly IBarcodeService? _barcodeService;

    public ShippingPage(IBarcodeService barcodeService)
    {
        InitializeComponent();
        _barcodeService = barcodeService;
        
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    // Выполняется при загрузке страницы
    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        try
        {
            _viewModel = new ShippingViewModel(_barcodeService);
            BindingContext = _viewModel;
            
            _viewModel.OperationCompleted += OnOperationCompleted;
            _viewModel.OperationCancelled += OnOperationCancelled;
            
            await _viewModel.Initialize();
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
        StopScannerAndDispose();
    }

    // Обработчик завершения операции
    private async void OnOperationCompleted(object? sender, EventArgs e)
    {
        StopScannerAndDispose();
        await Navigation.PopAsync();
    }

    // Обработчик отмены операции
    private async void OnOperationCancelled(object? sender, EventArgs e)
    {
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

    // Останавливает сканер и освобождает ресурсы
    private void StopScannerAndDispose()
    {
        _viewModel?.StopScanner();
        _viewModel?.Dispose();
    }
}