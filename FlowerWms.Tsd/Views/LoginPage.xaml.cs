using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.ViewModels;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

// Страница входа
public partial class LoginPage : BasePage
{
    private LoginViewModel? _viewModel;

    public LoginPage()
    {
        InitializeComponent();
        
        _viewModel = BindingContext as LoginViewModel;
        
        if (_viewModel != null)
        {
            _viewModel.LoginSuccess += OnLoginSuccess;
        }

        Loaded += OnPageLoaded;
    }

    // Выполняется при загрузке страницы
    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        var passwordEntry = this.FindByName<Entry>("PasswordEntry");
        if (passwordEntry != null)
        {
            passwordEntry.Completed += OnPasswordEntryCompleted;
        }
        
        if (_viewModel != null)
        {
            await _viewModel.CheckServerAsync();
        }
    }

    // Обработчик нажатия Enter в поле пароля
    private async void OnPasswordEntryCompleted(object? sender, EventArgs e)
    {
        if (_viewModel != null && !_viewModel.IsLoading)
        {
            await _viewModel.LoginCommand.ExecuteAsync(null);
        }
    }

    // Обработчик успешного входа
    private async void OnLoginSuccess(object? sender, LoginResponse response)
    {
        try
        {
            var homePage = new HomePage();
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