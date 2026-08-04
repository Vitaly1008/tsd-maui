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
    private bool _isScannerStarted;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _currentLocation = "UNKNOWN";

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    // ✅ Новые свойства для отображения информации о сканировании
    [ObservableProperty]
    private string _scanStatusText = "Отсканируйте штрихкод коробки";

    [ObservableProperty]
    private string _boxInfoText = string.Empty;

    [ObservableProperty]
    private bool _isBoxScanned;

    [ObservableProperty]
    private Color _scanStatusColor = Colors.Gray;

    public ObservableCollection<Box> ScannedBoxes => _operationViewModel.ScannedBoxes;

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public ReceivingViewModel(IBarcodeService? barcodeService = null)
    {
        _operationViewModel = new OperationViewModel("Receiving");
        _barcodeService = barcodeService;
        
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
        if (_barcodeService == null || _isScannerStarted) return;
        
        try
        {
            _barcodeService.StartListening();
            _isScannerStarted = true;
            System.Diagnostics.Debug.WriteLine("✅ Сканер запущен из ViewModel");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка запуска сканера: {ex.Message}");
        }
    }

    public void StopScanner()
    {
        if (_barcodeService == null || !_isScannerStarted) return;
        
        try
        {
            _barcodeService.StopListening();
            _isScannerStarted = false;
            System.Diagnostics.Debug.WriteLine("✅ Сканер остановлен из ViewModel");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка остановки сканера: {ex.Message}");
        }
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            await _operationViewModel.Initialize();
            var syncService = new SyncService();
            IsOnline = await syncService.CheckInternetManual();
            
            if (_barcodeService != null)
            {
                StartScanner();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Ошибка инициализации: {ex.Message}");
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

        await _operationViewModel.ScanBox(barcode);
        
        LastScannedBarcode = barcode;
        
        // ✅ Обновляем UI информацию о сканировании
        var lastBox = ScannedBoxes.LastOrDefault();
        if (lastBox != null)
        {
            IsBoxScanned = true;
            ScanStatusText = $"✅ Отсканировано: {barcode}";
            ScanStatusColor = Colors.Green;
            
            // Формируем информацию о коробке: цветок | количество | сорт | № коробки
            BoxInfoText = $"🌺 {lastBox.ProductName} | {lastBox.Quantity} шт. | {lastBox.Grade} | №{lastBox.BoxNumber}";
        }
        else
        {
            IsBoxScanned = false;
            ScanStatusText = "Отсканируйте штрихкод коробки";
            ScanStatusColor = Colors.Gray;
            BoxInfoText = string.Empty;
        }
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
        
        // ✅ Если удалили последнюю коробку — сбрасываем статус
        if (ScannedBoxes.Count == 0)
        {
            IsBoxScanned = false;
            ScanStatusText = "Отсканируйте штрихкод коробки";
            ScanStatusColor = Colors.Gray;
            BoxInfoText = string.Empty;
            LastScannedBarcode = null;
        }
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

        // ✅ Показываем штрихкоды вместо "Неизвестный продукт"
        var boxList = string.Join("\n", ScannedBoxes.Select((b, i) => 
            $"{i + 1}. {b.Barcode}")
        );

        await Application.Current?.MainPage?.DisplayAlert(
            $"📋 Список коробок ({ScannedBoxes.Count})",
            boxList,
            "OK"
        );
    }

    [RelayCommand]
    public async Task ShowError(string error)
    {
        await Application.Current?.MainPage?.DisplayAlert("Ошибка", error, "OK");
    }

    public void Dispose()
    {
        StopScanner();
        if (_barcodeService != null)
        {
            _barcodeService.OnBarcodeScanned -= OnBarcodeScanned;
        }
    }
}