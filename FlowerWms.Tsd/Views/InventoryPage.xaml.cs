using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd.Views;

public partial class InventoryPage : BasePage  // ✅ Наследуем BasePage
{
    private InventoryViewModel _viewModel;

    public InventoryPage()
    {
        InitializeComponent();
        _viewModel = BindingContext as InventoryViewModel;
        
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