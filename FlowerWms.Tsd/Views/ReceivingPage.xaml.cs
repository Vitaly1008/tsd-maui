using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

public partial class ReceivingPage : BasePage
{
    private ReceivingViewModel? _viewModel;
    private readonly IBarcodeService? _barcodeService;

    public ReceivingPage(IBarcodeService barcodeService)
    {
        // ✅ ВЫЗЫВАЕМ InitializeComponent() ЗДЕСЬ
        InitializeComponent();
        
        _barcodeService = barcodeService;
        
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        try
        {
            _viewModel = new ReceivingViewModel(_barcodeService);
            BindingContext = _viewModel;
            
            if (_viewModel != null)
            {
                _viewModel.OperationCompleted += OnOperationCompleted;
                _viewModel.OperationCancelled += OnOperationCancelled;
                
                await _viewModel.Initialize();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки ReceivingPage: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось загрузить страницу: {ex.Message}", "OK");
            await Navigation.PopAsync();
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _viewModel?.StopScanner();
    }

    private async void OnOperationCompleted(object? sender, EventArgs e)
    {
        _viewModel?.StopScanner();
        await Navigation.PopAsync();
    }

    private async void OnOperationCancelled(object? sender, EventArgs e)
    {
        _viewModel?.StopScanner();
        await Navigation.PopAsync();
    }
    
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel?.StopScanner();
        _viewModel?.Dispose();
    }
}