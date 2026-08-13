using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using FlowerWms.Tsd.Platforms.Android;

namespace FlowerWms.Tsd;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize,
    LaunchMode = LaunchMode.SingleTop
)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        
        // Сохраняет контекст для BarcodeService
        AndroidContext.Current = this.ApplicationContext;
        
        EnableDataWedge();
    }

    // Включает DataWedge для сканера Symbol/Zebra
    private void EnableDataWedge()
    {
        try
        {
            var intent = new Intent("com.symbol.datawedge.api.ACTION_SET_CONFIG");
            intent.PutExtra("com.symbol.datawedge.api.EXTRA_CONFIG", @"
            {
                ""MAIN_SCANNER"": {
                    ""SCANNER_ENABLED"": true
                }
            }");
            Android.App.Application.Context.SendBroadcast(intent);
            System.Diagnostics.Debug.WriteLine("DataWedge включен");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DataWedge ошибка: {ex.Message}");
        }
    }
}