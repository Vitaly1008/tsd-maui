using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;

namespace FlowerWms.Tsd.ViewModels;

/// <summary>
/// ViewModel для страницы отгрузки коробок
/// </summary>
public partial class ShippingViewModel : BaseOperationViewModel
{
    private Box? _currentSelectedBox;

    [ObservableProperty]
    private int _shipQuantity;

    [ObservableProperty]
    private int _maxQuantity;

    [ObservableProperty]
    private bool _isFullShipmentMode = true;

    [ObservableProperty]
    private bool _isPartialShipmentMode;

    [ObservableProperty]
    private bool _canPartialShip;

    [ObservableProperty]
    private string _shipQuantityDisplay = "0";

    [ObservableProperty]
    private string _shipModeDescription = "Полная отгрузка (вся коробка)";

    [ObservableProperty]
    private bool _isQuantityExceeded;

    [ObservableProperty]
    private bool _hasAvailabilityWarning;

    [ObservableProperty]
    private string _availabilityWarning = string.Empty;

    [ObservableProperty]
    private string _shipModeText = "Полная";

    [ObservableProperty]
    private Color _shipModeColor = Colors.Green;

    public ShippingViewModel(
        DatabaseHelper dbHelper,
        ApiService apiService,
        SyncQueueService syncQueueService,
        SyncService syncService,
        IBarcodeService barcodeService)
        : base("Shipping", dbHelper, apiService, syncQueueService, syncService, barcodeService)
    {
    }

