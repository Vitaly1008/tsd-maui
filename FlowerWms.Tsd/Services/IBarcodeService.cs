namespace FlowerWms.Tsd.Services;

// Интерфейс сканера штрихкодов
public interface IBarcodeService : IDisposable
{
    event Action<string>? OnBarcodeScanned;
    void StartListening();
    void StopListening();
    bool IsListening { get; }
}