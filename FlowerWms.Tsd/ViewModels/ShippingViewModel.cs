using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowerWms.Tsd.Models;
using FlowerWms.Tsd.Services;
using System.Collections.ObjectModel;

namespace FlowerWms.Tsd.ViewModels;

public partial class ShippingViewModel : ObservableObject
{
    private readonly OperationViewModel _operationViewModel;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _orderNumber;

    [ObservableProperty]
    private string? _lastScannedBarcode;

    [ObservableProperty]
    private bool _isOnline = true;

    public ObservableCollection<Box> ScannedBoxes => _operationViewModel.ScannedBoxes;

    public event EventHandler? OperationCompleted;
    public event EventHandler? OperationCancelled;

    public ShippingViewModel()
    {
        _operationViewModel = new OperationViewModel("Shipping");
        
        _operationViewModel.OperationCompleted += (s, e) => OperationCompleted?.Invoke(this, EventArgs.Empty);
        _operationViewModel.OperationCancelled += (s, e) => OperationCancelled?.Invoke(this, EventArgs.Empty);
    }

    public async Task Initialize()
    {
        IsLoading = true;
        try
        {
            await _operationViewModel.Initialize();
            var syncService = new SyncService();
            IsOnline = await syncService.CheckInternetManual();
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ✅ Кнопка "Назад" - вызывает отмену операции
    [RelayCommand]
    private async Task Back()
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
        
        await _operationViewModel.CancelOperation();
    }

    [RelayCommand]
    private async Task ScanBox(string barcode)
    {
        await _operationViewModel.ScanBox(barcode);
        LastScannedBarcode = barcode;
    }

    [RelayCommand]
    private async Task ScanOrder()
    {
        var result = await Application.Current?.MainPage?.DisplayPromptAsync(
            "📋 Введите номер заказа",
            "Например: SO-001234",
            "Подтвердить",
            "Отмена"
        );

        if (!string.IsNullOrEmpty(result))
        {
            OrderNumber = result;
        }
    }

    [RelayCommand]
    private async Task ConfirmOperation()
    {
        await _operationViewModel.ConfirmOperation($"Отгрузка по заказу {OrderNumber ?? "без заказа"}");
    }

    [RelayCommand]
    private void RemoveBox(int index)
    {
        _operationViewModel.RemoveBox(index);
    }

    [RelayCommand]
    private async Task ShowBoxesList()
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
}