namespace FlowerWms.Tsd.Models;

// Модель товара
public class Product
{
    public string Id { get; set; } = string.Empty;
    public string Ean13 { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ShortName { get; set; }
    public string? OneCGuid { get; set; }
    public string? Barcode { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    // Создает модель из JSON-словаря
    public static Product FromJson(Dictionary<string, object> json)
    {
        return new Product
        {
            Id = json.GetValueOrDefault("id", "")?.ToString() ?? "",
            Ean13 = json.GetValueOrDefault("ean13", "")?.ToString() ?? "",
            Name = json.GetValueOrDefault("name", "")?.ToString() ?? "",
            ShortName = json.GetValueOrDefault("shortName", "")?.ToString(),
            OneCGuid = json.GetValueOrDefault("oneCGuid", "")?.ToString(),
            Barcode = json.GetValueOrDefault("barcode", "")?.ToString(),
            CreatedAt = json.GetValueOrDefault("createdAt", 0) is long ca ? ca : 0,
            UpdatedAt = json.GetValueOrDefault("updatedAt", 0) is long ua ? ua : 0
        };
    }

    // Преобразует модель в словарь для отправки на сервер
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["id"] = Id,
            ["ean13"] = Ean13,
            ["name"] = Name,
            ["shortName"] = ShortName ?? "",
            ["oneCGuid"] = OneCGuid ?? "",
            ["barcode"] = Barcode ?? "",
            ["createdAt"] = CreatedAt,
            ["updatedAt"] = UpdatedAt
        };
    }
}