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

    public SyncService(
        ApiService apiService,
        DatabaseHelper dbHelper,
        OfflineService offlineService,
        SyncQueueService syncQueueService)
    {
        _apiService = apiService;
        _dbHelper = dbHelper;
        _offlineService = offlineService;
        _syncQueueService = syncQueueService;
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
    // ✅ 4.5. ЕДИНЫЙ МЕТОД ОБНОВЛЕНИЯ ЛОКАЛЬНОЙ БД (БАГ #4, #5, #19)
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

                // ✅ БАГ #5: сначала получаем коробки, ПОТОМ читаем pending-транзакции.
                //   Это сужает окно гонки (пользователь может создать транзакцию
                //   в момент между чтением и удалением).
                var boxes = await _apiService.GetAllBoxesForSync();
                Logger.Info($"1. количество коробок на сервере {boxes.Count}");

                // ✅ Читаем pending-транзакции ПОСЛЕ получения коробок
                var pendingTransactions = await _offlineService.GetUnsyncedTransactions();
                var localChangedBarcodes = new HashSet<string>();

                foreach (var tx in pendingTransactions)
                {
                    if (tx.operation_type == "Shipping" || tx.operation_type == "Receiving")
                    {
                        localChangedBarcodes.Add(tx.barcode);
                    }
                    // TODO: когда будет реализовано перемещение — добавить учёт "Move"
                }

                Logger.Info($"📋 Коробок с локальными изменениями: {localChangedBarcodes.Count}");

                // ✅ БАГ #4: очищаем и обновляем ВСЕГДА, даже если boxes пуст
                var preservedCount = await _dbHelper.DeleteAllBoxesExcept(localChangedBarcodes);
                Logger.Info($"🗑️ Удалено коробок: сохранено {preservedCount} (с локальными изменениями)");

                var insertedCount = 0;
                var skippedCount = 0;

                foreach (var box in boxes)
                {
                    if (localChangedBarcodes.Contains(box.Barcode))
                    {
                        Logger.Info($"⏭️ Пропускаем {box.Barcode} (есть локальные изменения)");
                        skippedCount++;
                        continue;
                    }

                    if (box.Status == BoxStatus.Draft ||
                        box.Status == BoxStatus.Active ||
                        box.Status == BoxStatus.Reserved)
                    {
                        var boxCache = new BoxCache
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
                        };

                        await _dbHelper.InsertBox(boxCache);
                        insertedCount++;
                    }
                }

                Logger.Info($"✅ Записано {insertedCount} коробок, пропущено {skippedCount} (с локальными изменениями)");

                // ✅ БАГ #4: обновляем timestamp ВСЕГДА, даже если коробок 0
                await _dbHelper.UpdateServerLastChanged(serverTimestamp);
                Logger.Info($"✅ ServerLastChanged обновлен: {serverTimestamp}");
            }
            else
            {
                Logger.Info("Локальный кэш актуален");
            }

            // ✅ 4.4.4. Зачистка архивных коробок (ВСЕГДА, независимо от LastChanged).
            // Локальная БД должна хранить ТОЛЬКО Draft/Active/Reserved.
            // Это гарантирует, что накопленный мусор от старых версий будет убран
            // даже если сервер не менялся.
            await _dbHelper.DeleteArchivedBoxes();
        }
        catch (Exception ex)
        {
            Logger.Error($"❌ Ошибка обновления кэша: {ex.Message}");
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

            // ✅ 4.5 Обновляем локальную БД (БАГ #16: не роняем всю синхронизацию)
            try
            {
                await RefreshLocalCacheFromServer();
            }
            catch (Exception ex)
            {
                Logger.Error($"⚠️ Ошибка обновления локального кэша (не критично): {ex.Message}");
            }

            SetStatus(SyncStatus.Online);
            Logger.Info("SyncAllData завершена");
        }
        catch (Exception ex)
        {
            SetStatus(SyncStatus.Offline);
            Logger.Error($"Ошибка SyncAllData: {ex.Message}");
            throw;
        }
    }

    // ============================================================
    // ✅ 1. СИНХРОНИЗАЦИЯ ПОСЛЕ ЛОГИНА
    // ============================================================
    public async Task SyncAfterLogin()
    {
        try
        {
            Logger.Info("🔄 Синхронизация после логина...");
            await SyncAllData();
            Logger.Info("✅ Синхронизация после логина завершена");
        }
        catch (Exception ex)
        {
            Logger.Error($"⚠️ Ошибка синхронизации после логина: {ex.Message}");
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

            await SyncProducts();
            await SyncLocations();

            await _syncQueueService.ProcessQueueAsync();

            try
            {
                await RefreshLocalCacheFromServer();
            }
            catch (Exception ex)
            {
                Logger.Error($"⚠️ Ошибка обновления локального кэша (не критично): {ex.Message}");
            }

            SetStatus(SyncStatus.Online);
            Logger.Info("SyncManual завершена");
        }
        catch (Exception ex)
        {
            SetStatus(SyncStatus.Offline);
            Logger.Error($"Ошибка SyncManual: {ex.Message}");
            throw;
        }
    }

    // ============================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ============================================================

    // ✅ БАГ #19: убран DateTime.MaxValue.AddDays(-1), используем константу
    private long SafeGetTimestamp(DateTime dateTime)
    {
        try
        {
            // Максимальный валидный timestamp для Unix (9999-12-31)
            const long MaxUnixMs = 253402300799999;

            if (dateTime <= DateTime.MinValue || dateTime.Year >= 9999)
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            var ts = new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
            if (ts < 0 || ts > MaxUnixMs)
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return ts;
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
                Logger.Info($"Синхронизировано продуктов: {productCacheList.Count}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка синхронизации продуктов: {ex.Message}");
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
                Logger.Info($"Синхронизировано локаций: {locationCacheList.Count}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка синхронизации локаций: {ex.Message}");
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