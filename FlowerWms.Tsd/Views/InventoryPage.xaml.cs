using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Views;

public partial class InventoryPage : BasePage
{
    private InventoryViewModel _viewModel;

    public InventoryPage()
    {
        InitializeComponent(); // ✅ Добавляем
        _viewModel = BindingContext as InventoryViewModel;
        
        if (_viewModel != null)
        {
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
                if (_viewModel.IsLocationMode)
                {
                    await _viewModel.ScanBoxInLocationCommand.ExecuteAsync(barcode);
                }
                else
                {
                    await _viewModel.ScanBarcodeCommand.ExecuteAsync(barcode);
                }
            }
        });
    }
}