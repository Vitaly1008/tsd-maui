namespace FlowerWms.Tsd.Models;

// Модель коробки
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
    public int Status { get; set; } = 1; // 1 - Active
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public string? OrderId { get; set; }
    
    // Вычисляемые поля для отображения (не хранятся в БД)
    public string ProductName { get; set; } = string.Empty;
    public string ProductEan13 { get; set; } = string.Empty;
    public string? LocationCode { get; set; }

    // Алиас для CurrentQuantity
    public int Quantity
    {
        get => CurrentQuantity;
        set => CurrentQuantity = value;
    }

    public bool IsDirty { get; set; }

    // Создает модель из JSON-словаря
    public static Box FromJson(Dictionary<string, object> json)
    {
        string id = "";
        var idObj = json.GetValueOrDefault("id", "");
        if (idObj is Guid guid)
            id = guid.ToString();
        else
            id = idObj?.ToString() ?? "";
        
        string barcode = json.GetValueOrDefault("barcode", "")?.ToString() ?? "";
        
        int boxNumber = 0;
        var boxNumberObj = json.GetValueOrDefault("boxNumber", 0);
        if (boxNumberObj is int i)
            boxNumber = i;
        else if (boxNumberObj is long l)
            boxNumber = (int)l;
        else if (boxNumberObj is string s && int.TryParse(s, out var n))
            boxNumber = n;
        else if (boxNumberObj is double d)
            boxNumber = (int)d;
        
        if (boxNumber == 0 && !string.IsNullOrEmpty(barcode))
        {
            var parts = barcode.Split('-');
            if (parts.Length == 4 && int.TryParse(parts[3], out var n))
                boxNumber = n;
        }
        
        string grade = "Premium";
        var gradeObj = json.GetValueOrDefault("grade", 9);
        
        if (gradeObj is int gradeInt)
        {
            grade = gradeInt switch
            {
                9 => "Premium",
                1 => "First",
                2 => "Second",
                3 => "Decorated",
                5 => "Rejected",
                _ => gradeObj?.ToString() ?? "Premium"
            };
        }
        else if (gradeObj is long gradeLong)
        {
            grade = ((int)gradeLong) switch
            {
                9 => "Premium",
                1 => "First",
                2 => "Second",
                3 => "Decorated",
                5 => "Rejected",
                _ => gradeObj?.ToString() ?? "Premium"
            };
        }
        else if (gradeObj is string gradeStr)
        {
            if (int.TryParse(gradeStr, out var g))
            {
                grade = g switch
                {
                    9 => "Premium",
                    1 => "First",
                    2 => "Second",
                    3 => "Decorated",
                    5 => "Rejected",
                    _ => gradeStr
                };
            }
            else
            {
                grade = gradeStr;
            }
        }
        
        int currentQuantity = 0;
        int initialQuantity = 0;
        
        var currentQtyObj = json.GetValueOrDefault("currentQuantity", 0);
        if (currentQtyObj is int cqi)
            currentQuantity = cqi;
        else if (currentQtyObj is long cql)
            currentQuantity = (int)cql;
        else if (currentQtyObj is string cqs && int.TryParse(cqs, out var cqn))
            currentQuantity = cqn;
        else if (currentQtyObj is double cqd)
            currentQuantity = (int)cqd;
        
        var initialQtyObj = json.GetValueOrDefault("initialQuantity", currentQuantity);
        if (initialQtyObj is int iqi)
            initialQuantity = iqi;
        else if (initialQtyObj is long iql)
            initialQuantity = (int)iql;
        else if (initialQtyObj is string iqs && int.TryParse(iqs, out var iqn))
            initialQuantity = iqn;
        else if (initialQtyObj is double iqd)
            initialQuantity = (int)iqd;
        
        if (currentQuantity == 0 && !string.IsNullOrEmpty(barcode))
        {
            var parts = barcode.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var q))
                currentQuantity = q;
        }
        if (initialQuantity == 0)
            initialQuantity = currentQuantity;
        
        int status = 1;
        var statusObj = json.GetValueOrDefault("status", 1);
        if (statusObj is int si)
            status = si;
        else if (statusObj is long sl)
            status = (int)sl;
        else if (statusObj is string ss && int.TryParse(ss, out var sn))
            status = sn;
        
        long createdAt = 0;
        var createdAtObj = json.GetValueOrDefault("createdAt", 0);
        if (createdAtObj is long cal)
            createdAt = cal;
        else if (createdAtObj is DateTime dt)
            createdAt = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        
        long updatedAt = 0;
        var updatedAtObj = json.GetValueOrDefault("updatedAt", 0);
        if (updatedAtObj is long ual)
            updatedAt = ual;
        else if (updatedAtObj is DateTime udt)
            updatedAt = new DateTimeOffset(udt).ToUnixTimeMilliseconds();
        
        string productId = json.GetValueOrDefault("productId", "")?.ToString() ?? "";
        string productName = json.GetValueOrDefault("productName", "")?.ToString() ?? "";
        if (string.IsNullOrEmpty(productName))
            productName = json.GetValueOrDefault("name", "Неизвестный продукт")?.ToString() ?? "Неизвестный продукт";
        string productEan13 = json.GetValueOrDefault("ean13", json.GetValueOrDefault("Ean13", ""))?.ToString() ?? "";
        string locationCode = json.GetValueOrDefault("locationCode", "")?.ToString() ?? "";
        string locationId = json.GetValueOrDefault("locationId", "")?.ToString() ?? "";
        
        return new Box
        {
            Id = id,
            Barcode = barcode,
            BoxNumber = boxNumber,
            Grade = grade,
            InitialQuantity = initialQuantity,
            CurrentQuantity = currentQuantity,
            ProductId = productId,
            LocationId = locationId,
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            OrderId = json.GetValueOrDefault("orderId", "")?.ToString(),
            ProductName = productName,
            ProductEan13 = productEan13,
            LocationCode = locationCode
        };
    }

    // Преобразует модель в словарь для отправки на сервер
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
            ["quantity"] = CurrentQuantity,
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