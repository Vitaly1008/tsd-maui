using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.Views;

// Главная страница
public partial class HomePage : BasePage
{
    private HomeViewModel? _viewModel;

    public HomePage()
    {
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

    // Выполняется при загрузке страницы
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
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации HomePage: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось загрузить главный экран: {ex.Message}", "OK");
        }
    }

    // Выполняется при появлении страницы
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel != null)
        {
            await _viewModel.RefreshPendingCount();
        }
    }

    // Возвращает сервис сканера
    private IBarcodeService? GetBarcodeService()
    {
        return Handler?.MauiContext?.Services?.GetService<IBarcodeService>();
    }

    // Отображает ошибку при недоступности сканера
    private async Task<bool> EnsureBarcodeService()
    {
        var barcodeService = GetBarcodeService();
        if (barcodeService == null)
        {
            await DisplayAlertAsync("Ошибка", "Сервис сканера недоступен", "OK");
            return false;
        }
        return true;
    }

    // Переход на страницу приемки
    private async void OnNavigateToReceiving(object? sender, EventArgs e)
    {
        if (!await EnsureBarcodeService()) return;
        
        try
        {
            var barcodeService = GetBarcodeService();
            var receivingPage = new ReceivingPage(barcodeService);
            await Navigation.PushAsync(receivingPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть приемку: {ex.Message}", "OK");
        }
    }

    // Переход на страницу отгрузки
    private async void OnNavigateToShipping(object? sender, EventArgs e)
    {
        if (!await EnsureBarcodeService()) return;
        
        try
        {
            var barcodeService = GetBarcodeService();
            var shippingPage = new ShippingPage(barcodeService);
            await Navigation.PushAsync(shippingPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть отгрузку: {ex.Message}", "OK");
        }
    }

    // Переход на страницу инвентаризации
    private async void OnNavigateToInventory(object? sender, EventArgs e)
    {
        if (!await EnsureBarcodeService()) return;
        
        try
        {
            var barcodeService = GetBarcodeService();
            var inventoryPage = new InventoryPage(barcodeService);
            await Navigation.PushAsync(inventoryPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть инвентаризацию: {ex.Message}", "OK");
        }
    }

    // Переход на страницу ожидающих транзакций
    private async void OnNavigateToPending(object? sender, EventArgs e)
    {
        await DisplayAlertAsync("Информация", "Страница списка транзакций в разработке", "OK");
    }

    // Выход из системы
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