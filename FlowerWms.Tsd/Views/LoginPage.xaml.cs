using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.ViewModels;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

public partial class LoginPage : BasePage
{
    private LoginViewModel _viewModel;

    public LoginPage()
    {
        InitializeComponent();
        _viewModel = BindingContext as LoginViewModel;
        
        if (_viewModel != null)
        {
            _viewModel.LoginSuccess += OnLoginSuccess;
        }

        // Обработка нажатия Enter
        this.Loaded += async (s, e) =>
        {
            var passwordEntry = this.FindByName<Entry>("PasswordEntry");
            if (passwordEntry != null)
            {
                passwordEntry.Completed += async (sender, args) =>
                {
                    if (_viewModel != null && !_viewModel.IsLoading)
                    {
                        await _viewModel.LoginCommand.ExecuteAsync(null);
                    }
                };
            }
            
            // ✅ Проверяем сервер при загрузке
            if (_viewModel != null)
            {
                await _viewModel.CheckServerAsync();
            }
        };
    }

    private async void OnLoginSuccess(object? sender, LoginResponse response)
    {
        try
        {
            await Navigation.PushAsync(new HomePage());
            Navigation.RemovePage(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка навигации: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось перейти на главный экран: {ex.Message}", "OK");
        }
    }
}