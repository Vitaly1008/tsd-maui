using Microsoft.Maui;
using Microsoft.Maui.Controls;
using FlowerWms.Tsd.Converters;
using FlowerWms.Tsd.Views;
using FlowerWms.Tsd.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FlowerWms.Tsd;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    // ✅ ИЗМЕНЕННЫЙ КОНСТРУКТОР
    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        
        try
        {
            InitializeResources();
            
            // ✅ СОЗДАЕМ LoginPage ЧЕРЕЗ DI
            var loginViewModel = _serviceProvider.GetService<LoginViewModel>();
            var loginPage = new LoginPage(loginViewModel, _serviceProvider);
            MainPage = new NavigationPage(loginPage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Критическая ошибка при создании App: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    // Инициализирует ресурсы приложения
    private void InitializeResources()
    {
        Resources = new ResourceDictionary();

        // ============================================================
        // СУЩЕСТВУЮЩИЕ КОНВЕРТЕРЫ
        // ============================================================
        Resources.Add("InvertBooleanConverter", new InvertBooleanConverter());
        Resources.Add("GreaterThanZeroConverter", new GreaterThanZeroConverter());
        Resources.Add("IsNotNullOrEmptyConverter", new IsNotNullOrEmptyConverter());
        Resources.Add("ConnectionIconConverter", new ConnectionIconConverter());
        Resources.Add("SyncStatusIconConverter", new SyncStatusIconConverter());
        Resources.Add("ModeBackgroundConverter", new ModeBackgroundConverter());
        Resources.Add("ModeTextColorConverter", new ModeTextColorConverter());
        Resources.Add("ModeLabelConverter", new ModeLabelConverter());
        Resources.Add("ModeBorderConverter", new ModeBorderConverter());
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
        Resources.Add("BoxStatusTextConverter", new BoxStatusTextConverter());
        Resources.Add("BoxStatusColorConverter", new BoxStatusColorConverter());
        Resources.Add("BoxStatusBackgroundConverter", new BoxStatusBackgroundConverter());
        Resources.Add("LocationDisplayConverter", new LocationDisplayConverter());

        // ============================================================
        // НОВЫЕ КОНВЕРТЕРЫ ДЛЯ ОЧЕРЕДИ СИНХРОНИЗАЦИИ
        // ============================================================
        Resources.Add("OperationIconConverter", new OperationIconConverter());
        Resources.Add("OperationTypeDisplayConverter", new OperationTypeDisplayConverter());
        Resources.Add("StatusDisplayConverter", new StatusDisplayConverter());
        Resources.Add("StatusColorConverter", new StatusColorConverter());
        Resources.Add("RetryCountDisplayConverter", new RetryCountDisplayConverter());
        Resources.Add("CreatedAtDisplayConverter", new CreatedAtDisplayConverter());
        Resources.Add("StringNotEmptyConverter", new StringNotEmptyConverter());
        Resources.Add("IsZeroConverter", new IsZeroConverter());

        // ============================================================
        // СТАНДАРТНЫЕ КОНВЕРТЕРЫ
        // ============================================================
        Resources.Add("IsNullConverter", new IsNullConverter());
        Resources.Add("CollectionNotEmptyConverter", new CollectionNotEmptyConverter());
        Resources.Add("EqualsValueConverter", new EqualsValueConverter());

        // ============================================================
        // ЦВЕТА
        // ============================================================
        Resources.Add("PrimaryColor", Color.FromArgb("#2E7D32"));
        Resources.Add("PrimaryDark", Color.FromArgb("#1B5E20"));
        Resources.Add("SecondaryColor", Color.FromArgb("#4CAF50"));
        Resources.Add("SuccessColor", Color.FromArgb("#52c41a"));
        Resources.Add("DangerColor", Color.FromArgb("#ff4d4f"));
        Resources.Add("WarningColor", Color.FromArgb("#faad14"));
        Resources.Add("InfoColor", Color.FromArgb("#1890ff"));

        AddStyles();
    }

    // Добавляет стили для кнопок
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

        // Маленькие кнопки для очереди
        Resources.Add("SmallPrimaryButton", new Style(typeof(Button))
        {
            BasedOn = Resources["PrimaryButton"] as Style,
            Setters = {
                new Setter { Property = Button.HeightRequestProperty, Value = 32 },
                new Setter { Property = Button.FontSizeProperty, Value = 11 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(8, 0) }
            }
        });

        Resources.Add("SmallSuccessButton", new Style(typeof(Button))
        {
            BasedOn = Resources["SuccessButton"] as Style,
            Setters = {
                new Setter { Property = Button.HeightRequestProperty, Value = 28 },
                new Setter { Property = Button.FontSizeProperty, Value = 12 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(0) },
                new Setter { Property = Button.WidthRequestProperty, Value = 36 }
            }
        });

        Resources.Add("SmallDangerButton", new Style(typeof(Button))
        {
            BasedOn = Resources["DangerButton"] as Style,
            Setters = {
                new Setter { Property = Button.HeightRequestProperty, Value = 28 },
                new Setter { Property = Button.FontSizeProperty, Value = 12 },
                new Setter { Property = Button.PaddingProperty, Value = new Thickness(0) },
                new Setter { Property = Button.WidthRequestProperty, Value = 36 }
            }
        });
    }
}