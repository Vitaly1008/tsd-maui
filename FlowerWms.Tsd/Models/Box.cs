using FlowerWms.Tsd.Helpers;
using System.Text.Json;

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
    public BoxStatus Status { get; set; } = BoxStatus.Draft;
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public string? OrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductEan13 { get; set; } = string.Empty;
    public string? LocationCode { get; set; }

    public int Quantity
    {
        get => CurrentQuantity;
        set => CurrentQuantity = value;
    }

    public bool IsPartial { get; set; }

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
        else if (boxNumberObj is JsonElement jeBox && jeBox.ValueKind == JsonValueKind.Number)
            boxNumber = jeBox.GetInt32();
        
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
        else if (gradeObj is JsonElement jeGrade)
        {
            if (jeGrade.ValueKind == JsonValueKind.String)
            {
                grade = jeGrade.GetString() ?? "Premium";
            }
            else if (jeGrade.ValueKind == JsonValueKind.Number)
            {
                var g = jeGrade.GetInt32();
                grade = g switch
                {
                    9 => "Premium",
                    1 => "First",
                    2 => "Second",
                    3 => "Decorated",
                    5 => "Rejected",
                    _ => "Premium"
                };
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
        else if (currentQtyObj is JsonElement jeQty && jeQty.ValueKind == JsonValueKind.Number)
            currentQuantity = jeQty.GetInt32();
        
        var initialQtyObj = json.GetValueOrDefault("initialQuantity", currentQuantity);
        if (initialQtyObj is int iqi)
            initialQuantity = iqi;
        else if (initialQtyObj is long iql)
            initialQuantity = (int)iql;
        else if (initialQtyObj is string iqs && int.TryParse(iqs, out var iqn))
            initialQuantity = iqn;
        else if (initialQtyObj is double iqd)
            initialQuantity = (int)iqd;
        else if (initialQtyObj is JsonElement jeInitQty && jeInitQty.ValueKind == JsonValueKind.Number)
            initialQuantity = jeInitQty.GetInt32();
        
        if (currentQuantity == 0 && !string.IsNullOrEmpty(barcode))
        {
            var parts = barcode.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var q))
                currentQuantity = q;
        }
        if (initialQuantity == 0)
            initialQuantity = currentQuantity;

        // ============================================================
        // ПАРСИНГ СТАТУСА С ПОДДЕРЖКОЙ JsonElement
        // ============================================================
        BoxStatus status = BoxStatus.Draft;
        var statusObj = json.GetValueOrDefault("status", 1);

        if (statusObj is BoxStatus bs)
        {
            status = bs;
        }
        else if (statusObj is int si)
        {
            status = (BoxStatus)si;
        }
        else if (statusObj is long sl)
        {
            status = (BoxStatus)sl;
        }
        else if (statusObj is string ss)
        {
            if (int.TryParse(ss, out var sn))
                status = (BoxStatus)sn;
            else if (Enum.TryParse<BoxStatus>(ss, true, out var se))
                status = se;
        }
        else if (statusObj is JsonElement jeStatus)
        {
            if (jeStatus.ValueKind == JsonValueKind.String)
            {
                var stringValue = jeStatus.GetString();
                if (int.TryParse(stringValue, out var sn))
                    status = (BoxStatus)sn;
                else if (Enum.TryParse<BoxStatus>(stringValue, true, out var se))
                    status = se;
            }
            else if (jeStatus.ValueKind == JsonValueKind.Number)
            {
                var numberValue = jeStatus.GetInt32();
                status = (BoxStatus)numberValue;
            }
        }

        // ============================================================
        // ПАРСИНГ ДАТ
        // ============================================================
        long createdAt = 0;
        var createdAtObj = json.GetValueOrDefault("createdAt", 0);
        if (createdAtObj is long cal)
            createdAt = cal;
        else if (createdAtObj is DateTime dt)
            createdAt = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        else if (createdAtObj is string dtStr && DateTime.TryParse(dtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dtParsed))
            createdAt = new DateTimeOffset(dtParsed).ToUnixTimeMilliseconds();
        else if (createdAtObj is JsonElement jeCreated && jeCreated.ValueKind == JsonValueKind.String)
        {
            var jeDtStr = jeCreated.GetString();
            if (DateTime.TryParse(jeDtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var jeDtParsed))
                createdAt = new DateTimeOffset(jeDtParsed).ToUnixTimeMilliseconds();
        }

        long updatedAt = 0;
        var updatedAtObj = json.GetValueOrDefault("updatedAt", 0);
        if (updatedAtObj is long ual)
            updatedAt = ual;
        else if (updatedAtObj is DateTime udt)
            updatedAt = new DateTimeOffset(udt).ToUnixTimeMilliseconds();
        else if (updatedAtObj is string udtStr && DateTime.TryParse(udtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var udtParsed))
            updatedAt = new DateTimeOffset(udtParsed).ToUnixTimeMilliseconds();
        else if (updatedAtObj is JsonElement jeUpdated && jeUpdated.ValueKind == JsonValueKind.String)
        {
            var jeUdtStr = jeUpdated.GetString();
            if (DateTime.TryParse(jeUdtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var jeUdtParsed))
                updatedAt = new DateTimeOffset(jeUdtParsed).ToUnixTimeMilliseconds();
        }

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
            ["status"] = (int)Status,
            ["createdAt"] = CreatedAt,
            ["updatedAt"] = UpdatedAt,
            ["orderId"] = OrderId ?? "",
            ["productName"] = ProductName,
            ["productEan13"] = ProductEan13,
            ["locationCode"] = LocationCode ?? ""
        };
    }

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
            IsPartial = false
        };
    }
}