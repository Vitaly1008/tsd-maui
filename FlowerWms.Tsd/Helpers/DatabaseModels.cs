using SQLite;

namespace FlowerWms.Tsd.Helpers;

[Table("offline_transactions")]
public class OfflineTransaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Unique]
    public string transaction_id { get; set; } = string.Empty;

    public string operation_type { get; set; } = string.Empty;
    public string barcode { get; set; } = string.Empty;
    public string payload { get; set; } = string.Empty;
    public string device_id { get; set; } = string.Empty;
    public long created_at { get; set; }
    public long? synced_at { get; set; }
    public int is_synced { get; set; }
    public int retry_count { get; set; }
    public string? error_message { get; set; }
}

[Table("boxes_cache")]
public class BoxCache
{
    // ✅ PrimaryKey по barcode (он уникальный)
    [PrimaryKey]
    public string barcode { get; set; } = string.Empty;
    
    public string box_id { get; set; } = string.Empty;
    public int box_number { get; set; }
    public string grade { get; set; } = string.Empty;
    public int initial_quantity { get; set; }
    public int current_quantity { get; set; }
    public string product_id { get; set; } = string.Empty;
    public string product_name { get; set; } = string.Empty;
    public string product_ean13 { get; set; } = string.Empty;
    public string? location_id { get; set; }
    public string? location_code { get; set; }
    public string? order_id { get; set; }
    public int status { get; set; } = 0; // По умолчанию Draft
    public long created_at { get; set; }
    public long updated_at { get; set; }
}

[Table("locations_cache")]
public class LocationCache
{
    [PrimaryKey]
    public string location_id { get; set; } = string.Empty;
    
    [Unique]
    public string code { get; set; } = string.Empty;
    
    public string name { get; set; } = string.Empty;
    public string? barcode { get; set; }
    public int is_active { get; set; } = 1;
    public long created_at { get; set; }
}

[Table("products_cache")]
public class ProductCache
{
    [PrimaryKey]
    public string product_id { get; set; } = string.Empty;
    
    [Unique]
    public string ean13 { get; set; } = string.Empty;
    
    public string name { get; set; } = string.Empty;
    public string? short_name { get; set; }
    public string? onec_guid { get; set; }
    public string? barcode { get; set; }
    public long created_at { get; set; }
    public long updated_at { get; set; }
}