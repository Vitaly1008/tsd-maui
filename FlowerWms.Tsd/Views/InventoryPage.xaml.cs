using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Views;

// Страница инвентаризации
public partial class InventoryPage : BasePage
{
    private InventoryViewModel? _viewModel;
    private readonly IBarcodeService? _barcodeService;

    public InventoryPage(IBarcodeService barcodeService)
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
            _viewModel = new InventoryViewModel(_barcodeService);
            BindingContext = _viewModel;
            
            _viewModel.OperationCancelled += OnOperationCancelled;
            await _viewModel.Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки InventoryPage: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось загрузить страницу: {ex.Message}", "OK");
            await Navigation.PopAsync();
        }
    }

    // Выполняется при выгрузке страницы
    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        StopScannerAndDispose();
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