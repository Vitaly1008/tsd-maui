using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;

namespace FlowerWms.Tsd.Services;

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

    // ============================================================
    // ✅ 4.5. ЕДИНЫЙ МЕТОД ОБНОВЛЕНИЯ ЛОКАЛЬНОЙ БД
    // ============================================================
    public async Task RefreshLocalCacheFromServer()
    {
        try
        {
            // 4.5.1. Получить LastChanged с сервера
            var serverLastChanged = await _apiService.GetServerLastChanged();
            long serverTimestamp = SafeGetTimestamp(serverLastChanged);
            
            // 4.5.2. Получить ServerLastChanged из локальной БД
            var localLastChanged = await _dbHelper.GetServerLastChanged();
            
            Logger.Info($"Обновление локальной БД: ServerLastChanged: {serverTimestamp}, LocalLastChanged: {localLastChanged}");
            
            // 4.5.3. Если LastChanged > ServerLastChanged
            if (serverTimestamp > localLastChanged)
            {
                Logger.Info($"Обновление локального кэша с сервера...");
                
                // 4.5.3.1. Загрузить все коробки с сервера
                var boxes = await _apiService.GetAllBoxesForSync();
                Logger.Info($"1. количество коробок на сервере {boxes.Count}");
                
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
                            Logger.Info($"2. Добавлена коробка в локальную базу: barcode = {box.Barcode}, status={box.Status}");
                        }
                    }
                    
                    if (boxCacheList.Any())
                    {
                        await _dbHelper.SyncBoxes(boxCacheList);
                        Logger.Info($"✅ Загружено {boxCacheList.Count} коробок");
                    }
                    
                    await _dbHelper.UpdateServerLastChanged(serverTimestamp);
                    Logger.Info($"✅ ServerLastChanged обновлен: {serverTimestamp}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Локальный кэш актуален");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления кэша: {ex.Message}");
            throw;
        }
    }

    // ============================================================
    // ✅ 4. СИНХРОНИЗАЦИЯ (ПОЛНЫЙ ПУНКТ)
    // ============================================================
    public async Task SyncAllData()
    {
        try
        {
            SetStatus(SyncStatus.Syncing);
            
            // ✅ Сначала синхронизируем справочники
            await SyncProducts();
            await SyncLocations();
            
            // ✅ 4.1-4.4 Обрабатываем очередь
            await _syncQueueService.ProcessQueueAsync();
            
            // ✅ 4.5 Обновляем локальную БД
            await RefreshLocalCacheFromServer();
            
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

    // ============================================================
    // ✅ 1. СИНХРОНИЗАЦИЯ ПОСЛЕ ЛОГИНА (ВЫПОЛНИТЬ ВЕСЬ п.4)
    // ============================================================
    public async Task SyncAfterLogin()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("🔄 Синхронизация после логина...");
            
            // ✅ Выполняем ВЕСЬ пункт 4
            await SyncAllData();
            
            System.Diagnostics.Debug.WriteLine("✅ Синхронизация после логина завершена");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка синхронизации после логина: {ex.Message}");
            throw;
        }
    }

    // ============================================================
    // ✅ РУЧНАЯ СИНХРОНИЗАЦИЯ
    // ============================================================
    public async Task SyncManual()
    {
        try
        {
            SetStatus(SyncStatus.Syncing);
            
            // ✅ Сначала синхронизируем справочники
            await SyncProducts();
            await SyncLocations();
            
            // ✅ Обрабатываем очередь
            await _syncQueueService.ProcessQueueAsync();
            
            // ✅ Обновляем локальную БД
            await RefreshLocalCacheFromServer();
            
            SetStatus(SyncStatus.Online);
            System.Diagnostics.Debug.WriteLine("SyncManual завершена");
        }
        catch (Exception ex)
        {
            SetStatus(SyncStatus.Offline);
            System.Diagnostics.Debug.WriteLine($"Ошибка SyncManual: {ex.Message}");
            throw;
        }
    }

    // ============================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ============================================================

    private long SafeGetTimestamp(DateTime dateTime)
    {
        try
        {
            if (dateTime <= DateTime.MinValue || dateTime > DateTime.MaxValue.AddDays(-1))
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
                System.Diagnostics.Debug.WriteLine($"Синхронизировано продуктов: {productCacheList.Count}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации продуктов: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"Синхронизировано локаций: {locationCacheList.Count}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка синхронизации локаций: {ex.Message}");
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
}