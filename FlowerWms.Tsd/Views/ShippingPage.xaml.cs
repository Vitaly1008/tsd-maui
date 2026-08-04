using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd.Views;

public partial class ShippingPage : BasePage
{
    private ShippingViewModel _viewModel;

    public ShippingPage()
    {
        InitializeComponent(); // ✅ Добавляем
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
}