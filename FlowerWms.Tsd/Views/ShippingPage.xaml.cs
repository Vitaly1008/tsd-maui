using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd.Views;

public partial class ShippingPage : BasePage
{
    private ShippingViewModel? _viewModel;

    public ShippingPage()
    {
        // ✅ ВЫЗЫВАЕМ InitializeComponent() ЗДЕСЬ
        InitializeComponent();
        
        _viewModel = BindingContext as ShippingViewModel;
        
        if (_viewModel != null)
        {
            _viewModel.OperationCompleted += OnOperationCompleted;
            _viewModel.OperationCancelled += OnOperationCancelled;
            Loaded += OnPageLoaded;
        }
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.Initialize();
        }
    }

    private async void OnOperationCompleted(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnOperationCancelled(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        
        var barcodeService = Handler?.MauiContext?.Services?.GetService<IBarcodeService>();
        if (barcodeService != null)
        {
            barcodeService.OnBarcodeScanned += OnBarcodeScanned;
            barcodeService.StartListening();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        var barcodeService = Handler?.MauiContext?.Services?.GetService<IBarcodeService>();
        if (barcodeService != null)
        {
            barcodeService.OnBarcodeScanned -= OnBarcodeScanned;
        }
    }

    private void OnBarcodeScanned(string barcode)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_viewModel != null)
            {
                await _viewModel.ScanBoxCommand.ExecuteAsync(barcode);
            }
        });
    }
}