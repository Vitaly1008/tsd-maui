using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd.Views;

public partial class HomePage: BasePage
{
    private HomeViewModel _viewModel;

    public HomePage()
    {
        InitializeComponent();  // ✅ Теперь работает
        _viewModel = BindingContext as HomeViewModel;
        
        if (_viewModel != null)
        {
            _viewModel.LogoutRequested += OnLogoutRequested;
            _viewModel.NavigateToReceivingRequested += OnNavigateToReceiving;
            _viewModel.NavigateToShippingRequested += OnNavigateToShipping;
            _viewModel.NavigateToInventoryRequested += OnNavigateToInventory;
            _viewModel.NavigateToPendingRequested += OnNavigateToPending;
            Loaded += OnPageLoaded;
        }
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        try
        {
            if (_viewModel != null)
            {
                await _viewModel.Initialize();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка инициализации HomePage: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось загрузить главный экран: {ex.Message}", "OK");
        }
    }

    private async void OnNavigateToReceiving(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new ReceivingPage());
    }

    private async void OnNavigateToShipping(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new ShippingPage());
    }

    private async void OnNavigateToInventory(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new InventoryPage());
    }

    private async void OnNavigateToPending(object? sender, EventArgs e)
    {
        await DisplayAlert("Информация", "Страница списка транзакций в разработке", "OK");
    }

    private async void OnLogoutRequested(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new LoginPage());
        Navigation.RemovePage(this);
    }
}