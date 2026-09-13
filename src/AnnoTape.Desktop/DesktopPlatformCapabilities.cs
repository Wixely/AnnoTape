using System.Diagnostics;
using AnnoTape.App.Platform;
using SkiaSharp;

namespace AnnoTape.DesktopHost;

public sealed class DesktopPlatformCapabilities : IPlatformCapabilities
{
    private const int MaximumPreviewDimension = 1600;

    public event Action<PhotoSelection?>? ExternalPhotoCompleted
    {
        add { }
        remove { }
    }

    public string AppDataPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AnnoTape",
        "Desktop");

    public Task<PhotoSelection?> PickPhotoAsync(CancellationToken cancellationToken = default) =>
        ShowPhotoPickerAsync(cancellationToken);

    // Desktop layout testing has no camera device contract, so the camera action opens the same
    // image picker and lets every editor state remain testable.
    public Task<PhotoSelection?> CapturePhotoAsync(CancellationToken cancellationToken = default) =>
        ShowPhotoPickerAsync(cancellationToken);

    public Task<string> PrepareDisplayImageAsync(
        string sourcePath,
        int rotationDegrees,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previewPath = Path.Combine(
            Path.GetDirectoryName(sourcePath) ?? AppDataPath,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}.preview.jpg");
        if (File.Exists(previewPath) && new FileInfo(previewPath).Length > 0) return previewPath;

        using var decoded = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidDataException("The imported photo cannot be decoded for display.");
        var scale = Math.Min(1f, MaximumPreviewDimension / (float)Math.Max(decoded.Width, decoded.Height));
        var scaledWidth = Math.Max(1, (int)MathF.Round(decoded.Width * scale));
        var scaledHeight = Math.Max(1, (int)MathF.Round(decoded.Height * scale));
        var rotation = ((rotationDegrees % 360) + 360) % 360;
        var outputWidth = rotation is 90 or 270 ? scaledHeight : scaledWidth;
        var outputHeight = rotation is 90 or 270 ? scaledWidth : scaledHeight;

        using var preview = new SKBitmap(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(preview))
        {
            canvas.Clear(SKColors.Black);
            canvas.Translate(outputWidth / 2f, outputHeight / 2f);
            canvas.RotateDegrees(rotation);
            canvas.Scale(scale);
            canvas.Translate(-decoded.Width / 2f, -decoded.Height / 2f);
            canvas.DrawBitmap(decoded, 0, 0);
        }

        var temporaryPath = $"{previewPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var image = SKImage.FromBitmap(preview);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 88)
                ?? throw new InvalidOperationException("The display preview could not be encoded.");
            using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoded.SaveTo(destination);
                destination.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, previewPath, true);
            return previewPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }, cancellationToken);

    public Task ShareFileAsync(string path, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static Task<PhotoSelection?> ShowPhotoPickerAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<PhotoSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Choose a photo for AnnoTape",
                    Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp;*.bmp|All files|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (Directory.Exists(pictures)) dialog.InitialDirectory = pictures;
                completion.TrySetResult(dialog.ShowDialog() == DialogResult.OK ? DescribeFile(dialog.FileName) : null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "AnnoTape photo picker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static PhotoSelection DescribeFile(string path)
    {
        using var input = File.OpenRead(path);
        using var codec = SKCodec.Create(input)
            ?? throw new InvalidDataException("The selected file is not a supported image.");
        var rotation = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0
        };
        return new(
            Path.GetFileName(path),
            Path.GetExtension(path),
            codec.Info.Width,
            codec.Info.Height,
            rotation,
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<Stream>(File.OpenRead(path));
            });
    }
}
