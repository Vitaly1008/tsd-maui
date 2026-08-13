using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

// Страница приемки коробок
public partial class ReceivingPage : BasePage
{
    private ReceivingViewModel? _viewModel;
    private readonly IBarcodeService? _barcodeService;

    public ReceivingPage(IBarcodeService barcodeService)
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
            _viewModel = new ReceivingViewModel(_barcodeService);
            BindingContext = _viewModel;
            
            _viewModel.OperationCompleted += OnOperationCompleted;
            _viewModel.OperationCancelled += OnOperationCancelled;
            
            await _viewModel.Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки ReceivingPage: {ex.Message}");
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

    // Останавливает сканер и освобождает ресурсы
    private void StopScannerAndDispose()
    {
        _viewModel?.StopScanner();
        _viewModel?.Dispose();
    }
}