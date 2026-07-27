namespace FlowerWms.Tsd.Services;

public interface IBarcodeService : IDisposable
{
    event Action<string>? OnBarcodeScanned;
    void StartListening();
    void StopListening();
    bool IsListening { get; }
}