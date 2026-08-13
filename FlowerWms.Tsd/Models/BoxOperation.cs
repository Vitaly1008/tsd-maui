namespace FlowerWms.Tsd.Models;

// Модель операции с коробкой
public class BoxOperation
{
    public string Id { get; set; } = string.Empty;
    public string BoxId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? FromLocationId { get; set; }
    public string? ToLocationId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public long CreatedAt { get; set; }
    public int QuantityAfter { get; set; }
    public int QuantityBefore { get; set; }
    public string? Comment { get; set; }

    // Создает модель из JSON-словаря
    public static BoxOperation FromJson(Dictionary<string, object> json)
    {
        return new BoxOperation
        {
            Id = json.GetValueOrDefault("id", "")?.ToString() ?? "",
            BoxId = json.GetValueOrDefault("boxId", "")?.ToString() ?? "",
            OperationType = json.GetValueOrDefault("operationType", "")?.ToString() ?? "",
            Quantity = json.GetValueOrDefault("quantity", 0) is int q ? q : 0,
            FromLocationId = json.GetValueOrDefault("fromLocationId", "")?.ToString(),
            ToLocationId = json.GetValueOrDefault("toLocationId", "")?.ToString(),
            DeviceId = json.GetValueOrDefault("deviceId", "")?.ToString() ?? "",
            CreatedAt = json.GetValueOrDefault("createdAt", 0) is long ca ? ca : 0,
            QuantityAfter = json.GetValueOrDefault("quantityAfter", 0) is int qa ? qa : 0,
            QuantityBefore = json.GetValueOrDefault("quantityBefore", 0) is int qb ? qb : 0,
            Comment = json.GetValueOrDefault("comment", "")?.ToString()
        };
    }

    // Преобразует модель в словарь для отправки на сервер
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["id"] = Id,
            ["boxId"] = BoxId,
            ["operationType"] = OperationType,
            ["quantity"] = Quantity,
            ["fromLocationId"] = FromLocationId ?? "",
            ["toLocationId"] = ToLocationId ?? "",
            ["deviceId"] = DeviceId,
            ["createdAt"] = CreatedAt,
            ["quantityAfter"] = QuantityAfter,
            ["quantityBefore"] = QuantityBefore,
            ["comment"] = Comment ?? ""
        };
    }
}