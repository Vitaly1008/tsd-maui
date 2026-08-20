using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Services;

// Основной сервис синхронизации
public class SyncService
{
    private readonly ApiService _apiService;
    private readonly DatabaseHelper _dbHelper;
    private readonly OfflineService _offlineService;
    private readonly SyncQueueService _syncQueueService;
    private SyncStatus _currentStatus = SyncStatus.Offline;

    public event EventHandler<SyncStatus>? StatusChanged;

    public SyncService()
    {
        _apiService = new ApiService();
        _dbHelper = new DatabaseHelper();
        _offlineService = new OfflineService();
        _syncQueueService = new SyncQueueService();
    }

    private void SetStatus(SyncStatus status)
    {
        if (_currentStatus != status)
        {
            _currentStatus = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    // Выполняет полную синхронизацию всех данных
    public async Task SyncAllData()
    {
        try
        {
            SetStatus(SyncStatus.Syncing);
            
            await SyncProducts();
            await SyncLocations();
            await SyncPartialBoxes();
            await SyncBoxes();
            
            SetStatus(SyncStatus.Online);
            System.Diagnostics.Debug.WriteLine("SyncAllData завершена");
        }
        catch (Exception ex)
        {
            SetStatus(SyncStatus.Offline);
            System.Diagnostics.Debug.WriteLine($"Ошибка SyncAllData: {ex.Message}");
            throw;
        }
    }

    // Синхронизирует справочник продуктов
    public async Task SyncProducts()
    {
        try
        {
            var products = await _apiService.GetAllProducts();
            if (products != null && products.Any())
            {
                var productCacheList = products.Select(p => new ProductCache
                {
                    product_id = p.Id,
                    ean13 = p.Ean13,
                    name = p.Name,
                    short_name = p.ShortName,
                    onec_guid = p.OneCGuid,
                    updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }).ToList();
                
                await _dbHelper.SyncProducts(productCacheList);
                System.Diagnostics.Debug.WriteLine($"Синхронизировано продуктов: {productCacheList.Count}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Нет продуктов для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации продуктов: {ex.Message}");
            throw;
        }
    }

    // Синхронизирует справочник локаций
    public async Task SyncLocations()
    {
        try
        {
            var locations = await _apiService.GetAllLocations();
            if (locations != null && locations.Any())
            {
                var locationCacheList = locations.Select(l => new LocationCache
                {
                    location_id = l.Id,
                    code = l.Code,
                    name = l.Name,
                    is_active = l.IsActive ? 1 : 0,
                    created_at = l.CreatedAt
                }).ToList();
                
                await _dbHelper.SyncLocations(locationCacheList);
                System.Diagnostics.Debug.WriteLine($"Синхронизировано локаций: {locationCacheList.Count}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Нет локаций для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации локаций: {ex.Message}");
            throw;
        }
    }

    // Синхронизирует активные коробки
    public async Task SyncBoxes()
    {
        try
        {
            var boxes = await _apiService.GetAllBoxes();
            if (boxes != null && boxes.Any())
            {
                var boxCacheList = new List<BoxCache>();
                
                foreach (var box in boxes)
                {
                    if (box.Status == BoxStatus.Active || box.Status == BoxStatus.Empty)
                    {
                        boxCacheList.Add(new BoxCache
                        {
                            barcode = box.Barcode,
                            box_id = box.Id,
                            box_number = box.BoxNumber,
                            grade = box.Grade,
                            initial_quantity = box.InitialQuantity,
                            current_quantity = box.CurrentQuantity,
                            product_id = box.ProductId,
                            product_name = box.ProductName,
                            product_ean13 = box.ProductEan13,
                            location_code = box.LocationCode ?? "UNKNOWN",
                            status = box.Status,
                            created_at = box.CreatedAt,
                            updated_at = box.UpdatedAt,
                            isPartial = 0
                        });
                    }
                }
                
                if (boxCacheList.Any())
                {
                    await _dbHelper.SyncBoxes(boxCacheList);
                    System.Diagnostics.Debug.WriteLine($"Синхронизировано коробок: {boxCacheList.Count}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Нет активных коробок для синхронизации");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Нет коробок для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации коробок: {ex.Message}");
            throw;
        }
    }

    // Проверяет доступность интернета
    public async Task<bool> CheckInternetManual()
    {
        try
        {
            var result = await _apiService.PingServer();
            SetStatus(result ? SyncStatus.Online : SyncStatus.Offline);
            return result;
        }
        catch
        {
            SetStatus(SyncStatus.Offline);
            return false;
        }
    }

    // Выполняет ручную синхронизацию
    public async Task SyncManual()
    {
        try
        {
            // Сначала синхронизируем справочники
            await SyncAllData();
            
            // Проверяем, есть ли транзакции для синхронизации
            var pendingCount = await _offlineService.GetPendingCount();
            if (pendingCount > 0)
            {
                await _syncQueueService.ProcessQueueAsync();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Нет транзакций для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка ручной синхронизации: {ex.Message}");
            throw;
        }
    }

    // Синхронизирует частично отгруженные коробки (по алгоритму)
    public async Task SyncPartialBoxes()
    {
        try
        {
            var partialBoxes = await _apiService.GetPartialBoxes();
            if (partialBoxes != null && partialBoxes.Any())
            {
                var boxCacheList = partialBoxes.Select(box => new BoxCache
                {
                    barcode = box.Barcode,
                    box_id = box.Id,
                    box_number = box.BoxNumber,
                    grade = box.Grade,
                    initial_quantity = box.InitialQuantity,
                    current_quantity = box.CurrentQuantity,
                    product_id = box.ProductId,
                    product_name = box.ProductName,
                    product_ean13 = box.ProductEan13,
                    location_code = box.LocationCode ?? "UNKNOWN",
                    status = box.Status,
                    created_at = box.CreatedAt,
                    updated_at = box.UpdatedAt,
                    isPartial = 1 // помечаем как частичную
                }).ToList();
                
                await _dbHelper.SyncBoxes(boxCacheList);
                System.Diagnostics.Debug.WriteLine($"Синхронизировано частичных коробок: {boxCacheList.Count}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Нет частичных коробок для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации частичных коробок: {ex.Message}");
            throw;
        }
    }
}