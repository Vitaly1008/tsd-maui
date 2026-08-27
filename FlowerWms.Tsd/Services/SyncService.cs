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

    // Безопасное преобразование timestamp
    private long SafeGetTimestamp(DateTime dateTime)
    {
        try
        {
            if (dateTime <= DateTime.MinValue || dateTime > DateTime.MaxValue.AddDays(-1))
            {
                System.Diagnostics.Debug.WriteLine("⚠️ DateTime вне допустимого диапазона, используем текущее время");
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
        }
        catch (ArgumentOutOfRangeException)
        {
            System.Diagnostics.Debug.WriteLine("⚠️ ArgumentOutOfRangeException, используем текущее время");
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    // Синхронизирует коробки с сервера (по алгоритму п.1.4)
    public async Task SyncBoxes()
    {
        try
        {
            var localLastChanged = await _dbHelper.GetServerLastChanged();
            var serverLastChanged = await _apiService.GetServerLastChanged();
            
            // ✅ Безопасное преобразование
            long serverTimestamp = SafeGetTimestamp(serverLastChanged);
            
            System.Diagnostics.Debug.WriteLine($"LocalLastChanged: {localLastChanged}, ServerLastChanged: {serverTimestamp}");
            
            if (serverTimestamp > localLastChanged)
            {
                System.Diagnostics.Debug.WriteLine("Обновление локального кэша с сервера...");
                
                var boxes = await _apiService.GetAllBoxesForSync();
                
                if (boxes != null && boxes.Any())
                {
                    var db = await _dbHelper.GetDatabaseAsync();
                    await db.ExecuteAsync("DELETE FROM boxes_cache");
                    
                    var boxCacheList = new List<BoxCache>();
                    foreach (var box in boxes)
                    {
                        if (box.Status == BoxStatus.Draft || 
                            box.Status == BoxStatus.Active || 
                            box.Status == BoxStatus.Reserved)
                        {
                            boxCacheList.Add(new BoxCache
                            {
                                barcode = box.Barcode,
                                box_id = box.Id,
                                box_number = box.BoxNumber,
                                grade = box.Grade,
                                initial_quantity = box.InitialQuantity > 0 ? box.InitialQuantity : box.CurrentQuantity,
                                current_quantity = box.CurrentQuantity,
                                product_id = box.ProductId,
                                product_name = box.ProductName,
                                product_ean13 = box.ProductEan13,
                                location_code = box.LocationCode ?? "UNKNOWN",
                                status = box.Status,
                                created_at = box.CreatedAt,
                                updated_at = box.UpdatedAt
                            });
                        }
                    }
                    
                    if (boxCacheList.Any())
                    {
                        await _dbHelper.SyncBoxes(boxCacheList);
                        System.Diagnostics.Debug.WriteLine($"✅ Загружено {boxCacheList.Count} коробок с сервера");
                    }
                    
                    await _dbHelper.UpdateServerLastChanged(serverTimestamp);
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Локальный кэш актуален");
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
            await SyncAllData();
            
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

    // ✅ НОВЫЙ МЕТОД: синхронизация после логина (по алгоритму п.1)
    public async Task SyncAfterLogin()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("🔄 Синхронизация после логина...");
            
            // ✅ 1.1. Проверить очередь
            var pendingCount = await _offlineService.GetPendingCount();
            
            // ✅ 1.2. Если есть несинхронизированные транзакции → синхронизация
            if (pendingCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"📦 Найдено {pendingCount} несинхронизированных транзакций");
                await _syncQueueService.ProcessQueueAsync();
            }
            
            // ✅ 1.3. Получить LastChanged с сервера
            // ✅ 1.4. Если LastChanged > ServerLastChanged → обновить кэш
            await SyncBoxes();
            
            System.Diagnostics.Debug.WriteLine("✅ Синхронизация после логина завершена");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка синхронизации после логина: {ex.Message}");
            throw;
        }
    }
}