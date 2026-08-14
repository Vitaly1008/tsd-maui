using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Services;

namespace FlowerWms.Tsd.ViewModels;

// ViewModel для страницы "О программе"
public partial class AboutViewModel : ObservableObject
{
    private readonly SyncQueueService _syncQueueService;
    private readonly AuthService _authService;

    [ObservableProperty]
    private string _appVersion = VersionHelper.GetVersion();

    [ObservableProperty]
    private string _buildDate = $"📅 Дата сборки: {VersionHelper.GetBuildDate()}";

    [ObservableProperty]
    private string _appName = "ALPHA WMS";

    [ObservableProperty]
    private string _appIcon = "📦";

    [ObservableProperty]
    private string _appMode = "Full Offline Mode";

    [ObservableProperty]
    private string _copyright = "© 2026 Alpha Flowers";

    [ObservableProperty]
    private string _copyrightDetail = "Все права защищены";

    [ObservableProperty]
    private bool _isClearing;

    public AboutViewModel()
    {
        _syncQueueService = new SyncQueueService();
        _authService = new AuthService();
    }

    // Очищает таблицу синхронизации (офлайн-транзакции)
    [RelayCommand]
    private async Task ClearSyncTable()
    {
        try
        {
            // Первое подтверждение - предупреждение
            var confirm = await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "⚠️ Очистка таблицы синхронизации",
                "Вы уверены, что хотите очистить таблицу синхронизации?\n\n" +
                "Все несинхронизированные операции будут потеряны безвозвратно!",
                "Да, продолжить",
                "Отмена"
            );

            if (confirm != true)
                return;

            // Второе подтверждение - ввод пароля
            var password = await Application.Current?.Windows[0]?.Page?.DisplayPromptAsync(
                "🔐 Подтверждение паролем",
                "Введите пароль вашей учетной записи для подтверждения операции:",
                "Подтвердить",
                "Отмена",
                maxLength: 50,
                keyboard: Keyboard.Text
            );

            if (string.IsNullOrEmpty(password))
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "Операция отменена",
                    "Вы не ввели пароль. Очистка отменена.",
                    "OK"
                );
                return;
            }

            // Проверяем пароль через AuthService
            var isValid = await ValidateUserPassword(password);
            
            if (!isValid)
            {
                await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                    "❌ Ошибка",
                    "Неверный пароль. Операция отменена.",
                    "OK"
                );
                return;
            }

            // Третье подтверждение - финальное
            var finalConfirm = await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "⚠️ Финальное подтверждение",
                "Вы действительно хотите очистить все несинхронизированные транзакции?\n\n" +
                "Это действие НЕЛЬЗЯ будет отменить!",
                "Да, я уверен",
                "Нет, отмена"
            );

            if (finalConfirm != true)
                return;

            IsClearing = true;

            // Выполняем очистку
            var count = await _syncQueueService.ClearSyncTable();

            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "✅ Очистка выполнена",
                $"Удалено {count} несинхронизированных транзакций.\n\n" +
                "Теперь вы можете продолжить работу.",
                "OK"
            );
        }
        catch (Exception ex)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "❌ Ошибка",
                $"Не удалось очистить таблицу синхронизации:\n{ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsClearing = false;
        }
    }

    // Проверяет пароль пользователя
    private async Task<bool> ValidateUserPassword(string password)
    {
        try
        {
            // Получаем текущего пользователя из SecureStorage
            var userJson = await SecureStorage.GetAsync("user");
            if (string.IsNullOrEmpty(userJson))
                return false;

            // Парсим данные пользователя
            var user = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(userJson);
            var username = user?.GetValueOrDefault("username") ?? string.Empty;

            if (string.IsNullOrEmpty(username))
                return false;

            // Пытаемся выполнить логин с введенным паролем
            var authService = new AuthService();
            var loginResult = await authService.Login(username, password);
            
            return loginResult != null && !string.IsNullOrEmpty(loginResult.Token);
        }
        catch
        {
            return false;
        }
    }
}