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

            // Поиск в кэше
            var cachedBox = await _dbHelper.GetBoxByBarcode(barcode);
            if (cachedBox != null)
            {
                box = BoxCacheToBox(cachedBox);
                System.Diagnostics.Debug.WriteLine($"Коробка найдена в кэше: #{box.BoxNumber}, остаток: {box.CurrentQuantity}");
            }

            // Поиск на сервере
            if (box == null && IsOnline)
            {
                try
                {
                    var serverBox = await _apiService.GetBoxByBarcode(barcode);
                    if (serverBox != null)
                    {
                        box = serverBox;
                        System.Diagnostics.Debug.WriteLine($"Коробка найдена на сервере: #{box.BoxNumber}");
                        await UpdateLocalBox(box);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка получения коробки с сервера: {ex.Message}");
                }
            }

            if (box == null)
            {
                SetError($"Коробка №{boxNumber} не найдена на складе!");
                return;
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
            IsDirty = cached.is_dirty == 1
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
                // ✅ Исправлено: используем _currentSelectedBox
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
        // ✅ Исправлено: берем из _currentSelectedBox
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

        // ✅ Добавлена проверка для частичной отгрузки
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

                // ✅ Улучшено: проверяем что коробка не локальная и есть интернет
                bool isLocalBox = box.Id.StartsWith("local_");
                bool canUseOnline = IsOnline && !isLocalBox;

                if (canUseOnline)
                {
                    try
                    {
                        Dictionary<string, object> result;
                        
                        if (isFullShipment)
                        {
                            result = await _apiService.ShipBox(
                                boxId: box.Id,
                                comment: "Полная отгрузка через ТСД"
                            );
                        }
                        else
                        {
                            result = await _apiService.ConsumeBox(
                                boxId: box.Id,
                                quantity: quantityToShip,
                                comment: $"Частичная отгрузка, остаток: {newQuantity} шт."
                            );
                        }

                        // ✅ Проверяем результат
                        if (result.TryGetValue("success", out var successObj) && 
                            successObj is bool success && success)
                        {
                            if (isFullShipment)
                                shippedCount++;
                            else
                                partialCount++;

                            // ✅ Улучшено: обновляем кэш без лишнего запроса
                            // Пробуем получить обновленную коробку из результата
                            if (result.TryGetValue("data", out var dataObj) && 
                                dataObj is Dictionary<string, object> data)
                            {
                                var updatedBox = Box.FromJson(data);
                                if (updatedBox != null && !string.IsNullOrEmpty(updatedBox.Id))
                                {
                                    await UpdateLocalBox(updatedBox);
                                    System.Diagnostics.Debug.WriteLine($"✅ Кэш обновлен из ответа: #{updatedBox.BoxNumber}");
                                }
                                else
                                {
                                    // Если не удалось распарсить, делаем запрос
                                    await RefreshBoxCache(box.Barcode);
                                }
                            }
                            else
                            {
                                // Если нет данных в ответе, делаем запрос
                                await RefreshBoxCache(box.Barcode);
                            }
                        }
                        else
                        {
                            var errorMsg = result.GetValueOrDefault("message")?.ToString() ?? "Неизвестная ошибка";
                            
                            // ✅ Если ошибка из-за сети, сохраняем локально
                            if (result.TryGetValue("offline", out var offlineObj) && 
                                offlineObj is bool offline && offline)
                            {
                                await SaveLocalShipment(box, quantityToShip, isFullShipment);
                                localCount++;
                            }
                            else
                            {
                                errors.Add($"Коробка #{box.BoxNumber}: {errorMsg}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // ✅ При любой ошибке сохраняем локально
                        System.Diagnostics.Debug.WriteLine($"Ошибка при отгрузке {box.BoxNumber}: {ex.Message}");
                        await SaveLocalShipment(box, quantityToShip, isFullShipment);
                        localCount++;
                    }
                }
                else
                {
                    // Офлайн или локальная коробка
                    await SaveLocalShipment(box, quantityToShip, isFullShipment);
                    localCount++;
                }
            }

            // ✅ Формируем сообщение с учетом ошибок
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

    // ✅ Новый метод: обновление кэша без лишних запросов
    private async Task RefreshBoxCache(string barcode)
    {
        try
        {
            var updatedBox = await _apiService.GetBoxByBarcode(barcode);
            if (updatedBox != null)
            {
                await UpdateLocalBox(updatedBox);
                System.Diagnostics.Debug.WriteLine($"✅ Кэш обновлен запросом: #{updatedBox.BoxNumber}");
            }
            else
            {
                await _dbHelper.DeleteBoxByBarcode(barcode);
                System.Diagnostics.Debug.WriteLine($"🗑️ Коробка удалена из кэша: {barcode}");
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
        
        // ✅ Используем BoxStatus enum
        BoxStatus newStatus;
        if (isFullShipment)
            newStatus = BoxStatus.Shipped;
        else if (newQuantity == 0)
            newStatus = BoxStatus.Empty;
        else
            newStatus = BoxStatus.Active;

        var boxCache = new BoxCache
        {
            barcode = box.Barcode,
            box_id = box.Id,
            box_number = box.BoxNumber,
            grade = box.Grade,
            initial_quantity = box.InitialQuantity,
            current_quantity = newQuantity,
            product_id = box.ProductId,
            product_name = box.ProductName,
            product_ean13 = box.ProductEan13,
            location_code = box.LocationCode ?? "UNKNOWN",
            status = newStatus, // ✅ Теперь используем enum
            created_at = box.CreatedAt,
            updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            is_dirty = 1
        };
        await _dbHelper.SaveBox(boxCache);

        var payload = new
        {
            boxId = box.Id,
            barcode = box.Barcode,
            boxNumber = box.BoxNumber,
            quantity = quantityToShip,
            newQuantity = newQuantity,
            status = (int)newStatus, // ✅ Сохраняем как int для совместимости
            isFullShipment = isFullShipment,
            operationType = "Shipping"
        };

        await _syncQueueService.EnqueueAsync(
            operationType: "Shipping",
            barcode: box.Barcode,
            payload: payload,
            deviceId: Constants.DeviceId
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