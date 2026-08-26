namespace AnnoTape.App.Platform;

public sealed record PhotoSelection(
    string DisplayName,
    string Extension,
    int PixelWidth,
    int PixelHeight,
    int RotationDegrees,
    Func<CancellationToken, Task<Stream>> OpenReadAsync);

public interface IPlatformCapabilities
{
    event Action<PhotoSelection?>? ExternalPhotoCompleted;
    string AppDataPath { get; }
    Task<PhotoSelection?> PickPhotoAsync(CancellationToken cancellationToken = default);
    Task<PhotoSelection?> CapturePhotoAsync(CancellationToken cancellationToken = default);
    Task<string> PrepareDisplayImageAsync(string sourcePath, int rotationDegrees, CancellationToken cancellationToken = default);
    Task ShareFileAsync(string path, string contentType, CancellationToken cancellationToken = default);
}
