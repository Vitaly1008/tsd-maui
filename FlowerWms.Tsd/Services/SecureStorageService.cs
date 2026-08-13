using Microsoft.Maui.Storage;

namespace FlowerWms.Tsd.Services;

// Безопасное хранение данных
public class SecureStorageService
{
    // Сохраняет токен
    public async Task SaveToken(string token)
    {
        await SecureStorage.SetAsync("token", token);
    }

    // Возвращает токен
    public async Task<string?> GetToken()
    {
        return await SecureStorage.GetAsync("token");
    }

    // Сохраняет данные пользователя
    public async Task SaveUser(string userJson)
    {
        await SecureStorage.SetAsync("user", userJson);
    }

    // Возвращает данные пользователя
    public async Task<string?> GetUser()
    {
        return await SecureStorage.GetAsync("user");
    }

    // Сохраняет адрес сервера
    public async Task SaveServerAdress(string key, string value)
    {
        await SecureStorage.SetAsync(key, value);
    }

    // Возвращает адрес сервера
    public async Task<string?> GetServerAdress(string key)
    {
        return await SecureStorage.GetAsync(key);
    }

    // Очищает все сохраненные данные
    public async Task Clear()
    {
        SecureStorage.Remove("token");
        SecureStorage.Remove("user");
    }
}