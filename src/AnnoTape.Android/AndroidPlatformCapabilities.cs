using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Provider;
using AndroidX.Core.Content;
using AnnoTape.App.Platform;
using File = Java.IO.File;
using IOException = System.IO.IOException;
using Orientation = Android.Media.Orientation;
using Path = System.IO.Path;
using Stream = System.IO.Stream;
using Uri = Android.Net.Uri;

namespace AnnoTape.AndroidHost;

public sealed class AndroidPlatformCapabilities(Activity activity) : IPlatformCapabilities
{
    private const int PickRequest = 4101;
    private const int CameraRequest = 4102;
    private const int SaveRequest = 4103;
    private const string ProviderAuthority = "app.annotape.mobile.files";
    private const string CameraPathKey = "camera_path";
    private readonly Activity _activity = activity;
    private TaskCompletionSource<PhotoSelection?>? _photoCompletion;
    private TaskCompletionSource<bool>? _saveCompletion;
    private string? _pendingSavePath;

    public event Action<PhotoSelection?>? ExternalPhotoCompleted;
    public event Action<string>? ExternalPhotoFailed;
    public string AppDataPath => _activity.FilesDir?.AbsolutePath ?? throw new InvalidOperationException("Android app storage is unavailable.");
    private ISharedPreferences Preferences => _activity.GetPreferences(FileCreationMode.Private) ?? throw new InvalidOperationException("Android preferences are unavailable.");

    public Task<PhotoSelection?> PickPhotoAsync(CancellationToken cancellationToken = default)
    {
        EnsureNoOperation();
        var intent = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? new Intent(MediaStore.ActionPickImages).SetType("image/*")
            : new Intent(Intent.ActionOpenDocument).SetType("image/*").AddCategory(Intent.CategoryOpenable);
        _photoCompletion = NewCompletion(cancellationToken);
        _activity.StartActivityForResult(intent, PickRequest);
        return _photoCompletion.Task;
    }

    public Task<PhotoSelection?> CapturePhotoAsync(CancellationToken cancellationToken = default)
    {
        EnsureNoOperation();
        using var directory = new File(_activity.CacheDir, "camera-pending");
        if (!directory.Exists() && !directory.Mkdirs()) throw new IOException("Could not create the camera staging directory.");
        var path = Path.Combine(directory.AbsolutePath, $"{Guid.NewGuid():N}.jpg");
        using var javaFile = new File(path);
        var output = FileProvider.GetUriForFile(_activity, ProviderAuthority, javaFile);
        var editor = Preferences.Edit() ?? throw new InvalidOperationException("Android preferences editor is unavailable.");
        editor.PutString(CameraPathKey, path);
        editor.Commit();

        var intent = new Intent(MediaStore.ActionImageCapture);
        intent.PutExtra(MediaStore.ExtraOutput, output);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        intent.ClipData = ClipData.NewRawUri("AnnoTape camera output", output);
        _photoCompletion = NewCompletion(cancellationToken);
        _activity.StartActivityForResult(intent, CameraRequest);
        return _photoCompletion.Task;
    }

    public Task ShareFileAsync(string path, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var file = new File(path);
        var uri = FileProvider.GetUriForFile(_activity, ProviderAuthority, file);
        var send = new Intent(Intent.ActionSend);
        send.SetType(contentType);
        send.PutExtra(Intent.ExtraStream, uri);
        send.AddFlags(ActivityFlags.GrantReadUriPermission);
        send.ClipData = ClipData.NewRawUri("AnnoTape export", uri);
        _activity.StartActivity(Intent.CreateChooser(send, "Share annotated image"));
        return Task.CompletedTask;
    }

    public Task<bool> SaveFileAsync(
        string path,
        string suggestedName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoOperation();
        if (!System.IO.File.Exists(path)) throw new FileNotFoundException("The exported image is unavailable.", path);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        _saveCompletion = completion;
        _pendingSavePath = path;
        try
        {
            var intent = new Intent(Intent.ActionCreateDocument)
                .SetType(contentType)
                .AddCategory(Intent.CategoryOpenable);
            intent.PutExtra(Intent.ExtraTitle, suggestedName);
            _activity.StartActivityForResult(intent, SaveRequest);
            return completion.Task;
        }
        catch
        {
            _saveCompletion = null;
            _pendingSavePath = null;
            throw;
        }
    }

    public Task<string> PrepareDisplayImageAsync(
        string sourcePath,
        int rotationDegrees,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        const int maximumDimension = 1600;
        var previewPath = Path.Combine(
            Path.GetDirectoryName(sourcePath) ?? AppDataPath,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}.preview.jpg");
        if (System.IO.File.Exists(previewPath) && new System.IO.FileInfo(previewPath).Length > 0)
            return previewPath;

        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(sourcePath, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
            throw new InvalidDataException("The imported photo cannot be decoded for display.");

        var sampleSize = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sampleSize > maximumDimension)
            sampleSize *= 2;
        using var decodeOptions = new BitmapFactory.Options
        {
            InSampleSize = sampleSize,
            InPreferredConfig = Bitmap.Config.Argb8888
        };
        using var decoded = BitmapFactory.DecodeFile(sourcePath, decodeOptions)
            ?? throw new InvalidDataException("The imported photo cannot be decoded for display.");

        Bitmap? rotated = null;
        var normalizedRotation = ((rotationDegrees % 360) + 360) % 360;
        if (normalizedRotation != 0)
        {
            using var matrix = new Matrix();
            matrix.PostRotate(normalizedRotation);
            rotated = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true)
                ?? throw new InvalidOperationException("The display preview could not be oriented.");
        }

