using System.Text.Json;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Services;

// Сервис аутентификации
public class AuthService
{
    private readonly ApiService _apiService;
    private readonly SecureStorageService _secureStorage;
    private readonly ServerDiscoveryService _discoveryService;

    // ✅ ДОБАВЛЕНО: DI через конструктор
    public AuthService(
        ApiService apiService,
        SecureStorageService secureStorage,
        ServerDiscoveryService discoveryService)
    {
        _apiService = apiService;
        _secureStorage = secureStorage;
        _discoveryService = discoveryService;
    }

    // Выполняет вход в систему
    public async Task<LoginResponse> Login(string username, string password)
    {
        // Проверяет доступность сервера
        var isAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);

        if (!isAvailable)
        {
            var newServer = await _discoveryService.GetServerAddress();
            if (!string.IsNullOrEmpty(newServer))
            {
                Constants.ApiBaseUrl = newServer;
            }
            else
            {
                throw new Exception("Сервер не найден в сети. Проверьте подключение.");
            }
        }

        // ✅ ИСПРАВЛЕНО: логин через ApiService (общий HttpClient)
        var loginResponse = await _apiService.LoginAsync(username, password);

        if (loginResponse == null || string.IsNullOrEmpty(loginResponse.Token))
        {
            throw new Exception("Неверный логин или пароль");
        }

        await _secureStorage.SaveToken(loginResponse.Token);
        await _secureStorage.SaveUser(JsonSerializer.Serialize(new
        {
            loginResponse.Username,
            loginResponse.Role
        }));

        return loginResponse;
    }

    // Проверяет валидность токена
    public async Task<bool> ValidateToken()
    {
        try
        {
            var token = await _secureStorage.GetToken();
            if (string.IsNullOrEmpty(token))
                return false;

            return await _apiService.PingServer();
        }
        catch
        {
            return false;
        }
    }

    // Проверяет, авторизован ли пользователь
    public async Task<bool> IsAuthenticated()
    {
        var token = await _secureStorage.GetToken();
        return !string.IsNullOrEmpty(token);
    }

    // Выполняет выход из системы
    public async Task Logout()
    {
        await _secureStorage.Clear();
    }
}