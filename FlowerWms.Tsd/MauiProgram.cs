﻿using Microsoft.Extensions.Logging;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Platforms.Android;
using FlowerWms.Tsd.ViewModels;

namespace FlowerWms.Tsd;

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

        // ===== БАЗОВЫЕ =====
        builder.Services.AddSingleton<DatabaseHelper>();
        builder.Services.AddSingleton<SecureStorageService>();

        // ===== УРОВЕНЬ 2 =====
        builder.Services.AddSingleton<OfflineService>();
        builder.Services.AddSingleton<ServerDiscoveryService>();

        // ===== УРОВЕНЬ 3 =====
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<SyncQueueService>();

        // ===== УРОВЕНЬ 4 =====
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<AuthService>();

        // ===== УРОВЕНЬ 5 =====
        builder.Services.AddSingleton<NetworkService>();
        builder.Services.AddSingleton<IBarcodeService, BarcodeService>();

        // ===== VIEWMODELS =====
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