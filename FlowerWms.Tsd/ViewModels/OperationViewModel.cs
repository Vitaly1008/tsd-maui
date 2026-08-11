using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using System.Collections.ObjectModel;
using System.Timers;

namespace FlowerWms.Tsd.ViewModels;

public partial class OperationViewModel : ObservableObject, IDisposable
{
    private readonly ApiService _apiService;
    private readonly OfflineService _offlineService;
    private readonly SyncService _syncService;
    private readonly System.Timers.Timer _autoSaveTimer;
    private string _operationType = string.Empty;
    private bool _disposed;
    private int _scanCountSinceLastSave;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentLocation = "UNKNOWN";

    [ObservableProperty]
    private string? _orderNumber;

    [ObservableProperty]
    private string? _orderId;

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<Box> ScannedBoxes { get; } = new();

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public OperationViewModel(string operationType)
    {
        _operationType = operationType;
        _apiService = new ApiService();
        _offlineService = new OfflineService();
        _syncService = new SyncService();

        // ✅ Автосохранение каждые 5 сканирований или 30 секунд
        _autoSaveTimer = new System.Timers.Timer(30000); // 30 секунд
        _autoSaveTimer.Elapsed += OnAutoSaveTimerElapsed;
        _autoSaveTimer.AutoReset = true;
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            var result = await _apiService.StartOperation(_operationType, Constants.DeviceId);
            
            // ✅ Запускаем таймер автосохранения
            _autoSaveTimer.Start();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ✅ Обработчик автосохранения
    private async void OnAutoSaveTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await AutoSaveIfNeeded();
    }

    private async Task AutoSaveIfNeeded()
    {
        if (_scanCountSinceLastSave == 0 || ScannedBoxes.Count == 0)
            return;

        try
        {
            var boxes = ScannedBoxes.ToList();
            
            var transactionId = await _offlineService.SaveTransaction(
                operationType: _operationType,
                barcode: string.Join(",", boxes.Select(b => b.Barcode)),
                payload: new
                {
                    operationType = _operationType,
                    boxes = boxes.Select(b => b.ToDictionary()),
                    locationCode = CurrentLocation,
                    orderNumber = OrderNumber,
                    isAutoSave = true,
                    deviceId = Constants.DeviceId,
                    timestamp = DateTime.UtcNow
                },
                deviceId: Constants.DeviceId
            );

            _scanCountSinceLastSave = 0;
            
            System.Diagnostics.Debug.WriteLine($"💾 Автосохранение: {boxes.Count} коробок");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка автосохранения: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ScanBox(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;

        if (ScannedBoxes.Any(b => b.Barcode == barcode))
        {
            ErrorMessage = "❌ Коробка уже отсканирована в этой сессии";
            await Application.Current?.MainPage?.DisplayAlert(
                "⚠️ Внимание",
                ErrorMessage,
                "OK"
            );
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // ✅ Проверка в БД
            var dbHelper = new DatabaseHelper();
            var exists = await dbHelper.IsBoxExistsByBarcode(barcode);
            if (exists)
            {
                ErrorMessage = "❌ Коробка уже существует на складе";
                await Application.Current?.MainPage?.DisplayAlert(
                    "⚠️ Внимание",
                    ErrorMessage,
                    "OK"
                );
                return;
            }

            var result = await _apiService.ScanBarcode(barcode, Constants.DeviceId);

            if (result.ContainsKey("data"))
            {
                var boxData = result["data"] as Dictionary<string, object> ?? new();
                var box = Box.FromJson(boxData);
                box.LocationCode = CurrentLocation;
                
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScannedBoxes.Add(box);
                    LastScannedBarcode = barcode;
                    _scanCountSinceLastSave++;
                });
            }
            else if (result.ContainsKey("offline") && (bool)result["offline"])
            {
                var box = await CreateLocalBox(barcode);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScannedBoxes.Add(box);
                    LastScannedBarcode = barcode;
                    _scanCountSinceLastSave++;
                });
            }
            else
            {
                ErrorMessage = result.ContainsKey("message") 
                    ? result["message"]?.ToString() ?? "Неизвестная ошибка"
                    : "Неизвестная ошибка";
            }

