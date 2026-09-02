using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection; // ← ДОБАВИТЬ

namespace FlowerWms.Tsd.Views;

// Страница входа
public partial class LoginPage : BasePage
{
    private LoginViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider; // ← ДОБАВИТЬ

    // ✅ ИЗМЕНЕННЫЙ КОНСТРУКТОР
    public LoginPage(LoginViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        BindingContext = _viewModel;
        
        _viewModel.LoginSuccess += OnLoginSuccess;
        Loaded += OnPageLoaded;
    }

    // Выполняется при загрузке страницы
    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        var passwordEntry = this.FindByName<Entry>("PasswordEntry");
        
        await _viewModel.CheckServerAsync();
    }

    // Обработчик успешного входа
    private async void OnLoginSuccess(object? sender, LoginResponse response)
    {
        try
        {
            // ✅ ПОЛУЧАЕМ HomeViewModel через DI
            var homeViewModel = _serviceProvider.GetService<HomeViewModel>();
            if (homeViewModel == null)
            {
                await DisplayAlertAsync("Ошибка", "Не удалось создать главную страницу", "OK");
                return;
            }
            
            var homePage = new HomePage(homeViewModel, _serviceProvider);
            await Navigation.PushAsync(homePage);
            Navigation.RemovePage(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка навигации: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось перейти на главный экран: {ex.Message}", "OK");
        }
    }
}