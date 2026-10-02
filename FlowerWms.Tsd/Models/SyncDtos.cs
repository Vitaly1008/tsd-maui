namespace FlowerWms.Tsd.Models;

public class SyncBoxesRequest
{
    public List<TransactionSyncDto> Transactions { get; set; } = new();
}

public class TransactionSyncDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string? LocationCode { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class SyncBoxesResponse
{
    public int TotalProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public List<SyncResultDto> ProcessedTransactions { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> ProblemBoxesCreated { get; set; } = new();
}

public class SyncResultDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public Guid? BoxId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public bool ProblemBoxCreated { get; set; }
}