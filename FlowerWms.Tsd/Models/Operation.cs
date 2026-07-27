namespace FlowerWms.Tsd.Models;

public enum OperationType
{
    Receiving,
    Shipping,
    Moving,
    Inventory
}

public enum SyncStatus
{
    Online,
    Offline,
    Syncing
}