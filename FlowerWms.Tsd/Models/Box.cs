namespace FlowerWms.Tsd.Models;

public class Box
{
    public string Id { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public int BoxNumber { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductEan13 { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public string? OrderId { get; set; }
    public string? Status { get; set; }

    public static Box FromJson(Dictionary<string, object> json)
    {
        return new Box
        {
            Id = json.GetValueOrDefault("boxId", json.GetValueOrDefault("id", ""))?.ToString() ?? "",
            Barcode = json.GetValueOrDefault("barcode", "")?.ToString() ?? "",
            BoxNumber = json.GetValueOrDefault("boxNumber", 0) is int i ? i : 
                        int.TryParse(json.GetValueOrDefault("boxNumber", "0")?.ToString(), out var n) ? n : 0,
            ProductName = json.GetValueOrDefault("productName", json.GetValueOrDefault("name", "Неизвестный продукт"))?.ToString() ?? "Неизвестный продукт",
            ProductEan13 = json.GetValueOrDefault("productEan13", json.GetValueOrDefault("ean13", ""))?.ToString() ?? "",
            Quantity = json.GetValueOrDefault("quantity", json.GetValueOrDefault("currentQuantity", 0)) is int q ? q :
                       int.TryParse(json.GetValueOrDefault("quantity", "0")?.ToString(), out var qn) ? qn : 0,
            Grade = json.GetValueOrDefault("grade", "Unknown")?.ToString() ?? "Unknown",
            LocationId = json.GetValueOrDefault("locationId", "")?.ToString(),
            LocationCode = json.GetValueOrDefault("locationCode", "")?.ToString(),
            OrderId = json.GetValueOrDefault("orderId", "")?.ToString(),
            Status = json.GetValueOrDefault("status", "")?.ToString()
        };
    }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["id"] = Id,
            ["barcode"] = Barcode,
            ["boxNumber"] = BoxNumber,
            ["productName"] = ProductName,
            ["productEan13"] = ProductEan13,
            ["quantity"] = Quantity,
            ["grade"] = Grade,
            ["locationId"] = LocationId ?? "",
            ["locationCode"] = LocationCode ?? "",
            ["orderId"] = OrderId ?? "",
            ["status"] = Status ?? ""
        };
    }
}

// Вспомогательный класс для работы с Dictionary
public static class DictionaryExtensions
{
    public static object? GetValueOrDefault(this Dictionary<string, object> dict, string key, object? defaultValue = null)
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }
}