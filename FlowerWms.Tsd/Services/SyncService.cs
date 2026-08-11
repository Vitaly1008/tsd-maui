using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Services;

public class SyncService
{
    private readonly ApiService _apiService;
    private readonly DatabaseHelper _dbHelper;
    private readonly SecureStorageService _secureStorage;
    private SyncStatus _currentStatus = SyncStatus.Offline;

    public event EventHandler<SyncStatus>? StatusChanged;

    public SyncService()
    {
        _apiService = new ApiService();
        _dbHelper = new DatabaseHelper();
        _secureStorage = new SecureStorageService();
    }

    private void SetStatus(SyncStatus status)
    {
        if (_currentStatus != status)
        {
            _currentStatus = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    /// <summary>
    /// Полная синхронизация - обновление кэша
    /// </summary>
    public async Task SyncAllData()
    {
        try
        {
            SetStatus(SyncStatus.Syncing);
            
            await SyncProducts();
            await SyncLocations();
            await SyncBoxes();
            
            SetStatus(SyncStatus.Online);
            System.Diagnostics.Debug.WriteLine("✅ SyncAllData завершена");
        }
        catch (Exception ex)
        {
            SetStatus(SyncStatus.Offline);
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка SyncAllData: {ex.Message}");
            throw;
        }
    }

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
                System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано продуктов: {productCacheList.Count}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Нет продуктов для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации продуктов: {ex.Message}");
            throw;
        }
    }

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
                System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано локаций: {locationCacheList.Count}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Нет локаций для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации локаций: {ex.Message}");
            throw;
        }
    }

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
                    // ✅ Синхронизируем только Active (1) и Empty (2)
                    if (box.Status == 1 || box.Status == 2)
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
                            is_dirty = 0
                        });
                    }
                }
                
                if (boxCacheList.Any())
                {
                    await _dbHelper.SyncBoxes(boxCacheList);
                    System.Diagnostics.Debug.WriteLine($"✅ Синхронизировано коробок: {boxCacheList.Count}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Нет активных коробок для синхронизации");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Нет коробок для синхронизации");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка синхронизации коробок: {ex.Message}");
            throw;
        }
    }

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

    /// <summary>
    /// Проверка авторизации (проверяет токен через API)
    /// </summary>
    public async Task<bool> ValidateToken()
    {
        try
        {
            var token = await _secureStorage.GetToken();
            if (string.IsNullOrEmpty(token))
                return false;
            
            // Проверяем токен через API (ping с авторизацией)
            return await _apiService.PingServer();
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> GetPendingCount()
    {
        try
        {
            var dbHelper = new DatabaseHelper();
            var db = await dbHelper.GetDatabaseAsync();
            
            var count = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM offline_transactions WHERE is_synced = 0"
            );
            
            return count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения счетчика: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Ручная синхронизация (для NetworkService)
    /// </summary>
    public async Task SyncManual()
    {
        try
        {
            await SyncAllData();
            
            // Обработка офлайн-транзакций
            var syncQueue = new SyncQueueService();
            await syncQueue.ProcessQueueAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка ручной синхронизации: {ex.Message}");
            throw;
        }
    }
}