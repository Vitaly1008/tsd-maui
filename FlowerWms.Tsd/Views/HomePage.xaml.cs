using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FlowerWms.Tsd.Views;

// Главная страница
public partial class HomePage : BasePage
{
    private HomeViewModel? _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public HomePage(HomeViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;
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

    // ✅ ИСПРАВЛЕНО: через _serviceProvider, а не через Handler.MauiContext
    private IBarcodeService? GetBarcodeService()
    {
        return _serviceProvider.GetService<IBarcodeService>();
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

            // ✅ ИСПРАВЛЕНО: проверка на null
            if (barcodeService == null || viewModel == null)
            {
                await DisplayAlertAsync("Ошибка", "Не удалось создать страницу приемки", "OK");
                return;
            }

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

            // ✅ ИСПРАВЛЕНО: проверка на null
            if (barcodeService == null || viewModel == null)
            {
                await DisplayAlertAsync("Ошибка", "Не удалось создать страницу отгрузки", "OK");
                return;
            }

            var shippingPage = new ShippingPage(barcodeService, viewModel);
            await Navigation.PushAsync(shippingPage);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Ошибка", $"Не удалось открыть отгрузку: {ex.Message}", "OK");
        }
    }

    // Переход на страницу инвентаризации (заглушка)
    private async void OnNavigateToInventory(object? sender, EventArgs e)
    {
        #warning Инвентаризация в разработке, раздел временно недоступен
        await DisplayAlertAsync("Временно не доступно", "Инвентаризация находится в разработке", "OK");
    }

    // Переход на страницу ожидающих транзакций
    private async void OnNavigateToPending(object? sender, EventArgs e)
    {
        try
        {
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
        try
        {
            // ✅ ИСПРАВЛЕНО: проверка на null
            var loginViewModel = _serviceProvider.GetService<LoginViewModel>();
            if (loginViewModel == null)
            {
                await DisplayAlertAsync("Ошибка", "Не удалось создать страницу входа", "OK");
                return;
            }

            var loginPage = new LoginPage(loginViewModel, _serviceProvider);

            await Navigation.PopToRootAsync();
            await Navigation.PushAsync(loginPage);

            var homePage = Navigation.NavigationStack.FirstOrDefault(p => p is HomePage);
            if (homePage != null)
            {
                Navigation.RemovePage(homePage);
            }

            // ✅ ДОБАВЛЕНО: dispose ViewModel после удаления страницы
            DisposeViewModel();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка logout: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось выполнить выход: {ex.Message}", "OK");
        }
    }

    // ✅ ДОБАВЛЕНО: освобождение ресурсов
    private void DisposeViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.LogoutRequested -= OnLogoutRequested;
            _viewModel.NavigateToReceivingRequested -= OnNavigateToReceiving;
            _viewModel.NavigateToShippingRequested -= OnNavigateToShipping;
            _viewModel.NavigateToInventoryRequested -= OnNavigateToInventory;
            _viewModel.NavigateToPendingRequested -= OnNavigateToPending;
            _viewModel.NavigateToAboutRequested -= OnNavigateToAbout;

            _viewModel.Dispose();
            _viewModel = null;
        }
    }

    // ✅ ДОБАВЛЕНО: освобождение при уходе страницы
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Не вызываем Dispose здесь, потому что страница может вернуться (например, после Push/Pop).
        // Dispose вызывается только при Logout или при удалении страницы из стека.
    }
}