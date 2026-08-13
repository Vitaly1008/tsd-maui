using Microsoft.Extensions.Logging;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Platforms.Android;

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

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Регистрация сервисов как Singleton
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<OfflineService>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<SyncQueueService>();
        builder.Services.AddSingleton<SecureStorageService>();
        builder.Services.AddSingleton<NetworkService>();
        builder.Services.AddSingleton<ServerDiscoveryService>();
        
        // Регистрация IBarcodeService как Singleton
        builder.Services.AddSingleton<IBarcodeService, BarcodeService>();

        return builder.Build();
    }
}