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

            // ✅ 3. ОПРЕДЕЛЯЕМ КОЛИЧЕСТВО ПО АЛГОРИТМУ
            int availableQuantity;
            
            if (isPartial && cachedBox != null)
            {
                // ✅ isPartial = true → количество из локальной БД
                availableQuantity = cachedBox.current_quantity;
                System.Diagnostics.Debug.WriteLine($"📦 Частичная коробка: количество из БД = {availableQuantity}");
            }
            else
            {
                // ✅ isPartial = false → количество из ШК (серверное значение)
                availableQuantity = box.CurrentQuantity > 0 ? box.CurrentQuantity : 100;
                System.Diagnostics.Debug.WriteLine($"📦 Целая коробка: количество из ШК = {availableQuantity}");
            }

            box.CurrentQuantity = availableQuantity;

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

                // ✅ СОЗДАЕМ ТРАНЗАКЦИЮ ВСЕГДА (и для онлайн, и для офлайн)
                // ⚠️ ВСЕ ПРОВЕРКИ БУДУТ ВЫПОЛНЕНЫ В МОМЕНТ СИНХРОНИЗАЦИИ!
                
                // ✅ Сохраняем в очередь
                await SaveLocalShipment(box, quantityToShip, isFullShipment);
                localCount++;

                // ✅ Обновляем локальный статус
                BoxStatus newStatus;
                if (isFullShipment)
                    newStatus = BoxStatus.Shipped;
                else if (newQuantity == 0)
                    newStatus = BoxStatus.Empty;
                else
                    newStatus = BoxStatus.Active;

                // ✅ isPartial НЕ ТРОГАЕМ — только статус и количество
                await _dbHelper.ForceUpdateBoxStatus(
                    barcode: box.Barcode,
                    newStatus: newStatus,
                    newQuantity: newQuantity,
                    isPartial: box.IsPartial // ⚠️ Сохраняем СТАРОЕ значение
                );
                
                if (isFullShipment)
                    shippedCount++;
                else
                    partialCount++;
            }

            // ✅ Формируем сообщение
            var message = $"Обработано {boxes.Count} коробок.\nВсего отгружено: {totalShippedQuantity} шт.\n\n";
            if (shippedCount > 0) message += $"✅ Полностью отгружено: {shippedCount}\n";
            if (partialCount > 0) message += $"✂️ Частично отгружено: {partialCount}\n";
            if (localCount > 0) message += $"📴 Сохранено локально: {localCount}\n";
            if (errors.Any()) message += $"\n⚠️ Ошибки:\n{string.Join("\n", errors)}";
            message += localCount == 0 && !errors.Any() 
                ? "\n✅ Данные синхронизированы с сервером." 
                : "\n📴 Данные сохранены локально.";

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

    // ✅ ИСПРАВЛЕНО: убрано дублирование сохранения транзакций
    // Теперь используется только ApiService (который теперь сохраняет Shipping)

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

        // ✅ ИСПРАВЛЕНО: используем ApiService для отгрузки (он сам сохранит в офлайн при необходимости)
        try
        {
            Dictionary<string, object> result;
            
            if (isFullShipment)
            {
                result = await _apiService.ShipBox(box.Id, $"Полная отгрузка через ТСД");
            }
            else
            {
                result = await _apiService.ConsumeBox(box.Id, quantityToShip, $"Частичная отгрузка: {quantityToShip} шт.");
            }
            
            // Если операция выполнена онлайн, обновляем кэш
            if (result.TryGetValue("success", out var success) && success is bool s && s)
            {
                // Обновляем локальный кэш с сервера
                await RefreshBoxCache(box.Barcode);
            }
            else
            {
                // Если офлайн, транзакция уже сохранена в OfflineService
                // Обновляем локальный статус
                await _dbHelper.ForceUpdateBoxStatus(
                    barcode: box.Barcode,
                    newStatus: newStatus,
                    newQuantity: newQuantity,
                    isPartial: box.IsPartial
                );
            }
        }
        catch (Exception ex)
        {
            // При ошибке сохраняем локально (транзакция уже сохранена в OfflineService через ApiService)
            System.Diagnostics.Debug.WriteLine($"⚠️ Ошибка отгрузки, сохранено локально: {ex.Message}");
            
            await _dbHelper.ForceUpdateBoxStatus(
                barcode: box.Barcode,
                newStatus: newStatus,
                newQuantity: newQuantity,
                isPartial: box.IsPartial
            );
        }
        
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