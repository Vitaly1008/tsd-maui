using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Helpers;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using System.Collections.ObjectModel;

namespace FlowerWms.Tsd.ViewModels;

public partial class OperationViewModel : ObservableObject
{
    private readonly ApiService _apiService;
    private readonly OfflineService _offlineService;
    private string _operationType = string.Empty;

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
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            var result = await _apiService.StartOperation(_operationType, Constants.DeviceId);
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

    [RelayCommand]
    public async Task ScanBox(string barcode)
    {
        if (string.IsNullOrEmpty(barcode)) return;

        // Проверка на дубликат
        if (ScannedBoxes.Any(b => b.Barcode == barcode))
        {
            ErrorMessage = "❌ Коробка уже отсканирована";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _apiService.ScanBarcode(barcode, Constants.DeviceId);

            if (result.ContainsKey("data"))
            {
                var boxData = result["data"] as Dictionary<string, object> ?? new();
                var box = Box.FromJson(boxData);
                
                // Обновляем локацию
                box.LocationCode = CurrentLocation;
                
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScannedBoxes.Add(box);
                    LastScannedBarcode = barcode;
                });
            }
            else if (result.ContainsKey("offline") && (bool)result["offline"])
            {
                // Офлайн-режим — создаем локальную коробку
                var box = CreateLocalBox(barcode);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ScannedBoxes.Add(box);
                    LastScannedBarcode = barcode;
                });
            }
            else
            {
                ErrorMessage = result.ContainsKey("message") 
                    ? result["message"]?.ToString() ?? "Неизвестная ошибка"
                    : "Неизвестная ошибка";
            }
        }
        catch (Exception ex)
        {
            // Офлайн-режим
            var box = CreateLocalBox(barcode);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Add(box);
                LastScannedBarcode = barcode;
            });
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
        
        // Обновляем локацию у всех коробок
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
    public async Task ConfirmOperation(string? comment = null)
    {
        if (ScannedBoxes.Count == 0)
        {
            ErrorMessage = "⚠️ Нет коробок для подтверждения";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var boxes = ScannedBoxes.ToList();
            
            // Сохраняем транзакцию
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
                    timestamp = DateTime.UtcNow
                },
                deviceId: Constants.DeviceId
            );

            // Проверяем интернет
            var syncService = new SyncService();
            var hasInternet = await syncService.CheckInternetManual();

            if (hasInternet)
            {
                // Синхронизация
                foreach (var box in boxes)
                {
                    await _apiService.SyncOfflineTransaction(
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
                }
                await _offlineService.MarkAsSynced(transactionId);
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ScannedBoxes.Clear();
                CurrentLocation = "UNKNOWN";
                OrderNumber = null;
                OrderId = null;
                LastScannedBarcode = null;
            });

            OperationCompleted?.Invoke(this, EventArgs.Empty);
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

    [RelayCommand]
    public async Task CancelOperation()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScannedBoxes.Clear();
            CurrentLocation = "UNKNOWN";
            OrderNumber = null;
            OrderId = null;
            LastScannedBarcode = null;
        });
        
        OperationCancelled?.Invoke(this, EventArgs.Empty);

        // Добавляем задержку для очистки
        await Task.CompletedTask;
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

        // Проверка на дубликат в сессии
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
            Status = "Active"
        };
    }

    [RelayCommand]
    private void ShowBoxesList()
    {
        // Обработка будет в View
    }
}