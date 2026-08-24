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

    public ShippingViewModel(IBarcodeService? barcodeService = null)
        : base("Shipping", barcodeService)
    {
    }

    protected override async Task ProcessBoxScan(string barcode)
    {
        try
        {
            if (ScannedBoxes.Any(b => b.Barcode == barcode))
            {
                SetError("Коробка уже отсканирована в этой сессии");
                return;
            }

            var (ean13, _, grade, boxNumber) = ParseBarcode(barcode);

            Box? box = null;
            bool isPartial = false;

            // ✅ 1. СНАЧАЛА ИЩЕМ В ЛОКАЛЬНОМ КЭШЕ
            var cachedBox = await _dbHelper.GetBoxByBarcode(barcode);
            if (cachedBox != null)
            {
                box = BoxCacheToBox(cachedBox);
                isPartial = cachedBox.isPartial == 1;
                System.Diagnostics.Debug.WriteLine($"📦 Коробка найдена в кэше: #{box.BoxNumber}, остаток: {box.CurrentQuantity}, isPartial: {isPartial}");
            }

            // ✅ 2. ЕСЛИ НЕТ В КЭШЕ И ЕСТЬ ИНТЕРНЕТ — ИЩЕМ НА СЕРВЕРЕ
            if (box == null && IsOnline)
            {
                try
                {
                    var serverBox = await _apiService.GetBoxByBarcode(barcode);
                    if (serverBox != null)
                    {
                        box = serverBox;
                        // Проверяем, является ли коробка частичной на сервере
                        // (это поле должно приходить от бекенда)
                        isPartial = serverBox.IsPartial;
                        System.Diagnostics.Debug.WriteLine($"📦 Коробка найдена на сервере: #{box.BoxNumber}, isPartial: {isPartial}");
                        await UpdateLocalBox(box);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Ошибка получения коробки с сервера: {ex.Message}");
                }
            }

            if (box == null)
            {
                SetError($"Коробка №{boxNumber} не найдена на складе!");
                return;
            }

            // ✅ 3. ЕСЛИ КОРОБКА isPartial — КОЛИЧЕСТВО БЕРЕМ ИЗ БД
            if (isPartial && cachedBox != null)
            {
                box.CurrentQuantity = cachedBox.current_quantity;
                System.Diagnostics.Debug.WriteLine($"📦 Частичная коробка: количество из БД = {box.CurrentQuantity}");
            }
            // ✅ 4. ЕСЛИ КОРОБКА НЕ isPartial — КОЛИЧЕСТВО БЕРЕМ ИЗ ШК
            else
            {
                // Количество уже установлено из ШК при парсинге
                box.CurrentQuantity = box.Quantity > 0 ? box.Quantity : 100;
                System.Diagnostics.Debug.WriteLine($"📦 Целая коробка: количество из ШК = {box.CurrentQuantity}");
            }

            // Проверки состояния коробки
            if (box.Status == BoxStatus.Shipped)
            {
                SetError($"Коробка №{box.BoxNumber} уже отгружена!");
                return;
            }

            if (box.Status == BoxStatus.Empty || box.CurrentQuantity <= 0)
            {
                SetError($"Коробка №{box.BoxNumber} пуста (остаток: 0)!", "📭", Colors.Orange);
                return;
            }

            if (string.IsNullOrEmpty(box.ProductName))
            {
                box.ProductName = await GetProductName(box.ProductEan13);
            }

            _currentSelectedBox = box;
            MaxQuantity = box.CurrentQuantity;

            AddBoxToList(box);
            UpdateModes();
        }
        catch (Exception ex)
        {
            SetError($"Ошибка: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static Box BoxCacheToBox(BoxCache cached)
    {
        return new Box
        {
            Id = cached.box_id,
            Barcode = cached.barcode,
            BoxNumber = cached.box_number,
            ProductName = cached.product_name,
            ProductEan13 = cached.product_ean13,
            CurrentQuantity = cached.current_quantity,
            InitialQuantity = cached.initial_quantity,
            Grade = cached.grade,
            LocationCode = cached.location_code,
            Status = cached.status,
            CreatedAt = cached.created_at,
            UpdatedAt = cached.updated_at,
            IsPartial = cached.isPartial == 1
        };
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
                // Исправлено: используем _currentSelectedBox
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
    public void SetPartialShipment()
    {
        if (!CanPartialShip)
        {
            Application.Current?.MainPage?.DisplayAlert(
                "Информация",
                "Частичная отгрузка доступна только для одной коробки",
                "OK"
            );
            return;
        }

        IsFullShipmentMode = false;
        IsPartialShipmentMode = true;
        // Исправлено: берем из _currentSelectedBox
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
            await Application.Current?.MainPage?.DisplayAlert(
                "Внимание",
                "Нет коробок для отгрузки",
                "OK"
            );
            return;
        }

        if (IsPartialShipmentMode && ShipQuantity <= 0)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                "Укажите количество для отгрузки",
                "OK"
            );
            return;
        }

        if (IsPartialShipmentMode && ShipQuantity > MaxQuantity)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                $"Нельзя отгрузить больше {MaxQuantity} шт.",
                "OK"
            );
            return;
        }

        IsLoading = true;
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
                    isFullShipment = quantityToShip >= box.CurrentQuantity;
                }
                else
                {
                    quantityToShip = box.CurrentQuantity;
                    isFullShipment = true;
                }

                if (quantityToShip <= 0) continue;
                if (quantityToShip > box.CurrentQuantity) quantityToShip = box.CurrentQuantity;

                int newQuantity = box.CurrentQuantity - quantityToShip;
                totalShippedQuantity += quantityToShip;

                // ✅ ВАЖНО: СНАЧАЛА ПРОВЕРЯЕМ СТАТУС КОРОБКИ НА СЕРВЕРЕ
                /*if (IsOnline && !box.Id.StartsWith("local_"))
                {
                    try
                    {
                        var serverBox = await _apiService.GetBoxByBarcode(box.Barcode);
                        if (serverBox == null)
                        {
                            errors.Add($"Коробка #{box.BoxNumber} не найдена на сервере!");
                            continue;
                        }
                        
                        if (serverBox.Status != BoxStatus.Active)
                        {
                            errors.Add($"Коробка #{box.BoxNumber} имеет статус {serverBox.Status}, отгрузка невозможна!");
                            continue;
                        }
                        
                        // ✅ Обновляем локальный кэш актуальными данными
                        box.CurrentQuantity = serverBox.CurrentQuantity;
                        newQuantity = box.CurrentQuantity - quantityToShip;
                        if (newQuantity < 0) newQuantity = 0;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Коробка #{box.BoxNumber}: ошибка проверки статуса - {ex.Message}");
                        continue;
                    }
                }*/

                // ✅ СОЗДАЕМ ТРАНЗАКЦИЮ ВСЕГДА (и для онлайн, и для офлайн)
                bool isLocalBox = box.Id.StartsWith("local_");
                bool needOnlineSync = IsOnline && !isLocalBox;

                // ✅ 1. СОХРАНЯЕМ В ОЧЕРЕДЬ (гарантированно)
                await SaveLocalShipment(box, quantityToShip, isFullShipment);
                localCount++;

                // ✅ 2. ЕСЛИ ЕСТЬ ИНТЕРНЕТ - ПЫТАЕМСЯ ОТПРАВИТЬ СРАЗУ
                if (needOnlineSync)
                {
                    try
                    {
                        Dictionary<string, object> result;
                        
                        if (isFullShipment)
                        {
                            result = await _apiService.ShipBox(box.Id, "Полная отгрузка через ТСД");
                        }
                        else
                        {
                            result = await _apiService.ConsumeBox(box.Id, quantityToShip, $"Частичная отгрузка, остаток: {box.CurrentQuantity - quantityToShip} шт.");
                        }

                        if (result.TryGetValue("success", out var successObj) && successObj is bool success && success)
                        {
                            // ✅ УСПЕШНО - удаляем из очереди
                            // (нужно будет добавить метод удаления транзакции)
                            shippedCount++;
                            localCount--; // уменьшаем счетчик локальных
                        }
                        else
                        {
                            var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                            errors.Add($"Коробка #{box.BoxNumber}: {errorMsg}");
                            // ✅ ТРАНЗАКЦИЯ ОСТАЕТСЯ В ОЧЕРЕДИ ДЛЯ ПОВТОРНОЙ ПОПЫТКИ
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Коробка #{box.BoxNumber}: {ex.Message}");
                        // ✅ ТРАНЗАКЦИЯ ОСТАЕТСЯ В ОЧЕРЕДИ ДЛЯ ПОВТОРНОЙ ПОПЫТКИ
                    }
                }

                // ✅ 3. ОБНОВЛЯЕМ ЛОКАЛЬНЫЙ СТАТУС
                BoxStatus newStatus;
                if (isFullShipment)
                    newStatus = BoxStatus.Shipped;
                else if (newQuantity == 0)
                    newStatus = BoxStatus.Empty;
                else
                    newStatus = BoxStatus.Active;

                await _dbHelper.ForceUpdateBoxStatus(box.Barcode, newStatus, newQuantity);
                
                if (isFullShipment)
                    shippedCount++;
                else
                    partialCount++;
            }

            // Формируем сообщение
            var message = $"Обработано {boxes.Count} коробок.\nВсего отгружено: {totalShippedQuantity} шт.\n\n";
            if (shippedCount > 0) message += $"✅ Полностью отгружено: {shippedCount}\n";
            if (partialCount > 0) message += $"✂️ Частично отгружено: {partialCount}\n";
            if (localCount > 0) message += $"📴 Сохранено локально: {localCount}\n";
            if (errors.Any()) message += $"\n⚠️ Ошибки:\n{string.Join("\n", errors)}";
            message += localCount == 0 && !errors.Any() ? "\n✅ Данные синхронизированы с сервером." : "\n📴 Данные сохранены локально.";

            await Application.Current?.MainPage?.DisplayAlert(
                errors.Any() ? "Внимание" : localCount == 0 ? "Успешно" : "Внимание",
                message,
                "OK"
            );

            ClearSession();
            OnOperationCompleted();
        }
        catch (Exception ex)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                $"Не удалось выполнить отгрузку: {ex.Message}",
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Новый метод: обновление кэша без лишних запросов
    private async Task RefreshBoxCache(string barcode)
    {
        try
        {
            var updatedBox = await _apiService.GetBoxByBarcode(barcode);
            if (updatedBox != null)
            {
                // ✅ Проверяем статус в локальной БД перед обновлением
                var localBox = await _dbHelper.GetBoxByBarcode(barcode);
                
                // Если локальная коробка имеет статус Shipped или Empty, 
                // а сервер вернул старый статус, то сохраняем локальный статус
                if (localBox != null && 
                    (localBox.status == BoxStatus.Shipped || localBox.status == BoxStatus.Empty))
                {
                    // Сохраняем локальный статус и количество
                    updatedBox.Status = localBox.status;
                    updatedBox.CurrentQuantity = localBox.current_quantity;
                    System.Diagnostics.Debug.WriteLine($"⚠️ Восстановлен локальный статус {localBox.status} для {barcode}");
                }
                
                await UpdateLocalBox(updatedBox);
            }
            else
            {
                await _dbHelper.DeleteBoxByBarcode(barcode);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка обновления кэша: {ex.Message}");
        }
    }

    private async Task SaveLocalShipment(Box box, int quantityToShip, bool isFullShipment)
    {
        int newQuantity = box.CurrentQuantity - quantityToShip;
        
        BoxStatus newStatus;
        if (isFullShipment)
        {
            newStatus = BoxStatus.Shipped;
            newQuantity = 0;
        }
        else if (newQuantity == 0)
        {
            newStatus = BoxStatus.Empty;
        }
        else
        {
            newStatus = BoxStatus.Active;
        }

        // ✅ Сохраняем в очередь для синхронизации
        var payload = new
        {
            boxId = box.Id,
            barcode = box.Barcode,
            boxNumber = box.BoxNumber,
            quantity = quantityToShip,
            newQuantity = newQuantity,
            status = (int)newStatus,
            isFullShipment = isFullShipment,
            operationType = "Shipping",
            currentQuantity = box.CurrentQuantity
        };

        await _syncQueueService.EnqueueAsync(
            operationType: "Shipping",
            barcode: box.Barcode,
            payload: payload,
            deviceId: Constants.DeviceId
        );

        // ✅ Обновляем ТОЛЬКО статус и количество в локальной БД
        // ✅ isPartial НЕ ТРОГАЕМ — он обновится только с сервера после синхронизации
        await _dbHelper.ForceUpdateBoxStatus(
            barcode: box.Barcode,
            newStatus: newStatus,
            newQuantity: newQuantity,
            isPartial: box.IsPartial // ⚠️ Сохраняем СТАРОЕ значение isPartial
        );
        
        System.Diagnostics.Debug.WriteLine($"📴 Сохранена локальная отгрузка: #{box.BoxNumber}, {quantityToShip} шт., статус: {newStatus}");
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