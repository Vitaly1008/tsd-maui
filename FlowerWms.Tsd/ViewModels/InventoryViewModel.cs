using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;

namespace FlowerWms.Tsd.ViewModels;

public enum InventoryMode
{
    Inventory,
    Move
}

public partial class InventoryViewModel : ObservableObject
{
    private readonly ApiService _apiService;
    private readonly LoggerService _logger;
    private Box? _selectedBox;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    [ObservableProperty]
    private InventoryMode _currentMode = InventoryMode.Inventory;

    [ObservableProperty]
    private string _selectedBoxNumber = "Не выбрана";

    [ObservableProperty]
    private string _currentValue = "Ожидание...";

    [ObservableProperty]
    private int _newQuantity;

    [ObservableProperty]
    private string? _targetLocation;

    [ObservableProperty]
    private string _actionButtonText = "✅ Подтвердить количество";

    public bool IsInventoryMode => CurrentMode == InventoryMode.Inventory;
    public bool IsMoveMode => CurrentMode == InventoryMode.Move;

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public InventoryViewModel()
    {
        _apiService = new ApiService();
        _logger = new LoggerService();
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            var syncService = new SyncService();
            IsOnline = await syncService.CheckInternetManual();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ScanBox(string barcode)
    {
        IsLoading = true;
        LastScannedBarcode = barcode;

        try
        {
            var result = await _apiService.ScanBarcode(barcode, Constants.DeviceId);
            
            if (result.ContainsKey("data"))
            {
                var boxData = result["data"] as Dictionary<string, object> ?? new();
                _selectedBox = Box.FromJson(boxData);
                
                SelectedBoxNumber = $"#{_selectedBox.BoxNumber}";
                
                if (CurrentMode == InventoryMode.Inventory)
                {
                    NewQuantity = _selectedBox.Quantity;
                    CurrentValue = _selectedBox.Quantity.ToString();
                    ActionButtonText = "✅ Подтвердить количество";
                }
                else
                {
                    CurrentValue = "Сканируйте новую локацию";
                    ActionButtonText = "✅ Подтвердить перемещение";
                }
                
                _logger.Success($"✅ Коробка #{_selectedBox.BoxNumber} выбрана");
            }
            else if (result.ContainsKey("offline") && (bool)result["offline"])
            {
                // Офлайн-режим — создаем локальную коробку
                var box = CreateLocalBox(barcode);
                _selectedBox = box;
                SelectedBoxNumber = $"#{box.BoxNumber} (офлайн)";
                NewQuantity = box.Quantity;
                CurrentValue = box.Quantity.ToString();
                _logger.Success("✅ Коробка создана локально (офлайн)");
            }
        }
        catch (Exception ex)
        {
            // Офлайн-режим
            var box = CreateLocalBox(barcode);
            _selectedBox = box;
            SelectedBoxNumber = $"#{box.BoxNumber} (офлайн)";
            NewQuantity = box.Quantity;
            CurrentValue = box.Quantity.ToString();
            _logger.Success("✅ Коробка создана локально (офлайн)");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SwitchMode(InventoryMode mode)
    {
        CurrentMode = mode;
        _selectedBox = null;
        SelectedBoxNumber = "Не выбрана";
        CurrentValue = "Ожидание...";
        NewQuantity = 0;
        TargetLocation = null;
        
        ActionButtonText = mode == InventoryMode.Inventory 
            ? "✅ Подтвердить количество" 
            : "✅ Подтвердить перемещение";
        
        _logger.Info($"🔄 Смена режима: {(mode == InventoryMode.Inventory ? "Инвентаризация" : "Перемещение")}");
    }

    [RelayCommand]
    private void ScanLocation(string locationCode)
    {
        if (CurrentMode != InventoryMode.Move)
        {
            _logger.Warning("⚠️ Сканирование локации доступно только в режиме 'Перемещение'");
            return;
        }

        TargetLocation = locationCode;
        CurrentValue = locationCode;
        _logger.Info($"📍 Целевая локация: {locationCode}");
    }

    [RelayCommand]
    private async Task ConfirmAction()
    {
        if (_selectedBox == null)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Ошибка",
                "Сначала отсканируйте коробку",
                "OK"
            );
            return;
        }

        IsLoading = true;

        try
        {
            if (CurrentMode == InventoryMode.Inventory)
            {
                // Инвентаризация — обновляем количество
                if (NewQuantity <= 0)
                {
                    await Application.Current?.MainPage?.DisplayAlert(
                        "Ошибка",
                        "Введите корректное количество",
                        "OK"
                    );
                    return;
                }

                // TODO: API вызов для обновления количества
                // await _apiService.InventoryBox(_selectedBox.Id, NewQuantity);
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Успешно",
                    $"Коробка #{_selectedBox.BoxNumber} обновлена: {NewQuantity} шт.",
                    "OK"
                );
                
                _logger.Success($"✅ Инвентаризация: коробка #{_selectedBox.BoxNumber}, количество: {NewQuantity}");
            }
            else
            {
                // Перемещение
                if (string.IsNullOrEmpty(TargetLocation))
                {
                    await Application.Current?.MainPage?.DisplayAlert(
                        "Ошибка",
                        "Сначала отсканируйте целевую локацию",
                        "OK"
                    );
                    return;
                }

                // TODO: API вызов для перемещения
                // await _apiService.MoveBox(_selectedBox.Id, TargetLocation);
                
                await Application.Current?.MainPage?.DisplayAlert(
                    "✅ Успешно",
                    $"Коробка #{_selectedBox.BoxNumber} перемещена в {TargetLocation}",
                    "OK"
                );
                
                _logger.Success($"✅ Перемещение: коробка #{_selectedBox.BoxNumber} → {TargetLocation}");
            }

            // Сбрасываем состояние
            _selectedBox = null;
            SelectedBoxNumber = "Не выбрана";
            CurrentValue = "Ожидание...";
            NewQuantity = 0;
            TargetLocation = null;
            LastScannedBarcode = null;
        }
        catch (Exception ex)
        {
            _logger.Error($"❌ Ошибка: {ex.Message}");
            await Application.Current?.MainPage?.DisplayAlert("Ошибка", ex.Message, "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CancelOperation()
    {
        _selectedBox = null;
        SelectedBoxNumber = "Не выбрана";
        CurrentValue = "Ожидание...";
        NewQuantity = 0;
        TargetLocation = null;
        LastScannedBarcode = null;
        
        OperationCancelled?.Invoke(this, EventArgs.Empty);
        _logger.Info("❌ Операция отменена");
    }

    private Box CreateLocalBox(string barcode)
    {
        var parts = barcode.Split('-');
        
        var ean13 = parts.Length > 0 ? parts[0] : "0000000000000";
        var quantity = parts.Length > 1 && int.TryParse(parts[1], out var q) ? q : 100;
        var grade = parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) ? parts[2] : "Premium";
        var boxNumber = parts.Length > 3 && int.TryParse(parts[3], out var n) ? n : 0;

        if (boxNumber == 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(barcode, @"-(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var n2))
                boxNumber = n2;
            else
                boxNumber = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 10000);
        }

        return new Box
        {
            Id = $"local_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Barcode = barcode,
            BoxNumber = boxNumber,
            ProductName = $"Локальная коробка #{boxNumber}",
            ProductEan13 = ean13,
            Quantity = quantity,
            Grade = grade,
            LocationCode = "UNKNOWN",
            Status = "Active"
        };
    }

    public bool IsActionEnabled => _selectedBox != null && (CurrentMode != InventoryMode.Move || !string.IsNullOrEmpty(TargetLocation));
}