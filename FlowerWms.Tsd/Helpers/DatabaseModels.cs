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
    [PrimaryKey]
    public string box_id { get; set; } = string.Empty;
    
    [Unique]
    public string barcode { get; set; } = string.Empty;
    
    public int box_number { get; set; }
    public string product_name { get; set; } = string.Empty;
    public string product_ean13 { get; set; } = string.Empty;
    public int quantity { get; set; }
    public string grade { get; set; } = string.Empty;
    public string? location_code { get; set; }
    public string? status { get; set; }
    public long updated_at { get; set; }
}

[Table("locations_cache")]
public class LocationCache
{
    [PrimaryKey]
    public string location_id { get; set; } = string.Empty;
    
    [Unique]
    public string code { get; set; } = string.Empty;
    
    public string? name { get; set; }
    public string? barcode { get; set; }
    public long updated_at { get; set; }
}