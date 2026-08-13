namespace FlowerWms.Tsd.Models;

// Типы операций
public enum OperationType
{
    Receiving,
    Shipping,
    Moving,
    Inventory
}

// Статусы синхронизации
public enum SyncStatus
{
    Online,
    Offline,
    Syncing
}