        var temporaryPath = $"{previewPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!(rotated ?? decoded).Compress(Bitmap.CompressFormat.Jpeg!, 88, destination))
                    throw new InvalidOperationException("The display preview could not be encoded.");
                destination.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            System.IO.File.Move(temporaryPath, previewPath, true);
            return previewPath;
        }
        finally
        {
            rotated?.Dispose();
            if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
        }
    }, cancellationToken);

    internal void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == SaveRequest)
        {
            HandleSaveResult(resultCode, data?.Data);
            return;
        }
        if (requestCode is not (PickRequest or CameraRequest)) return;
        PhotoSelection? selection = null;
        Exception? failure = null;
        try
        {
            if (resultCode == Result.Ok)
            {
                if (requestCode == PickRequest && data?.Data is { } picked)
                {
                    if ((data.Flags & ActivityFlags.GrantPersistableUriPermission) != 0)
                        _activity.ContentResolver?.TakePersistableUriPermission(picked, ActivityFlags.GrantReadUriPermission);
                    selection = DescribeUri(picked);
                }
                else if (requestCode == CameraRequest)
                {
                    var path = Preferences.GetString(CameraPathKey, null);
                    if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path)) selection = DescribeFile(path);
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (requestCode == CameraRequest)
            {
                var editor = Preferences.Edit();
                editor?.Remove(CameraPathKey);
                editor?.Commit();
            }
            var completion = _photoCompletion;
            _photoCompletion = null;
            if (completion is not null)
            {
                if (failure is not null) completion.TrySetException(failure);
                else completion.TrySetResult(selection);
            }
            else if (failure is not null) ExternalPhotoFailed?.Invoke(failure.Message);
            else ExternalPhotoCompleted?.Invoke(selection);
        }
    }

    private void HandleSaveResult(Result resultCode, Uri? destination)
    {
        var completion = _saveCompletion;
        var sourcePath = _pendingSavePath;
        _saveCompletion = null;
        _pendingSavePath = null;
        if (completion is null) return;
        if (resultCode != Result.Ok)
        {
            completion.TrySetResult(false);
            return;
        }
        if (destination is null || string.IsNullOrWhiteSpace(sourcePath))
        {
            completion.TrySetException(new IOException("The save destination was unavailable."));
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var resolver = _activity.ContentResolver ?? throw new InvalidOperationException("Android content resolver is unavailable.");
                await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                await using var output = resolver.OpenOutputStream(destination, "w")
                    ?? throw new IOException("The selected destination could not be opened.");
                await source.CopyToAsync(output);
                await output.FlushAsync();
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
    }

    private PhotoSelection DescribeUri(Uri uri)
    {
        var resolver = _activity.ContentResolver ?? throw new InvalidOperationException("Android content resolver is unavailable.");
        var mime = resolver.GetType(uri);
        if (!string.IsNullOrWhiteSpace(mime) && !mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a supported image file; videos cannot be measured.");
        var name = QueryName(uri) ?? "photo";
        using var dimensionsStream = resolver.OpenInputStream(uri) ?? throw new IOException("The selected photo could not be opened.");
        var (width, height) = ReadDimensions(dimensionsStream);
        using var orientationStream = resolver.OpenInputStream(uri) ?? throw new IOException("The selected photo could not be opened.");
        var rotation = ReadRotation(orientationStream);
        var extension = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(extension)) extension = MimeToExtension(mime);
        return new(name, extension, width, height, rotation, _ =>
            Task.FromResult<Stream>(resolver.OpenInputStream(uri) ?? throw new IOException("The selected photo is no longer available.")));
    }

    private static PhotoSelection DescribeFile(string path)
    {
        using var dimensionsStream = System.IO.File.OpenRead(path);
        var (width, height) = ReadDimensions(dimensionsStream);
        using var orientationStream = System.IO.File.OpenRead(path);
        var rotation = ReadRotation(orientationStream);
        return new(Path.GetFileName(path), Path.GetExtension(path), width, height, rotation,
            _ => Task.FromResult<Stream>(System.IO.File.OpenRead(path)));
    }

    private string? QueryName(Uri uri)
    {
        using var cursor = _activity.ContentResolver?.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
        if (cursor is null || !cursor.MoveToFirst()) return null;
        var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
        return index < 0 ? null : cursor.GetString(index);
    }

    private static (int Width, int Height) ReadDimensions(Stream stream)
    {
        var options = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeStream(stream, null, options);
        if (options.OutWidth <= 0 || options.OutHeight <= 0)
            throw new InvalidDataException("Choose a supported image file; videos and damaged files cannot be measured.");
        return (options.OutWidth, options.OutHeight);
    }

    private static int ReadRotation(Stream stream)
    {
        try
        {
            using var exif = new ExifInterface(stream);
            return exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal) switch
            {
                (int)Orientation.Rotate90 => 90,
                (int)Orientation.Rotate180 => 180,
                (int)Orientation.Rotate270 => 270,
                _ => 0
            };
        }
        catch (IOException) { return 0; }
    }

    private static string MimeToExtension(string? mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/heic" or "image/heif" => ".heic",
        _ => ".jpg"
    };

    private TaskCompletionSource<PhotoSelection?> NewCompletion(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<PhotoSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion;
    }

    private void EnsureNoOperation()
    {
        if (_photoCompletion is not null || _saveCompletion is not null)
            throw new InvalidOperationException("A photo or file request is already open.");
    }
}
