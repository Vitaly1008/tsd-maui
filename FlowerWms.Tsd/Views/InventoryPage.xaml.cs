using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Views;

// Страница инвентаризации
public partial class InventoryPage : BasePage
{
    private InventoryViewModel _viewModel;
    private readonly IBarcodeService _barcodeService;

    // ✅ ИЗМЕНЕННЫЙ КОНСТРУКТОР
    public InventoryPage(IBarcodeService barcodeService, InventoryViewModel viewModel)
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
            
            _viewModel.OperationCancelled += OnOperationCancelled;
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