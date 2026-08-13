namespace FlowerWms.Tsd.Models;

// Модель локации
public class Location
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public long CreatedAt { get; set; }
    public string? Barcode { get; set; }

    // Создает модель из JSON-словаря
    public static Location FromJson(Dictionary<string, object> json)
    {
        return new Location
        {
            Id = json.GetValueOrDefault("id", "")?.ToString() ?? "",
            Code = json.GetValueOrDefault("code", "")?.ToString() ?? "",
            Name = json.GetValueOrDefault("name", "")?.ToString() ?? "",
            IsActive = json.GetValueOrDefault("isActive", true) is bool ia ? ia : true,
            CreatedAt = json.GetValueOrDefault("createdAt", 0) is long ca ? ca : 0,
            Barcode = json.GetValueOrDefault("barcode", "")?.ToString()
        };
    }

    // Преобразует модель в словарь для отправки на сервер
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["id"] = Id,
            ["code"] = Code,
            ["name"] = Name,
            ["isActive"] = IsActive,
            ["createdAt"] = CreatedAt,
            ["barcode"] = Barcode ?? ""
        };
    }
}