            // ✅ Автосохранение после 5 сканирований
            if (_scanCountSinceLastSave >= 5)
            {
                await AutoSaveIfNeeded();
            }
        }
        catch (Exception ex)
        {
            try
            {
                var box = await CreateLocalBox(barcode);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScannedBoxes.Add(box);
                    LastScannedBarcode = barcode;
                    _scanCountSinceLastSave++;
                });
            }
            catch (Exception innerEx)
            {
                ErrorMessage = innerEx.Message;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ScanLocation(string locationCode)
    {
        CurrentLocation = locationCode;
        
        for (int i = 0; i < ScannedBoxes.Count; i++)
        {
            var box = ScannedBoxes[i];
            ScannedBoxes[i] = new Box
            {
                Id = box.Id,
                Barcode = box.Barcode,
                BoxNumber = box.BoxNumber,
                ProductName = box.ProductName,
                ProductEan13 = box.ProductEan13,
                Quantity = box.Quantity,
                Grade = box.Grade,
                LocationCode = locationCode,
                OrderId = box.OrderId,
                Status = box.Status
            };
        }
    }

    [RelayCommand]
    public void RemoveBox(int index)
    {
        if (index >= 0 && index < ScannedBoxes.Count)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.RemoveAt(index);
            });
        }
    }

    [RelayCommand]
    public async Task ConfirmOperation(string? comment = null)
    {
        if (ScannedBoxes.Count == 0)
        {
            ErrorMessage = "⚠️ Нет коробок для подтверждения";
            return;
        }

        // ✅ Останавливаем таймер перед подтверждением
        _autoSaveTimer.Stop();
        
        // ✅ Делаем последнее автосохранение
        await AutoSaveIfNeeded();

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var boxes = ScannedBoxes.ToList();
            
            var transactionId = await _offlineService.SaveTransaction(
                operationType: _operationType,
                barcode: string.Join(",", boxes.Select(b => b.Barcode)),
                payload: new
                {
                    operationType = _operationType,
                    boxes = boxes.Select(b => b.ToDictionary()),
                    locationCode = CurrentLocation,
                    orderNumber = OrderNumber,
                    comment = comment,
                    deviceId = Constants.DeviceId,
                    timestamp = DateTime.UtcNow,
                    isConfirmed = true
                },
                deviceId: Constants.DeviceId
            );

            var hasInternet = await _syncService.CheckInternetManual();

            if (hasInternet)
            {
                bool allSuccess = true;
                string lastError = "";

                foreach (var box in boxes)
                {
                    try
                    {
                        var result = await _apiService.SyncOfflineTransaction(
                            transactionId: transactionId,
                            operationType: _operationType,
                            barcode: box.Barcode,
                            payload: new Dictionary<string, object>
                            {
                                ["boxId"] = box.Id,
                                ["boxNumber"] = box.BoxNumber,
                                ["productName"] = box.ProductName,
                                ["productEan13"] = box.ProductEan13,
                                ["quantity"] = box.Quantity,
                                ["locationCode"] = CurrentLocation,
                                ["grade"] = box.Grade,
                                ["operationType"] = _operationType
                            }
                        );
                        
                        if (!(bool)result["success"])
                        {
                            allSuccess = false;
                            lastError = result.ContainsKey("message") 
                                ? result["message"]?.ToString() ?? "Неизвестная ошибка"
                                : "Неизвестная ошибка";
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        allSuccess = false;
                        lastError = ex.Message;
                        break;
                    }
                }

                if (allSuccess)
                {
                    await _offlineService.MarkAsSynced(transactionId);
                    
                    await Application.Current?.MainPage?.DisplayAlert(
                        "✅ Успешно",
                        $"Синхронизировано {boxes.Count} коробок",
                        "OK"
                    );
                }
                else
                {
                    await _offlineService.MarkAsError(transactionId, lastError);
                    
                    await Application.Current?.MainPage?.DisplayAlert(
                        "⚠️ Внимание",
                        $"Операция сохранена офлайн. Будет синхронизирована позже.\nОшибка: {lastError}",
                        "OK"
                    );
                }
            }
            else
            {
                await Application.Current?.MainPage?.DisplayAlert(
                    "📴 Офлайн-режим",
                    $"Операция сохранена. Будет синхронизирована при подключении к сети.",
                    "OK"
                );
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Clear();
                CurrentLocation = "UNKNOWN";
                OrderNumber = null;
                OrderId = null;
                LastScannedBarcode = null;
                _scanCountSinceLastSave = 0;
            });

            OperationCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            
            await Application.Current?.MainPage?.DisplayAlert(
                "❌ Ошибка",
                ex.Message,
                "OK"
            );
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CancelOperation()
    {
        // ✅ Останавливаем таймер
        _autoSaveTimer.Stop();
        
        // ✅ Делаем автосохранение при отмене
        await AutoSaveIfNeeded();
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            CurrentLocation = "UNKNOWN";
            OrderNumber = null;
            OrderId = null;
            LastScannedBarcode = null;
            _scanCountSinceLastSave = 0;
        });
        
        OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    private async Task<Box> CreateLocalBox(string barcode)
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

        var existsInCache = await IsBoxNumberExistsInCache(boxNumber);
        if (existsInCache)
        {
            throw new Exception($"❌ Коробка №{boxNumber} уже существует на складе!");
        }

        if (ScannedBoxes.Any(b => b.BoxNumber == boxNumber))
        {
            throw new Exception($"❌ Коробка №{boxNumber} уже отсканирована в этой сессии!");
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
            LocationCode = CurrentLocation,
            Status = 1 //"Active"
        };
    }

    private async Task<bool> IsBoxNumberExistsInCache(int boxNumber)
    {
        try
        {
            var dbHelper = new DatabaseHelper();
            // ✅ Используем правильный метод
            return await dbHelper.IsActiveBoxNumberExists(boxNumber);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка проверки дубликата: {ex.Message}");
            return false;
        }
    }

    [RelayCommand]
    private void ShowBoxesList()
    {
        // Обработка будет в View
    }

    // ✅ Реализация IDisposable
    public void Dispose()
    {
        if (_disposed) return;
        
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}