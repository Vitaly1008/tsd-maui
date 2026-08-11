using Microsoft.Maui;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Converters;

namespace FlowerWms.Tsd;

public partial class App : Application
{
    public App()
    {
        try
        {
            InitializeResources();
            MainPage = new NavigationPage(new Views.LoginPage());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Критическая ошибка при создании App: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    private void InitializeResources()
    {
        Resources = new ResourceDictionary();

        // Регистрация конвертеров
        Resources.Add("InverseBooleanConverter", new InverseBooleanConverter());
        Resources.Add("GreaterThanZeroConverter", new GreaterThanZeroConverter());
        Resources.Add("IsNotNullConverter", new IsNotNullConverter());
        Resources.Add("StringNotEmptyConverter", new StringNotEmptyConverter());
        Resources.Add("ConnectionIconConverter", new ConnectionIconConverter());
        Resources.Add("SyncStatusIconConverter", new SyncStatusIconConverter());
        Resources.Add("ModeBackgroundConverter", new ModeBackgroundConverter());
        Resources.Add("ModeTextColorConverter", new ModeTextColorConverter());
        Resources.Add("ModeLabelConverter", new ModeLabelConverter());
        Resources.Add("ScanStatusBackgroundConverter", new ScanStatusBackgroundConverter());
        Resources.Add("ScanStatusBorderConverter", new ScanStatusBorderConverter());
        Resources.Add("ScanStatusIconConverter", new ScanStatusIconConverter());
        Resources.Add("ScanStatusTextConverter", new ScanStatusTextConverter());
        Resources.Add("ScanStatusColorConverter", new ScanStatusColorConverter());
        Resources.Add("ScanStatusSubtitleConverter", new ScanStatusSubtitleConverter());
        Resources.Add("SearchButtonTextConverter", new SearchButtonTextConverter());
        Resources.Add("StatusBarPaddingConverter", new StatusBarPaddingConverter());
        Resources.Add("ExpandIconConverter", new ExpandIconConverter());

        Resources.Add("BoxStatusIconConverter", new BoxStatusIconConverter());
        Resources.Add("BoxStatusLabelConverter", new BoxStatusLabelConverter());
        Resources.Add("BoxStatusColorConverter", new BoxStatusColorConverter());
        Resources.Add("LocationDisplayConverter", new LocationDisplayConverter());

        Resources.Add("StatusTextConverter", new StatusTextConverter());
        Resources.Add("StatusColorConverter", new StatusColorConverter());
        Resources.Add("ModeBorderConverter", new ModeBorderConverter());

        // Цвета
        Resources.Add("PrimaryColor", Color.FromArgb("#2E7D32"));
        Resources.Add("PrimaryDark", Color.FromArgb("#1B5E20"));
        Resources.Add("SecondaryColor", Color.FromArgb("#4CAF50"));
        Resources.Add("SuccessColor", Color.FromArgb("#52c41a"));
        Resources.Add("DangerColor", Color.FromArgb("#ff4d4f"));
        Resources.Add("WarningColor", Color.FromArgb("#faad14"));
        Resources.Add("InfoColor", Color.FromArgb("#1890ff"));

        AddStyles();
    }

    private void AddStyles()
    {
        Resources.Add("PrimaryButton", new Style(typeof(Button))
        {
            Setters = {
                new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#2E7D32") },
                new Setter { Property = Button.TextColorProperty, Value = Colors.White },
                new Setter { Property = Button.FontSizeProperty, Value = 15.0 },
                new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Button.HeightRequestProperty, Value = 44.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 10 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(12, 8) }
            }
        });

        Resources.Add("SecondaryButton", new Style(typeof(Button))
        {
            Setters = {
                new Setter { Property = Button.BackgroundColorProperty, Value = Colors.White },
                new Setter { Property = Button.TextColorProperty, Value = Color.FromArgb("#2E7D32") },
                new Setter { Property = Button.FontSizeProperty, Value = 15.0 },
                new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Button.HeightRequestProperty, Value = 44.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 10 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(12, 8) },
                new Setter { Property = Button.BorderWidthProperty, Value = 1.5 },
                new Setter { Property = Button.BorderColorProperty, Value = Color.FromArgb("#2E7D32") }
            }
        });

        Resources.Add("DangerButton", new Style(typeof(Button))
        {
            Setters = {
                new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#ff4d4f") },
                new Setter { Property = Button.TextColorProperty, Value = Colors.White },
                new Setter { Property = Button.FontSizeProperty, Value = 15.0 },
                new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Button.HeightRequestProperty, Value = 44.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 10 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(12, 8) }
            }
        });

        Resources.Add("SuccessButton", new Style(typeof(Button))
        {
            Setters = {
                new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#4CAF50") },
                new Setter { Property = Button.TextColorProperty, Value = Colors.White },
                new Setter { Property = Button.FontSizeProperty, Value = 15.0 },
                new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Button.HeightRequestProperty, Value = 44.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 10 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(12, 8) }
            }
        });
    }
}