using Microsoft.Maui.Storage;

namespace FlowerWms.Tsd.Services;

public class SecureStorageService
{
    public async Task SaveToken(string token)
    {
        await SecureStorage.SetAsync("token", token);
    }

    public async Task<string?> GetToken()
    {
        return await SecureStorage.GetAsync("token");
    }

    public async Task SaveUser(string userJson)
    {
        await SecureStorage.SetAsync("user", userJson);
    }

    public async Task<string?> GetUser()
    {
        return await SecureStorage.GetAsync("user");
    }

    // ✅ Добавляем универсальные методы для хранения любых данных
    public async Task SaveAsync(string key, string value)
    {
        await SecureStorage.SetAsync(key, value);
    }

    public async Task<string?> GetAsync(string key)
    {
        return await SecureStorage.GetAsync(key);
    }

    public async Task Clear()
    {
        SecureStorage.Remove("token");
        SecureStorage.Remove("user");
    }
}