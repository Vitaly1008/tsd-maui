using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd.Views;

// Страница "О программе"
public partial class AboutPage : BasePage
{
    private AboutViewModel _viewModel;

    // ✅ ИЗМЕНЕННЫЙ КОНСТРУКТОР
    public AboutPage(AboutViewModel viewModel)
    {
        InitializeComponent();
        
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }
}