namespace FlowerWms.Tsd.Models;

// Модель запроса аутентификации
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // Преобразует модель в словарь для отправки на сервер
    public Dictionary<string, string> ToDictionary()
    {
        return new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = Password
        };
    }
}

// Модель ответа аутентификации
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    // Создает модель из JSON-словаря
    public static LoginResponse FromJson(Dictionary<string, object> json)
    {
        return new LoginResponse
        {
            Token = json["token"]?.ToString() ?? string.Empty,
            Username = json["username"]?.ToString() ?? string.Empty,
            Role = json["role"]?.ToString() ?? string.Empty,
            ExpiresAt = DateTime.Parse(json["expiresAt"]?.ToString() ?? DateTime.UtcNow.ToString())
        };
    }
}