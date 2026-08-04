namespace FlowerWms.Tsd.Models;

public class Box
{
    public string Id { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public int BoxNumber { get; set; }
    public string Grade { get; set; } = string.Empty;
    public int InitialQuantity { get; set; }
    public int CurrentQuantity { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string? LocationId { get; set; }
    public int Status { get; set; } = 1; // 1 = Active
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public string? OrderId { get; set; }
    
    // Вычисляемые поля (для отображения, не хранятся в БД)
    public string ProductName { get; set; } = string.Empty;
    public string ProductEan13 { get; set; } = string.Empty;
    public string? LocationCode { get; set; }

    // ✅ Добавляем свойство Quantity для удобства (алиас для CurrentQuantity)
    public int Quantity
    {
        get => CurrentQuantity;
        set => CurrentQuantity = value;
    }

    public static Box FromJson(Dictionary<string, object> json)
    {
        return new Box
        {
            Id = json.GetValueOrDefault("id", json.GetValueOrDefault("boxId", ""))?.ToString() ?? "",
            Barcode = json.GetValueOrDefault("barcode", "")?.ToString() ?? "",
            BoxNumber = json.GetValueOrDefault("boxNumber", 0) is int i ? i : 
                        int.TryParse(json.GetValueOrDefault("boxNumber", "0")?.ToString(), out var n) ? n : 0,
            Grade = json.GetValueOrDefault("grade", "Unknown")?.ToString() ?? "Unknown",
            InitialQuantity = json.GetValueOrDefault("initialQuantity", 0) is int iq ? iq :
                              int.TryParse(json.GetValueOrDefault("initialQuantity", "0")?.ToString(), out var iqn) ? iqn : 0,
            CurrentQuantity = json.GetValueOrDefault("currentQuantity", json.GetValueOrDefault("quantity", 0)) is int cq ? cq :
                              int.TryParse(json.GetValueOrDefault("currentQuantity", "0")?.ToString(), out var cqn) ? cqn : 0,
            ProductId = json.GetValueOrDefault("productId", "")?.ToString() ?? "",
            LocationId = json.GetValueOrDefault("locationId", "")?.ToString(),
            Status = json.GetValueOrDefault("status", 1) is int s ? s : 1,
            CreatedAt = json.GetValueOrDefault("createdAt", 0) is long ca ? ca : 0,
            UpdatedAt = json.GetValueOrDefault("updatedAt", 0) is long ua ? ua : 0,
            OrderId = json.GetValueOrDefault("orderId", "")?.ToString(),
            
            ProductName = json.GetValueOrDefault("productName", json.GetValueOrDefault("name", "Неизвестный продукт"))?.ToString() ?? "Неизвестный продукт",
            ProductEan13 = json.GetValueOrDefault("productEan13", json.GetValueOrDefault("ean13", ""))?.ToString() ?? "",
            LocationCode = json.GetValueOrDefault("locationCode", json.GetValueOrDefault("code", ""))?.ToString()
        };
    }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["id"] = Id,
            ["barcode"] = Barcode,
            ["boxNumber"] = BoxNumber,
            ["grade"] = Grade,
            ["initialQuantity"] = InitialQuantity,
            ["currentQuantity"] = CurrentQuantity,
            ["quantity"] = CurrentQuantity, // ✅ Добавляем для совместимости
            ["productId"] = ProductId,
            ["locationId"] = LocationId ?? "",
            ["status"] = Status,
            ["createdAt"] = CreatedAt,
            ["updatedAt"] = UpdatedAt,
            ["orderId"] = OrderId ?? "",
            ["productName"] = ProductName,
            ["productEan13"] = ProductEan13,
            ["locationCode"] = LocationCode ?? ""
        };
    }
}