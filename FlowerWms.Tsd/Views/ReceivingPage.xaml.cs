using FlowerWms.Tsd.ViewModels;
using FlowerWms.Tsd.Services;
using Microsoft.Maui.Controls;

namespace FlowerWms.Tsd.Views;

public partial class ReceivingPage : BasePage
{
    private ReceivingViewModel? _viewModel;

    public ReceivingPage()
    {
        InitializeComponent();
        // ❌ НЕ СОЗДАЁМ ViewModel здесь — Handler ещё null
        
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        try
        {
            // ✅ ПОЛУЧАЕМ СЕРВИС ПОСЛЕ ИНИЦИАЛИЗАЦИИ Handler
            var barcodeService = Handler?.MauiContext?.Services?.GetService<IBarcodeService>();
            _viewModel = new ReceivingViewModel(barcodeService);
            BindingContext = _viewModel;
            
            if (_viewModel != null)
            {
                _viewModel.OperationCompleted += OnOperationCompleted;
                _viewModel.OperationCancelled += OnOperationCancelled;
                await _viewModel.Initialize();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка загрузки ReceivingPage: {ex.Message}");
            await DisplayAlert("Ошибка", $"Не удалось загрузить страницу: {ex.Message}", "OK");
            await Navigation.PopAsync();
        }
    }

    private async void OnOperationCompleted(object? sender, EventArgs e)
    {
        _viewModel?.StopScanner();
        await Navigation.PopAsync();
    }

    private async void OnOperationCancelled(object? sender, EventArgs e)
    {
        _viewModel?.StopScanner();
        await Navigation.PopAsync();
    }
    
    // ✅ Освобождаем ресурсы при уходе со страницы
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel?.StopScanner();
    }
}