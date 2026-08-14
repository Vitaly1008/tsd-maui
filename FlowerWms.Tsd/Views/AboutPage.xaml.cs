using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd.Views;

// Страница "О программе"
public partial class AboutPage : BasePage
{
    private AboutViewModel? _viewModel;

    public AboutPage()
    {
        InitializeComponent();
        _viewModel = BindingContext as AboutViewModel;
    }
}