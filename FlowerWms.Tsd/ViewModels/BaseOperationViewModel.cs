using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using FlowerWms.Tsd.Helpers;
using System.Collections.ObjectModel;

namespace FlowerWms.Tsd.ViewModels;

/// <summary>
/// Базовый ViewModel для операций (приемка/отгрузка).
/// Все «общие» свойства (ScannedBoxes, ScannedCount, IsBoxScanned, BoxInfoText,
/// IsBoxListExpanded) живут в BaseScannerViewModel — здесь их НЕ дублируем.
/// </summary>
public abstract partial class BaseOperationViewModel : BaseScannerViewModel
{
    protected readonly string _operationType;

    [ObservableProperty]
    private string _currentLocation = "UNKNOWN";

    [ObservableProperty]
    private string? _orderNumber;

    [ObservableProperty]
    private string? _orderId;

    [ObservableProperty]
    private string _boxNumberDisplay = string.Empty;

    // ===== СОБЫТИЯ =====
    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    // ===== ЗАЩИЩЕННЫЕ МЕТОДЫ ДЛЯ ВЫЗОВА СОБЫТИЙ =====
    protected virtual void OnOperationCompleted()
    {
        OperationCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected virtual void OnOperationCancelled()
    {
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    protected BaseOperationViewModel(
        string operationType,
        DatabaseHelper dbHelper,
        ApiService apiService,
        SyncQueueService syncQueueService,
        SyncService syncService,
        IBarcodeService? barcodeService = null)
        : base(dbHelper, apiService, syncQueueService, syncService, barcodeService)
    {
        _operationType = operationType;
    }

    public override async Task Initialize(IBarcodeService? barcodeService = null)
    {
        await base.Initialize(barcodeService);
    }

    protected override async Task ProcessBarcode(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;

        if (TryParseLocationBarcode(barcode, out var locationCode))
        {
            await ProcessLocationScan(locationCode);
            return;
        }

        if (TryParseBoxBarcode(barcode, out _, out _, out _, out _, out _))
        {
            await ProcessBoxScan(barcode);
            return;
        }

        SetError("Некорректный формат штрихкода");
    }

    protected virtual async Task ProcessLocationScan(string locationCode)
    {
        try
        {
            // Регистр нормализован в TryParseLocationBarcode (ToUpperInvariant)
            var location = await _dbHelper.GetLocationByCode(locationCode);

            if (location == null)
            {
                // Локация не найдена в локальной БД — ставим UNKNOWN, без ошибки.
                // Синхронизацию локаций не дёргаем: она идёт в общем SyncManual.
                Logger.Warning($"⚠️ Локация '{locationCode}' не найдена в кэше. Ставим UNKNOWN.");
                CurrentLocation = "UNKNOWN";
                LastScannedBarcode = locationCode;
                SetStatus($"Локация: UNKNOWN (не найдена в кэше)", "📍", Colors.Orange);
            }
            else if (location.is_active != 1)
            {
                SetWarning($"Локация '{locationCode}' неактивна", "⚠️", Colors.Orange);
                return;
            }
            else
            {
                CurrentLocation = locationCode;
                LastScannedBarcode = locationCode;
                SetStatus($"Локация: {locationCode} ({location.name})", "📍", Colors.Blue);
            }

            // Все уже отсканированные коробки переезжают в новую локацию.
            // Обновляем и в памяти (ScannedBoxes), и в boxes_cache.
            // Синхронизация читает локацию из boxes_cache — так смена локации
            // в середине сессии корректно уходит на сервер.
            foreach (var box in ScannedBoxes)
            {
                box.LocationCode = CurrentLocation;
            }

            if (ScannedBoxes.Count > 0)
            {
                var barcodes = ScannedBoxes.Select(b => b.Barcode).ToList();
                await _dbHelper.UpdateBoxesLocation(barcodes, CurrentLocation);
            }
        }
        catch (Exception ex)
        {
            SetError($"Ошибка проверки локации: {ex.Message}");
        }
    }

    protected abstract Task ProcessBoxScan(string barcode);

    [RelayCommand]
    public virtual async Task ConfirmOperation()
    {
        if (ScannedBoxes.Count == 0)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Внимание",
                "Нет коробок для подтверждения",
                "OK"
            );
            return;
        }

        // Должен быть переопределён в наследниках
        await Task.CompletedTask;
    }

    [RelayCommand]
    public virtual async Task CancelOperation()
    {
        if (ScannedBoxes.Count > 0)
        {
            var confirm = await Application.Current?.MainPage?.DisplayAlert(
                "Выход",
                $"Вы отсканировали {ScannedBoxes.Count} коробок. Выйти без сохранения?",
                "Да",
                "Нет"
            );

            if (confirm == false) return;
        }

        StopScanner();
        ClearSession();
        
        OnOperationCancelled();
    }

    [RelayCommand]
    public virtual async Task ShowBoxesList()
    {
        if (ScannedBoxes.Count == 0)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "Список коробок",
                "Нет отсканированных коробок",
                "OK"
            );
            return;
        }

        var boxList = string.Join("\n", ScannedBoxes.Select((b, i) =>
            $"{i + 1}. #{b.BoxNumber} | {b.ProductName} | {b.Quantity} шт. | {b.Grade} | {(b.Status == BoxStatus.Active ? "✅" : "📭")}")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"Список коробок ({ScannedBoxes.Count})",
            boxList,
            "OK"
        );
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}