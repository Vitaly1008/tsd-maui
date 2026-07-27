using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

public partial class ReceivingPage : BasePage
{
    private ReceivingViewModel _viewModel;

    public ReceivingPage()
    {
        InitializeComponent();
        
        // ✅ Создаем ViewModel через DI
        var barcodeService = Handler?.MauiContext?.Services?.GetService<IBarcodeService>();
        _viewModel = new ReceivingViewModel(barcodeService);
        BindingContext = _viewModel;
        
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
}