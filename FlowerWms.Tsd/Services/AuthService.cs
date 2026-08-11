using System.Text.Json;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Services;

public class AuthService
{
    private readonly ApiService _api;
    private readonly SecureStorageService _secureStorage;
    private readonly ServerDiscoveryService _discoveryService;
    private readonly ApiService _apiService;

    public AuthService()
    {
        _api = new ApiService();
        _secureStorage = new SecureStorageService();
        _discoveryService = new ServerDiscoveryService();
        _apiService = new ApiService();
    }

    public async Task<LoginResponse> Login(string username, string password)
    {
        try
        {
            // ✅ Проверяем доступность сервера через простой Ping
            var isAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            
            if (!isAvailable)
            {   
                // Пытаемся найти сервер
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

            var request = new LoginRequest { Username = username, Password = password };
            
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            
            var content = new StringContent(
                JsonSerializer.Serialize(new { username, password }),
                System.Text.Encoding.UTF8,
                "application/json"
            );
            
            var response = await client.PostAsync($"{Constants.ApiBaseUrl}/api/auth/login", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Ошибка авторизации: {responseContent}");
            }

            var loginResponse = JsonSerializer.Deserialize<LoginResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (loginResponse != null)
            {
                await _secureStorage.SaveToken(loginResponse.Token);
                await _secureStorage.SaveUser(JsonSerializer.Serialize(new
                {
                    loginResponse.Username,
                    loginResponse.Role
                }));
                return loginResponse;
            }

            throw new Exception("Не удалось обработать ответ от сервера");
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public async Task<bool> ValidateToken()
    {
        try
        {
            var token = await _secureStorage.GetToken();
            if (string.IsNullOrEmpty(token))
                return false;
            
            // Проверка через API
            return await _apiService.PingServer();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsAuthenticated()
    {
        var token = await _secureStorage.GetToken();
        return !string.IsNullOrEmpty(token);
    }

    public async Task Logout()
    {
        await _secureStorage.Clear();
    }
}