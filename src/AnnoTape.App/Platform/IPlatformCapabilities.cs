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
    event Action<string>? ExternalPhotoFailed;
    string AppDataPath { get; }
    Task<PhotoSelection?> PickPhotoAsync(CancellationToken cancellationToken = default);
    Task<PhotoSelection?> CapturePhotoAsync(CancellationToken cancellationToken = default);
    Task<string> PrepareDisplayImageAsync(string sourcePath, int rotationDegrees, CancellationToken cancellationToken = default);
    Task ShareFileAsync(string path, string contentType, CancellationToken cancellationToken = default);
    Task<bool> SaveFileAsync(string path, string suggestedName, string contentType, CancellationToken cancellationToken = default);
}
