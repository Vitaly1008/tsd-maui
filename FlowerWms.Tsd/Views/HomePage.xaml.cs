using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Views;

public partial class HomePage : BasePage
{
    private HomeViewModel? _viewModel;

    public HomePage()
    {
        // ✅ ВЫЗЫВАЕМ InitializeComponent() ЗДЕСЬ
        InitializeComponent();
        
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
            await DisplayAlertAsync("Ошибка", $"Не удалось загрузить главный экран: {ex.Message}", "OK");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel != null)
        {
            await _viewModel.RefreshPendingCount();
        }
    }

    private async void OnNavigateToReceiving(object? sender, EventArgs e)
    {
        try
        {
            var barcodeService = Handler?.MauiContext?.Services?.GetService<IBarcodeService>();
            if (barcodeService == null)
            {
                await DisplayAlertAsync("Ошибка", "Сервис сканера недоступен", "OK");
                return;
            }
            
            var receivingPage = new ReceivingPage(barcodeService);
            await Navigation.PushAsync(receivingPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть приемку: {ex.Message}", "OK");
        }
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
        await DisplayAlertAsync("Информация", "Страница списка транзакций в разработке", "OK");
    }

    private async void OnLogoutRequested(object? sender, EventArgs e)
    {
        await Navigation.PopToRootAsync();
        await Navigation.PushAsync(new LoginPage());
        
        var homePage = Navigation.NavigationStack.FirstOrDefault(p => p is HomePage);
        if (homePage != null)
        {
            Navigation.RemovePage(homePage);
        }
    }
}