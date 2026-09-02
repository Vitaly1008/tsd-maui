using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;
using Microsoft.Extensions.DependencyInjection; // ← ДОБАВИТЬ

namespace FlowerWms.Tsd.Views;

// Главная страница
public partial class HomePage : BasePage
{
    private HomeViewModel? _viewModel;
    private readonly IServiceProvider _serviceProvider; // ← ДОБАВИТЬ

    // ✅ ИЗМЕНЕННЫЙ КОНСТРУКТОР
    public HomePage(HomeViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        
        _viewModel = viewModel;
        BindingContext = _viewModel; // ← ВАЖНО: устанавливаем BindingContext
        _serviceProvider = serviceProvider;
        
        if (_viewModel != null)
        {
            _viewModel.LogoutRequested += OnLogoutRequested;
            _viewModel.NavigateToReceivingRequested += OnNavigateToReceiving;
            _viewModel.NavigateToShippingRequested += OnNavigateToShipping;
            _viewModel.NavigateToInventoryRequested += OnNavigateToInventory;
            _viewModel.NavigateToPendingRequested += OnNavigateToPending;
            _viewModel.NavigateToAboutRequested += OnNavigateToAbout;
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
            var viewModel = _serviceProvider.GetService<ReceivingViewModel>();
            var receivingPage = new ReceivingPage(barcodeService, viewModel);
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
            var viewModel = _serviceProvider.GetService<ShippingViewModel>();
            var shippingPage = new ShippingPage(barcodeService, viewModel);
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
        #warning Инвентаризация в разработке, раздел временно недоступен
        await DisplayAlertAsync("Временно не доступно", "Инвентаризация находится в разработке", "OK");
        /*if (!await EnsureBarcodeService()) return;
        
        try
        {
            var barcodeService = GetBarcodeService();
            var inventoryPage = new InventoryPage(barcodeService);
            await Navigation.PushAsync(inventoryPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть инвентаризацию: {ex.Message}", "OK");
        }*/
    }

    // Переход на страницу ожидающих транзакций
    private async void OnNavigateToPending(object? sender, EventArgs e)
    {
        try
        {
            // ✅ ПОЛУЧАЕМ ViewModel через DI
            var viewModel = _serviceProvider.GetService<SyncQueueViewModel>();
            if (viewModel == null)
            {
                await DisplayAlertAsync("Ошибка", "Не удалось создать страницу очереди", "OK");
                return;
            }
            
            var syncQueuePage = new SyncQueuePage(viewModel);
            await Navigation.PushAsync(syncQueuePage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть очередь: {ex.Message}", "OK");
        }
    }

    // Переход на страницу "О программе"
    private async void OnNavigateToAbout(object? sender, EventArgs e)
    {
        try
        {
            // ✅ ПОЛУЧАЕМ AboutViewModel через DI
            var viewModel = _serviceProvider.GetService<AboutViewModel>();
            if (viewModel == null)
            {
                await DisplayAlertAsync("Ошибка", "Не удалось создать страницу", "OK");
                return;
            }
            
            var aboutPage = new AboutPage(viewModel);
            await Navigation.PushAsync(aboutPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть страницу: {ex.Message}", "OK");
        }
    }

    // Выход из системы
    private async void OnLogoutRequested(object? sender, EventArgs e)
    {
        // ✅ СОЗДАЕМ LoginPage через DI
        var loginViewModel = _serviceProvider.GetService<LoginViewModel>();
        var loginPage = new LoginPage(loginViewModel, _serviceProvider);
        
        await Navigation.PopToRootAsync();
        await Navigation.PushAsync(loginPage);
        
        var homePage = Navigation.NavigationStack.FirstOrDefault(p => p is HomePage);
        if (homePage != null)
        {
            Navigation.RemovePage(homePage);
        }
    }
}