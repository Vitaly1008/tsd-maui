using Microsoft.Extensions.Logging;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Platforms.Android;
using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd;

// Конфигурация приложения
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Регистрация сервисов как Singleton
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<OfflineService>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<SyncQueueService>();
        builder.Services.AddSingleton<SecureStorageService>();
        builder.Services.AddSingleton<NetworkService>();
        builder.Services.AddSingleton<ServerDiscoveryService>();
        builder.Services.AddSingleton<IBarcodeService, BarcodeService>();

        // ✅ РЕГИСТРАЦИЯ ВСЕХ VIEWMODEL
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<SyncQueueViewModel>();
        builder.Services.AddTransient<AboutViewModel>();
        builder.Services.AddTransient<ReceivingViewModel>();
        builder.Services.AddTransient<ShippingViewModel>();
        builder.Services.AddTransient<InventoryViewModel>();

        return builder.Build();
    }
}