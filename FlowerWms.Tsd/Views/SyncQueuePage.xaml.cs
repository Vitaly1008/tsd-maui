using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd.Views;

public partial class SyncQueuePage : BasePage
{
    private SyncQueueViewModel? _viewModel;

    public SyncQueuePage()
    {
        InitializeComponent();
        
        _viewModel = BindingContext as SyncQueueViewModel;
        
        if (_viewModel != null)
        {
            _viewModel.BackRequested += OnBackRequested;
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
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки SyncQueuePage: {ex.Message}");
            await DisplayAlertAsync("Ошибка", $"Не удалось загрузить страницу: {ex.Message}", "OK");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel != null)
        {
            await _viewModel.Refresh();
        }
    }

    private async void OnBackRequested(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}