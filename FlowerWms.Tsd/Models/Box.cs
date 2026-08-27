using FlowerWms.Tsd.Helpers;  // Добавляем для BoxCache

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
    
    // Изменяем int на BoxStatus
    public BoxStatus Status { get; set; } = BoxStatus.Draft;
    
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

    public bool IsPartial { get; set; }

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

        // Изменяем парсинг статуса - используем BoxStatus
        BoxStatus status = BoxStatus.Draft;
        var statusObj = json.GetValueOrDefault("status", 1);
        
        if (statusObj is BoxStatus bs)
            status = bs;
        else if (statusObj is int si)
            status = (BoxStatus)si;
        else if (statusObj is long sl)
            status = (BoxStatus)sl;
        else if (statusObj is string ss)
        {
            if (int.TryParse(ss, out var sn))
                status = (BoxStatus)sn;
            else if (Enum.TryParse<BoxStatus>(ss, true, out var se))
                status = se;
        }

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
            ["status"] = (int)Status,  // Приводим к int для отправки
            ["createdAt"] = CreatedAt,
            ["updatedAt"] = UpdatedAt,
            ["orderId"] = OrderId ?? "",
            ["productName"] = ProductName,
            ["productEan13"] = ProductEan13,
            ["locationCode"] = LocationCode ?? ""
        };
    }

    // Преобразует Box в BoxCache для локального хранения
    public BoxCache ToCache()
    {
        return new BoxCache
        {
            barcode = Barcode,
            box_id = Id,
            box_number = BoxNumber,
            grade = Grade,
            initial_quantity = InitialQuantity,
            current_quantity = CurrentQuantity,
            product_id = ProductId,
            product_name = ProductName,
            product_ean13 = ProductEan13,
            location_code = LocationCode ?? "UNKNOWN",
            status = Status,
            created_at = CreatedAt,
            updated_at = UpdatedAt
        };
    }

    // Создает Box из BoxCache
    public static Box FromCache(BoxCache cache)
    {
        return new Box
        {
            Id = cache.box_id,
            Barcode = cache.barcode,
            BoxNumber = cache.box_number,
            Grade = cache.grade,
            InitialQuantity = cache.initial_quantity,
            CurrentQuantity = cache.current_quantity,
            ProductId = cache.product_id,
            ProductName = cache.product_name,
            ProductEan13 = cache.product_ean13,
            LocationCode = cache.location_code,
            Status = cache.status,
            CreatedAt = cache.created_at,
            UpdatedAt = cache.updated_at,
            IsPartial = false // ✅ ВСЕГДА FALSE
        };
    }
}