    protected override async Task ProcessBoxScan(string barcode)
    {
        // ✅ ИСПРАВЛЕНО: защита от параллельного сканирования
        if (IsLoading) return;

        IsLoading = true;

        try
        {
            if (ScannedBoxes.Any(b => b.Barcode == barcode))
            {
                SetError("Коробка уже отсканирована в этой сессии");
                return;
            }

            var (ean13, _, grade, boxNumber) = ParseBarcode(barcode);

            // ✅ 3.3. Проверка наличия в локальной БД
            var cachedBox = await _dbHelper.GetBoxByBarcode(barcode);
            Box? box = null;

            if (cachedBox != null)
            {
                // ✅ 3.3.1. Есть в локальной БД
                box = Box.FromCache(cachedBox);
                Logger.Info($"📦 Коробка найдена в локальной БД: #{box.BoxNumber}, остаток: {box.CurrentQuantity}, статус: {box.Status}");

                // ✅ 3.3.2. Проверка статуса
                if (box.Status == BoxStatus.Draft)
                {
                    Logger.Warning($"Коробка #{boxNumber} имеет статус {box.Status}");
                    SetError($"Коробка #{boxNumber} не активирована (Draft)");
                    return;
                }
                if (box.Status == BoxStatus.Shipped)
                {
                    SetError($"Коробка #{boxNumber} уже отгружена");
                    return;
                }
                if (box.Status == BoxStatus.Empty)
                {
                    SetError($"Коробка #{boxNumber} пуста");
                    return;
                }
                // ✅ ДОБАВЛЕНО: явная проверка Discarded
                if (box.Status == BoxStatus.Discarded)
                {
                    SetError($"Коробка #{boxNumber} списана");
                    return;
                }

                // ✅ ДОБАВЛЕНО: предупреждение для Reserved
                if (box.Status == BoxStatus.Reserved)
                {
                    SetWarning($"Коробка #{boxNumber} зарезервирована", "⚠️", Colors.Orange);
                    // продолжаем — Reserved разрешён
                }
                // Active — разрешён без предупреждения
            }
            else
            {
                // ✅ 3.3.1. Нет в локальной БД → ошибка
                SetError($"Коробка #{boxNumber} не найдена в локальной БД");
                return;
            }

            // ✅ 3.4. Количество из локальной БД
            int availableQuantity = box.CurrentQuantity;
            if (availableQuantity <= 0)
            {
                SetError($"Коробка #{boxNumber} пуста (остаток: 0)");
                return;
            }

            // Проверяем, есть ли товар в очереди на синхронизацию
            var pendingCount = await _syncQueueService.GetPendingCount();
            if (pendingCount > 0)
            {
                SetWarning($"Есть несинхронизированные операции ({pendingCount}). Данные могут быть неактуальны.", "⚠️", Colors.Orange);
            }

            if (string.IsNullOrEmpty(box.ProductName))
            {
                box.ProductName = await GetProductName(box.ProductEan13);
            }

            _currentSelectedBox = box;
            MaxQuantity = availableQuantity;

            // ✅ Добавляем в сессию
            AddBoxToList(box);
            UpdateModes();
        }
        catch (Exception ex)
        {
            SetError($"Ошибка: {ex.Message}");
            Logger.Error($"Ошибка сканирования: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateModes()
    {
        if (ScannedBoxes.Count > 1)
        {
            CanPartialShip = false;
            IsFullShipmentMode = true;
            IsPartialShipmentMode = false;
            ShipQuantity = ScannedBoxes.Sum(b => b.CurrentQuantity);
            ShipQuantityDisplay = ShipQuantity.ToString();
            ShipModeDescription = "Полная отгрузка (все коробки)";
            ShipModeText = "Полная";
            ShipModeColor = Colors.Green;
        }
        else if (ScannedBoxes.Count == 1)
        {
            CanPartialShip = true;

            if (!IsPartialShipmentMode)
            {
                IsFullShipmentMode = true;
                ShipQuantity = _currentSelectedBox?.CurrentQuantity ?? 0;
                ShipQuantityDisplay = ShipQuantity.ToString();
                ShipModeDescription = "Полная отгрузка (вся коробка)";
                ShipModeText = "Полная";
                ShipModeColor = Colors.Green;
            }
        }
        else
        {
            CanPartialShip = false;
            IsFullShipmentMode = true;
            IsPartialShipmentMode = false;
            ShipQuantity = 0;
            ShipQuantityDisplay = "0";
            ShipModeDescription = "Полная отгрузка";
            ShipModeText = "Полная";
            ShipModeColor = Colors.Green;
        }
    }

    [RelayCommand]
    public void SetFullShipment()
    {
        if (!CanPartialShip && ScannedBoxes.Count > 1) return;

        IsFullShipmentMode = true;
        IsPartialShipmentMode = false;
        ShipQuantity = _currentSelectedBox?.CurrentQuantity ?? ScannedBoxes.Sum(b => b.CurrentQuantity);
        ShipQuantityDisplay = ShipQuantity.ToString();
        ShipModeDescription = ScannedBoxes.Count > 1
            ? "Полная отгрузка (все коробки)"
            : "Полная отгрузка (вся коробка)";
        ShipModeText = "Полная";
        ShipModeColor = Colors.Green;
        IsQuantityExceeded = false;
        HasAvailabilityWarning = false;
        AvailabilityWarning = string.Empty;
    }

    [RelayCommand]
    public async Task SetPartialShipment()
    {
        if (!CanPartialShip)
        {
            // ✅ ИСПРАВЛЕНО: устаревший API
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Информация",
                "Частичная отгрузка доступна только для одной коробки",
                "OK"
            );
            return;
        }

        IsFullShipmentMode = false;
        IsPartialShipmentMode = true;
        ShipQuantity = _currentSelectedBox?.CurrentQuantity ?? 0;
        ShipQuantityDisplay = ShipQuantity.ToString();
        ShipModeDescription = "Частичная отгрузка (укажите количество)";
        ShipModeText = "Частичная";
        ShipModeColor = Colors.Orange;
        ValidateQuantity();
    }

    [RelayCommand]
    public void IncreaseQuantity()
    {
        if (!IsPartialShipmentMode || _currentSelectedBox == null) return;
        if (ShipQuantity < _currentSelectedBox.CurrentQuantity)
        {
            ShipQuantity++;
            ShipQuantityDisplay = ShipQuantity.ToString();
            ValidateQuantity();
        }
    }

    [RelayCommand]
    public void DecreaseQuantity()
    {
        if (!IsPartialShipmentMode || _currentSelectedBox == null) return;
        if (ShipQuantity > 1)
        {
            ShipQuantity--;
            ShipQuantityDisplay = ShipQuantity.ToString();
            ValidateQuantity();
        }
    }

    private void ValidateQuantity()
    {
        if (ShipQuantity > MaxQuantity && MaxQuantity > 0)
        {
            IsQuantityExceeded = true;
            HasAvailabilityWarning = true;
            AvailabilityWarning = $"Указано {ShipQuantity} шт., доступно {MaxQuantity} шт.";
        }
        else
        {
            IsQuantityExceeded = false;
            HasAvailabilityWarning = false;
            AvailabilityWarning = string.Empty;
        }
    }

    public override async Task ConfirmOperation()
    {
        if (ScannedBoxes.Count == 0)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Внимание",
                "Нет коробок для отгрузки",
                "OK"
            );
            return;
        }

        if (IsPartialShipmentMode && ShipQuantity <= 0)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                "Укажите количество для отгрузки",
                "OK"
            );
            return;
        }

        if (IsPartialShipmentMode && ShipQuantity > MaxQuantity)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                $"Нельзя отгрузить больше {MaxQuantity} шт.",
                "OK"
            );
            return;
        }

        IsLoading = true;
        bool operationSucceeded = false;
        var boxes = ScannedBoxes.ToList();
        int shippedCount = 0;
        int partialCount = 0;
        int localCount = 0;
        int totalShippedQuantity = 0;
        var errors = new List<string>();

        try
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                int quantityToShip;
                bool isFullShipment;

                if (i == boxes.Count - 1 && IsPartialShipmentMode && CanPartialShip)
                {
                    quantityToShip = ShipQuantity;
                    isFullShipment = false;
                }
                else
                {
                    quantityToShip = box.CurrentQuantity;
                    isFullShipment = true;
                }

                if (quantityToShip <= 0) continue;
                if (quantityToShip > box.CurrentQuantity) quantityToShip = box.CurrentQuantity;

                // ✅ ИСПРАВЛЕНО: try/catch на итерацию — одна ошибка не прерывает остальные
                try
                {
                    await SaveLocalShipment(box, quantityToShip, isFullShipment);
                    localCount++;
                    totalShippedQuantity += quantityToShip;

                    if (isFullShipment)
                        shippedCount++;
                    else
                        partialCount++;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка отгрузки коробки #{box.BoxNumber}: {ex.Message}");
                    errors.Add($"#{box.BoxNumber}: {ex.Message}");
                }
            }

            // ✅ Получаем количество операций в очереди
            var pendingCount = await _syncQueueService.GetPendingCount();

            // ✅ Формируем сообщение
            var message = $"Обработано {boxes.Count} коробок.\n" +
                        $"Всего отгружено: {totalShippedQuantity} шт.\n\n";
            if (shippedCount > 0) message += $"✅ Полностью отгружено: {shippedCount}\n";
            if (partialCount > 0) message += $"✂️ Частично отгружено: {partialCount}\n";
            if (localCount > 0) message += $"📴 Сохранено локально: {localCount}\n";
            if (errors.Any()) message += $"\n⚠️ Ошибки:\n{string.Join("\n", errors)}";

            if (pendingCount > 0)
            {
                message += $"\n\n📤 Ожидает синхронизации: {pendingCount} операций.\n" +
                        $"Синхронизируйте вручную в разделе 'Очередь'.";
            }
            else
            {
                message += "\n\n✅ Все операции синхронизированы!";
            }

            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                errors.Any() ? "Внимание" : "Успешно",
                message,
                "OK"
            );

            operationSucceeded = true;
        }
        catch (Exception ex)
        {
            await Application.Current?.Windows[0]?.Page?.DisplayAlertAsync(
                "Ошибка",
                $"Не удалось выполнить отгрузку: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }

        // ✅ ИСПРАВЛЕНО: вынесено после try/catch
        if (operationSucceeded)
        {
            ClearSession();
            OnOperationCompleted();
        }
    }

    private async Task SaveLocalShipment(Box box, int quantityToShip, bool isFullShipment)
    {
        if (quantityToShip <= 0)
        {
            Logger.Warning($"⚠️ Попытка отгрузки с quantity=0 для #{box.BoxNumber}");
            return;
        }

        // ✅ Если quantityToShip больше, чем есть — корректируем
        if (quantityToShip > box.CurrentQuantity)
        {
            Logger.Warning($"⚠️ quantityToShip ({quantityToShip}) > CurrentQuantity ({box.CurrentQuantity}). Корректируем.");
            quantityToShip = box.CurrentQuantity;
            isFullShipment = true;
        }

        int newQuantity = box.CurrentQuantity - quantityToShip;

        BoxStatus newStatus;

        // ✅ ЛОГИКА (по решению B):
        // - Полная отгрузка → Shipped (кол-во = 0)
        // - Частичная с new_quantity == 0 → Empty
        // - Частичная с new_quantity > 0 → Active
        if (isFullShipment)
        {
            newStatus = BoxStatus.Shipped;
            newQuantity = 0;
        }
        else
        {
            if (newQuantity == 0)
                newStatus = BoxStatus.Empty;
            else
                newStatus = BoxStatus.Active;
        }

        // ✅ Обновляем локальную БД
        await _dbHelper.ForceUpdateBoxStatus(
            barcode: box.Barcode,
            newStatus: newStatus,
            newQuantity: newQuantity
        );

        // ✅ Создаём транзакцию для синхронизации
        var payload = new
        {
            boxId = box.Id,
            barcode = box.Barcode,
            quantity = quantityToShip,
            isFullShipment = isFullShipment,
            currentQuantity = box.CurrentQuantity,
            newQuantity = newQuantity,
            previousStatus = (int)box.Status,
            previousQuantity = box.CurrentQuantity
        };

        await _syncQueueService.EnqueueAsync(
            operationType: "Shipping",
            barcode: box.Barcode,
            payload: payload,
            deviceId: Constants.DeviceId
        );

        Logger.Info($"📴 Сохранена локальная отгрузка: #{box.BoxNumber}, {quantityToShip} шт., isFullShipment={isFullShipment}, новый статус={newStatus}, новое количество={newQuantity}");
    }

    public override void RemoveBox(object parameter)
    {
        if (parameter is Box box && ScannedBoxes.Contains(box))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Remove(box);
                ScannedCount = ScannedBoxes.Count;

                if (ScannedBoxes.Count == 0)
                {
                    ClearSession();
                    _currentSelectedBox = null;
                    CanPartialShip = false;
                    ShipQuantity = 0;
                    ShipQuantityDisplay = "0";
                }
                else if (ScannedBoxes.Count == 1)
                {
                    _currentSelectedBox = ScannedBoxes.First();
                    MaxQuantity = _currentSelectedBox.CurrentQuantity;
                    UpdateModes();
                }
                else
                {
                    CanPartialShip = false;
                    IsFullShipmentMode = true;
                    IsPartialShipmentMode = false;
                    ShipQuantity = ScannedBoxes.Sum(b => b.CurrentQuantity);
                    ShipQuantityDisplay = ShipQuantity.ToString();
                    ShipModeDescription = "Полная отгрузка (все коробки)";
                    ShipModeText = "Полная";
                    ShipModeColor = Colors.Green;
                }
            });
        }
    }
}