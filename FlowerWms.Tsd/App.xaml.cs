using Microsoft.Maui;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Converters;

namespace FlowerWms.Tsd;

// ✅ ДОБАВЛЯЕМ КЛЮЧЕВОЕ СЛОВО partial
public partial class App : Application
{
    public App()
    {
        try
        {
            InitializeResources();
            Resources.Add("StatusBarPaddingConverter", new StatusBarPaddingConverter());
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

        // Цвета - используем Color.FromArgb
        Resources.Add("PrimaryColor", Color.FromArgb("#2E7D32"));
        Resources.Add("PrimaryDark", Color.FromArgb("#1B5E20"));
        Resources.Add("SecondaryColor", Color.FromArgb("#4CAF50"));
        Resources.Add("SuccessColor", Color.FromArgb("#52c41a"));
        Resources.Add("DangerColor", Color.FromArgb("#ff4d4f"));
        Resources.Add("WarningColor", Color.FromArgb("#faad14"));
        Resources.Add("InfoColor", Color.FromArgb("#1890ff"));

        // Стиль для кнопок - PrimaryButton
        Resources.Add("PrimaryButton", new Style(typeof(Button))
        {
            Setters = {
                new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#2E7D32") },
                new Setter { Property = Button.TextColorProperty, Value = Colors.White },
                new Setter { Property = Button.FontSizeProperty, Value = 14.0 },
                new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Button.HeightRequestProperty, Value = 42.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 8 }
            }
        });

        // Стиль для кнопок - SecondaryButton
        Resources.Add("SecondaryButton", new Style(typeof(Button))
        {
            Setters = {
                new Setter { Property = Button.BackgroundColorProperty, Value = Colors.White },
                new Setter { Property = Button.TextColorProperty, Value = Color.FromArgb("#2E7D32") },
                new Setter { Property = Button.FontSizeProperty, Value = 14.0 },
                new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Button.HeightRequestProperty, Value = 42.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 8 },
                new Setter { Property = Button.BorderWidthProperty, Value = 1.0 },
                new Setter { Property = Button.BorderColorProperty, Value = Color.FromArgb("#2E7D32") }
            }
        });

        // Стиль для кнопок - DangerButton
        Resources.Add("DangerButton", new Style(typeof(Button))
        {
            Setters = {
                new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#ff4d4f") },
                new Setter { Property = Button.TextColorProperty, Value = Colors.White },
                new Setter { Property = Button.FontSizeProperty, Value = 14.0 },
                new Setter { Property = Button.FontAttributesProperty, Value = FontAttributes.Bold },
                new Setter { Property = Button.HeightRequestProperty, Value = 42.0 },
                new Setter { Property = Button.CornerRadiusProperty, Value = 8 }
            }
        });

        // Стиль для Entry
        Resources.Add(new Style(typeof(Entry))
        {
            Setters = {
                new Setter { Property = Entry.HeightRequestProperty, Value = 48.0 },
                new Setter { Property = Entry.FontSizeProperty, Value = 16.0 },
                new Setter { Property = Entry.BackgroundColorProperty, Value = Colors.White },
                new Setter { Property = Entry.TextColorProperty, Value = Color.FromArgb("#333333") }
            }
        });

        // Стиль для Label
        Resources.Add(new Style(typeof(Label))
        {
            Setters = {
                new Setter { Property = Label.FontFamilyProperty, Value = "Arial" }
            }
        });
    }
}