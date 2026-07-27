using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using System.Collections.ObjectModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.ApplicationModel;

namespace FlowerWms.Tsd.ViewModels;

public partial class ReceivingViewModel : ObservableObject
{
    private readonly OperationViewModel _operationViewModel;
    private readonly IBarcodeService? _barcodeService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentLocation = "UNKNOWN";

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    public ObservableCollection<Box> ScannedBoxes => _operationViewModel.ScannedBoxes;

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    // ✅ Исправленный конструктор - получаем IBarcodeService через DI
    public ReceivingViewModel(IBarcodeService? barcodeService = null)
    {
        _operationViewModel = new OperationViewModel("Receiving");
        
        // Получаем сервис через DI
        _barcodeService = barcodeService ?? 
            (Application.Current?.Handler?.MauiContext?.Services?.GetService<IBarcodeService>());
        
        if (_barcodeService != null)
        {
            _barcodeService.OnBarcodeScanned += OnBarcodeScanned;
        }
        
        _operationViewModel.OperationCompleted += (s, e) => OperationCompleted?.Invoke(this, EventArgs.Empty);
        _operationViewModel.OperationCancelled += (s, e) => OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnBarcodeScanned(string barcode)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ScanBox(barcode);
        });
    }

    public void StartScanner()
    {
        _barcodeService?.StartListening();
    }

    public void StopScanner()
    {
        _barcodeService?.StopListening();
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            await _operationViewModel.Initialize();
            var syncService = new SyncService();
            IsOnline = await syncService.CheckInternetManual();
            StartScanner();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ScanBox(string barcode)
    {
        await _operationViewModel.ScanBox(barcode);
        LastScannedBarcode = barcode;
    }

    [RelayCommand]
    public void ScanLocation(string locationCode)
    {
        _operationViewModel.ScanLocation(locationCode);
        CurrentLocation = locationCode;
        LastScannedBarcode = locationCode;
    }

    [RelayCommand]
    public async Task ConfirmOperation()
    {
        await _operationViewModel.ConfirmOperation("Приемка через ТСД");
    }

    [RelayCommand]
    public async Task CancelOperation()
    {
        await _operationViewModel.CancelOperation();
        StopScanner();
    }

    [RelayCommand]
    public void RemoveBox(int index)
    {
        _operationViewModel.RemoveBox(index);
    }

    [RelayCommand]
    public async Task ShowLocationInput()
    {
        var result = await Application.Current?.MainPage?.DisplayPromptAsync(
            "📍 Введите код локации",
            "Например: A-01, B-02-03",
            "Подтвердить",
            "Отмена",
            CurrentLocation
        );

        if (!string.IsNullOrEmpty(result))
        {
            ScanLocation(result);
        }
    }

    [RelayCommand]
    public async Task ShowBoxesList()
    {
        if (ScannedBoxes.Count == 0)
        {
            await Application.Current?.MainPage?.DisplayAlert(
                "📋 Список коробок",
                "Нет отсканированных коробок",
                "OK"
            );
            return;
        }

        var boxNumbers = string.Join("\n", ScannedBoxes.Select((b, i) => 
            $"{i + 1}. #{b.BoxNumber} - {b.ProductName} ({b.Quantity} шт.)")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"📋 Список коробок ({ScannedBoxes.Count})",
            boxNumbers,
            "OK"
        );
    }

    [RelayCommand]
    public async Task ShowError(string error)
    {
        await Application.Current?.MainPage?.DisplayAlert("Ошибка", error, "OK");
    }
}