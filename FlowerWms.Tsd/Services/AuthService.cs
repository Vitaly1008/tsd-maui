using System.Text.Json;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.Services;

public class AuthService
{
    private readonly ApiService _api;
    private readonly SecureStorageService _secureStorage;
    private readonly LoggerService _logger;
    private readonly ServerDiscoveryService _discoveryService;

    public AuthService()
    {
        _api = new ApiService();
        _secureStorage = new SecureStorageService();
        _logger = new LoggerService();
        _discoveryService = new ServerDiscoveryService();
    }

    public async Task<LoginResponse> Login(string username, string password)
    {
        try
        {
            // ✅ Проверяем доступность сервера через простой Ping
            var isAvailable = await _discoveryService.PingServer(Constants.ApiBaseUrl);
            
            if (!isAvailable)
            {
                _logger.Warning("⚠️ Сервер не доступен. Попытка поиска...");
                
                // Пытаемся найти сервер
                var newServer = await _discoveryService.GetServerAddress();
                if (!string.IsNullOrEmpty(newServer))
                {
                    Constants.ApiBaseUrl = newServer;
                    _logger.Success($"✅ Сервер найден: {newServer}");
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
                
                _logger.Success($"✅ Пользователь {loginResponse.Username} вошел в систему");
                return loginResponse;
            }

            throw new Exception("Не удалось обработать ответ от сервера");
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка входа: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> ValidateToken(string token)
    {
        try
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            
            var response = await client.GetAsync($"{Constants.ApiBaseUrl}/api/auth/me");
            
            if (response.IsSuccessStatusCode)
            {
                _logger.Success("✅ Токен валидный");
                return true;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.Warning("⚠️ Токен невалидный (401)");
                return false;
            }

            _logger.Warning($"🌐 Не удалось проверить токен: {response.StatusCode}");
            return true;
        }
        catch
        {
            return true;
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
        _logger.Info("🔑 Пользователь вышел из системы");
    }
}