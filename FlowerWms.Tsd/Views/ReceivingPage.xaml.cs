using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

// Страница приемки коробок
public partial class ReceivingPage : BasePage
{
    private ReceivingViewModel _viewModel;
    private readonly IBarcodeService _barcodeService;

    // ✅ ИЗМЕНЕННЫЙ КОНСТРУКТОР
    public ReceivingPage(IBarcodeService barcodeService, ReceivingViewModel viewModel)
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